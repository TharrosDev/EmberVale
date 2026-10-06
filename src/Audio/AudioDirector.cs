using System.Collections.Generic;
using Embervale.Combat;
using Embervale.Combat.Actions;
using Embervale.Core.Diagnostics;
using Embervale.Core.Events;
using Embervale.Core.Pooling;
using Embervale.Core.Services;
using Embervale.Entities;
using Embervale.Items;
using Embervale.Magic;
using Embervale.Magic.Vfx;
using Embervale.Progression;
using Godot;

namespace Embervale.Audio;

/// <summary>
/// The heart of the audio system (Phase 31A): consumes the sound/music cue events the rest of the game
/// already publishes (<see cref="SoundCueRequestedEvent"/> from combat swings/impacts,
/// <see cref="MusicCueRequestedEvent"/> from narrative beats such as a boss defeat) and plays them on the
/// mixer buses through pooled players. Registered in the <c>ServiceLocator</c> so any system can also
/// request a cue directly (<see cref="PlayCue(string, Vector3)"/> / <see cref="PlayCue(string)"/>) — the
/// UI-click and footstep hooks in later Phase 31 sub-phases use exactly that.
///
/// Runs with <see cref="Node.ProcessModeEnum.Always"/> so menu/pause cues still sound while the tree is
/// paused. Bus volumes are owned by <c>SettingsService.ApplyAudio()</c> (they route straight to
/// <c>AudioServer</c>), so this director does not touch volume — it only plays.
///
/// <para><b>Spells</b> are sounded here from the magic events, with <see cref="SpellAudio"/> deciding
/// the cue, the pitch and the level: the school's cast cue as the spell leaves the caster (at the
/// release of its wind-up, not at the start of it), a riser under a wind-up long enough to have one,
/// the school's impact on <see cref="SpellImpactEvent"/> and its blast on <see cref="SpellBurstEvent"/>,
/// and the shared one-shots (a fizzle on an interrupt, a ward breaking, a freeze, thunder after a
/// heavy lightning hit; a blink and a heal in place of their school's cast). Identical cues inside
/// <see cref="SpellAudio.CoalesceSeconds"/> are one sound, and the things that repeat (a channel's
/// ticks, a zone's pulses) are held to a slower beat and a lower level.</para>
/// </summary>
public partial class AudioDirector : Node
{
    /// <summary>How long after its wind-up should have ended a cast is still waited for.</summary>
    private const double WindupGraceSeconds = 0.75;

    /// <summary>A riser this close to its natural end is left to finish, never stopped: past its end
    /// the pooled player may already be someone else's sound.</summary>
    private const double RiserEndMargin = 0.05;

    /// <summary>The emitter that stands for "anyone" in <see cref="_lastPlayed"/>.</summary>
    private const long AnyEmitter = -1;

    private const int MaxRemembered = 256;

    /// <summary>A cast between its wind-up starting and the spell leaving.</summary>
    private readonly record struct PendingCast(
        string SpellId, double Expires, PositionalSfxPlayer? Riser, AudioStream? RiserStream, double RiserEnds);

    private AudioLibrary _library = null!;
    private NodePool<PositionalSfxPlayer> _sfxPool = null!;
    private NodePool<OneShotAudioPlayer> _flatPool = null!;

    // Spell cues. Keyed by runtime ids and cue ids only, so nothing here keeps an entity alive.
    private readonly Dictionary<ulong, PendingCast> _windups = new();
    private readonly Dictionary<(string Cue, long Emitter), double> _lastPlayed = new();
    private string _zonePulseSpell = string.Empty;
    private double _zonePulseAt = double.MinValue;
    private bool _restoring;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        _sfxPool = new NodePool<PositionalSfxPlayer>(() => new PositionalSfxPlayer { Released = p => _sfxPool.Return(p) }, prewarm: 6);
        _flatPool = new NodePool<OneShotAudioPlayer>(() => new OneShotAudioPlayer { Released = p => _flatPool.Return(p) }, prewarm: 2);

        // The application's library when there is one (UiAudio registers it at boot), so a session
        // does not synthesise every placeholder again. Otherwise this director's own, shared so
        // MusicDirector reuses the built streams and owned by this node: the registration goes
        // when the director does, without _ExitTree having to remember it.
        if (ServiceLocator.Instance is { } locator && locator.TryGet(out AudioLibrary shared))
        {
            _library = shared;
        }
        else
        {
            _library = new AudioLibrary();
            ServiceScope.RegisterOwned(this, _library);
        }

        EventBus.Instance?.Subscribe<SoundCueRequestedEvent>(OnSoundCue);
        EventBus.Instance?.Subscribe<MusicCueRequestedEvent>(OnMusicCue);
        EventBus.Instance?.Subscribe<ItemPickedUpEvent>(OnItemPickedUp);
        EventBus.Instance?.Subscribe<SpellCastEvent>(OnSpellCast);
        EventBus.Instance?.Subscribe<LeveledUpEvent>(OnLeveledUp);
        EventBus.Instance?.Subscribe<CastWindupStartedEvent>(OnCastWindup);
        EventBus.Instance?.Subscribe<ActionReleasedEvent>(OnActionReleased);
        EventBus.Instance?.Subscribe<SpellInterruptedEvent>(OnSpellInterrupted);
        EventBus.Instance?.Subscribe<SpellImpactEvent>(OnSpellImpact);
        EventBus.Instance?.Subscribe<SpellBurstEvent>(OnSpellBurst);
        EventBus.Instance?.Subscribe<WardBrokenEvent>(OnWardBroken);
        EventBus.Instance?.Subscribe<StatusEffectAppliedEvent>(OnStatusApplied);
        EventBus.Instance?.Subscribe<GameLoadingEvent>(OnGameLoading);
        EventBus.Instance?.Subscribe<GameLoadedEvent>(OnGameLoaded);
        SetProcess(false);
        Log.Info($"AudioDirector ready ({_library.Count} cues, {_library.RealCount} from real assets, buses={AudioServer.BusCount}).");
    }

    public override void _ExitTree()
    {
        EventBus.Instance?.Unsubscribe<SoundCueRequestedEvent>(OnSoundCue);
        EventBus.Instance?.Unsubscribe<MusicCueRequestedEvent>(OnMusicCue);
        EventBus.Instance?.Unsubscribe<ItemPickedUpEvent>(OnItemPickedUp);
        EventBus.Instance?.Unsubscribe<SpellCastEvent>(OnSpellCast);
        EventBus.Instance?.Unsubscribe<LeveledUpEvent>(OnLeveledUp);
        EventBus.Instance?.Unsubscribe<CastWindupStartedEvent>(OnCastWindup);
        EventBus.Instance?.Unsubscribe<ActionReleasedEvent>(OnActionReleased);
        EventBus.Instance?.Unsubscribe<SpellInterruptedEvent>(OnSpellInterrupted);
        EventBus.Instance?.Unsubscribe<SpellImpactEvent>(OnSpellImpact);
        EventBus.Instance?.Unsubscribe<SpellBurstEvent>(OnSpellBurst);
        EventBus.Instance?.Unsubscribe<WardBrokenEvent>(OnWardBroken);
        EventBus.Instance?.Unsubscribe<StatusEffectAppliedEvent>(OnStatusApplied);
        EventBus.Instance?.Unsubscribe<GameLoadingEvent>(OnGameLoading);
        EventBus.Instance?.Unsubscribe<GameLoadedEvent>(OnGameLoaded);
        _windups.Clear();
        _lastPlayed.Clear();
        _sfxPool?.Clear();
        _flatPool?.Clear();
    }

    /// <summary>Only runs while a load is restoring state: the first frame after it ends the quiet,
    /// for a load that never says it finished (a probe's, a failed one).</summary>
    public override void _Process(double delta)
    {
        _restoring = false;
        SetProcess(false);
    }

    /// <summary>Plays a cue positionally in 3D (falls back to a flat play for a non-positional cue id).</summary>
    public void PlayCue(string cueId, Vector3 position) => PlayCue(cueId, position, 0f, 1f);

    /// <summary>Plays a cue positionally with a per-play volume offset and pitch (footstep variation).</summary>
    public void PlayCue(string cueId, Vector3 position, float volumeDb, float pitchScale)
    {
        if (!_library.TryGet(cueId, out AudioStream stream))
        {
            return;
        }

        if (!AudioCueRouting.IsPositional(cueId))
        {
            PlayFlat(stream, AudioCueRouting.BusFor(cueId));
            return;
        }

        PlayPositional(stream, cueId, position, volumeDb, pitchScale);
    }

    private PositionalSfxPlayer PlayPositional(
        AudioStream stream, string cueId, Vector3 position, float volumeDb, float pitchScale)
    {
        PositionalSfxPlayer player = _sfxPool.Get();
        AddChild(player);
        player.PlayCue(stream, AudioCueRouting.BusFor(cueId), position, volumeDb, pitchScale);
        return player;
    }

    /// <summary>Plays a cue non-positionally (2D) — music, UI, ambience one-shots.</summary>
    public void PlayCue(string cueId)
    {
        if (_library.TryGet(cueId, out AudioStream stream))
        {
            PlayFlat(stream, AudioCueRouting.BusFor(cueId));
        }
    }

    private void PlayFlat(AudioStream stream, StringName bus)
    {
        OneShotAudioPlayer player = _flatPool.Get();
        AddChild(player);
        player.PlayCue(stream, bus);
    }

    private void OnSoundCue(SoundCueRequestedEvent e) => PlayCue(e.CueId, e.Position, e.VolumeDb, e.PitchScale);

    private void OnMusicCue(MusicCueRequestedEvent e) => PlayCue(e.CueId);

    private void OnItemPickedUp(ItemPickedUpEvent e) => PlayCue("sfx.pickup", e.Owner.Body.GlobalPosition);

    // --- spells ------------------------------------------------------------------------------------

    private static double Now => Time.GetTicksUsec() / 1_000_000.0;

    private static long EmitterOf(IEntity? entity) => entity == null ? AnyEmitter - 1 : (long)(entity.RuntimeId & long.MaxValue);

    /// <summary>Where <paramref name="entity"/> is, <paramref name="height"/> above its origin. False
    /// for one with no live body in the tree (despawned in the same resolution that raised the event).</summary>
    private static bool TryPosition(IEntity? entity, float height, out Vector3 position)
    {
        if (entity?.Body is { } body && IsInstanceValid(body) && body.IsInsideTree())
        {
            position = body.GlobalPosition + (Vector3.Up * height);
            return true;
        }

        position = Vector3.Zero;
        return false;
    }

    /// <summary>
    /// Plays a spell cue unless the same cue has only just sounded: anyone's inside
    /// <see cref="SpellAudio.CoalesceSeconds"/>, or <paramref name="emitter"/>'s own inside
    /// <paramref name="gap"/>. Returns the player, or null when nothing played.
    /// </summary>
    private PositionalSfxPlayer? PlaySpellCue(
        SpellCue cue, Vector3 position, long emitter = AnyEmitter, double gap = SpellAudio.CoalesceSeconds)
    {
        if (_restoring)
        {
            return null;
        }

        double now = Now;
        if (_lastPlayed.TryGetValue((cue.CueId, AnyEmitter), out double lastAny) &&
            SpellAudio.Coalesces(cue.CueId, now, cue.CueId, lastAny))
        {
            return null;
        }

        if (_lastPlayed.TryGetValue((cue.CueId, emitter), out double last) && SpellAudio.TooSoon(now, last, gap))
        {
            return null;
        }

        if (!_library.TryGet(cue.CueId, out AudioStream stream))
        {
            return null;
        }

        if (_lastPlayed.Count >= MaxRemembered)
        {
            _lastPlayed.Clear();
        }

        _lastPlayed[(cue.CueId, AnyEmitter)] = now;
        _lastPlayed[(cue.CueId, emitter)] = now;
        return PlayPositional(stream, cue.CueId, position, cue.VolumeDb, cue.PitchScale);
    }

    /// <summary>A wind-up began. The riser plays under it when it is long enough to have one, and
    /// the cast cue that the same call raises next is held for the release.</summary>
    private void OnCastWindup(CastWindupStartedEvent e)
    {
        ulong id = e.Caster.RuntimeId;
        StopRiser(id);
        double now = Now;
        if (_windups.Count >= MaxRemembered)
        {
            _windups.Clear();
        }

        PositionalSfxPlayer? riser = null;
        AudioStream? riserStream = null;
        double riserEnds = 0d;
        if (SpellAudio.PlaysWindup(e.WindupSeconds) && TryPosition(e.Caster, 1.2f, out Vector3 at))
        {
            SpellCue cue = SpellAudio.WindupRiser(e.WindupSeconds, CombatPerspective.IsPlayer(e.Caster));
            riser = PlaySpellCue(cue, at, EmitterOf(e.Caster));
            if (riser != null)
            {
                riserStream = riser.Stream;
                riserEnds = now + (riserStream.GetLength() / cue.PitchScale);
            }
        }

        _windups[id] = new PendingCast(
            e.SpellId, now + e.WindupSeconds + WindupGraceSeconds, riser, riserStream, riserEnds);
    }

    /// <summary>A spell was cast. One still in its wind-up sounds when the action releases it; anything
    /// else (an actor with no action timeline, a channel's tick, a support cast) sounds now.</summary>
    private void OnSpellCast(SpellCastEvent e)
    {
        if (_windups.TryGetValue(e.Caster.RuntimeId, out PendingCast pending) && pending.SpellId == e.SpellId &&
            Now < pending.Expires)
        {
            return;
        }

        PlayCast(e.Caster, e.SpellId);
    }

    /// <summary>The cast action reached its release: the spell leaves, and so does its sound.</summary>
    private void OnActionReleased(ActionReleasedEvent e)
    {
        if (e.Kind != ActionKind.Cast || !_windups.Remove(e.Actor.RuntimeId, out PendingCast pending))
        {
            return;
        }

        if (Now < pending.Expires)
        {
            PlayCast(e.Actor, pending.SpellId);
        }
    }

    /// <summary>A cast was knocked out of its caster: the riser stops and the spell fizzles.</summary>
    private void OnSpellInterrupted(SpellInterruptedEvent e)
    {
        StopRiser(e.Caster.RuntimeId);
        _windups.Remove(e.Caster.RuntimeId);
        if (TryPosition(e.Caster, 1.2f, out Vector3 at))
        {
            PlaySpellCue(SpellAudio.OneShot(SpellAudio.Fizzle, CombatPerspective.IsPlayer(e.Caster), GD.Randf()), at);
        }
    }

    private void PlayCast(IEntity caster, string spellId)
    {
        if (!TryPosition(caster, 1.2f, out Vector3 at))
        {
            return;
        }

        if (SpellDatabase.Get(spellId) is not { } spell)
        {
            PlayCue(SpellAudio.FallbackCast, at);
            return;
        }

        bool channelled = spell.CastMode == CastMode.Channeled;
        SpellCue cue = SpellAudio.Cast(
            spell.School,
            blinks: spell.BlinkDistance > 0f,
            heals: spell.Healing > 0f && spell.BaseDamage <= 0f,
            channelled,
            spell.ImpactWeight,
            CombatPerspective.IsPlayer(caster),
            GD.Randf());
        PlaySpellCue(cue, at, EmitterOf(caster), SpellAudio.CastGap(channelled));
    }

    /// <summary>Stops a caster's riser if it is still that riser: the player is pooled, so one that
    /// has run to its end is left alone (it may be another sound by now).</summary>
    private void StopRiser(ulong casterId)
    {
        if (!_windups.TryGetValue(casterId, out PendingCast pending) || pending.Riser is not { } riser ||
            !IsInstanceValid(riser))
        {
            return;
        }

        if (Now < pending.RiserEnds - RiserEndMargin && riser.IsInsideTree() && riser.Playing &&
            riser.Stream == pending.RiserStream)
        {
            riser.Stop(); // Stop does not raise Finished, so the pool takes it back here.
            _sfxPool.Return(riser);
        }

        _windups[casterId] = pending with { Riser = null, RiserStream = null };
    }

    private static DamageType SchoolOf(string spellId) =>
        SpellDatabase.Get(spellId)?.School ?? DamageType.Physical;

    /// <summary>A spell landed on a target: the school's impact, softer when a zone's pulse landed it.</summary>
    private void OnSpellImpact(SpellImpactEvent e)
    {
        if (!TryPosition(e.Target, 1.1f, out Vector3 at))
        {
            return;
        }

        DamageType school = SchoolOf(e.SpellId);
        bool byPlayer = CombatPerspective.IsPlayer(e.Caster);
        bool zonePulse = e.SpellId == _zonePulseSpell && SpellAudio.TooSoon(Now, _zonePulseAt, SpellAudio.CoalesceSeconds);
        PlaySpellCue(SpellAudio.Impact(school, zonePulse, e.Weight, e.Charge, byPlayer, GD.Randf()), at);
        if (!zonePulse)
        {
            PlayThunder(school, e.Weight, e.Charge, byPlayer, at);
        }
    }

    /// <summary>A spell burst over an area: the school's blast. A lingering zone pulses every tick,
    /// so its blast is quieter and held to one in <see cref="SpellAudio.ZoneGapSeconds"/> per caster.</summary>
    private void OnSpellBurst(SpellBurstEvent e)
    {
        bool zonePulse = e.Source == SpellBurstSource.Zone;
        if (zonePulse)
        {
            // The hits this pulse lands arrive next, in the same resolution.
            _zonePulseSpell = e.SpellId;
            _zonePulseAt = Now;
        }

        DamageType school = SchoolOf(e.SpellId);
        bool byPlayer = CombatPerspective.IsPlayer(e.Caster);
        SpellCue cue = SpellAudio.Burst(school, zonePulse, e.Weight, e.Charge, byPlayer, GD.Randf());
        PlaySpellCue(cue, e.Position, zonePulse ? EmitterOf(e.Caster) : AnyEmitter, SpellAudio.BurstGap(zonePulse));
        if (!zonePulse)
        {
            PlayThunder(school, e.Weight, e.Charge, byPlayer, e.Position);
        }
    }

    private void PlayThunder(DamageType school, float weight, float charge, bool byPlayer, Vector3 at)
    {
        if (SpellAudio.Thunders(school, weight, charge))
        {
            PlaySpellCue(SpellAudio.OneShot(SpellAudio.Thunder, byPlayer, GD.Randf()), at, AnyEmitter,
                SpellAudio.ThunderGapSeconds);
        }
    }

    // A ward breaking and a freeze taking hold are news to the player whoever they happen to, so
    // neither takes the enemy's 3 dB off.
    private void OnWardBroken(WardBrokenEvent e)
    {
        if (TryPosition(e.Target, 1.1f, out Vector3 at))
        {
            PlaySpellCue(SpellAudio.OneShot(SpellAudio.WardBreak, byPlayer: true, GD.Randf()), at);
        }
    }

    private void OnStatusApplied(StatusEffectAppliedEvent e)
    {
        if (e.EffectId == StatusIds.Frozen && TryPosition(e.Target, 1.1f, out Vector3 at))
        {
            PlaySpellCue(SpellAudio.OneShot(SpellAudio.Freeze, byPlayer: true, GD.Randf()), at);
        }
    }

    /// <summary>A load restores state, it does not narrate it: the casts the old timeline was waiting
    /// on are dropped, and what the restore re-applies (a saved freeze) makes no sound.</summary>
    private void OnGameLoading(GameLoadingEvent _)
    {
        foreach (ulong id in new List<ulong>(_windups.Keys))
        {
            StopRiser(id);
        }

        _windups.Clear();
        _restoring = true;
        SetProcess(true);
    }

    private void OnGameLoaded(GameLoadedEvent _) => _restoring = false;

    // Level-up is a player-centric flourish — play it 2D (always centred/audible), not positional.
    private void OnLeveledUp(LeveledUpEvent e) => PlayCue("sfx.levelup");
}
