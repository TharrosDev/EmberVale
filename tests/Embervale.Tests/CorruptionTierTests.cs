using Embervale.Corruption;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// Covers the pure tier mapping behind the corruption meter. The component's value clamping and
/// event firing run against Godot's <c>EventBus</c> and so are exercised in-engine (the 23B dev
/// console), but <see cref="CorruptionTiers.Of"/> is pure and load-bearing for every corruption
/// consequence (appearance, dialogue gates, endings), so it is pinned here.
/// </summary>
public class CorruptionTierTests
{
    [Theory]
    [InlineData(0, CorruptionTier.Untainted)]
    [InlineData(19, CorruptionTier.Untainted)]
    [InlineData(20, CorruptionTier.Touched)]
    [InlineData(39, CorruptionTier.Touched)]
    [InlineData(40, CorruptionTier.Marked)]
    [InlineData(59, CorruptionTier.Marked)]
    [InlineData(60, CorruptionTier.Ashbound)]
    [InlineData(79, CorruptionTier.Ashbound)]
    [InlineData(80, CorruptionTier.Embers)]
    [InlineData(100, CorruptionTier.Embers)]
    public void Of_MapsValueToTier(int value, CorruptionTier expected)
    {
        Assert.Equal(expected, CorruptionTiers.Of(value));
    }

    [Fact]
    public void Of_IsMonotonicAcrossTheRange()
    {
        CorruptionTier previous = CorruptionTier.Untainted;
        for (int value = CorruptionTiers.Min; value <= CorruptionTiers.Max; value++)
        {
            CorruptionTier tier = CorruptionTiers.Of(value);
            Assert.True(tier >= previous, $"Corruption {value} should not fall to a lower tier than the previous value.");
            previous = tier;
        }
    }
}
