using Embervale.Enemies;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// Covers the Ashen variant's reward curve and the invariant the whole affliction rests on: being
/// corrupted must never be a downgrade. Applying it needs a live entity (stat modifiers, materials),
/// so <c>Afflict</c> itself is exercised in-engine; the arithmetic is pinned here.
/// </summary>
public class AshenAfflictionTests
{
    [Theory]
    [InlineData(10, 15)]
    [InlineData(22, 33)]   // enemy.wolf
    [InlineData(80, 120)]  // enemy.stone_sentinel
    [InlineData(1, 2)]     // rounds up rather than vanishing
    [InlineData(95, 142)]  // enemy.arcane_echo: 142.5 rounds to even, not up
    public void AfflictedXp_AddsHalfAgain(int baseXp, int expected)
    {
        Assert.Equal(expected, AshenAffliction.AfflictedXp(baseXp));
    }

    /// <summary>A zero or negative base must pass through untouched rather than being scaled into a
    /// stranger number — an archetype that grants no XP shouldn't start granting some when corrupted.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void AfflictedXp_LeavesNonPositiveAlone(int baseXp)
    {
        Assert.Equal(baseXp, AshenAffliction.AfflictedXp(baseXp));
    }

    [Fact]
    public void AfflictedXp_IsAlwaysWorthAtLeastTheBase()
    {
        for (int baseXp = 1; baseXp <= 500; baseXp++)
        {
            Assert.True(
                AshenAffliction.AfflictedXp(baseXp) >= baseXp,
                $"an Ashen kill paid less than a plain one at base {baseXp}");
        }
    }

    /// <summary>The invariant behind the fiction: corruption makes a creature worse to fight, never
    /// better. A negative bonus here would quietly turn every Ashen spawn into a weaker one.</summary>
    [Fact]
    public void Bonuses_OnlyEverStrengthen()
    {
        Assert.True(AshenAffliction.HealthBonus > 0f);
        Assert.True(AshenAffliction.PowerBonus > 0f);
    }

    [Fact]
    public void ModifierSource_IsTaggedSoTheAfflictionCanBeIdentified()
    {
        Assert.False(string.IsNullOrWhiteSpace(AshenAffliction.ModifierSource));
    }
}
