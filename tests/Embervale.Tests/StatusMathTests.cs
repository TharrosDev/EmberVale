using Embervale.Magic;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// Pins the DoT tick cadence behind <see cref="StatusEffectsComponent"/>. The component applies the
/// damage through Godot stats; the catch-up arithmetic — how many ticks a frame fires and the
/// carry-over to the next — lives in <see cref="StatusMath.AdvanceDot"/> and is exercised here.
/// </summary>
public class StatusMathTests
{
    private const double Tolerance = 1e-9;

    [Fact]
    public void AdvanceDot_BeforeIntervalElapses_FiresNoTick()
    {
        (int ticks, double timer) = StatusMath.AdvanceDot(1.0, 0.4, 1.0);
        Assert.Equal(0, ticks);
        Assert.Equal(0.6, timer, Tolerance);
    }

    [Fact]
    public void AdvanceDot_AtTheBoundary_FiresExactlyOneTick()
    {
        (int ticks, double timer) = StatusMath.AdvanceDot(1.0, 1.0, 1.0);
        Assert.Equal(1, ticks);
        Assert.Equal(1.0, timer, Tolerance); // timer hit 0, reset to a full interval
    }

    [Fact]
    public void AdvanceDot_LargeDelta_CatchesUpEveryMissedTick()
    {
        // Timer 0.5, then 2.6s elapse over a 1.0s interval: ticks at -0.5, +0.5 over... → 3 ticks.
        (int ticks, double timer) = StatusMath.AdvanceDot(0.5, 2.6, 1.0);
        Assert.Equal(3, ticks);
        Assert.Equal(0.9, timer, Tolerance);
    }

    [Fact]
    public void AdvanceDot_CarriesTheRemainderTowardTheNextTick()
    {
        (int ticks, double timer) = StatusMath.AdvanceDot(0.3, 0.5, 0.5);
        Assert.Equal(1, ticks);
        Assert.Equal(0.3, timer, Tolerance);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    public void AdvanceDot_NonPositiveInterval_IsANoOp(double interval)
    {
        (int ticks, double timer) = StatusMath.AdvanceDot(1.0, 5.0, interval);
        Assert.Equal(0, ticks);
        Assert.Equal(1.0, timer, Tolerance);
    }
}
