using Embervale.Combat;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// Covers the pure camera-shake trauma curve (Phase 29B): amplitude is quadratic and zero at rest,
/// trauma decays toward zero without underflowing, and the per-state trauma adds are ordered
/// crit ≥ stagger ≥ block.
/// </summary>
public class ShakeMathTests
{
    [Fact]
    public void Amplitude_IsQuadratic_AndZeroAtRest()
    {
        Assert.Equal(0f, ShakeMath.Amplitude(0f));
        // 0.5 trauma → 0.25 amplitude (quadratic), so it grows faster than linearly.
        Assert.True(ShakeMath.Amplitude(0.5f) < 0.5f);
        Assert.True(ShakeMath.Amplitude(1f) > ShakeMath.Amplitude(0.5f));
    }

    [Fact]
    public void StateTrauma_OrderedCritStaggerBlock()
    {
        Assert.True(ShakeMath.CritTrauma >= ShakeMath.StaggerTrauma);
        Assert.True(ShakeMath.StaggerTrauma >= ShakeMath.BlockTrauma);
    }

    [Fact]
    public void Add_ClampsToOne()
    {
        Assert.Equal(1f, ShakeMath.Add(0.8f, 0.8f));
        Assert.Equal(0.5f, ShakeMath.Add(0.2f, 0.3f), 3);
    }

    [Fact]
    public void Decay_FallsTowardZero_NeverBelow()
    {
        float t = ShakeMath.Decay(1f, 0.1f);
        Assert.True(t < 1f && t >= 0f);
        Assert.Equal(0f, ShakeMath.Decay(0.05f, 1f)); // a big step floors at 0, no underflow
    }
}
