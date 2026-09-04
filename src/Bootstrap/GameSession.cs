using Embervale.Core;
using Embervale.Core.Diagnostics;
using Embervale.Core.Services;
using Embervale.Narrative;
using Embervale.Races;
using Embervale.Save;
using Embervale.UI;
using Embervale.World;
using Godot;

namespace Embervale.Bootstrap;

/// <summary>
/// One playthrough, and the middle composition root. It owns every service whose lifetime is a save
/// file's — the clock, autosave, the ledgers, map discovery, the party, the persistence directors —
/// and it owns the four hosts that own everything else: the world, the UI, the player and the
/// developer tools.
///
/// <para><b>Freeing this node ends a session.</b> The scope it hosts goes with it and takes every
/// registration, and each host's own <c>_ExitTree</c> takes its subtree. That is what makes New
/// Game → quit → New Game work in one process, which it never did before: the services used to be
/// children of the bootstrap, which is to say children of the process.</para>
///
/// <para><see cref="Build"/> is deliberately one ordered list rather than several installers called
/// in an arbitrary order. <b>The order is load-bearing and always was</b> — the clock before the
/// NPCs that read it, weather before the sky that reads weather, the audio director before the music
/// director that reuses its library, cell persistence before the streamer whose events it wants, the
/// player before the tutorial that watches them. Splitting it into per-layer installers would hide
/// exactly the constraint that matters, so instead each step delegates to the host that owns the
/// thing being built, and the sequence stays readable in one place.</para>
/// </summary>
public sealed partial class GameSession : Node3D, IServiceScopeHost
{
    private ServiceScope? _scope;

    /// <summary>Session lifetime: what one save file owns.</summary>
    public ServiceScope Scope => _scope ??= new ServiceScope(ServiceLifetime.Session);

    /// <summary>The host that created this session; the route back to quit/abort.</summary>
    public SessionLifecycleCoordinator Lifecycle { get; init; } = null!;

    /// <summary>The save slot this session reads and writes.</summary>
    public string Slot { get; init; } = "quick";

    /// <summary>The character being played — race and name. Chosen at creation, restored on load.</summary>
    public CharacterProfile Profile { get; init; } = CharacterProfile.Human;

    /// <summary>True for a new game (grant the race's innate perks/spells/reputation), false for a
    /// load (the overlay brings them back and re-granting would double them).</summary>
    public bool ApplyStartingGrants { get; init; } = true;

    /// <summary>The region the session is in. Written by the world director on a transition; read
    /// by the save header and the respawn point.</summary>
    public string CurrentRegionId { get; set; } = GameIds.Regions.EmberCrown;

    /// <summary>World lifetime. Freed and rebuilt independently of the session, which is what makes
    /// "unload the world, keep the save" expressible at all.</summary>
    public WorldHost World { get; private set; } = null!;

    public WorldSessionDirector WorldDirector { get; private set; } = null!;

    public UICompositionRoot Ui { get; private set; } = null!;

    public PlayerHost Players { get; private set; } = null!;

    public LoadingCoordinator Loading { get; private set; } = null!;

    public SaveHeaderComposer Header { get; private set; } = null!;

    /// <summary>The developer surfaces (console, debug HUD, profiler, integrity checker, the
    /// training dummy and the single-key cheats). Null in a capture or exported build.</summary>
    public DeveloperToolsHost? DevTools { get; private set; }

    /// <summary>The new-game prologue. Null once it has played; a load never builds one.</summary>
    public OpeningSequence? Opening { get; private set; }

    public override void _EnterTree()
    {
        Name = "GameSession";
        _ = Scope; // open the scope before any child registers into it

        // The hosts exist before Build so anything constructed inside it has somewhere to go and a
        // lifetime already decided by its parent.
        World = new WorldHost();
        AddChild(World);

        WorldDirector = new WorldSessionDirector { Name = "WorldDirector", Session = this };
        World.AddChild(WorldDirector);

        Ui = new UICompositionRoot { Name = "UIRoot" };
        AddChild(Ui);

        Players = new PlayerHost { Name = "PlayerHost", Session = this };
        AddChild(Players);

        Loading = new LoadingCoordinator { Name = "Loading", Session = this };
        AddChild(Loading);

        Header = new SaveHeaderComposer(this);
    }

    public override void _ExitTree()
    {
        _scope?.Dispose();
        _scope = null;
    }

    /// <summary>
    /// Assembles the session. Shared by New Game and Load; neither changes game state here — the
    /// loading gate does that once the world under the player is real.
    /// </summary>
    public void Build()
    {
        // 1. The world's sun, sky, tonemap and ground, before any UI that draws over them.
        WorldDirector.BuildEnvironment();

        // 2. The shell UI: HUD, toasts, combat feedback, pause menu, loading screen.
        Ui.BuildShell();

        // 3. The prologue, built with the rest of the shell so it can play the moment the world is
        //    assembled; it stays hidden unless New Game asks for it. The slice's ending director and
        //    closing card sit beside it.
        Opening = new OpeningSequence();
        AddChild(Opening);
        ServiceScope.RegisterOwned(Opening, Opening);
        AddChild(new SliceDirector { Name = "Slice" });
        AddChild(new ClosingSequence());

        // 4. Developer surfaces. A capture or exported build makes none of them.
        if (BuildProfile.ShowDeveloperTools)
        {
            DevTools = new DeveloperToolsHost { Name = "DeveloperTools", Session = this };
            AddChild(DevTools);
            DevTools.BuildOverlays();
        }

        // 5. Autosave cadence on top of the slot system: rotates through auto1..auto3 on a timer /
        //    quest completion / level-up, never touching the player's manual slot.
        var autosave = new AutosaveService { Name = "Autosave" };
        AddChild(autosave);
        ServiceScope.RegisterOwned(autosave, autosave);

        // 6. Dev-only telemetry, added before the player spawn below so it sees the whole session.
        AddChild(new Embervale.Analytics.AnalyticsSink());

        // 7. The modal and event-driven panels.
        Ui.BuildPanels();

        // 8. The world clock drives NPC routines; before the NPCs so it is resolvable when their
        //    schedules first read the time.
        var clock = new WorldClock { Name = "WorldClock" };
        AddChild(clock);
        Ui.SetClock(clock);
        DevTools?.SetClock(clock);

        // 9. Weather before the sky so the sky controller can read the active state on its first
        //    frame; both are the world's, not the session's.
        WorldDirector.BuildWeatherAndSky(Ui, DevTools);

        // 10. Persistent spawned actors: a director that recreates saved named actors and containers
        //     on load (the save manager alone only restores components of actors already in scene).
        PersistentActorRegistry.Clear();
        PersistentActorRegistry.Register(GameIds.Templates.Cache, PlayerHost.BuildPersistentCache);
        Embervale.Housing.PlaceableTemplates.RegisterAll();
        var persistentSpawns = new PersistentSpawnDirector { Name = "PersistentSpawns" };
        AddChild(persistentSpawns);

        // 11. The training dummy and the sandbox props are scaffolding, not content: a stranger
        //     playing the slice must never see a training dummy in the town square.
        if (BuildProfile.SpawnSandboxContent)
        {
            DevTools?.SpawnDummy();
        }

        Players.SpawnPlayer();

        if (BuildProfile.SpawnSandboxContent)
        {
            SandboxProps.Seed(World);
        }

        // 12. Ambient encounters and world events — the world's.
        WorldDirector.BuildDirectors(Ui, DevTools);
        Players.SpawnPersistentActors(persistentSpawns);

        // 13. Session directors that need the player to already exist.
        AddChild(new Embervale.Onboarding.TutorialDirector { Name = "Tutorial" });
        AddChild(new Embervale.Companions.CompanionRoster { Name = "Companions" });
        AddChild(new Embervale.Enemies.BossEncounterDirector { Name = "BossEncounter" });
        AddChild(new Embervale.Combat.HitStopDirector { Name = "HitStop" });
        AddChild(new Embervale.Combat.CombatFeedbackDirector { Name = "CombatFeedback" });

        // 14. Audio. The music director reuses the library the audio director registers, so order.
        var audio = new Embervale.Audio.AudioDirector { Name = "Audio" };
        AddChild(audio);
        ServiceScope.RegisterOwned(audio, audio);
        AddChild(new Embervale.Audio.MusicDirector { Name = "Music" });
        AddChild(new Embervale.Audio.AmbienceDirector { Name = "Ambience" });

        // 15. Streamed-cell persistence, before the streamer so it is subscribed to the cell
        //     load/unload events before the first cell streams in.
        AddChild(new CellPersistenceDirector { Name = "CellPersistence" });

        // 16. World-map discovery, before the streamer so it catches the first cell's POIs.
        var mapService = new MapService { Name = "MapService" };
        AddChild(mapService);
        mapService.DiscoverRegion(CurrentRegionId);
        AddChild(new WaypointBeacon { Name = "WaypointBeacon" });

        var fastTravel = new FastTravelService { Name = "FastTravel" };
        AddChild(fastTravel);

        // 17. Property ownership, beside fast travel because claiming a holding registers it as a
        //     travel destination.
        AddChild(new Embervale.Housing.HousingService { Name = "Housing" });

        // 18. The economy's six ledgers. Each holds the one fact that cannot be derived from the
        //     day: what is left in stock, what was confiscated, what is on consignment, which
        //     contracts were filled, the day's wager allowance, who has already been haggled with.
        AddChild(new Embervale.Economy.ShopStockService { Name = "ShopStock" });
        AddChild(new Embervale.Economy.ContrabandImpound { Name = "ContrabandImpound" });
        AddChild(new Embervale.Economy.ConsignmentLedger { Name = "Consignment" });
        AddChild(new Embervale.Economy.ContractLedger { Name = "Contracts" });
        AddChild(new Embervale.Economy.WagerLedger { Name = "Wagers" });
        AddChild(new Embervale.Economy.HaggleLedger { Name = "Haggles" });
        AddChild(new Embervale.Economy.SupplyShockService { Name = "SupplyShocks" });

        // 19. Placement mode: the ghost and the commit. Not ISaveable — a placed prop persists
        //     through the spawn director above, which records template, position and yaw.
        AddChild(new Embervale.Housing.PlacementDirector { Name = "Placement" });
        AddChild(new PlacementHud());

        // 20. The map screen and the bestiary, both reading services built above.
        Ui.BuildMapScreen(mapService, fastTravel);
        var bestiary = new Embervale.Enemies.BestiaryService { Name = "Bestiary" };
        AddChild(bestiary);
        Ui.BuildBestiaryPanel(bestiary);

        // 21. Last: the streamer, which needs the region and moves the player onto its spawn.
        WorldDirector.BuildStreamer(mapService);

        Log.Info($"Session built for slot '{Slot}' in region '{CurrentRegionId}'.");
    }
}
