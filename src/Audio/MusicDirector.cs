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
///   <item><b>Boss</b> — <see cref="BossEncounterStartedEvent"/> until that boss dies; overrides combat.</item>
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
    private ulong _bossId;
    private bool _hasBoss;
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
        PruneEngaged();
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
        if (_hasBoss && e.Entity.RuntimeId == _bossId)
        {
            _hasBoss = false;
            _machine.BossActive = false;
        }

        Reevaluate();
    }

    private void OnBossStarted(BossEncounterStartedEvent e)
    {
        _bossId = e.Boss.RuntimeId;
        _hasBoss = true;
        _machine.BossActive = true;
        Reevaluate();
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

    private void PruneEngaged()
    {
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
