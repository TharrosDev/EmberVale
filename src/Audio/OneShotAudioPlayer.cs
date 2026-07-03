using System;
using Godot;

namespace Embervale.Audio;

/// <summary>
/// A poolable non-positional one-shot sound (Phase 31A) for 2D cues — music stings, UI clicks — that
/// have no world position. Same reclaim contract as <see cref="PositionalSfxPlayer"/>: on
/// <see cref="AudioStreamPlayer.Finished"/> it invokes <see cref="Released"/> (the pool reclaims it) or
/// frees itself when there is none.
/// </summary>
public partial class OneShotAudioPlayer : AudioStreamPlayer
{
    /// <summary>Reclaim callback (the pool's <c>Return</c>). When null, the player frees itself.</summary>
    public Action<OneShotAudioPlayer>? Released { get; set; }

    public override void _Ready() => Finished += OnFinished;

    /// <summary>(Re)arms and plays the cue on the given bus. Add to the tree first.</summary>
    public void PlayCue(AudioStream stream, StringName bus)
    {
        Bus = bus;
        Stream = stream;
        Play();
    }

    private void OnFinished()
    {
        if (Released != null)
        {
            Released(this);
        }
        else
        {
            QueueFree();
        }
    }
}
