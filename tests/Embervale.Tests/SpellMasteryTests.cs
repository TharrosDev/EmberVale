using Embervale.Magic;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// Covers the pure spell-rank damage curve: rank 1 is the base 1×, each rank above adds its per-rank
/// fraction.
/// </summary>
public class SpellMasteryTests
{
    [Fact]
    public void Rank1_IsBase()
    {
        Assert.Equal(1f, SpellMastery.DamageMultiplier(1, 0.3f), 3);
        Assert.Equal(1f, SpellMastery.DamageMultiplier(0, 0.3f), 3); // unknown spells don't scale below base
    }

    [Fact]
    public void HigherRanks_AddPerRank()
    {
        Assert.Equal(1.3f, SpellMastery.DamageMultiplier(2, 0.3f), 3);
        Assert.Equal(1.6f, SpellMastery.DamageMultiplier(3, 0.3f), 3);
    }
}
