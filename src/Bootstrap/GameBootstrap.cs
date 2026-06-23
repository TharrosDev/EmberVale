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
    private DebugHud _hud = null!;
    private DevConsole _console = null!;
    private ProfilerOverlay _profiler = null!;
    private InventoryPanel _inventoryPanel = null!;
    private QuestLogPanel _questLogPanel = null!;
    private DialoguePanel _dialoguePanel = null!;
    private CraftingPanel _craftingPanel = null!;
    private WorldClock _clock = null!;
    private WeatherDirector _weather = null!;
    private SkyController _sky = null!;
    private PersistentSpawnDirector _persistentSpawns = null!;
    private DirectionalLight3D _sun = null!;
    private Godot.Environment _environment = null!;
    private Entity? _dummy;
    private PlayerCharacter? _player;
    private MainMenu? _mainMenu;
    private bool _sandboxBuilt;
    private double _respawnCountdown = -1d;

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

        Log.Info("=== Embervale bootstrapping (Phase 20: Deep Debugging) ===");

        // The bootstrap is the flow manager for the sandbox, so it must keep
        // processing input even while the tree is paused (to unpause).
        ProcessMode = ProcessModeEnum.Always;

        GameInput.EnsureActions();

        // Localization spine (Phase 24G): load the string catalogue and select the locale before any
        // UI is built, so every player-facing string resolves through Loc.T from the first frame.
        Loc.Initialize();

        ContentDatabases.InitializeAll();

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
            NewGameRequested = StartNewGame,
            LoadGameRequested = StartLoadedGame,
        };
        AddChild(_mainMenu);
        GameManager.Instance?.ChangeState(GameState.MainMenu);
        Log.Info("Main menu ready. New Game to enter the world.");
    }

    /// <summary>Starts a fresh game into <paramref name="slot"/> (Phase 24C): builds the world and
    /// enters <see cref="GameState.Playing"/> with a clean playtime. Invoked by the slot browser.</summary>
    private void StartNewGame(string slot)
    {
        if (!BeginSession(slot))
        {
            return;
        }

        SaveManager.Instance?.ResetPlaytime();
        BuildWorld();
        GameManager.Instance?.ChangeState(GameState.Playing);
        Log.Info($"New game started in slot '{slot}'. Sandbox ready (WASD move, LMB attack, E interact, I inventory).");
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

        BuildWorld();
        SaveManager.Instance?.LoadGame(slot);
        GameManager.Instance?.ChangeState(GameState.Playing);
        Log.Info($"Loaded game from slot '{slot}'. Sandbox ready.");
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
        }

        return true;
    }

    /// <summary>Assembles the playable sandbox (no state transition). Shared by the New and Load
    /// session paths.</summary>
    private void BuildWorld()
    {
        BuildEnvironment();

        // The purpose-built game HUD is the default overlay; the DebugHud is now a
        // developer panel toggled with F3. Toasts and the pause menu round out the game UI.
        _gameHud = new GameHud();
        AddChild(_gameHud);
        _hud = new DebugHud();
        AddChild(_hud);
        AddChild(new Notifications());
        AddChild(new PauseMenu());

        // Deep-debugging tools (Phase 20): dev console (F1), profiler (F4), and a standing
        // world-integrity checker that periodically validates runtime invariants.
        _console = new DevConsole();
        AddChild(_console);
        _profiler = new ProfilerOverlay();
        AddChild(_profiler);
        AddChild(new WorldIntegrityChecker());

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
        _questLogPanel = new QuestLogPanel();
        AddChild(_questLogPanel);
        _dialoguePanel = new DialoguePanel();
        AddChild(_dialoguePanel);
        _craftingPanel = new CraftingPanel();
        AddChild(_craftingPanel);

        // The world clock drives NPC routines; create it before the NPCs below so it is
        // registered in the ServiceLocator when their schedules first read the time.
        _clock = new WorldClock { Name = "WorldClock" };
        AddChild(_clock);
        _hud.SetClock(_clock);
        _gameHud.SetClock(_clock);

        // Weather before the sky so the SkyController can read the active state on its
        // first frame; the sky drives the (already-built) sun + environment.
        _weather = new WeatherDirector { Name = "Weather" };
        AddChild(_weather);
        _hud.SetWeather(_weather);
        _gameHud.SetWeather(_weather);

        _sky = new SkyController { Name = "Sky", Sun = _sun, Environment = _environment };
        AddChild(_sky);

        // Persistent spawned actors: a director that recreates saved named actors/containers on
        // load (the SaveManager alone only restores components of actors already in the scene).
        PersistentActorRegistry.Clear();
        PersistentActorRegistry.Register(GameIds.Templates.Cache, BuildPersistentCache);
        _persistentSpawns = new PersistentSpawnDirector { Name = "PersistentSpawns" };
        AddChild(_persistentSpawns);

        SubscribeEvents();
        SpawnDummy();
        SpawnPlayer();
        SpawnEnemyCamp();
        SpawnLoot();
        SpawnQuestGiver();
        SpawnCraftingStations();
        SpawnEncounterDirector();
        SpawnPersistentActors();
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

        switch (key.Keycode)
        {
            case Key.H:
                HealTarget(20f);
                break;
            case Key.R:
                ForceRespawn();
                break;
            case Key.X:
                _player?.GetComponent<ProgressionComponent>()?.AddXp(50);
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
                if (SaveManager.Instance is { } loader) { loader.LoadGame(loader.ActiveSlot); }
                break;
            case Key.F1:
                _console.Toggle();
                break;
            case Key.F3:
                _hud.Toggle();
                break;
            case Key.F4:
                _profiler.Toggle();
                break;
            // Esc is owned by the PauseMenu (it opens the pause menu and pauses the game).
        }
    }

    // --- Scene assembly -----------------------------------------------------

    private void BuildEnvironment()
    {
        // No camera here — the player provides the active third-person camera. The sun's
        // orientation/energy/colour are animated by the SkyController off the world clock.
        _sun = new DirectionalLight3D
        {
            Name = "Sun",
            RotationDegrees = new Vector3(-55f, -40f, 0f),
            ShadowEnabled = true,
        };
        AddChild(_sun);

        // Sky background; with the default ambient source (background) this also
        // provides soft ambient light, so unlit faces are not pure black. The
        // SkyController dims the sky at night and applies weather fog to this env.
        var worldEnv = new WorldEnvironment();
        _environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Sky,
        };
        _environment.Sky = new Sky { SkyMaterial = new ProceduralSkyMaterial() };
        worldEnv.Environment = _environment;
        AddChild(worldEnv);

        // A generous ground plane so dynamic encounters (spawned ~14–20m out) land on
        // visible terrain; the collider below is an infinite plane regardless.
        var floor = new MeshInstance3D
        {
            Mesh = new PlaneMesh { Size = new Vector2(80f, 80f) },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.18f, 0.22f, 0.20f) },
        };
        AddChild(floor);

        // Physics collider for the ground so the player can stand on it.
        var floorBody = new StaticBody3D { Name = "FloorBody" };
        floorBody.AddChild(new CollisionShape3D { Shape = new WorldBoundaryShape3D() });
        AddChild(floorBody);
    }

    private void SpawnEncounterDirector()
    {
        AddChild(new EncounterDirector { Name = "Encounters" });
        Log.Info("Encounter director online — patrols by day, warbands by night, more in storms.");

        var events = new WorldEventDirector { Name = "WorldEvents" };
        AddChild(events);
        _hud.SetWorldEvents(events);
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

        var mesh = new MeshInstance3D
        {
            Name = "Mesh",
            Mesh = new CapsuleMesh { Radius = 0.4f, Height = 1.8f },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.70f, 0.30f, 0.28f) },
        };
        dummy.AddChild(mesh);

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
        _hud.SetTarget(dummy);

        Log.Info($"Spawned '{dummy.DisplayName}' — max health {stats.GetValue(StatType.Health):0} (base 100 +20% blessing).");
    }

    private void SpawnPlayer()
    {
        _player = PlayerFactory.Create(PlayerSpawn);
        AddChild(_player);
        ServiceLocator.Instance?.Register(_player);
        _hud.SetPlayer(_player);
        _gameHud.SetPlayer(_player);
        _inventoryPanel.SetInventory(_player.GetComponent<InventoryComponent>());
        _inventoryPanel.SetEquipment(_player.GetComponent<EquipmentComponent>());
        _inventoryPanel.SetProgression(_player.GetComponent<ProgressionComponent>());
        _inventoryPanel.SetPerks(_player.GetComponent<PerksComponent>());
        _inventoryPanel.SetReputation(_player.GetComponent<ReputationComponent>());
        _inventoryPanel.SetCorruption(_player.GetComponent<CorruptionComponent>());

        QuestLogComponent? questLog = _player.GetComponent<QuestLogComponent>();
        _questLogPanel.SetQuestLog(questLog);

        // Seed a starter quest so the journal has content the moment you press Play.
        if (questLog != null && QuestDatabase.Get(GameIds.Quests.CullGoblins) is { } starter)
        {
            questLog.StartQuest(starter);
        }

        Log.Info($"Spawned player at {_player.Position}. Facing the training dummy.");
    }

    private void SpawnLoot()
    {
        // A few collectables strewn between the player and the goblin camp.
        TryDropPickup(GameIds.Items.HealthPotion, 2, new Vector3(1.5f, 0f, 2f));
        TryDropPickup(GameIds.Items.IronOre, 3, new Vector3(-2f, 0f, 0f));
        TryDropPickup(GameIds.Items.Ruby, 1, new Vector3(0f, 0f, -3f));
        TryDropPickup(GameIds.Currency.Gold, 25, new Vector3(2.5f, 0f, -1f));

        // Crafting materials so the stations to the west have something to work with.
        TryDropPickup(GameIds.Items.IronOre, 4, new Vector3(-4.5f, 0f, 6f));
        TryDropPickup(GameIds.Items.GoblinHide, 4, new Vector3(-4f, 0f, 6.8f));
        TryDropPickup(GameIds.Items.HealingHerb, 5, new Vector3(-3.2f, 0f, 6.6f));

        // Equippable gear to try out the equipment screen.
        TryDropPickup(GameIds.Items.LeatherCap, 1, new Vector3(-1.2f, 0f, 3f));
        TryDropPickup(GameIds.Items.LeatherVest, 1, new Vector3(-3f, 0f, 2.5f));
        TryDropPickup(GameIds.Items.SteelSword, 1, new Vector3(1.5f, 0f, -2.5f));
        TryDropPickup(GameIds.Items.IronRing, 1, new Vector3(3f, 0f, -3.5f));

        // A procedurally-rolled Rare blade to show off the affix pipeline.
        if (ItemDatabase.Get(GameIds.Items.SteelSword) is EquippableItemResource sword)
        {
            ItemInstance rolled = LootGenerator.RollAffixed(sword, ItemRarity.Rare);
            AddChild(ItemPickupFactory.Create(rolled, 1, new Vector3(-1.5f, 0f, -1.5f)));
            Log.Info($"Seeded a rolled drop: {rolled.DisplayName}.");
        }
    }

    private void TryDropPickup(string itemId, int quantity, Vector3 position)
    {
        ItemResource? item = ItemDatabase.Get(itemId);
        if (item != null)
        {
            AddChild(ItemPickupFactory.Create(item, quantity, position));
        }
    }

    private void SpawnQuestGiver()
    {
        var giver = new Entity
        {
            Name = "QuestGiver",
            DisplayName = "Village Elder",
            TemplateId = GameIds.Npcs.Elder,
            Position = new Vector3(3f, 0f, 4f),
        };

        giver.AddChild(new MeshInstance3D
        {
            Name = "Mesh",
            Mesh = new CapsuleMesh { Radius = 0.4f, Height = 1.8f },
            Position = new Vector3(0f, 0.9f, 0f),
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.85f, 0.78f, 0.45f) },
        });

        var collider = new StaticBody3D { Name = "Collider" };
        collider.AddChild(new CollisionShape3D
        {
            Shape = new CapsuleShape3D { Radius = 0.4f, Height = 1.8f },
            Position = new Vector3(0f, 0.9f, 0f),
        });
        giver.AddChild(collider);

        // The elder is a villager: killing him would tank reputation with his faction.
        giver.AddChild(new FactionComponent { Name = "Faction", FactionId = GameIds.Factions.Villagers });

        // The elder now offers his task in conversation: the dialogue's choices start
        // the quest and remember you via a story flag (see data/dialogue/Elder.tres).
        giver.AddChild(new DialogueComponent { Name = "Dialogue", DialogueId = GameIds.Dialogues.Elder });

        // A daily routine: the elder walks between the well, the forge and home as the
        // world clock turns, and flees if goblins raise the alarm nearby.
        giver.AddChild(new ScheduleComponent { Name = "Schedule", ScheduleId = GameIds.Schedules.Elder });
        AddChild(giver);
        Log.Info("The Village Elder keeps a daily routine near the spawn — talk to him for a task.");
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

        cache.AddChild(new MeshInstance3D
        {
            Name = "Mesh",
            Mesh = new BoxMesh { Size = new Vector3(0.8f, 0.8f, 0.8f) },
            Position = new Vector3(0f, 0.4f, 0f),
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.55f, 0.43f, 0.20f) },
        });

        var collider = new StaticBody3D { Name = "Collider" };
        collider.AddChild(new CollisionShape3D
        {
            Shape = new BoxShape3D { Size = new Vector3(0.8f, 0.8f, 0.8f) },
            Position = new Vector3(0f, 0.4f, 0f),
        });
        cache.AddChild(collider);

        // A persistent container's contents round-trip through the inventory save path.
        cache.AddChild(new InventoryComponent { Name = "Inventory", Capacity = 12 });
        return cache;
    }

    private void SpawnCraftingStations()
    {
        // A little crafting yard west of the spawn: smelt/forge at the forge, tan/stitch at
        // the workbench, brew at the alchemy table. Walk up and press E to use one.
        AddChild(CraftingStationFactory.Create(
            CraftingStationType.Forge, "Forge", new Vector3(-4.5f, 0f, 4.5f), new Color(0.45f, 0.20f, 0.16f)));
        AddChild(CraftingStationFactory.Create(
            CraftingStationType.Workbench, "Workbench", new Vector3(-3f, 0f, 5f), new Color(0.45f, 0.32f, 0.18f)));
        AddChild(CraftingStationFactory.Create(
            CraftingStationType.Alchemy, "Alchemy Table", new Vector3(-3.5f, 0f, 6.2f), new Color(0.20f, 0.42f, 0.40f)));
        Log.Info("A crafting yard sits west of spawn — forge, workbench and alchemy table.");
    }

    private void SpawnEnemyCamp()
    {
        var director = new EnemySpawnDirector
        {
            Name = "GoblinCamp",
            Position = new Vector3(0f, 0f, -8f),
            MaxAlive = 3,
            SpawnRadius = 6f,
        };
        AddChild(director);
        Log.Info("A goblin camp stirs to the north (−Z).");
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
            ServiceLocator.Instance?.Unregister<Entity>();
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
    }

    /// <summary>Supplies the gameplay fields of a save header (Phase 24B). Read lazily by the
    /// <see cref="SaveManager"/> at save time; the region name comes from the active region's
    /// <see cref="RegionResource"/> (Phase 25A).</summary>
    private Godot.Collections.Dictionary BuildSaveHeader()
    {
        string region = RegionDatabase.Get(_currentRegionId)?.DisplayName ?? "Unknown Region";
        var header = new Godot.Collections.Dictionary { ["region"] = region };
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
        }

        return header;
    }
}
