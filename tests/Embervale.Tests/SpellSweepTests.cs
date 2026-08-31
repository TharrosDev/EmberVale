using Embervale.Magic;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// Pins the rule that stops a fast spell passing through a thin target or wall: a bolt never moves
/// further than its own radius between two collision tests. Before the 2026-08-30 debugging pass a
/// projectile moved its whole frame's travel in one go and tested only where it landed, so anything
/// in the gap was skipped entirely.
/// </summary>
public class SpellSweepTests
{
    private const float Radius = 0.25f;
    private const int MaxSteps = 16;

    [Theory]
    // (speed m/s, frame seconds) — a slow bolt at 60 Hz, a fast one at 60 Hz, and a 30 Hz hitch.
    [InlineData(8f, 1f / 60f)]
    [InlineData(40f, 1f / 60f)]
    [InlineData(40f, 1f / 30f)]
    [InlineData(60f, 1f / 30f)]
    public void NoStepIsLongerThanTheBolt(float speed, float frameSeconds)
    {
        float distance = speed * frameSeconds;
        Assert.True(SpellSweep.SubStepLength(distance, Radius, MaxSteps) <= Radius + 1e-4f);
    }

    [Fact]
    public void ASingleStepIsEnoughWhenTheTravelIsShorterThanTheBolt()
    {
        Assert.Equal(1, SpellSweep.SubStepCount(0.2f, Radius, MaxSteps));
    }

    [Fact]
    public void TheStepCountIsCappedSoAnAbsurdSpeedCannotSpin()
    {
        Assert.Equal(MaxSteps, SpellSweep.SubStepCount(distance: 10_000f, Radius, MaxSteps));
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(-1f)]
    public void AStationaryOrBackwardsTravelStillTestsOnce(float distance)
    {
        Assert.Equal(1, SpellSweep.SubStepCount(distance, Radius, MaxSteps));
    }

    [Fact]
    public void ADegenerateRadiusDoesNotDivideByZero()
    {
        Assert.Equal(1, SpellSweep.SubStepCount(5f, radius: 0f, MaxSteps));
    }

    /// <summary>The regression itself, stated as the defect: at the speeds the game authors, one
    /// step per frame leaves a gap wider than a wall.</summary>
    [Fact]
    public void OneStepPerFrameWouldLeaveAGapWiderThanTheBolt()
    {
        float distance = 40f * (1f / 60f);
        Assert.True(distance > Radius, "the test's premise: a fast bolt outruns its own radius");
        Assert.True(SpellSweep.SubStepCount(distance, Radius, MaxSteps) > 1);
    }
}
