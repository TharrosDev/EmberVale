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

    // --- PickDispel: Arcane's on-hit identity (Phase 34E.5) -----------------

    private static (string, bool, double)[] Effects(params (string, bool, double)[] effects) => effects;

    [Fact]
    public void PickDispel_TakesTheLongestLastingBuff()
    {
        string? picked = StatusMath.PickDispel(Effects(
            ("status.regrowth", true, 2.0),
            ("status.arcane_ward", true, 8.0),
            ("status.other_ward", true, 5.0)));

        Assert.Equal("status.arcane_ward", picked);
    }

    /// <summary>The regression that matters: stripping a target's own burning would heal the fight
    /// for them, so a harmful effect must never be eligible however long it has left.</summary>
    [Fact]
    public void PickDispel_NeverStripsAHarmfulEffect()
    {
        Assert.Null(StatusMath.PickDispel(Effects(
            ("status.burning", false, 99.0),
            ("status.decay", false, 50.0),
            ("status.chill", false, 30.0))));
    }

    [Fact]
    public void PickDispel_IgnoresHarmfulEvenWhenItOutlastsTheBuff()
    {
        string? picked = StatusMath.PickDispel(Effects(
            ("status.burning", false, 99.0),
            ("status.regrowth", true, 1.0)));

        Assert.Equal("status.regrowth", picked);
    }

    [Fact]
    public void PickDispel_NullWhenNothingIsActive()
    {
        Assert.Null(StatusMath.PickDispel(Effects()));
    }

    /// <summary>Equal durations must resolve the same way every run — the caller enumerates a
    /// dictionary, whose order is not a contract, so the tie-break is what keeps a fight
    /// reproducible.</summary>
    [Fact]
    public void PickDispel_TieBreaksDeterministicallyRegardlessOfOrder()
    {
        string? forward = StatusMath.PickDispel(Effects(
            ("status.alpha", true, 4.0),
            ("status.beta", true, 4.0)));
        string? reversed = StatusMath.PickDispel(Effects(
            ("status.beta", true, 4.0),
            ("status.alpha", true, 4.0)));

        Assert.Equal("status.alpha", forward);
        Assert.Equal(forward, reversed);
    }

    [Fact]
    public void PickDispel_HandlesAnExpiringBuffWithNoTimeLeft()
    {
        Assert.Equal("status.regrowth", StatusMath.PickDispel(Effects(("status.regrowth", true, 0.0))));
    }
}
