using System.Collections.Generic;
using Embervale.Core.Diagnostics;
using Embervale.Core.Events;
using Embervale.Core.Services;
using Embervale.Enemies;
using Embervale.Entities;
using Embervale.Player;
using Embervale.World;
using Godot;

namespace Embervale.Audio;

/// <summary>
/// Adaptive music (Phase 31B): drives a four-state machine (explore / safe / combat / boss) from EventBus
/// signals and crossfades between two looping music players on the Music bus. Runs with
/// <see cref="Node.ProcessModeEnum.Always"/> so a track keeps its fade going across a pause.
///
/// State inputs:
/// <list type="bullet">
///   <item><b>Combat</b> — an enemy publishing <see cref="EnemyStateChangedEvent"/> in Combat/Retreat is
///     tracked; it leaves the set on any other state, on <see cref="EntityDiedEvent"/>, or when its body
///     is freed (periodic prune). Combat music holds while the set is non-empty.</item>
///   <item><b>Boss</b> — <see cref="BossEncounterStartedEvent"/> until that boss dies <em>or its body
///     leaves the world</em> (the same periodic prune); overrides combat.</item>
///   <item><b>Safe</b> — the player is inside the active region's <see cref="SafeZones"/> (polled).</item>
/// </list>
/// Beds come from the shared <see cref="AudioLibrary"/> (real CC0 tracks when present, else procedural
/// pads), so authored music swaps in by dropping files under <c>assets/audio/music/</c>.
/// </summary>
public partial class MusicDirector : Node
{
    private const float FadeSeconds = 1.5f;
    private const float SilentDb = -60f;
    private const float SafePollSeconds = 0.5f;

    private static readonly Dictionary<MusicState, string> Beds = new()
    {
        [MusicState.Explore] = "music.explore",
        [MusicState.Safe] = "music.safe",
        [MusicState.Combat] = "music.combat",
        [MusicState.Boss] = "music.boss",
    };

    private readonly MusicStateMachine _machine = new();
    private readonly Dictionary<ulong, IEntity> _engaged = new();
    private AudioLibrary _library = null!;
    private AudioStreamPlayer _a = null!;
    private AudioStreamPlayer _b = null!;
    private bool _aActive = true;

    /// <summary>The boss whose encounter is holding boss music, or null. Held as the entity rather
    /// than its <c>RuntimeId</c> so <see cref="PruneDead"/> can ask whether its body is still alive —
    /// a boss that leaves without dying used to strand the music, since only a death cleared it.</summary>
    private IEntity? _boss;
    private MusicState? _current;
    private float _fade = 1f; // 1 = settled; <1 = crossfading
    private float _safeTimer;

    private AudioStreamPlayer Active => _aActive ? _a : _b;
    private AudioStreamPlayer Idle => _aActive ? _b : _a;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        _library = ServiceLocator.Instance != null && ServiceLocator.Instance.TryGet(out AudioLibrary lib)
            ? lib
            : new AudioLibrary();

        _a = NewPlayer();
        _b = NewPlayer();
        AddChild(_a);
        AddChild(_b);

        EventBus.Instance?.Subscribe<EnemyStateChangedEvent>(OnEnemyState);
        EventBus.Instance?.Subscribe<EntityDiedEvent>(OnDied);
        EventBus.Instance?.Subscribe<BossEncounterStartedEvent>(OnBossStarted);

        Apply(_machine.Resolve(), instant: true);
        Log.Info("MusicDirector ready (adaptive: explore/safe/combat/boss).");
    }

    public override void _ExitTree()
    {
        EventBus.Instance?.Unsubscribe<EnemyStateChangedEvent>(OnEnemyState);
        EventBus.Instance?.Unsubscribe<EntityDiedEvent>(OnDied);
        EventBus.Instance?.Unsubscribe<BossEncounterStartedEvent>(OnBossStarted);
    }

    public override void _Process(double delta)
    {
        PollSafeZone((float)delta);
        PruneDead();
        AdvanceFade((float)delta);
    }

    private static AudioStreamPlayer NewPlayer() => new()
    {
        Bus = Embervale.Settings.AudioBuses.Music,
        VolumeDb = SilentDb,
    };

    private void OnEnemyState(EnemyStateChangedEvent e)
    {
        bool engaged = e.State is EnemyState.Combat or EnemyState.Retreat;
        if (engaged)
        {
            _engaged[e.Enemy.RuntimeId] = e.Enemy;
        }
        else
        {
            _engaged.Remove(e.Enemy.RuntimeId);
        }

        Reevaluate();
    }

    private void OnDied(EntityDiedEvent e)
    {
        _engaged.Remove(e.Entity.RuntimeId);
        if (_boss != null && ReferenceEquals(e.Entity, _boss))
        {
            ClearBoss();
        }

        Reevaluate();
    }

    private void OnBossStarted(BossEncounterStartedEvent e)
    {
        _boss = e.Boss;
        _machine.BossActive = true;
        Reevaluate();
    }

    private void ClearBoss()
    {
        _boss = null;
        _machine.BossActive = false;
    }

    private void PollSafeZone(float delta)
    {
        _safeTimer -= delta;
        if (_safeTimer > 0f)
        {
            return;
        }

        _safeTimer = SafePollSeconds;
        bool inSafe = ServiceLocator.Instance != null
            && ServiceLocator.Instance.TryGet(out PlayerCharacter player)
            && SafeZones.Contains(player.Body.GlobalPosition);
        if (inSafe != _machine.InSafeZone)
        {
            _machine.InSafeZone = inSafe;
            Reevaluate();
        }
    }

    /// <summary>
    /// Drops combatants — and the boss — whose bodies have been freed. Death is the tidy exit and
    /// raises an event; leaving the world is the untidy one and raises nothing. A region transition
    /// or a load mid-fight frees the actors outright, and the boss was only ever cleared by
    /// <see cref="OnDied"/>, so walking out of the arena left boss music playing over the whole rest
    /// of the game with no way back to explore.
    /// </summary>
    private void PruneDead()
    {
        if (_boss != null && !GodotObject.IsInstanceValid(_boss.Body))
        {
            ClearBoss();
            Reevaluate();
        }

        if (_engaged.Count == 0)
        {
            return;
        }

        List<ulong>? stale = null;
        foreach (KeyValuePair<ulong, IEntity> pair in _engaged)
        {
            if (!GodotObject.IsInstanceValid(pair.Value.Body))
            {
                (stale ??= new List<ulong>()).Add(pair.Key);
            }
        }

        if (stale == null)
        {
            return;
        }

        foreach (ulong id in stale)
        {
            _engaged.Remove(id);
        }

        Reevaluate();
    }

    private void Reevaluate()
    {
        _machine.Combatants = _engaged.Count;
        MusicState next = _machine.Resolve();
        if (_current != next)
        {
            Apply(next, instant: false);
        }
    }

    private void Apply(MusicState state, bool instant)
    {
        if (!Beds.TryGetValue(state, out string? cueId) || !_library.TryGet(cueId, out AudioStream stream))
        {
            return;
        }

        _current = state;

        if (instant)
        {
            Active.Stream = stream;
            Active.VolumeDb = 0f;
            Active.Play();
            _fade = 1f;
            return;
        }

        // Crossfade: arm the idle player with the new bed, swap roles, fade the two past each other.
        AudioStreamPlayer incoming = Idle;
        incoming.Stream = stream;
        incoming.VolumeDb = SilentDb;
        incoming.Play();
        _aActive = !_aActive;
        _fade = 0f;
    }

    private void AdvanceFade(float delta)
    {
        if (_fade >= 1f)
        {
            return;
        }

        _fade = Mathf.Min(1f, _fade + delta / FadeSeconds);
        Active.VolumeDb = Mathf.Lerp(SilentDb, 0f, _fade);
        Idle.VolumeDb = Mathf.Lerp(0f, SilentDb, _fade);
        if (_fade >= 1f)
        {
            Idle.Stop();
        }
    }
}
