using Embervale.Enemies;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// Pins the two pure decision helpers behind the Phase 34A behaviour profiles. Both are load-bearing
/// and invisible in play until they're wrong: a broken fan-out puts a whole warband in single file,
/// and a broken guard rhythm leaves a shield-carrier either permanently open or permanently blocking.
///
/// The profile resource itself is a Godot <c>Resource</c>, so it stays out of this project by the
/// no-GodotObject rule in the csproj — the validator covers its authored values instead.
/// </summary>
public class AiProfileBehaviourTests
{
    // --- PackFlank ---------------------------------------------------------

    [Fact]
    public void ApproachAngle_LeadMemberChargesStraightIn() =>
        Assert.Equal(0f, PackFlank.ApproachAngle(0, 40f));

    [Fact]
    public void ApproachAngle_NoSpreadKeepsEveryoneOnTheLine()
    {
        for (int slot = 0; slot < 5; slot++)
        {
            Assert.Equal(0f, PackFlank.ApproachAngle(slot, 0f));
        }
    }

    [Theory]
    [InlineData(1, 40f)]
    [InlineData(2, -40f)]
    [InlineData(3, 80f)]
    [InlineData(4, -80f)]
    public void ApproachAngle_FansAlternatelyOutward(int slot, float expected) =>
        Assert.Equal(expected, PackFlank.ApproachAngle(slot, 40f));

    [Fact]
    public void ApproachAngle_ClampsSoNobodyPathsTheLongWayRound()
    {
        // Slot 9 would otherwise want 5 x 40 = 200 degrees, i.e. round the back through the wall.
        Assert.Equal(PackFlank.MaxAngleDegrees, PackFlank.ApproachAngle(9, 40f));
        Assert.Equal(-PackFlank.MaxAngleDegrees, PackFlank.ApproachAngle(10, 40f));
    }

    [Fact]
    public void ApproachAngle_NegativeSlotIsTreatedAsTheLead() =>
        Assert.Equal(0f, PackFlank.ApproachAngle(-3, 40f));

    // --- GuardCycle --------------------------------------------------------

    [Fact]
    public void IsUp_ProfileWithNoBlockDurationNeverGuards()
    {
        Assert.False(GuardCycle.IsUp(0d, 0f, 1.5f));
        Assert.False(GuardCycle.IsUp(5d, 0f, 1.5f));
    }

    [Theory]
    [InlineData(0.0, true)]     // the fight opens on the guard
    [InlineData(1.3, true)]
    [InlineData(1.5, false)]    // guard drops — the punish window
    [InlineData(2.9, false)]
    [InlineData(3.0, true)]     // and the rhythm repeats
    [InlineData(4.4, true)]
    [InlineData(4.5, false)]
    public void IsUp_AlternatesOnAReadableRhythm(double elapsed, bool expected) =>
        Assert.Equal(expected, GuardCycle.IsUp(elapsed, 1.5f, 1.5f));

    [Fact]
    public void IsUp_NoRecoveryWindowMeansAPermanentGuard() =>
        Assert.True(GuardCycle.IsUp(99d, 1.5f, 0f));

    /// <summary>Elapsed should never be negative, but C#'s % keeps the sign — without the wrap the
    /// rhythm would invert on the first frame instead of reading as the tail of a cycle.</summary>
    [Fact]
    public void IsUp_NegativeElapsedWrapsIntoTheCycleInsteadOfInverting() =>
        Assert.False(GuardCycle.IsUp(-0.2d, 1.5f, 1.5f));

    [Fact]
    public void IsUp_RhythmIsPeriodic()
    {
        for (int cycle = 0; cycle < 4; cycle++)
        {
            Assert.True(GuardCycle.IsUp(cycle * 3.0, 1.5f, 1.5f));
            Assert.False(GuardCycle.IsUp((cycle * 3.0) + 2.0, 1.5f, 1.5f));
        }
    }
}
