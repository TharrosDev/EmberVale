using System;
using System.Collections.Generic;
using System.Linq;
using Embervale.Bootstrap;
using Embervale.World;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// Safe placement's pure half: the ring search's candidate order and coverage, the refusal tally
/// that turns "placement failed" into a diagnosis, and the loading gate's timing rules.
/// </summary>
public sealed class SafePlacementSearchTests
{
    [Fact]
    public void RingsGoNearestFirstAndStayInsideTheRadius()
    {
        List<(float X, float Z)> offsets = PlacementSearch.RingOffsets(3f, 1f, 1.2f).ToList();
        Assert.NotEmpty(offsets);
        float previous = 0f;
        foreach ((float x, float z) in offsets)
        {
            float r = MathF.Sqrt((x * x) + (z * z));
            Assert.True(r <= 3f + 1e-4f);
            Assert.True(r >= previous - 1e-4f, "a further ring was tried before a nearer one");
            previous = r;
        }
    }

    [Fact]
    public void EveryRingHasAtLeastSixPointsAndCoversEveryDirection()
    {
        List<(float X, float Z)> ring = PlacementSearch.RingOffsets(1f, 1f, 5f).ToList();
        Assert.Equal(6, ring.Count);
        // Six points round a circle leave no gap wider than 60 degrees.
        List<float> angles = ring.Select(o => MathF.Atan2(o.Z, o.X)).OrderBy(a => a).ToList();
        for (int i = 0; i < angles.Count; i++)
        {
            float next = i + 1 < angles.Count ? angles[i + 1] : angles[0] + MathF.Tau;
            Assert.True(next - angles[i] <= (MathF.Tau / 6f) + 1e-3f);
        }
    }

    [Fact]
    public void TheCandidateCountMatchesTheEnumeration()
    {
        Assert.Equal(PlacementSearch.RingOffsets(3f, 1f, 1.2f).Count(), PlacementSearch.CandidateCount(3f, 1f, 1.2f));
        // The default search stays cheap enough to run on a failed placement every frame.
        Assert.InRange(PlacementSearch.CandidateCount(
            SafePlacementService.DefaultSearchRadius, SafePlacementService.SearchSpacing,
            SafePlacementService.SearchArcSpacing), 12, 48);
    }

    [Theory]
    [InlineData(0f, 1f, 1f)]
    [InlineData(-1f, 1f, 1f)]
    [InlineData(3f, 0f, 1f)]
    [InlineData(3f, 1f, 0f)]
    [InlineData(float.NaN, 1f, 1f)]
    public void ANonPositiveSearchIsOff(float radius, float spacing, float arc)
    {
        Assert.Empty(PlacementSearch.RingOffsets(radius, spacing, arc));
    }

    [Fact]
    public void TheReportNamesTheDominantRefusal()
    {
        var report = new SafePlacementReport();
        report.Record(PlacementRejection.Blocked);
        report.Record(PlacementRejection.NoGround);
        report.Record(PlacementRejection.NoGround);
        Assert.False(report.Succeeded);
        Assert.Equal(3, report.Attempts);
        Assert.Equal(PlacementRejection.NoGround, report.Dominant);
        string summary = report.Summary();
        Assert.Contains("no candidate accepted", summary);
        Assert.Contains("NoGround×2", summary);
        Assert.Contains("Blocked×1", summary);
    }

    [Fact]
    public void TheReportRemembersWhichCandidateWon()
    {
        var report = new SafePlacementReport();
        report.Record(PlacementRejection.TooSteep);
        report.Record(PlacementRejection.None);
        report.Record(PlacementRejection.None); // a later success does not move the winner
        Assert.True(report.Succeeded);
        Assert.Equal(1, report.AcceptedIndex);
        Assert.StartsWith("placed on candidate 1", report.Summary());
    }

    [Fact]
    public void AnEmptyReportHasNoDominantRefusal()
    {
        var report = new SafePlacementReport();
        Assert.Equal(PlacementRejection.None, report.Dominant);
        Assert.Equal(-1, report.AcceptedIndex);
    }

    [Theory]
    [InlineData(false, false, false, LoadingWait.Streamer)]
    [InlineData(false, true, true, LoadingWait.Streamer)]
    [InlineData(true, false, false, LoadingWait.Collision)]
    [InlineData(true, true, false, LoadingWait.Placement)]
    [InlineData(true, true, true, LoadingWait.None)]
    public void TheGateWaitsOnTheFirstUnclearedStage(bool streamer, bool ground, bool placed, LoadingWait expected)
    {
        Assert.Equal(expected, LoadingGateRules.Pending(streamer, ground, placed));
    }

    [Fact]
    public void ProgressIsReportedOncePerInterval()
    {
        Assert.False(LoadingGateRules.ReportDue(1d, 0d, 5d));
        Assert.True(LoadingGateRules.ReportDue(5d, 0d, 5d));
        Assert.False(LoadingGateRules.ReportDue(7d, 5d, 5d));
        Assert.True(LoadingGateRules.ReportDue(10.1d, 5d, 5d));
        Assert.False(LoadingGateRules.ReportDue(100d, 0d, 0d), "an interval of zero turns reporting off");
    }

    [Fact]
    public void PlacementRetriesAreBounded()
    {
        Assert.False(LoadingGateRules.PlacementExhausted(19, 20));
        Assert.True(LoadingGateRules.PlacementExhausted(20, 20));
    }
}
