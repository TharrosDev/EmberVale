using Embervale.Audio;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// Pins <see cref="AmbientEmitterComponent.NextInterval"/> (Phase 38K second pass). The interesting
/// failures are both at the ends of the jitter range, and both are audible rather than crashy: a zero
/// spread turns an ambience emitter into a metronome, and a full spread with an unlucky roll asks for a
/// zero-length wait — an emitter that fires every frame, which is a wall of sound and not an ambience.
/// </summary>
public class AmbientEmitterTests
{
    [Fact]
    public void NoJitterAlwaysReturnsTheMeanInterval()
    {
        Assert.Equal(6d, AmbientEmitterComponent.NextInterval(6f, 0f, 0f), 4);
        Assert.Equal(6d, AmbientEmitterComponent.NextInterval(6f, 0f, 0.5f), 4);
        Assert.Equal(6d, AmbientEmitterComponent.NextInterval(6f, 0f, 1f), 4);
    }

    [Fact]
    public void JitterSpreadsSymmetricallyAroundTheMean()
    {
        // spread 0.5 => the roll walks the interval across [0.5x, 1.5x], mean at roll 0.5.
        Assert.Equal(3d, AmbientEmitterComponent.NextInterval(6f, 0.5f, 0f), 4);
        Assert.Equal(6d, AmbientEmitterComponent.NextInterval(6f, 0.5f, 0.5f), 4);
        Assert.Equal(9d, AmbientEmitterComponent.NextInterval(6f, 0.5f, 1f), 4);
    }

    [Fact]
    public void AFullSpreadOnTheUnluckiestRollStillWaits()
    {
        // 1 - 1 + 2*1*0 == 0, so without the floor this is a cue every single frame.
        Assert.True(AmbientEmitterComponent.NextInterval(6f, 1f, 0f) >= 0.05d);
        Assert.True(AmbientEmitterComponent.NextInterval(0.0001f, 1f, 0f) >= 0.05d);
    }

    [Fact]
    public void OutOfRangeInputsAreClampedRatherThanTrusted()
    {
        // A negative interval or an out-of-range roll comes from hand-authored .tscn exports and from
        // a future RNG that is not GD.Randf; neither may produce a negative wait.
        Assert.True(AmbientEmitterComponent.NextInterval(-4f, 0.5f, 0.5f) > 0d);
        Assert.Equal(AmbientEmitterComponent.NextInterval(6f, 0.5f, 1f),
                     AmbientEmitterComponent.NextInterval(6f, 0.5f, 4f), 4);
        Assert.Equal(AmbientEmitterComponent.NextInterval(6f, 1f, 0f),
                     AmbientEmitterComponent.NextInterval(6f, 4f, -2f), 4);
    }
}
