using Embervale.World;
using Xunit;

namespace Embervale.Tests;

public sealed class WorldPerformanceRulesTests
{
    private static readonly WorldPerformanceLimits Limits = new(
        700, 4800, 9000, 256, 800, 1800, 14000, 2048d, 25d);

    [Fact]
    public void ValidBudget_IsAccepted()
    {
        Assert.True(WorldPerformanceRules.Valid(Limits));
    }

    [Fact]
    public void ZeroShippingLimit_IsRejected()
    {
        Assert.False(WorldPerformanceRules.Valid(Limits with { MaxDrawCalls = 0 }));
    }

    [Fact]
    public void SnapshotAtLimits_Passes()
    {
        var snapshot = new WorldPerformanceSnapshot(9000, 800, 1800, 14000, 2048d, 25d);
        Assert.Empty(WorldPerformanceRules.Assess(Limits, snapshot));
    }

    [Fact]
    public void SnapshotReportsEveryExceededDimension()
    {
        var snapshot = new WorldPerformanceSnapshot(9001, 801, 1801, 14001, 2049d, 26d);
        Assert.Equal(6, WorldPerformanceRules.Assess(Limits, snapshot).Count);
    }

    [Fact]
    public void FailureSignature_IgnoresChangingMeasurementsForTheSameDimensions()
    {
        var first = new WorldPerformanceSnapshot(9000, 800, 1800, 14000, 2048d, 26d);
        var later = first with { FrameMilliseconds = 41.75d };

        Assert.Equal("frame-time", WorldPerformanceRules.FailureSignature(Limits, first));
        Assert.Equal(
            WorldPerformanceRules.FailureSignature(Limits, first),
            WorldPerformanceRules.FailureSignature(Limits, later));
    }

    [Fact]
    public void FailureSignature_ChangesWhenAnotherBudgetDimensionStartsFailing()
    {
        var frameOnly = new WorldPerformanceSnapshot(9000, 800, 1800, 14000, 2048d, 26d);
        var frameAndDrawCalls = frameOnly with { DrawCalls = 1801 };

        Assert.Equal("frame-time", WorldPerformanceRules.FailureSignature(Limits, frameOnly));
        Assert.Equal("draw-calls,frame-time", WorldPerformanceRules.FailureSignature(Limits, frameAndDrawCalls));
    }

    [Theory]
    [InlineData(4, 1024d, 2048d, 4)]
    [InlineData(4, 2049d, 2048d, 1)]
    [InlineData(0, 100d, 2048d, 1)]
    public void StreamingConcurrency_ThrottlesUnderMemoryPressure(
        int authored, double currentMb, double limitMb, int expected)
    {
        Assert.Equal(expected, WorldPerformanceRules.ThreadedLoadConcurrency(authored, currentMb, limitMb));
    }

    [Fact]
    public void DistributionReportsTailPercentiles()
    {
        double[] frames = { 1d, 2d, 3d, 4d, 100d };
        WorldFrameDistribution result = WorldPerformanceRules.Distribution(frames);

        Assert.Equal(22d, result.Average);
        Assert.Equal(3d, result.P50);
        Assert.Equal(100d, result.P95);
        Assert.Equal(100d, result.P99);
        Assert.Equal(100d, result.Worst);
    }
}
