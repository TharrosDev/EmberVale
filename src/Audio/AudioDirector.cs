using Embervale.Core.Diagnostics;
using Embervale.Core.Events;
using Embervale.Core.Pooling;
using Embervale.Core.Services;
using Embervale.Items;
using Embervale.Magic;
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
/// </summary>
public partial class AudioDirector : Node
{
    private readonly AudioLibrary _library = new();
    private NodePool<PositionalSfxPlayer> _sfxPool = null!;
    private NodePool<OneShotAudioPlayer> _flatPool = null!;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        _sfxPool = new NodePool<PositionalSfxPlayer>(() => new PositionalSfxPlayer { Released = p => _sfxPool.Return(p) }, prewarm: 6);
        _flatPool = new NodePool<OneShotAudioPlayer>(() => new OneShotAudioPlayer { Released = p => _flatPool.Return(p) }, prewarm: 2);

        // Shared so MusicDirector reuses the built streams. Owned by this node: the registration
        // goes when the director does, without _ExitTree having to remember it.
        ServiceScope.RegisterOwned(this, _library);
        EventBus.Instance?.Subscribe<SoundCueRequestedEvent>(OnSoundCue);
        EventBus.Instance?.Subscribe<MusicCueRequestedEvent>(OnMusicCue);
        EventBus.Instance?.Subscribe<ItemPickedUpEvent>(OnItemPickedUp);
        EventBus.Instance?.Subscribe<SpellCastEvent>(OnSpellCast);
        EventBus.Instance?.Subscribe<LeveledUpEvent>(OnLeveledUp);
        Log.Info($"AudioDirector ready ({_library.Count} cues, {_library.RealCount} from real assets, buses={AudioServer.BusCount}).");
    }

    public override void _ExitTree()
    {
        EventBus.Instance?.Unsubscribe<SoundCueRequestedEvent>(OnSoundCue);
        EventBus.Instance?.Unsubscribe<MusicCueRequestedEvent>(OnMusicCue);
        EventBus.Instance?.Unsubscribe<ItemPickedUpEvent>(OnItemPickedUp);
        EventBus.Instance?.Unsubscribe<SpellCastEvent>(OnSpellCast);
        EventBus.Instance?.Unsubscribe<LeveledUpEvent>(OnLeveledUp);
        _sfxPool?.Clear();
        _flatPool?.Clear();
    }

    /// <summary>Plays a cue positionally in 3D (falls back to a flat play for a non-positional cue id).</summary>
    public void PlayCue(string cueId, Vector3 position)
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

        PositionalSfxPlayer player = _sfxPool.Get();
        AddChild(player);
        player.PlayCue(stream, AudioCueRouting.BusFor(cueId), position);
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

    private void OnSoundCue(SoundCueRequestedEvent e) => PlayCue(e.CueId, e.Position);

    private void OnMusicCue(MusicCueRequestedEvent e) => PlayCue(e.CueId);

    private void OnItemPickedUp(ItemPickedUpEvent e) => PlayCue("sfx.pickup", e.Owner.Body.GlobalPosition);

    private void OnSpellCast(SpellCastEvent e) => PlayCue("sfx.cast", e.Caster.Body.GlobalPosition);

    // Level-up is a player-centric flourish — play it 2D (always centred/audible), not positional.
    private void OnLeveledUp(LeveledUpEvent e) => PlayCue("sfx.levelup");
}
