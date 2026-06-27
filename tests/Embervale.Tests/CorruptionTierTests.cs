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

    [Theory]
    [InlineData(0, EndingPath.Dawnfire)]
    [InlineData(39, EndingPath.Dawnfire)]
    [InlineData(40, EndingPath.Undecided)]
    [InlineData(59, EndingPath.Undecided)]
    [InlineData(60, EndingPath.LordOfEmbers)]
    [InlineData(100, EndingPath.LordOfEmbers)]
    public void EligibilityOf_MapsValueToEndingPath(int value, EndingPath expected)
    {
        // The both-endings dial (Phase 23H): low corruption keeps Dawnfire open, high commits to
        // Lord of Embers, the band between is Undecided. Phase 49 consumes this.
        Assert.Equal(expected, CorruptionTiers.EligibilityOf(value));
    }

    // --- Transition (Phase 25.5C: load/apply tier-event sync) ---------------

    [Fact]
    public void Transition_UpwardAcrossBand_Changes()
    {
        var (old, @new, changed) = CorruptionTiers.Transition(10, 50);
        Assert.Equal(CorruptionTier.Untainted, old);
        Assert.Equal(CorruptionTier.Marked, @new);
        Assert.True(changed);
    }

    [Fact]
    public void Transition_DownwardAcrossBand_Changes()
    {
        // The 25.5C bug: an in-session load that lowers corruption must still report a change so
        // appearance/vignette/dread reset. The old Load() assumed an Untainted baseline and missed this.
        var (old, @new, changed) = CorruptionTiers.Transition(85, 10);
        Assert.Equal(CorruptionTier.Embers, old);
        Assert.Equal(CorruptionTier.Untainted, @new);
        Assert.True(changed);
    }

    [Fact]
    public void Transition_WithinSameBand_DoesNotChange()
    {
        var (old, @new, changed) = CorruptionTiers.Transition(40, 55); // both Marked
        Assert.Equal(CorruptionTier.Marked, old);
        Assert.Equal(CorruptionTier.Marked, @new);
        Assert.False(changed);
    }

    [Fact]
    public void Transition_BetweenTwoNonUntaintedTiers_Changes()
    {
        var (old, @new, changed) = CorruptionTiers.Transition(85, 50); // Embers -> Marked
        Assert.Equal(CorruptionTier.Embers, old);
        Assert.Equal(CorruptionTier.Marked, @new);
        Assert.True(changed);
    }
}
