using Embervale.Magic;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// The per-school mastery curve (Phase 29.5C): casts convert to ranks slowly and cap, and each rank
/// empowers the school by a fixed share. The component wiring (cast tracking, save/load) is Godot-bound
/// and verified by build/run.
/// </summary>
public class SchoolMasteryMathTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(9, 0)]
    [InlineData(10, 1)]
    [InlineData(25, 2)]
    [InlineData(50, 5)]
    [InlineData(999, 5)] // capped at MaxRank
    public void RankForPoints_ClimbsPerThreshold_AndCaps(int points, int expectedRank)
    {
        Assert.Equal(expectedRank, SchoolMasteryMath.RankForPoints(points));
    }

    [Fact]
    public void PowerMultiplier_IsOnePlusPerRank()
    {
        Assert.Equal(1f, SchoolMasteryMath.PowerMultiplier(0), 3);
        Assert.Equal(1.08f, SchoolMasteryMath.PowerMultiplier(1), 3);
        Assert.Equal(1.40f, SchoolMasteryMath.PowerMultiplier(5), 3);
    }
}
