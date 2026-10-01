using Embervale.Magic;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// The per-school mastery curve: casts convert to ranks along fixed thresholds and cap, and each rank
/// gives a modest, readable step (power every rank, cooldown trims at 2 and 4, an attunement at 3).
/// The component wiring (cast tracking, save/load, the stat modifier) is Godot-bound and probed.
/// </summary>
public class SchoolMasteryMathTests
{
    [Theory]
    [InlineData(-5, 0)]
    [InlineData(0, 0)]
    [InlineData(9, 0)]
    [InlineData(10, 1)]
    [InlineData(24, 1)]
    [InlineData(25, 2)]
    [InlineData(45, 3)]
    [InlineData(70, 4)]
    [InlineData(99, 4)]
    [InlineData(100, 5)]
    [InlineData(9999, 5)] // capped at MaxRank
    public void RankForPoints_ClimbsPerThreshold_AndCaps(int points, int expectedRank)
    {
        Assert.Equal(expectedRank, SchoolMasteryMath.RankForPoints(points));
    }

    [Fact]
    public void Thresholds_AskMoreEachRank()
    {
        int previousStep = 0;
        for (int rank = 1; rank <= SchoolMasteryMath.MaxRank; rank++)
        {
            int step = SchoolMasteryMath.PointsForRank(rank) - SchoolMasteryMath.PointsForRank(rank - 1);
            Assert.True(step >= previousStep, $"rank {rank} asks {step}, less than the step before it");
            previousStep = step;
        }
    }

    [Fact]
    public void ProgressToNext_ReportsPointsIntoTheCurrentRank()
    {
        Assert.Equal((0, 10), SchoolMasteryMath.ProgressToNext(0));
        Assert.Equal((4, 15), SchoolMasteryMath.ProgressToNext(14)); // rank 1 began at 10, rank 2 needs 25
        Assert.Equal((0, 0), SchoolMasteryMath.ProgressToNext(100));  // capped
    }

    [Fact]
    public void PowerMultiplier_IsOnePlusPerRank_AndCapped()
    {
        Assert.Equal(1f, SchoolMasteryMath.PowerMultiplier(0), 3);
        Assert.Equal(1.08f, SchoolMasteryMath.PowerMultiplier(1), 3);
        Assert.Equal(1.40f, SchoolMasteryMath.PowerMultiplier(5), 3);
        Assert.Equal(1.40f, SchoolMasteryMath.PowerMultiplier(50), 3);
    }

    [Theory]
    [InlineData(0, 1.00f)]
    [InlineData(1, 1.00f)]
    [InlineData(2, 0.95f)]
    [InlineData(3, 0.95f)]
    [InlineData(4, 0.90f)]
    [InlineData(5, 0.90f)]
    public void CooldownMultiplier_TrimsAtRankTwoAndFour(int rank, float expected)
    {
        Assert.Equal(expected, SchoolMasteryMath.CooldownMultiplier(rank), 3);
    }

    [Theory]
    [InlineData(2, 0f)]
    [InlineData(3, 15f)]
    [InlineData(5, 15f)]
    public void Attunement_ArrivesAtRankThree(int rank, float expected)
    {
        Assert.Equal(expected, SchoolMasteryMath.AttunementResistFor(rank), 3);
    }

    [Fact]
    public void Channels_BankOnePointPerSecond_OthersAlways()
    {
        Assert.True(SchoolMasteryMath.BanksPoint(channelled: false, secondsSinceLast: 0d));
        Assert.True(SchoolMasteryMath.BanksPoint(channelled: true, secondsSinceLast: double.MaxValue));
        Assert.False(SchoolMasteryMath.BanksPoint(channelled: true, secondsSinceLast: 0.2d));
        Assert.True(SchoolMasteryMath.BanksPoint(channelled: true, secondsSinceLast: 1.0d));
    }

    [Theory]
    [InlineData(9, 10, 1)]
    [InlineData(10, 11, 0)]
    [InlineData(24, 25, 2)]
    [InlineData(99, 100, 5)]
    [InlineData(100, 101, 0)]
    [InlineData(30, 5, 0)] // a lowered total never "gains"
    public void RankGained_ReportsOnlyAThresholdCrossing(int before, int after, int expected)
    {
        Assert.Equal(expected, SchoolMasteryMath.RankGained(before, after));
    }
}
