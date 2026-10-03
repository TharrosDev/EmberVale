using Embervale.Magic;
using Xunit;

namespace Embervale.Tests;

public class MagicAuditNumericTests
{
    [Fact]
    public void DotArithmeticPreservesCountAndPhaseAcrossFramePartitions()
    {
        (int wholeTicks, double wholeTimer) = StatusMath.AdvanceDot(0.375d, 2.625d, 0.5d);
        int partitionTicks = 0;
        double partitionTimer = 0.375d;
        for (int i = 0; i < 21; i++)
        {
            (int ticks, double timer) = StatusMath.AdvanceDot(partitionTimer, 0.125d, 0.5d);
            partitionTicks += ticks;
            partitionTimer = timer;
        }

        Assert.Equal(5, wholeTicks);
        Assert.Equal(0.25d, wholeTimer);
        Assert.Equal(wholeTicks, partitionTicks);
        Assert.Equal(wholeTimer, partitionTimer);
    }

    [Theory]
    [InlineData(4d)]
    [InlineData(6d)]
    [InlineData(8d)]
    [InlineData(12d)]
    public void EntireAuthoredStatusLifetimeKeepsEveryDueTick(double duration)
    {
        (int ticks, double timer) = StatusMath.AdvanceDot(1d, duration, 1d);
        Assert.Equal((int)duration, ticks);
        Assert.Equal(1d, timer);
    }

    [Theory]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(double.NaN)]
    [InlineData(-1d)]
    public void InvalidDotElapsedTimeKeepsTheExistingFiniteTimer(double elapsed)
    {
        Assert.Equal((0, 0.25d), StatusMath.AdvanceDot(0.25d, elapsed, 1d));
    }

    [Theory]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(double.NaN)]
    [InlineData(0d)]
    [InlineData(-1d)]
    public void InvalidDotIntervalCannotGenerateTicksOrPoisonTheTimer(double interval)
    {
        Assert.Equal((0, 0.25d), StatusMath.AdvanceDot(0.25d, 5d, interval));
    }

    [Theory]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(double.NaN)]
    public void NonfiniteDotTimerResetsToTheValidInterval(double timer)
    {
        Assert.Equal((0, 1d), StatusMath.AdvanceDot(timer, 5d, 1d));
    }

    [Fact]
    public void ExtremeFiniteDotBacklogHasBoundedCallbacksAndKeepsCadence()
    {
        (int ticks, double timer) = StatusMath.AdvanceDot(0.5d, 1_000_000.75d, 1d);
        Assert.Equal(StatusMath.MaxCatchUpTicks, ticks);
        Assert.Equal(0.75d, timer);
        Assert.Equal((0, 0.5d), StatusMath.AdvanceDot(timer, 0.25d, 1d));
    }

    [Fact]
    public void TinyDotIntervalCannotStallCatchUpArithmetic()
    {
        (int ticks, double timer) = StatusMath.AdvanceDot(1d, double.MaxValue, double.Epsilon);
        Assert.Equal(StatusMath.MaxCatchUpTicks, ticks);
        Assert.True(double.IsFinite(timer));
        Assert.InRange(timer, double.Epsilon, double.Epsilon);
    }

    [Fact]
    public void OverflowingDotTimerDifferenceStillPreservesFiniteCountAndPhase()
    {
        (int ticks, double timer) = StatusMath.AdvanceDot(-double.MaxValue, double.MaxValue, double.MaxValue);
        Assert.Equal(3, ticks);
        Assert.Equal(double.MaxValue, timer);
    }

    [Theory]
    [InlineData(float.MaxValue, 0.25f)]
    [InlineData(1e20f, 0.25f)]
    [InlineData(1f, float.Epsilon)]
    [InlineData(float.PositiveInfinity, 0.25f)]
    public void HugeTravelSaturatesBeforeIntegerConversion(float distance, float radius)
    {
        Assert.Equal(16, SpellSweep.SubStepCount(distance, radius, 16));
    }

    [Theory]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    [InlineData(float.NaN)]
    public void InvalidSweepRadiusStillProducesOneTest(float radius)
    {
        Assert.Equal(1, SpellSweep.SubStepCount(float.PositiveInfinity, radius, 16));
    }

    [Fact]
    public void StreamingHomingSelectionKeepsBrandPriorityAndFirstTie()
    {
        var candidates = new (float DistanceSquared, bool Branded)[]
        {
            (1f, false), (25f, true), (9f, true), (9f, true), (0.1f, false),
        };
        int best = 0;
        for (int i = 1; i < candidates.Length; i++)
        {
            if (SpellHoming.IsPreferred(candidates[i].DistanceSquared, candidates[i].Branded,
                    candidates[best].DistanceSquared, candidates[best].Branded))
            {
                best = i;
            }
        }

        Assert.Equal(2, best);
        Assert.Equal(best, SpellHoming.Pick(candidates));
    }
}
