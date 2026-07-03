using System;
using Godot;

namespace Embervale.Audio;

/// <summary>
/// A poolable 3D one-shot sound (Phase 31A), mirroring the <see cref="Combat.ImpactEffect"/> pattern:
/// on <see cref="AudioStreamPlayer3D.Finished"/> it invokes <see cref="Released"/> (the pool reclaims it)
/// instead of freeing. With no callback it frees itself. The <c>Finished</c> connection is made once in
/// <see cref="_Ready"/>; reuse re-enters the tree without re-running <c>_Ready</c>, so it stays wired.
/// </summary>
public partial class PositionalSfxPlayer : AudioStreamPlayer3D
{
    /// <summary>Reclaim callback (the pool's <c>Return</c>). When null, the player frees itself.</summary>
    public Action<PositionalSfxPlayer>? Released { get; set; }

    public override void _Ready() => Finished += OnFinished;

    /// <summary>(Re)arms and plays the cue at a world position on the given bus. Add to the tree first.</summary>
    public void PlayCue(AudioStream stream, StringName bus, Vector3 position)
    {
        Bus = bus;
        Stream = stream;
        GlobalPosition = position;
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
