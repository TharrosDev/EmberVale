using Embervale.Factions;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// Pins the reputation value → <see cref="ReputationTier"/> bands. Hostility is decided by
/// <c>TierOf(faction) &lt;= HostileThreshold</c>, so these band edges are the flip points; the mapping
/// is pure (Godot-free) and checked directly across the full range.
/// </summary>
public class ReputationTierTests
{
    [Theory]
    [InlineData(-100, ReputationTier.Hated)]
    [InlineData(-75, ReputationTier.Hated)]    // top of Hated
    [InlineData(-74, ReputationTier.Hostile)]  // bottom of Hostile
    [InlineData(-25, ReputationTier.Hostile)]  // top of Hostile
    [InlineData(-24, ReputationTier.Unfriendly)]
    [InlineData(-1, ReputationTier.Unfriendly)] // just below neutral
    [InlineData(0, ReputationTier.Neutral)]
    [InlineData(24, ReputationTier.Neutral)]
    [InlineData(25, ReputationTier.Friendly)]
    [InlineData(59, ReputationTier.Friendly)]
    [InlineData(60, ReputationTier.Honored)]
    [InlineData(89, ReputationTier.Honored)]
    [InlineData(90, ReputationTier.Allied)]
    [InlineData(100, ReputationTier.Allied)]
    public void Of_MapsValueToBandAtTheBoundaries(int value, ReputationTier expected)
    {
        Assert.Equal(expected, ReputationTiers.Of(value));
    }

    [Fact]
    public void Of_IsMonotonicNonDecreasingAcrossTheRange()
    {
        int previous = (int)ReputationTiers.Of(ReputationTiers.Min);
        for (int v = ReputationTiers.Min; v <= ReputationTiers.Max; v++)
        {
            int tier = (int)ReputationTiers.Of(v);
            Assert.True(tier >= previous, $"tier must not drop as reputation rises (value {v})");
            previous = tier;
        }
    }
}
