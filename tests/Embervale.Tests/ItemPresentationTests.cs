using System;
using System.Collections.Generic;
using System.Linq;
using Embervale.Items;
using Embervale.Stats;
using Embervale.UI;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// Phase 37.5C: the item screen's pure logic. All three of these are invisible when wrong and
/// plausible on screen, which is why they are pinned rather than eyeballed:
/// a missing glyph renders as a .notdef box, an unstable sort reshuffles the grid under the
/// player's cursor, and a comparison sign error tells them a downgrade is an upgrade.
/// </summary>
public class ItemPresentationTests
{
    // --- Glyphs -------------------------------------------------------------

    /// <summary>Every category needs its own silhouette — the glyph *is* the category read in a
    /// grid, so two sharing one would make two categories indistinguishable.</summary>
    [Fact]
    public void EveryCategoryHasADistinctGlyph()
    {
        var types = Enum.GetValues<ItemType>();
        var glyphs = types.Select(ItemPresentation.Glyph).ToList();

        Assert.All(glyphs, g => Assert.False(string.IsNullOrWhiteSpace(g)));
        Assert.Equal(types.Length, glyphs.Distinct().Count());
    }

    // --- Sorting ------------------------------------------------------------

    private static ItemPresentation.SortKey Key(string name, ItemRarity rarity, float weight, int value) =>
        new(name, (int)rarity, weight, value);

    [Fact]
    public void RaritySortPutsTheInterestingEndFirst()
    {
        var items = new List<ItemPresentation.SortKey>
        {
            Key("a", ItemRarity.Common, 1f, 1),
            Key("b", ItemRarity.Legendary, 1f, 1),
            Key("c", ItemRarity.Rare, 1f, 1),
        };

        var sorted = ItemPresentation.Sort(items, ItemPresentation.SortOrder.Rarity, k => k).ToList();
        Assert.Equal(new[] { "b", "c", "a" }, sorted.Select(k => k.Name));
    }

    [Fact]
    public void WeightSortAscendsAndValueSortDescends()
    {
        var items = new List<ItemPresentation.SortKey>
        {
            Key("heavy", ItemRarity.Common, 9f, 1),
            Key("light", ItemRarity.Common, 1f, 50),
        };

        Assert.Equal("light", ItemPresentation.Sort(items, ItemPresentation.SortOrder.Weight, k => k).First().Name);
        Assert.Equal("light", ItemPresentation.Sort(items, ItemPresentation.SortOrder.Value, k => k).First().Name);
    }

    /// <summary>
    /// The order must be **total**, not merely correct on the primary key. The character screen
    /// rebuilds on every inventory change, so under a partial order two items of equal rarity could
    /// swap places on each rebuild — the grid would reshuffle under the player's cursor every time
    /// they picked up a coin.
    /// </summary>
    [Theory]
    [InlineData(ItemPresentation.SortOrder.Name)]
    [InlineData(ItemPresentation.SortOrder.Rarity)]
    [InlineData(ItemPresentation.SortOrder.Weight)]
    [InlineData(ItemPresentation.SortOrder.Value)]
    public void SortIsTotalSoTheGridNeverReshuffles(ItemPresentation.SortOrder order)
    {
        // Every item ties on every key except the name.
        var items = new List<ItemPresentation.SortKey>
        {
            Key("delta", ItemRarity.Rare, 2f, 7),
            Key("alpha", ItemRarity.Rare, 2f, 7),
            Key("charlie", ItemRarity.Rare, 2f, 7),
            Key("bravo", ItemRarity.Rare, 2f, 7),
        };

        var first = ItemPresentation.Sort(items, order, k => k).Select(k => k.Name).ToList();

        // Feeding the previous result back in must be a fixed point, and so must a reversed input.
        var again = ItemPresentation.Sort(
            Enumerable.Reverse(items), order, k => k).Select(k => k.Name).ToList();

        Assert.Equal(first, again);
        Assert.Equal(new[] { "alpha", "bravo", "charlie", "delta" }, first);
    }

    // --- Comparison ---------------------------------------------------------

    private static (StatType, float, ModifierType) Flat(StatType stat, float value) =>
        (stat, value, ModifierType.Flat);

    [Fact]
    public void PositiveDeltaMeansEquippingIsAnUpgrade()
    {
        var result = ItemPresentation.Compare(
            new[] { Flat(StatType.Armor, 10f) },
            new[] { Flat(StatType.Armor, 4f) });

        (StatType stat, float delta) = Assert.Single(result);
        Assert.Equal(StatType.Armor, stat);
        Assert.Equal(6f, delta, 3);
    }

    [Fact]
    public void NegativeDeltaMeansEquippingIsADowngrade()
    {
        var result = ItemPresentation.Compare(
            new[] { Flat(StatType.Armor, 2f) },
            new[] { Flat(StatType.Armor, 9f) });

        Assert.Equal(-7f, Assert.Single(result).Delta, 3);
    }

    /// <summary>
    /// An item can carry the same stat from its template bonus *and* from a rolled affix — a sword
    /// with +2 Power and a "+3 Power" prefix. Comparing entry by entry would report two separate
    /// deltas for one stat and get the sign of the pair wrong whenever they disagreed.
    /// </summary>
    [Fact]
    public void RepeatedStatsAreSummedBeforeComparing()
    {
        var result = ItemPresentation.Compare(
            new[] { Flat(StatType.PhysicalPower, 2f), Flat(StatType.PhysicalPower, 3f) },
            new[] { Flat(StatType.PhysicalPower, 4f) });

        Assert.Equal(1f, Assert.Single(result).Delta, 3);
    }

    /// <summary>An empty slot is the case the player most needs the comparison for.</summary>
    [Fact]
    public void ComparingAgainstNothingShowsTheWholeBonus()
    {
        var result = ItemPresentation.Compare(new[] { Flat(StatType.Health, 25f) }, null);
        Assert.Equal(25f, Assert.Single(result).Delta, 3);
    }

    /// <summary>Losing a stat entirely has to show as a loss, not as an absence.</summary>
    [Fact]
    public void AStatOnlyTheEquippedItemHasReadsAsALoss()
    {
        var result = ItemPresentation.Compare(
            new[] { Flat(StatType.Armor, 5f) },
            new[] { Flat(StatType.Armor, 5f), Flat(StatType.CritChance, 0.1f) });

        (StatType stat, float delta) = Assert.Single(result);
        Assert.Equal(StatType.CritChance, stat);
        Assert.True(delta < 0f, "dropping a stat the worn item had must read as negative");
    }

    /// <summary>A delta of zero is noise — the row would say "this changes nothing".</summary>
    [Fact]
    public void IdenticalStatsProduceNoRows()
    {
        var result = ItemPresentation.Compare(
            new[] { Flat(StatType.Armor, 5f), Flat(StatType.Health, 10f) },
            new[] { Flat(StatType.Health, 10f), Flat(StatType.Armor, 5f) });

        Assert.Empty(result);
    }
}
