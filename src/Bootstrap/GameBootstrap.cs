using Embervale.Combat;
using Embervale.Core;
using Embervale.Core.Diagnostics;
using Embervale.Core.Events;
using Embervale.Core.Services;
using Embervale.Enemies;
using Embervale.Entities;
using Embervale.Player;
using Embervale.Save;
using Embervale.Stats;
using Embervale.UI;
using Godot;

namespace Embervale.Bootstrap;

/// <summary>
/// Entry point attached to the root of <c>Main.tscn</c>. It assembles a small
/// playable sandbox that exercises the core systems end to end:
///   * builds a minimal 3D world (light, sky, collidable floor),
///   * spawns a first-person <see cref="PlayerFactory">player</see> that walks,
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

    private DebugHud _hud = null!;
    private Entity? _dummy;
    private PlayerCharacter? _player;
    private double _respawnCountdown = -1d;

    public override void _Ready()
    {
        Log.Info("=== Embervale bootstrapping (Phase 2: Player Controller) ===");

        // The bootstrap is the flow manager for the sandbox, so it must keep
        // processing input even while the tree is paused (to unpause).
        ProcessMode = ProcessModeEnum.Always;

        GameInput.EnsureActions();
        BuildEnvironment();

        _hud = new DebugHud();
        AddChild(_hud);

        SubscribeEvents();
        SpawnDummy();
        SpawnPlayer();
        SpawnEnemyCamp();

        GameManager.Instance?.ChangeState(GameState.Playing);
        Log.Info("Sandbox ready. WASD move, mouse look, LMB attack, RMB block. Goblins roam to the north.");
    }

    public override void _ExitTree()
    {
        UnsubscribeEvents();
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
            case Key.F5:
                SaveManager.Instance?.SaveGame("quick");
                break;
            case Key.F9:
                SaveManager.Instance?.LoadGame("quick");
                break;
            case Key.Escape:
                GameManager.Instance?.TogglePause();
                break;
        }
    }

    // --- Scene assembly -----------------------------------------------------

    private void BuildEnvironment()
    {
        // No camera here — the player provides the active first-person camera.
        var light = new DirectionalLight3D
        {
            RotationDegrees = new Vector3(-55f, -40f, 0f),
            ShadowEnabled = true,
        };
        AddChild(light);

        // Sky background; with the default ambient source (background) this also
        // provides soft ambient light, so unlit faces are not pure black.
        var worldEnv = new WorldEnvironment();
        var env = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Sky,
        };
        env.Sky = new Sky { SkyMaterial = new ProceduralSkyMaterial() };
        worldEnv.Environment = env;
        AddChild(worldEnv);

        var floor = new MeshInstance3D
        {
            Mesh = new PlaneMesh { Size = new Vector2(24f, 24f) },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.18f, 0.22f, 0.20f) },
        };
        AddChild(floor);

        // Physics collider for the ground so the player can stand on it.
        var floorBody = new StaticBody3D { Name = "FloorBody" };
        floorBody.AddChild(new CollisionShape3D { Shape = new WorldBoundaryShape3D() });
        AddChild(floorBody);
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
        Log.Info($"Spawned player at {_player.Position}. Facing the training dummy.");
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
        else
        {
            // Enemies handle their own death/despawn via the spawn director.
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
}
