using Embervale.Combat;
using Embervale.Core;
using Embervale.Core.Diagnostics;
using Embervale.Core.Events;
using Embervale.Core.Services;
using Embervale.Corruption;
using Embervale.Debugging;
using Embervale.Entities;
using Embervale.Factions;
using Embervale.Magic;
using Embervale.Player;
using Embervale.Progression;
using Embervale.Save;
using Embervale.Stats;
using Embervale.UI;
using Embervale.World;
using Godot;

namespace Embervale.Bootstrap;

/// <summary>
/// Every developer surface in a session, in one place and behind one gate.
///
/// <para>The console (F1), the debug HUD (F3), the profiler (F4), the standing integrity checker,
/// the training dummy and the single-key cheats used to be threaded through the bootstrap beside
/// the game's own systems, each with its own <c>BuildProfile</c> check. They are all here now and
/// the gate is one <c>if</c> in <see cref="GameSession.Build"/>: <b>a capture or exported build
/// never constructs this node at all</b>, so there is nothing to accidentally respond to a stray
/// keypress.</para>
///
/// <para>Quick save and quick load are the exception and stay unconditional — they are player
/// conveniences, not developer affordances — so this node processes keys in every build and filters
/// the cheats itself.</para>
/// </summary>
public sealed partial class DeveloperToolsHost : Node
{
    private const string DummyAttributesPath = "res://data/attributes/DummyAttributes.tres";
    private const float RespawnDelaySeconds = 3f;

    private DebugHud? _hud;
    private DevConsole? _console;
    private ProfilerOverlay? _profiler;
    private Entity? _dummy;
    private PlayerCharacter? _player;
    private double _respawnCountdown = -1d;

    public GameSession Session { get; init; } = null!;

    public override void _EnterTree()
    {
        ProcessMode = ProcessModeEnum.Always;
        EventBus.Instance?.Subscribe<EntityDamagedEvent>(OnEntityDamaged);
        EventBus.Instance?.Subscribe<EntityDiedEvent>(OnEntityDied);
        EventBus.Instance?.Subscribe<GameSavedEvent>(OnGameSaved);
    }

    public override void _ExitTree()
    {
        EventBus.Instance?.Unsubscribe<EntityDamagedEvent>(OnEntityDamaged);
        EventBus.Instance?.Unsubscribe<EntityDiedEvent>(OnEntityDied);
        EventBus.Instance?.Unsubscribe<GameSavedEvent>(OnGameSaved);
    }

    /// <summary>The debug HUD, the console, the profiler and the standing integrity checker. The
    /// checker goes with them: it exists to shout at the developer, and it costs a scan every five
    /// seconds.</summary>
    public void BuildOverlays()
    {
        _hud = new DebugHud();
        AddChild(_hud);

        _console = new DevConsole();
        AddChild(_console);

        _profiler = new ProfilerOverlay();
        AddChild(_profiler);

        AddChild(new WorldIntegrityChecker());
    }

    public void SetClock(WorldClock clock) => _hud?.SetClock(clock);

    public void SetWeather(WeatherDirector weather) => _hud?.SetWeather(weather);

    public void SetWorldEvents(WorldEventDirector events) => _hud?.SetWorldEvents(events);

    public void SetPlayer(PlayerCharacter player)
    {
        _player = player;
        _hud?.SetPlayer(player);
    }

    public override void _Process(double delta)
    {
        if (_respawnCountdown <= 0d)
        {
            return;
        }

        _respawnCountdown -= delta;
        if (_respawnCountdown <= 0d)
        {
            _respawnCountdown = -1d;
            SpawnDummy();
        }
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true, Echo: false } key)
        {
            return;
        }

        // Quick save/load stay in every build — they are player conveniences. The cheats below them
        // are developer affordances and a capture build must not respond to a stray H or X.
        if (!BuildProfile.ShowDeveloperTools && key.Keycode is not (Key.F5 or Key.F9 or Key.Escape))
        {
            return;
        }

        switch (key.Keycode)
        {
            case Key.H:
                HealDummy(20f);
                break;
            case Key.R:
                ForceRespawnDummy();
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
                if (SaveManager.Instance is { } saver)
                {
                    saver.SaveGame(saver.ActiveSlot);
                }

                break;
            case Key.F9:
                if (SaveManager.Instance is { } loader && !loader.LoadGame(loader.ActiveSlot))
                {
                    Session.Lifecycle.AbortToTitle(
                        $"Quickload of slot '{loader.ActiveSlot}' failed; returning to the title screen.");
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

    /// <summary>
    /// The training dummy: an independent target on team 2 that both the player and enemies can
    /// strike. It is scaffolding, not content — the fastest way to exercise damage, status effects
    /// and the death/respawn loop — and a stranger playing the slice must never see one.
    /// </summary>
    public void SpawnDummy()
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
        dummy.AddChild(new CombatComponent { Name = "Combat", Team = 2 });

        // So spell DoTs/slows can be observed landing on the practice target.
        dummy.AddChild(new StatusEffectsComponent { Name = "StatusEffects" });
        dummy.AddChild(new StatusEffectVfxComponent { Name = "StatusVfx" });

        // The wooden training-dummy model has its origin at the feet; the dummy entity's origin is
        // its capsule CENTRE, so the visual sits 1 m down. Capsule fallback if unimported.
        if (GD.Load<PackedScene>(ModelAssets.TrainingDummy)?.Instantiate() is Node3D dummyVisual)
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

        // Solid collider so the player cannot walk through the dummy. Shapes are centred at the
        // local origin to line up with the mesh.
        var collider = new StaticBody3D { Name = "Collider" };
        collider.AddChild(new CollisionShape3D { Shape = new CapsuleShape3D { Radius = 0.4f, Height = 1.8f } });
        dummy.AddChild(collider);

        var hurtbox = new Hurtbox { Name = "Hurtbox" };
        hurtbox.AddChild(new CollisionShape3D { Shape = new CapsuleShape3D { Radius = 0.4f, Height = 1.8f } });
        dummy.AddChild(hurtbox);

        AddChild(dummy);

        // Demonstrate the modifier pipeline: a "blessing" raises max health 20%.
        stats.GetStat(StatType.Health).AddModifier(
            new StatModifier(0.20f, ModifierType.PercentAdd, "Blessing of Vigor"));
        stats.RefillResources();

        _dummy = dummy;
        ServiceScope.RegisterOwned(dummy, dummy);
        _hud?.SetTarget(dummy);

        Log.Info($"Spawned '{dummy.DisplayName}' — max health {stats.GetValue(StatType.Health):0} (base 100 +20% blessing).");
    }

    private void DespawnDummy()
    {
        if (_dummy != null && IsInstanceValid(_dummy))
        {
            // No unregister: the registration is owned by the node, and goes with it.
            _dummy.QueueFree();
        }

        _dummy = null;
    }

    private void ForceRespawnDummy()
    {
        _respawnCountdown = -1d;
        SpawnDummy();
    }

    private void HealDummy(float amount)
    {
        if (_dummy != null && IsInstanceValid(_dummy) && _dummy.TryGetComponent(out StatsComponent stats))
        {
            stats.Heal(amount);
        }
    }

    /// <summary>Debug: nudge goblin reputation up so they eventually stand down — proof that faction
    /// standing drives AI aggression.</summary>
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

    private static void OnEntityDamaged(EntityDamagedEvent e)
    {
        Log.Info($"{e.Entity.DisplayName} took {e.Amount:0} damage ({e.RemainingHealth:0} HP left).");
    }

    private void OnEntityDied(EntityDiedEvent e)
    {
        if (ReferenceEquals(e.Entity, _dummy))
        {
            Log.Info($"{e.Entity.DisplayName} destroyed. Respawning in {RespawnDelaySeconds:0}s...");
            _respawnCountdown = RespawnDelaySeconds;
        }
        else if (e.Entity is Enemies.EnemyEntity)
        {
            // Enemies despawn through their own AI's death state; their loot component rolls drops.
            Log.Info($"{e.Entity.DisplayName} was defeated.");
        }
    }

    private static void OnGameSaved(GameSavedEvent e)
    {
        Log.Info($"Game saved to slot '{e.Slot}'.");
    }
}
