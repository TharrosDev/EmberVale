using System.Collections.Generic;
using Embervale.Analytics;
using Embervale.Combat;
using Embervale.Core;
using Embervale.Core.Diagnostics;
using Embervale.Core.Events;
using Embervale.Core.Services;
using Embervale.Corruption;
using Embervale.Crafting;
using Embervale.Debugging;
using Embervale.Dialogue;
using Embervale.Enemies;
using Embervale.Entities;
using Embervale.Factions;
using Embervale.Items;
using Embervale.Localization;
using Embervale.Loot;
using Embervale.Magic;
using Embervale.Npc;
using Embervale.Player;
using Embervale.Progression;
using Embervale.Quests;
using Embervale.Races;
using Embervale.Save;
using Embervale.Settings;
using Embervale.Stats;
using Embervale.UI;
using Embervale.World;
using Godot;

namespace Embervale.Bootstrap;

/// <summary>
/// Entry point attached to the root of <c>Main.tscn</c>. It assembles a small
/// playable sandbox that exercises the core systems end to end:
///   * builds a minimal 3D world (light, sky, collidable floor),
///   * spawns a third-person <see cref="PlayerFactory">player</see> that walks,
///     looks and melee-attacks,
///   * spawns a component-based training dummy whose health/death/respawn flow
///     through the <see cref="EventBus"/>,
///   * persists and restores state through the <see cref="SaveManager"/>.
///
/// This is the "playable ugly prototype" that proves the architecture runs and
/// the seam later phases (combat, AI, loot) plug into.
/// </summary>
public partial class GameBootstrap : Node3D
{
    private const string DummyAttributesPath = "res://data/attributes/DummyAttributes.tres";
    private const float RespawnDelaySeconds = 3f;
    private static readonly Vector3 PlayerSpawn = new(0f, 1.2f, 5f);

    private GameHud _gameHud = null!;
    // Developer overlays: null in a capture build (Phase 33E), so every use is null-conditional.
    private DebugHud? _hud;
    private DevConsole? _console;
    private ProfilerOverlay? _profiler;
    private InventoryPanel _inventoryPanel = null!;
    private SpellbookPanel _spellbookPanel = null!;
    private HotbarPanel _hotbarPanel = null!;
    private QuestLogPanel _questLogPanel = null!;
    private MapScreen _mapScreen = null!;
    private Housing.PlacementDirector _placement = null!;
    private WorldClock _clock = null!;
    private WeatherDirector _weather = null!;
    private SkyController _sky = null!;
    private PersistentSpawnDirector _persistentSpawns = null!;
    private OpeningSequence? _opening;
    private Entity? _dummy;
    private PlayerCharacter? _player;
    private MainMenu? _mainMenu;
    private bool _sandboxBuilt;

    /// <summary>Whether the dev `--play` flag has already been acted on this process.</summary>
    private bool _playFlagConsumed;
    private double _respawnCountdown = -1d;

    // Phase 26C: the active creation profile (race + identity) the player spawns from. New Game uses
    // the Human default (26D's creator will set the chosen one here); Load rebuilds it from the slot
    // header and spawns without re-granting innate perks/spells/reputation (the overlay restores them).
    private CharacterProfile _activeProfile = CharacterProfile.Human;
    private bool _applyStartingGrants = true;

    // Phase 25C hard transitions: the active streamer, the neighbour portals spawned for the current
    // region (cleared/rebuilt on transition), and a short loading-screen settle so the destination
    // cells stream in before play resumes.
    private RegionStreamer? _streamer;
    private MapService? _mapService;
    private FastTravelService? _fastTravel;
    private Housing.HousingService? _housing;
    private Economy.ShopStockService? _shopStock;
    private Economy.ContrabandImpound? _impound;
    private Economy.ConsignmentLedger? _consignment;
    private Economy.ContractLedger? _contracts;
    private Economy.WagerLedger? _wagers;
    private Economy.HaggleLedger? _haggles;
    private Economy.SupplyShockService? _shocks;
    private readonly System.Collections.Generic.List<Entity> _portals = new();
    // Post-transition settle (Phase 25.5B): time spent on the loading screen since a region load
    // began (-1 = not loading). Play resumes when the streamer reports the destination has finished
    // streaming in (no pop-in), bounded by a min show time and a safety cap so a failed cell can't hang.
    private double _loadingElapsed = -1d;
    private const double LoadingMinSeconds = 0.15d;
    private const double LoadingMaxSeconds = 3.0d;

    // The region the sandbox represents (Phase 25A). Until streaming (25B) the world is this one
    // region; the save header reads its display name from the RegionDatabase.
    private string _currentRegionId = GameIds.Regions.EmberCrown;

    public override void _Ready()
    {
        // Headless content check: `godot --headless --path . -- --validate` runs the full
        // validator and quits (non-zero on failure) without building the sandbox. Handle it
        // before anything else so the tool path is fast and side-effect free.
        if (HeadlessValidation.Requested())
        {
            HeadlessValidation.Run(GetTree());
            return;
        }

        // The same shape for the economy report (38N1): `-- --economy` prints the arbitrage table
        // and quits. Always exit 0 — it is an observation, not a gate.
        if (HeadlessEconomy.Requested())
        {
            HeadlessEconomy.Run(GetTree());
            return;
        }

        // And a content census: `-- --state` prints what is on disk and quits (agent-ergonomics
        // pass). It replaces a handful of greps at the start of a session and cannot drift from
        // reality, because it reads the same databases the game loads.
        if (HeadlessState.Requested())
        {
            HeadlessState.Run(GetTree());
            return;
        }

        Log.Info("=== Embervale bootstrapping (Phase 20: Deep Debugging) ===");

        // The bootstrap is the flow manager for the sandbox, so it must keep
        // processing input even while the tree is paused (to unpause).
        ProcessMode = ProcessModeEnum.Always;

        GameInput.EnsureActions();

        // Localization spine (Phase 24G): load the string catalogue and select the locale before any
        // UI is built, so every player-facing string resolves through Loc.T from the first frame.
        Loc.Initialize();

        ContentDatabases.InitializeAll();

        // Audio buses (Phase 31A): create the master/music/SFX/ambience/UI/voice mixer buses before the
        // settings apply below, so that first apply sets every bus volume (it skips buses not yet made).
        Embervale.Audio.AudioBusLayout.Ensure();

        // Player options (Phase 24E): load user://settings.tres (or defaults) and apply graphics +
        // audio to the engine before anything is shown, so the very first frame honours them. The
        // service is registered so the menu/pause settings panel (24F) can mutate and re-apply it.
        var settings = new SettingsService();
        settings.LoadAndApply();
        ServiceLocator.Instance?.Register(settings);

        // With every database + the enemy registry populated, validate that the authored
        // content cross-references resolve (item/enemy/quest/spell ids). Broken references
        // surface here at boot rather than silently failing mid-playthrough.
        Log.Info(ContentValidator.Run());

        // Phase 24A: boot to the title menu, not straight into the world. The sandbox is built
        // on New Game (StartNewGame), keeping the existing bootstrap path intact behind it.
        ShowMainMenu();
    }

    /// <summary>Shows the title screen and parks the game in <see cref="GameState.MainMenu"/>.</summary>
    private void ShowMainMenu()
    {
        _mainMenu = new MainMenu
        {
            NewCharacterRequested = StartNewGame,
            LoadGameRequested = StartLoadedGame,
        };
        AddChild(_mainMenu);
        GameManager.Instance?.ChangeState(GameState.MainMenu);
        Log.Info("Main menu ready. New Game to enter the world.");

        // Dev convenience (parallels --validate): launching with `-- --play` boots straight into the
        // most recent save, so gameplay — and the systems that only init on world build (audio
        // directors, spawners) — can be launched deterministically from the command line / MCP:
        //   godot --path . -- --play
        // ⚠️ Consumed once per process. `ShowMainMenu` runs again every time the player quits to the
        // title (37.5H), and without this latch the dev flag fires on that return too — dropping
        // them straight back into the save they just left, which looks exactly like "return to main
        // menu just reloads the world". Only reachable from a `--play` launch, which is how it hid.
        // `--hudshots` implies `--play`: the harness needs a live session with a real player in it,
        // and boots the same way. See HudShots for why this exists at all — the MCP drives the
        // editor, where the HUD has not been constructed yet, so nothing else here can see it.
        bool hudShots = HasCmdFlag("--hudshots");
        bool panelShots = HasCmdFlag("--panelshots");
        bool shrineShots = HasCmdFlag("--shrine-shots");
        if (!_playFlagConsumed && (hudShots || panelShots || shrineShots || HasCmdFlag("--play")) &&
            MostRecentSlot() is { } slot)
        {
            _playFlagConsumed = true;
            string mode = hudShots ? "--hudshots" : panelShots ? "--panelshots" : shrineShots ? "--shrine-shots" : "--play";
            Log.Info($"{mode}: continuing most recent save '{slot}'.");
            StartLoadedGame(slot);

            // Synchronous viewport readback and PNG compression dominate capture frames. Keep those
            // tool costs out of the world's sustained-performance telemetry; ordinary --play runs
            // continue to sample the exact same budgets.
            if (hudShots || panelShots || shrineShots)
            {
                _streamer?.SetPerformanceSamplingEnabled(false);
            }

            if (hudShots)
            {
                AddChild(new Debugging.HudShots { Name = "HudShots" });
            }

            if (panelShots)
            {
                AddChild(new Debugging.PanelShots
                {
                    Name = "PanelShots",
                    Map = _mapScreen,
                    Journal = _questLogPanel,
                });
            }

            if (shrineShots)
            {
                AddChild(new Debugging.ShrineShots { Name = "ShrineShots" });
            }
        }
    }

    /// <summary>True if <paramref name="flag"/> was passed after <c>--</c> or as a raw engine arg.</summary>
    private static bool HasCmdFlag(string flag)
    {
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg == flag)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The slot of the most recently saved game, or null if there are no saves.</summary>
    private static string? MostRecentSlot()
    {
        if (SaveManager.Instance is not { } manager)
        {
            return null;
        }

        SaveSlotInfo? latest = null;
        foreach (SaveSlotInfo info in manager.ListSlots())
        {
            if (latest == null || info.TimestampUnix > latest.TimestampUnix)
            {
                latest = info;
            }
        }

        return latest?.Slot;
    }

    /// <summary>Starts a fresh game into <paramref name="slot"/> (Phase 24C): builds the world and
    /// enters <see cref="GameState.Playing"/> with a clean playtime. Invoked by the slot browser.</summary>
    private void StartNewGame(string slot, CharacterProfile profile)
    {
        if (!BeginSession(slot))
        {
            return;
        }

        // New character (Phase 26D): spawn from the creator's chosen profile and grant the race's
        // innate perks/spells/reputation.
        _activeProfile = profile;
        _applyStartingGrants = true;

        SaveManager.Instance?.ResetPlaytime();
        BuildWorld();
        GameManager.Instance?.ChangeState(GameState.Playing);

        // The prologue (Phase 33A) plays over the already-built world, so creation flows into the
        // narration and the narration lifts on the Ember Crown with nothing left to load. It holds
        // player input until it ends; a load skips it entirely.
        _opening?.Play(_activeProfile);
        Log.Info($"New game started in slot '{slot}'. Prologue playing; the Ember Crown is built behind it.");
    }

    /// <summary>Loads an existing save into a freshly-built world (Phase 24C): builds the sandbox,
    /// then overlays the slot's saved state onto the registered saveables (the F9 path right after a
    /// fresh build), continuing that save's playtime.</summary>
    private void StartLoadedGame(string slot)
    {
        if (!BeginSession(slot))
        {
            return;
        }

        // Restore the saved character before building: the race must be known at spawn (BuildWorld →
        // PlayerFactory.Create) so its stat deltas apply; the innate perks/spells/reputation come back
        // via the LoadGame overlay below, so don't re-grant them here (Phase 26C).
        if (SaveManager.Instance?.ReadHeader(slot) is { } header)
        {
            _activeProfile = CharacterProfile.FromHeaderFields(new Dictionary<string, string>
            {
                ["race_id"] = header.RaceId,
                ["char_name"] = header.CharacterName,
            });

            // Restore the saved region BEFORE BuildWorld so the streamer/portals/safe-zone/map all
            // configure for it; the player transform is applied after the world + load overlay land.
            if (!string.IsNullOrEmpty(header.RegionId) && RegionDatabase.Get(header.RegionId) != null)
            {
                _currentRegionId = header.RegionId;
            }
        }

        _applyStartingGrants = false;

        BuildWorld();

        // LoadGame calls ApplySavedLocation at the end of its overlay, which returns the player to
        // where they saved (BuildWorld spawned them at the region's start tile). The region was
        // already switched above, so that call finds it current and only writes the transform.
        if (SaveManager.Instance?.LoadGame(slot) == false)
        {
            AbortToTitle($"Save slot '{slot}' failed to restore; returning to the title screen.");
            return;
        }

        GameManager.Instance?.ChangeState(GameState.Playing);
        Log.Info($"Loaded game from slot '{slot}' as {_activeProfile.CharacterName} ({_activeProfile.RaceId}). Sandbox ready.");
    }

    /// <summary>Common entry for the New/Load paths: guards single-build, tears down the menu, makes
    /// the chosen slot active, and wires the save-header provider. Returns false if already built.</summary>
    private bool BeginSession(string slot)
    {
        if (_sandboxBuilt)
        {
            return false;
        }

        _sandboxBuilt = true;
        _mainMenu?.QueueFree();
        _mainMenu = null;

        if (SaveManager.Instance != null)
        {
            // Subsequent quick/manual saves target this slot, and headers are stamped from live
            // gameplay state via the provider (Phase 24B) without coupling the manager to gameplay.
            SaveManager.Instance.ActiveSlot = slot;
            SaveManager.Instance.HeaderProvider = BuildSaveHeader;
            SaveManager.Instance.LocationApplier = ApplySavedLocation;
        }

        return true;
    }

    /// <summary>Assembles the playable sandbox (no state transition). Shared by the New and Load
    /// session paths.</summary>
    private void BuildWorld()
    {
        // Sun, sky, tonemap and ground. The two handles come back rather than living in fields:
        // the SkyController below is their only other reader, and it is in this same method.
        WorldEnvironmentBuilder.Result env = WorldEnvironmentBuilder.Build(this);

        // The purpose-built game HUD is the default overlay; the DebugHud is now a
        // developer panel toggled with F3. Toasts and the pause menu round out the game UI.
        _gameHud = new GameHud();
        AddChild(_gameHud);

        // The DebugHud is a developer overlay (F3), not part of the game's UI — a capture build
        // never builds it (Phase 33E).
        if (BuildProfile.ShowDeveloperTools)
        {
            _hud = new DebugHud();
            AddChild(_hud);
        }
        AddChild(new Notifications());
        AddChild(new CombatFeedbackOverlay()); // Phase 29D: crit/block/stagger/parry screen flash
        AddChild(new PauseMenu());
        AddChild(new LoadingScreen());

        // The new-game prologue (33A). Built with the rest of the shell so it can play the moment
        // the world is assembled; it stays hidden unless StartNewGame asks for it.
        _opening = new OpeningSequence();
        AddChild(_opening);
        ServiceLocator.Instance?.Register(_opening);

        // The slice's ending (33D): the director watches for the player leaving through the
        // Frostfang door after the Iron King has fallen, and the closing card answers it.
        AddChild(new Embervale.Narrative.SliceDirector { Name = "Slice" });
        AddChild(new ClosingSequence());

        // Deep-debugging tools (Phase 20): dev console (F1), profiler (F4), and a standing
        // world-integrity checker that periodically validates runtime invariants. All three are
        // developer surfaces; a capture build ships without them (Phase 33E). The integrity checker
        // goes too — it exists to shout at the developer, and it costs a scan every five seconds.
        if (BuildProfile.ShowDeveloperTools)
        {
            _console = new DevConsole();
            AddChild(_console);
            _profiler = new ProfilerOverlay();
            AddChild(_profiler);
            AddChild(new WorldIntegrityChecker());
        }

        // Autosave cadence (Phase 24D) on top of the slot system: rotates through auto1..auto3 on a
        // timer / quest-completion / level-up, never touching the player's manual slot. Registered so
        // the `autosave` dev command can reach it.
        var autosave = new AutosaveService { Name = "Autosave" };
        AddChild(autosave);
        ServiceLocator.Instance?.Register(autosave);

        // Dev-only telemetry: logs deaths/quests/level-ups to user://analytics/ for later
        // balance/QA. A no-op in retail builds (gated on OS.IsDebugBuild). Added before the
        // player/quest spawn below so it captures the seeded starter quest.
        AddChild(new AnalyticsSink());
        _inventoryPanel = new InventoryPanel();
        AddChild(_inventoryPanel);

        _spellbookPanel = new SpellbookPanel();
        AddChild(_spellbookPanel);
        _hotbarPanel = new HotbarPanel { Dock = _gameHud.BottomDock };
        AddChild(_hotbarPanel);
        _questLogPanel = new QuestLogPanel();
        AddChild(_questLogPanel);
        // ⚠️ The six panels below are added WITHOUT a field, and that is deliberate. Each is
        // event-driven — one instance answering every merchant, container, appraiser or board in the
        // world — so once it is in the tree nothing ever calls back into it. They used to be stored
        // in fields that were assigned and never read again: object state describing nothing.
        // A new panel earns a field when something reads it, not because its neighbours have one.
        AddChild(new DialoguePanel());
        AddChild(new CraftingPanel());
        // The stash window (37B) — event-driven like the crafting panel, one instance for every
        // container in the world.
        AddChild(new StoragePanel());
        // The shop window (38A) — same one-instance-for-every-merchant shape as the two above.
        AddChild(new VendorPanel());
        // The appraiser's window (38P2) — the first panel that only reads. Same one instance for
        // every appraiser, answered off an event, so the service knows nothing about the UI.
        AddChild(new AppraisalPanel());
        // The caravan board (38Q2) — one instance for every board, answered off an event. It reads the
        // clock rather than a snapshot, so what it shows cannot go stale while it is open.
        AddChild(new ContractBoardPanel());

        // The world clock drives NPC routines; create it before the NPCs below so it is
        // registered in the ServiceLocator when their schedules first read the time.
        _clock = new WorldClock { Name = "WorldClock" };
        AddChild(_clock);
        _hud?.SetClock(_clock);
        _gameHud.SetClock(_clock);

        // Weather before the sky so the SkyController can read the active state on its
        // first frame; the sky drives the (already-built) sun + environment.
        _weather = new WeatherDirector { Name = "Weather" };
        AddChild(_weather);
        _hud?.SetWeather(_weather);
        _gameHud.SetWeather(_weather);

        _sky = new SkyController { Name = "Sky", Sun = env.Sun, Environment = env.Environment };
        AddChild(_sky);

        // Persistent spawned actors: a director that recreates saved named actors/containers on
        // load (the SaveManager alone only restores components of actors already in the scene).
        PersistentActorRegistry.Clear();
        PersistentActorRegistry.Register(GameIds.Templates.Cache, BuildPersistentCache);
        // Everything the player can set down in a holding (37C). Registered here so a placed prop
        // rebuilds itself on load through exactly the same path a saved container does.
        Housing.PlaceableTemplates.RegisterAll();
        _persistentSpawns = new PersistentSpawnDirector { Name = "PersistentSpawns" };
        AddChild(_persistentSpawns);

        SubscribeEvents();

        // Sandbox test props (Phase 33E): the training dummy, the debug goblin camp, the loose loot
        // pile and the spell tome on its plinth were how the systems got exercised before there was
        // content. They are still the fastest way to do that — but a stranger playing the slice must
        // never see a training dummy in the town square, so a capture build places none of them.
        if (BuildProfile.SpawnSandboxContent)
        {
            SpawnDummy();
        }

        SpawnPlayer();

        if (BuildProfile.SpawnSandboxContent)
        {
            SandboxProps.Seed(this);
        }

        SpawnEncounterDirector();
        SpawnPersistentActors();

        // Onboarding (Phase 33B): watches the player perform the basic verbs and shows one hint at a
        // time. Never blocks input; honours the Settings toggle; persists so a reload doesn't
        // re-teach. Added after the player spawn so its first hint has someone to watch.
        AddChild(new Embervale.Onboarding.TutorialDirector { Name = "Tutorial" });

        // Companions (Phase 32A): the party roster. Added after the player spawn so a recruit can be
        // placed in formation immediately, and before the load overlay lands so a saved party
        // reconciles itself back into the world. (The archetypes are seeded in ContentDatabases.)
        AddChild(new Embervale.Companions.CompanionRoster { Name = "Companions" });

        // Boss fight flow beats (Phase 28C): intro lock on summon, slow-mo on the boss's defeat. The
        // GameHud reacts to the same events for the healthbar/title/defeat banner.
        AddChild(new Embervale.Enemies.BossEncounterDirector { Name = "BossEncounter" });

        // Hit-stop (Phase 29A): a brief freeze-frame on landed hits so blows read with weight.
        AddChild(new Embervale.Combat.HitStopDirector { Name = "HitStop" });

        // Combat feedback (Phase 29C): pooled impact sparks + sound-cue hooks on every hit.
        AddChild(new Embervale.Combat.CombatFeedbackDirector { Name = "CombatFeedback" });

        // Audio (Phase 31A): the AudioDirector consumes the sound/music cue events combat & bosses
        // already publish, playing them on the mixer buses. Registered so any system can request cues.
        var audio = new Embervale.Audio.AudioDirector { Name = "Audio" };
        AddChild(audio);
        ServiceLocator.Instance?.Register(audio);

        // Adaptive music (Phase 31B): explore/safe/combat/boss music state machine, crossfading on the
        // Music bus. Added after Audio so it reuses the shared AudioLibrary the AudioDirector registers.
        AddChild(new Embervale.Audio.MusicDirector { Name = "Music" });

        // Environmental ambience (Phase 31D): weather/locale/time looping bed on the Ambience bus.
        AddChild(new Embervale.Audio.AmbienceDirector { Name = "Ambience" });

        // Streamed-cell persistence (Phase 25D): remembers per-actor state across cell unload/reload
        // (dead enemies stay dead, looted pickups stay gone). Added before the streamer so it is
        // subscribed to the cell load/unload events before the first cell streams in.
        AddChild(new CellPersistenceDirector { Name = "CellPersistence" });

        // World-map discovery (Phase 25E): tracks visited regions/POIs and persists them. Created
        // before the streamer so it catches the first cell-load POIs; the map screen reads it.
        _mapService = new MapService { Name = "MapService" };
        AddChild(_mapService);
        _mapService.DiscoverRegion(_currentRegionId); // the starting region is known immediately

        // The map waypoint, standing in the world (39.5A). A mark you can only see by opening the
        // map is a mark you have to keep opening the map to follow.
        AddChild(new WaypointBeacon { Name = "WaypointBeacon" });

        // Fast-travel network (Phase 25G): the set of attuned travel nodes; persists, read by the map.
        _fastTravel = new FastTravelService { Name = "FastTravel" };
        AddChild(_fastTravel);

        // Property ownership (Phase 37A): what the player holds, persisted. Built beside fast travel
        // because claiming a holding registers it as a travel destination.
        _housing = new Housing.HousingService { Name = "Housing" };
        AddChild(_housing);

        // Shop stock (38B): remaining counts, restock stamps and the wares a leveled pool rolled.
        // ShopResource is shared and not ISaveable, so this is where depletion can both live and
        // persist. Restock is lazy-on-open, so there is nothing here that ticks.
        _shopStock = new Economy.ShopStockService { Name = "ShopStock" };
        AddChild(_shopStock);

        // Confiscated contraband (38O): what the Crossway wardens have taken and not yet given back.
        // Its own node rather than a field on the shop service, because it is the player's property in
        // someone else's keeping — nothing about it is a shop, and it outlives every shop it touches.
        _impound = new Economy.ContrabandImpound { Name = "ContrabandImpound" };
        AddChild(_impound);

        // Consignment listings (38P): what the player has left with a broker and what it has earned.
        // Beside the impound and not inside the shop service for the same reason that one is out: the
        // goods are the player's, held by someone else, and the listing outlives every visit to the shop.
        _consignment = new Economy.ConsignmentLedger { Name = "Consignment" };
        AddChild(_consignment);

        // Filled supply contracts (38Q2). Beside the two above and for a narrower reason: the board
        // itself is derived from the day and needs no storage at all, so the only thing here is the
        // record of what the player has already delivered — which is also the only thing stopping a
        // posting being filled twice in one rotation.
        _contracts = new Economy.ContractLedger { Name = "Contracts" };
        AddChild(_contracts);

        // Throws taken at the gambling tables (38R2). The fourth of these and the one holding the most
        // weight for its size: the outcome of a throw is derived from the day, so nothing about the
        // result needs storing — but the day's allowance is the ONLY bound on a house that pays out,
        // and it is this node.
        _wagers = new Economy.WagerLedger { Name = "Wagers" };
        AddChild(_wagers);

        // Merchants already talked down today (38S). The fifth, and the same division of labour as the
        // fourth: the outcome is a function of the day and needs no storage, while the one attempt a day
        // is the only thing stopping the player asking until the answer changes.
        _haggles = new Economy.HaggleLedger { Name = "Haggles" };
        AddChild(_haggles);

        // Supply shocks (38T). The sixth, and the first whose state is not a record of what the player
        // did to a price but of what happened to one: the roll is a pure function of the day, so nothing
        // here can be rerolled, but a shortage the player has hauled goods into ends early and no clock
        // can derive that. It is a node rather than something inside WorldEventDirector for the reason
        // the gate names — that director is not ISaveable, so a shock in it would end at every reload.
        _shocks = new Economy.SupplyShockService { Name = "SupplyShocks" };
        AddChild(_shocks);

        // Placement mode (37C): the ghost and the commit. Not ISaveable — a placed prop persists
        // through the PersistentSpawnDirector above, which already records template, position and yaw.
        _placement = new Housing.PlacementDirector { Name = "Placement" };
        AddChild(_placement);
        AddChild(new PlacementHud());

        // Held in a field since 39.5C so `--panelshots` can open and drive it — the map screen is
        // where all three of 39.5A's shipped defects lived, and nothing here could photograph it.
        _mapScreen = new MapScreen();
        AddChild(_mapScreen);
        _mapScreen.SetMapService(_mapService);
        _mapScreen.SetFastTravel(_fastTravel);

        // The Ash Hunters' field journal (Phase 34G): counts every creature the party puts down and
        // persists it. Service-backed like the map — it documents the world, not the player.
        var bestiary = new BestiaryService { Name = "Bestiary" };
        AddChild(bestiary);

        var bestiaryPanel = new BestiaryPanel();
        AddChild(bestiaryPanel);
        bestiaryPanel.SetBestiary(bestiary);

        SpawnRegionStreamer();
    }

    /// <summary>Streams the active region's sub-cells around the player (Phase 25B). The procedural
    /// sandbox stays the always-loaded base; the streamer manages the region's authored cells.</summary>
    private void SpawnRegionStreamer()
    {
        _streamer = new RegionStreamer { Name = "RegionStreamer" };
        RegionResource? region = RegionDatabase.Get(_currentRegionId);
        _streamer.Configure(region);
        AddChild(_streamer);
        ServiceLocator.Instance?.Register(_streamer);
        RegionSetup.RebuildPortals(this, _portals, region);
        RegionSetup.ApplySafeZones(region);
        Weave.Set(region?.WeavePotency ?? Weave.DefaultPotency);
    }


    /// <summary>Performs a hard region-to-region load (Phase 25C): show the loading screen, unload the
    /// current region's cells, re-target the streamer, teleport the player to the destination spawn,
    /// rebuild its portals, autosave the boundary, then settle for a few frames so the new cells stream
    /// in before play resumes.</summary>
    private void OnRegionTransitionRequested(RegionTransitionRequestedEvent e)
    {
        RegionResource? destination = RegionDatabase.Get(e.RegionId);
        if (destination == null || _player == null || _streamer == null)
        {
            Log.Warn($"Region transition to '{e.RegionId}' aborted (unknown region or world not built).");
            return;
        }

        if (e.RegionId == _currentRegionId)
        {
            return;
        }

        if (!RegionSetup.PayToll(_player, destination))
        {
            return;
        }

        PerformRegionLoad(destination, destination.SpawnPoint, $"Entering {destination.DisplayName}...");
    }


    /// <summary>Fast-travels to a discovered <see cref="FastTravelService"/> node (Phase 25G): resolves
    /// the node and reuses the 25C hard-load path, but lands the player at the node's exact position
    /// (and allows same-region jumps, unlike a neighbour portal).</summary>
    private void OnFastTravelRequested(FastTravelRequestedEvent e)
    {
        if (ServiceLocator.Instance is not { } locator || !locator.TryGet(out FastTravelService travel) ||
            !travel.TryGetNode(e.NodeId, out TravelNode node))
        {
            Log.Warn($"Fast travel to '{e.NodeId}' aborted (unknown node).");
            return;
        }

        RegionResource? destination = RegionDatabase.Get(node.RegionId);
        if (destination == null || _player == null || _streamer == null)
        {
            Log.Warn($"Fast travel to '{e.NodeId}' aborted (unknown region or world not built).");
            return;
        }

        // The fee (38C) is charged here rather than at the map screen because this is where the map
        // button and the `travel goto` console command converge — gating either one alone would leave
        // the other a free ride. Fails closed: no gold, no jump.
        int fee = Economy.TravelCosts.FeeFor(node, _streamer.ActiveRegionId);
        if (fee > 0 &&
            (_player.GetComponent<InventoryComponent>() is not { } purse ||
                !purse.RemoveItem(GameIds.Currency.Gold, fee)))
        {
            Log.Warn($"Fast travel to '{e.NodeId}' refused: {fee} gold required.");
            return;
        }

        PerformRegionLoad(destination, node.Position, $"Fast travelling to {node.Label}...");
    }

    /// <summary>The shared hard-load (Phase 25C/25G): show the loading screen, swap the streamer to the
    /// destination region (only when it actually changes), teleport the player to <paramref name="landing"/>,
    /// autosave the boundary, then settle for a few frames so the new cells stream in before play resumes.
    /// The world clock and weather are untouched, so arrival respects the current time/weather.</summary>
    /// <param name="autosave">False when the move is itself a restore (see
    /// <see cref="ApplySavedLocation"/>): autosaving the state we just read back is pure churn, and
    /// on the autosave ring it would overwrite an older save with a copy of the one being loaded.</param>
    private void PerformRegionLoad(RegionResource destination, Vector3 landing, string message, bool autosave = true)
    {
        GameManager.Instance?.ChangeState(GameState.Loading);

        if (destination.Id != _currentRegionId)
        {
            _streamer!.UnloadAll();
            _currentRegionId = destination.Id;
            _streamer.Configure(destination);
            _mapService?.DiscoverRegion(destination.Id); // entering reveals it on the map (Phase 25E)
            RegionSetup.RebuildPortals(this, _portals, destination);
            RegionSetup.ApplySafeZones(destination);
            Weave.Set(destination.WeavePotency);
        }

        _player!.Velocity = Vector3.Zero;
        // After Configure, so WorldGround is the DESTINATION region's field rather than the one the
        // player is leaving — a cross-region jump clamped against the wrong heightfield is worse
        // than not clamping at all.
        _player.GlobalPosition = SafeLanding(landing);

        // The band comes with you (Phase 32D). Walking companions across a region boundary is not a
        // thing they can do, so they are cut to formation the moment the player lands.
        if (ServiceLocator.Instance != null &&
            ServiceLocator.Instance.TryGet(out Embervale.Companions.CompanionRoster party))
        {
            party.RegroupNow();
        }

        if (autosave && ServiceLocator.Instance != null && ServiceLocator.Instance.TryGet(out AutosaveService autosaveService))
        {
            autosaveService.RequestRegionChangeAutosave();
        }

        // Hold the loading screen until the streamer reports the destination settled (Phase 25.5B);
        // resumes in _Process. Replaces the old fixed delay so big regions don't pop in after the
        // screen clears, and small ones don't wait needlessly.
        _loadingElapsed = 0d;
        Log.Info(message);
    }

    /// <summary>
    /// Returns the player to the transform and region a save was written at. Wired into
    /// <see cref="SaveManager.LocationApplier"/> by <see cref="BeginSession"/>, so it runs at the
    /// end of <b>every</b> load — the slot browser, F9, and the pause menu — rather than only the
    /// route that happened to implement it.
    ///
    /// ⚠️ <b>A cross-region load is a hard load, not a teleport.</b> Writing the transform alone
    /// would drop the player into a region whose cells, portals, safe zones and Weave potency are
    /// still configured for the one they were standing in.
    /// </summary>
    private void ApplySavedLocation(SaveSlotInfo header)
    {
        if (_player == null)
        {
            return;
        }

        Vector3 landing = SafeLanding(new Vector3(header.PlayerX, header.PlayerY, header.PlayerZ));

        // Cross-region: reuse the streamer swap + loading-screen settle the portal path already uses.
        // StartLoadedGame set _currentRegionId from this same header before BuildWorld, so a load
        // from the slot browser never takes this branch — only a mid-session F9/pause load can.
        if (!string.IsNullOrEmpty(header.RegionId) && header.RegionId != _currentRegionId &&
            RegionDatabase.Get(header.RegionId) is { } destination)
        {
            PerformRegionLoad(destination, landing, $"Loaded into {destination.DisplayName} from a save.", autosave: false);
        }
        else
        {
            _player.Velocity = Vector3.Zero;
            _player.GlobalPosition = landing;
        }

        _player.Rotation = new Vector3(_player.Rotation.X, header.PlayerYaw, _player.Rotation.Z);
    }

    /// <summary>
    /// ⚠️ <b>NEVER PUT THE PLAYER UNDER THE GROUND (the 2026-08-29 geography overhaul).</b> Every
    /// teleport in the game — a portal, a fast-travel jump, a save restore, the <c>region</c> dev
    /// command — writes an absolute Y that was safe for exactly as long as every cell floor's top
    /// face was y = 0. The world has real elevation now, so a point that has drifted below the
    /// surface leaves the player inside a hillside with no way out and no error anywhere.
    ///
    /// The v1 -> v2 save migration throws pre-overhaul transforms away, but this covers the routes
    /// a migration cannot: a hand-edited save, a fast-travel point recorded before a cell was
    /// re-cut, a landform edit under someone's autosave. It lifts and never lowers, so a legitimate
    /// jump onto a terrace or a rooftop is untouched.
    /// </summary>
    private static Vector3 SafeLanding(Vector3 landing)
    {
        float ground = WorldGround.HeightAt(landing.X, landing.Z);
        if (landing.Y >= ground + 0.05f)
        {
            return landing;
        }

        Log.Warn($"Landing {landing} is below the ground at {ground:F2} m; lifting the player onto it.");
        return new Vector3(landing.X, ground + 0.1f, landing.Z);
    }

    /// <summary>
    /// Leaves a session that cannot be trusted. Reached only when <see cref="SaveManager.LoadGame"/>
    /// reports a partial restore: continuing would hand the player a world assembled from some of
    /// the save and some of whatever was already live, and the next autosave would write that over
    /// the good file. Mirrors the pause menu's quit-to-title, which is the only teardown this build
    /// has — the world cannot be dismantled in place (see <c>_sandboxBuilt</c>).
    /// </summary>
    private void AbortToTitle(string reason)
    {
        Log.Error(reason);

        UiState.ClearAll();
        Godot.Input.MouseMode = Godot.Input.MouseModeEnum.Visible;
        GameManager.Instance?.ChangeState(GameState.MainMenu);

        Callable.From(() => GetTree().ReloadCurrentScene()).CallDeferred();
    }

    public override void _ExitTree()
    {
        UnsubscribeEvents();

        // Safety net for scene reloads: every gameplay node unsubscribes in its own
        // OnTeardown, but if any leaked a handler it would keep the freed object alive and
        // fire with stale state on the next load. The autoloads (EventBus/ServiceLocator/
        // GameManager/SaveManager) never subscribe, so clearing here is safe.
        int leaked = EventBus.Instance?.TotalSubscriberCount() ?? 0;
        if (leaked > 0)
        {
            Log.Warn($"{leaked} event handler(s) survived scene teardown; clearing as a safety net (check OnTeardown unsubscribes).");
        }

        EventBus.Instance?.Clear();
    }

    public override void _Process(double delta)
    {
        if (_respawnCountdown > 0d)
        {
            _respawnCountdown -= delta;
            if (_respawnCountdown <= 0d)
            {
                _respawnCountdown = -1d;
                SpawnDummy();
            }
        }

        // Hard region transition (Phase 25C/25.5B): hold GameState.Loading until the streamer has
        // finished streaming the destination in around the player (no pop-in), bounded by a minimum
        // show time and a safety cap so a cell that fails to load can never hang the loading screen.
        if (_loadingElapsed >= 0d)
        {
            _loadingElapsed += delta;
            bool settled = _streamer == null || _streamer.IsSettled();
            if ((settled && _loadingElapsed >= LoadingMinSeconds) || _loadingElapsed >= LoadingMaxSeconds)
            {
                _loadingElapsed = -1d;
                GameManager.Instance?.ChangeState(GameState.Playing);
            }
        }
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        // The debug/save shortcuts below reach into world objects only created by StartNewGame,
        // so they do nothing while the title menu is up (Phase 24A).
        if (!_sandboxBuilt)
        {
            return;
        }

        if (@event is not InputEventKey { Pressed: true, Echo: false } key)
        {
            return;
        }

        // Quick save/load stay in every build — they are player conveniences. The cheats below them
        // are developer affordances and a capture build must not respond to a stray H or X (33E).
        if (!BuildProfile.ShowDeveloperTools &&
            key.Keycode is not (Key.F5 or Key.F9 or Key.Escape))
        {
            return;
        }

        switch (key.Keycode)
        {
            case Key.H:
                HealTarget(20f);
                break;
            case Key.R:
                ForceRespawn();
                break;
            case Key.X:
                if (_player?.GetComponent<ProgressionComponent>() is { } prog)
                {
                    prog.AddXp(prog.XpToNext - prog.CurrentXp); // debug: one full level
                }

                break;
            case Key.P:
                _player?.GetComponent<CorruptionComponent>()?.Add(10);
                break;
            case Key.K:
                AdjustGoblinReputation();
                break;
            case Key.F5:
                if (SaveManager.Instance is { } saver) { saver.SaveGame(saver.ActiveSlot); }
                break;
            case Key.F9:
                if (SaveManager.Instance is { } loader && !loader.LoadGame(loader.ActiveSlot))
                {
                    AbortToTitle($"Quickload of slot '{loader.ActiveSlot}' failed; returning to the title screen.");
                }
                break;
            case Key.F1:
                _console?.Toggle();
                break;
            case Key.F3:
                _hud?.Toggle();
                break;
            case Key.F4:
                _profiler?.Toggle();
                break;
            // Esc is owned by the PauseMenu (it opens the pause menu and pauses the game).
        }
    }

    // --- Scene assembly -----------------------------------------------------

    private void SpawnEncounterDirector()
    {
        AddChild(new EncounterDirector { Name = "Encounters" });
        Log.Info("Encounter director online — patrols by day, warbands by night, more in storms.");

        var events = new WorldEventDirector { Name = "WorldEvents" };
        AddChild(events);
        _hud?.SetWorldEvents(events);
        _gameHud.SetWorldEvents(events);
        Log.Info("World-event director online — raids, caches and champion hunts with rewards.");
    }

    private void SpawnDummy()
    {
        DespawnDummy();

        AttributeSet attributes = GD.Load<AttributeSet>(DummyAttributesPath) ?? AttributeSet.CreateDefault();

        var dummy = new Entity
        {
            DisplayName = "Training Dummy",
            TemplateId = "debug.training_dummy",
            Position = new Vector3(0f, 1f, 0f),
        };

        var stats = new StatsComponent { Name = "Stats", Attributes = attributes };
        dummy.AddChild(stats);

        // Team 2: an independent target both the player and enemies can strike.
        dummy.AddChild(new CombatComponent { Name = "Combat", Team = 2 });

        // So spell DoTs/slows can be observed landing on the practice target.
        dummy.AddChild(new StatusEffectsComponent { Name = "StatusEffects" });
        dummy.AddChild(new Magic.StatusEffectVfxComponent { Name = "StatusVfx" });

        // 30J: the wooden training-dummy model (origin at feet; the dummy entity's origin is
        // its capsule CENTRE, so the visual sits 1 m down). Capsule fallback if unimported.
        if (GD.Load<PackedScene>("res://assets/models/props/prp_training_dummy.glb")?.Instantiate() is Node3D dummyVisual)
        {
            dummyVisual.Name = "Mesh";
            dummyVisual.Position = new Vector3(0f, -1f, 0f);
            dummy.AddChild(dummyVisual);
        }
        else
        {
            dummy.AddChild(new MeshInstance3D
            {
                Name = "Mesh",
                Mesh = new CapsuleMesh { Radius = 0.4f, Height = 1.8f },
                MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.70f, 0.30f, 0.28f) },
            });
        }

        // Solid collider so the player cannot walk through the dummy. The dummy's
        // origin is at its capsule centre (it is spawned at y=1), so shapes are
        // centred at the local origin to line up with the mesh.
        var collider = new StaticBody3D { Name = "Collider" };
        collider.AddChild(new CollisionShape3D
        {
            Shape = new CapsuleShape3D { Radius = 0.4f, Height = 1.8f },
        });
        dummy.AddChild(collider);

        // Hurtbox so melee hitboxes can deliver damage to the dummy.
        var hurtbox = new Hurtbox { Name = "Hurtbox" };
        hurtbox.AddChild(new CollisionShape3D
        {
            Shape = new CapsuleShape3D { Radius = 0.4f, Height = 1.8f },
        });
        dummy.AddChild(hurtbox);

        AddChild(dummy);

        // Demonstrate the modifier pipeline: a "blessing" raises max health 20%.
        stats.GetStat(StatType.Health).AddModifier(
            new StatModifier(0.20f, ModifierType.PercentAdd, "Blessing of Vigor"));
        stats.RefillResources();

        _dummy = dummy;
        ServiceLocator.Instance?.Register(dummy);
        _hud?.SetTarget(dummy);

        Log.Info($"Spawned '{dummy.DisplayName}' — max health {stats.GetValue(StatType.Health):0} (base 100 +20% blessing).");
    }

    private void SpawnPlayer()
    {
        _player = PlayerFactory.Create(PlayerSpawn, _activeProfile, _applyStartingGrants);
        AddChild(_player);
        ServiceLocator.Instance?.Register(_player);
        _hud?.SetPlayer(_player);
        _gameHud.SetPlayer(_player);
        _inventoryPanel.SetInventory(_player.GetComponent<InventoryComponent>());
        _inventoryPanel.SetEquipment(_player.GetComponent<EquipmentComponent>());
        _inventoryPanel.SetHotbar(_player.GetComponent<HotbarComponent>());
        _hotbarPanel.SetHotbar(_player.GetComponent<HotbarComponent>());
        _hotbarPanel.SetInventory(_player.GetComponent<InventoryComponent>());
        _inventoryPanel.SetProgression(_player.GetComponent<ProgressionComponent>());
        _inventoryPanel.SetPerks(_player.GetComponent<PerksComponent>());
        _spellbookPanel.SetSpellcasting(_player.GetComponent<SpellcastingComponent>());
        _spellbookPanel.SetProgression(_player.GetComponent<ProgressionComponent>());
        _inventoryPanel.SetReputation(_player.GetComponent<ReputationComponent>());
        _inventoryPanel.SetCorruption(_player.GetComponent<CorruptionComponent>());
        _inventoryPanel.SetStats(_player.GetComponent<StatsComponent>());

        QuestLogComponent? questLog = _player.GetComponent<QuestLogComponent>();
        _questLogPanel.SetQuestLog(questLog);

        // No seeded quest (Phase 33D). The sandbox used to auto-start `quest.cull_goblins` so the
        // journal had content on Play; that quest was deleted outright in 41A, having sat unstartable
        // ever since. In the slice the first quest is the guild board's bounty,
        // earned by walking into town and talking to someone. A journal that is already full before
        // the player has done anything undercuts the whole opening.

        Log.Info($"Spawned player at {_player.Position}. Facing the training dummy.");
    }

    private void SpawnPersistentActors()
    {
        // A persistent supply cache: it is recreated on load (existence + transform) and its
        // InventoryComponent restores its contents — proving the spawned-actor persistence path.
        _persistentSpawns.Spawn(GameIds.Templates.Cache, "cache.world.start", new Vector3(5f, 0f, 0f));
        Log.Info("A persistent supply cache sits east of spawn; it survives save/load (try F5, despawn it, F9).");
    }

    /// <summary>Builds a persistent storage cache prop (registered as the "prop.cache" template).</summary>
    private static Node3D BuildPersistentCache(Vector3 position)
    {
        var cache = new Entity
        {
            Name = "PersistentCache",
            DisplayName = "Supply Cache",
            Position = position,
        };

        // 30J: the banded cache-chest model (origin at feet), box fallback if unimported.
        if (GD.Load<PackedScene>("res://assets/models/props/prp_cache_chest.glb")?.Instantiate() is Node3D chestVisual)
        {
            chestVisual.Name = "Mesh";
            cache.AddChild(chestVisual);
        }
        else
        {
            cache.AddChild(new MeshInstance3D
            {
                Name = "Mesh",
                Mesh = new BoxMesh { Size = new Vector3(0.8f, 0.8f, 0.8f) },
                Position = new Vector3(0f, 0.4f, 0f),
                MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.55f, 0.43f, 0.20f) },
            });
        }

        var collider = new StaticBody3D { Name = "Collider" };
        collider.AddChild(new CollisionShape3D
        {
            Shape = new BoxShape3D { Size = new Vector3(0.8f, 0.8f, 0.8f) },
            Position = new Vector3(0f, 0.4f, 0f),
        });
        cache.AddChild(collider);

        // A persistent container's contents round-trip through the inventory save path.
        var inventory = new InventoryComponent { Name = "Inventory", Capacity = 12 };
        cache.AddChild(inventory);

        // 30L: chests are lootable — E transfers the contents to the player. Seed starter loot on
        // a fresh spawn; a save's restored (possibly emptied) contents overwrite this on load.
        cache.AddChild(new ContainerLootComponent { Name = "Loot" });
        if (ItemDatabase.Get(GameIds.Items.HealthPotion) is { } potion)
        {
            inventory.AddItem(potion, 2);
        }

        if (ItemDatabase.Get(GameIds.Currency.Gold) is { } gold)
        {
            inventory.AddItem(gold, 20);
        }

        return cache;
    }

    private void RespawnPlayer()
    {
        if (_player == null || !IsInstanceValid(_player))
        {
            return;
        }

        _player.Velocity = Vector3.Zero;
        _player.GlobalPosition = PlayerSpawn;
        _player.GetComponent<StatsComponent>()?.RefillResources();
        Log.Info("You were slain — respawning at the start.");
    }

    // --- Interaction --------------------------------------------------------

    private void HealTarget(float amount)
    {
        if (TryGetStats(out StatsComponent stats))
        {
            stats.Heal(amount);
        }
    }

    /// <summary>Debug: nudge goblin reputation up so they eventually stand down — proof
    /// that faction standing drives AI aggression.</summary>
    private void AdjustGoblinReputation()
    {
        ReputationComponent? reputation = _player?.GetComponent<ReputationComponent>();
        if (reputation == null)
        {
            return;
        }

        reputation.Add(GameIds.Factions.Goblins, 20);
        ReputationTier tier = reputation.TierOf(GameIds.Factions.Goblins);
        bool hostile = reputation.IsHostile(GameIds.Factions.Goblins);
        Log.Info($"Goblin standing: {ReputationTiers.Label(tier)} ({reputation.Get(GameIds.Factions.Goblins)}) — " +
                 $"{(hostile ? "still hostile" : "they now leave you be")}.");
    }

    private void ForceRespawn()
    {
        _respawnCountdown = -1d;
        SpawnDummy();
    }

    private void DespawnDummy()
    {
        if (_dummy != null && IsInstanceValid(_dummy))
        {
            ServiceLocator.Instance?.Unregister(_dummy);
            _dummy.QueueFree();
        }

        _dummy = null;
    }

    private bool TryGetStats(out StatsComponent stats)
    {
        if (_dummy != null && IsInstanceValid(_dummy) && _dummy.TryGetComponent(out stats))
        {
            return true;
        }

        stats = null!;
        return false;
    }

    // --- Event wiring -------------------------------------------------------

    private void SubscribeEvents()
    {
        EventBus bus = EventBus.Instance;
        bus.Subscribe<EntityDamagedEvent>(OnEntityDamaged);
        bus.Subscribe<EntityDiedEvent>(OnEntityDied);
        bus.Subscribe<GameSavedEvent>(OnGameSaved);
        bus.Subscribe<GameLoadedEvent>(OnGameLoaded);
        bus.Subscribe<RegionTransitionRequestedEvent>(OnRegionTransitionRequested);
        bus.Subscribe<FastTravelRequestedEvent>(OnFastTravelRequested);
    }

    private void UnsubscribeEvents()
    {
        EventBus? bus = EventBus.Instance;
        if (bus == null)
        {
            return;
        }

        bus.Unsubscribe<EntityDamagedEvent>(OnEntityDamaged);
        bus.Unsubscribe<EntityDiedEvent>(OnEntityDied);
        bus.Unsubscribe<GameSavedEvent>(OnGameSaved);
        bus.Unsubscribe<GameLoadedEvent>(OnGameLoaded);
        bus.Unsubscribe<RegionTransitionRequestedEvent>(OnRegionTransitionRequested);
        bus.Unsubscribe<FastTravelRequestedEvent>(OnFastTravelRequested);
    }

    private void OnEntityDamaged(EntityDamagedEvent e)
    {
        Log.Info($"{e.Entity.DisplayName} took {e.Amount:0} damage ({e.RemainingHealth:0} HP left).");
    }

    private void OnEntityDied(EntityDiedEvent e)
    {
        if (ReferenceEquals(e.Entity, _player))
        {
            RespawnPlayer();
        }
        else if (ReferenceEquals(e.Entity, _dummy))
        {
            Log.Info($"{e.Entity.DisplayName} destroyed. Respawning in {RespawnDelaySeconds:0}s...");
            _respawnCountdown = RespawnDelaySeconds;
        }
        else if (e.Entity is EnemyEntity)
        {
            // Enemies despawn via the spawn director; their LootComponent rolls and
            // spawns drops from a loot table (see EnemyFactory).
            Log.Info($"{e.Entity.DisplayName} was defeated.");
        }
    }

    private void OnGameSaved(GameSavedEvent e)
    {
        Log.Info($"Game saved to slot '{e.Slot}'.");
    }

    private void OnGameLoaded(GameLoadedEvent e)
    {
        Log.Info($"Game loaded from slot '{e.Slot}'.");

        // The player wakes at full vitals on every load (maintainer direction, 2026-07-02).
        // Deferred two steps so it lands after StatsComponent's own deferred resource restore.
        Callable.From(() => Callable.From(RefillPlayerResources).CallDeferred()).CallDeferred();
    }

    private static void RefillPlayerResources()
    {
        if (ServiceLocator.Instance is { } locator && locator.TryGet(out PlayerCharacter player) &&
            IsInstanceValid(player) && player.GetComponent<StatsComponent>() is { } stats)
        {
            stats.RefillResources();
        }
    }

    /// <summary>Supplies the gameplay fields of a save header (Phase 24B). Read lazily by the
    /// <see cref="SaveManager"/> at save time; the region name comes from the active region's
    /// <see cref="RegionResource"/> (Phase 25A).</summary>
    private Godot.Collections.Dictionary BuildSaveHeader()
    {
        string region = RegionDatabase.Get(_currentRegionId)?.DisplayName ?? "Unknown Region";
        // region_id is the restorable id (vs. the display name) so a load returns to the saved region.
        var header = new Godot.Collections.Dictionary
        {
            ["region"] = region,
            ["region_id"] = _currentRegionId,
        };

        // The chosen race + identity (Phase 26C) so a reload rebuilds the right character.
        foreach (KeyValuePair<string, string> field in _activeProfile.ToHeaderFields())
        {
            header[field.Key] = field.Value;
        }
        if (ServiceLocator.Instance != null && ServiceLocator.Instance.TryGet(out PlayerCharacter player))
        {
            if (player.GetComponent<ProgressionComponent>() is { } progression)
            {
                header["level"] = progression.Level;
            }

            if (player.GetComponent<CorruptionComponent>() is { } corruption)
            {
                header["corruption_tier"] = CorruptionTiers.Label(corruption.Tier);
            }

            // Player world transform, so a load returns the player to where they stood (not the start tile).
            Vector3 pos = player.GlobalPosition;
            header["player_x"] = pos.X;
            header["player_y"] = pos.Y;
            header["player_z"] = pos.Z;
            header["player_yaw"] = player.Rotation.Y;
        }

        return header;
    }
}
