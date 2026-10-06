using System;
using System.Collections.Generic;
using System.Linq;
using Embervale.Core;
using Embervale.Items;
using Embervale.UI;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// The item screens' pure rules added with the inventory upgrade: search, the gear-slot filter, which
/// worn slots a candidate is compared against, set and unique-effect text, quantity clamps and the
/// buyback shelf. Each is a line on screen that reads plausibly when wrong.
/// </summary>
public class ItemPresentationIcsTests
{
    [Fact]
    public void SpendCreditCountsOnlyUnitsNotAlreadyBoughtBack()
    {
        // Sell one, buy it back, sell it again: the second sale is the same unit and is not fresh.
        var credits = new Dictionary<string, int>();
        Assert.Equal(1, ItemPresentation.SpendCredit(credits, "hide", 1));

        credits["hide"] = 1; // the buyback
        Assert.Equal(0, ItemPresentation.SpendCredit(credits, "hide", 1));
        Assert.False(credits.ContainsKey("hide"));

        // A stack larger than the credit is fresh only for the difference, and the rest carries over.
        credits["hide"] = 3;
        Assert.Equal(2, ItemPresentation.SpendCredit(credits, "hide", 5));
        Assert.Empty(credits);

        credits["hide"] = 5;
        Assert.Equal(0, ItemPresentation.SpendCredit(credits, "hide", 2));
        Assert.Equal(3, credits["hide"]);

        // Another kind's credit is not this kind's, and a non-sale spends nothing.
        Assert.Equal(4, ItemPresentation.SpendCredit(credits, "ruby", 4));
        Assert.Equal(0, ItemPresentation.SpendCredit(credits, "hide", 0));
        Assert.Equal(3, credits["hide"]);
    }

    // --- Sorting ------------------------------------------------------------

    [Fact]
    public void TypeSortGroupsByCategoryThenPutsTheBestFirst()
    {
        var items = new List<ItemPresentation.SortKey>
        {
            new("ore", (int)ItemRarity.Common, 0f, 1, Type: (int)ItemType.Material),
            new("axe", (int)ItemRarity.Common, 0f, 1, Type: (int)ItemType.Weapon),
            new("blade", (int)ItemRarity.Epic, 0f, 1, Type: (int)ItemType.Weapon),
        };

        var sorted = ItemPresentation.Sort(items, ItemPresentation.SortOrder.Type, k => k).Select(k => k.Name).ToList();

        // The two weapons sit together, the Epic one first; the material is never between them.
        Assert.Equal(sorted.IndexOf("blade") + 1, sorted.IndexOf("axe"));
    }

    [Fact]
    public void LevelSortDescendsAndBreaksTiesByName()
    {
        var items = new List<ItemPresentation.SortKey>
        {
            new("b", 0, 0f, 0, Level: 14),
            new("a", 0, 0f, 0, Level: 14),
            new("c", 0, 0f, 0, Level: 41),
        };

        Assert.Equal(
            new[] { "c", "a", "b" },
            ItemPresentation.Sort(items, ItemPresentation.SortOrder.Level, k => k).Select(k => k.Name));
    }

    // --- Search and filters -------------------------------------------------

    [Theory]
    [InlineData("", true)]
    [InlineData("   ", true)]
    [InlineData("iron", true)]
    [InlineData("IRON SW", true)]
    [InlineData("sword iron", true)]
    [InlineData("weapon", true)] // the category counts
    [InlineData("iron bow", false)] // every term has to land somewhere
    [InlineData("steel", false)]
    public void SearchNeedsEveryTermSomewhere(string query, bool expected) =>
        Assert.Equal(expected, ItemPresentation.Matches(query, "Iron Sword", "Weapon", null));

    [Fact]
    public void ASlotFilterHidesEverythingThatIsNotThatSlot()
    {
        Assert.True(ItemPresentation.PassesFilter(ItemType.Armor, EquipmentSlot.Head, null, null));
        Assert.True(ItemPresentation.PassesFilter(ItemType.Armor, EquipmentSlot.Head, ItemType.Armor, EquipmentSlot.Head));
        Assert.False(ItemPresentation.PassesFilter(ItemType.Armor, EquipmentSlot.Chest, null, EquipmentSlot.Head));
        Assert.False(ItemPresentation.PassesFilter(ItemType.Consumable, EquipmentSlot.None, null, EquipmentSlot.Head));
        Assert.False(ItemPresentation.PassesFilter(ItemType.Armor, EquipmentSlot.Head, ItemType.Weapon, null));
    }

    // --- Comparison slots ---------------------------------------------------

    [Fact]
    public void OrdinaryGearIsComparedWithItsOwnSlotOnly()
    {
        Assert.Equal(new[] { EquipmentSlot.Chest }, ItemPresentation.RivalSlots(EquipmentSlot.Chest));
        Assert.Empty(ItemPresentation.RivalSlots(EquipmentSlot.None));
    }

    /// <summary>A ring is compared with every ring slot the game has, found by name, each once. The
    /// enum has a single ring slot today, so this pins the shape of the answer: ring slots only, no
    /// duplicates, whatever else is in the list.</summary>
    [Fact]
    public void ARingIsComparedWithEveryRingSlotThereIs()
    {
        Assert.Equal(new[] { EquipmentSlot.Ring }, ItemPresentation.RivalSlots(EquipmentSlot.Ring));

        var known = new[] { EquipmentSlot.Amulet, EquipmentSlot.Ring, EquipmentSlot.Head, EquipmentSlot.Ring };
        Assert.Equal(new[] { EquipmentSlot.Ring }, ItemPresentation.RivalSlots(EquipmentSlot.Ring, known));
    }

    // --- Requirements, sets, unique effects ---------------------------------

    [Theory]
    [InlineData(0, 1, true)]
    [InlineData(18, 18, true)]
    [InlineData(18, 17, false)]
    public void LevelRequirement(int required, int level, bool met) =>
        Assert.Equal(met, ItemPresentation.MeetsLevel(required, level));

    [Fact]
    public void WornPiecesCountsDistinctSetPiecesOnly()
    {
        string[] pieces = { "item.armor.x_helm", "item.armor.x_chest", "item.ring.x" };
        string[] worn = { "item.ring.x", "item.ring.x", "item.armor.x_helm", "item.weapon.other" };

        Assert.Equal(2, ItemPresentation.WornPieces(pieces, worn));
        Assert.Equal(0, ItemPresentation.WornPieces(pieces, Array.Empty<string>()));
    }

    [Theory]
    [InlineData(12f, false, "+12")]
    [InlineData(-3f, false, "-3")]
    [InlineData(0.08f, true, "+8%")]
    [InlineData(0.125f, true, "+12.5%")]
    public void BonusNumbers(float value, bool percent, string expected) =>
        Assert.Equal(expected, ItemPresentation.BonusNumber(value, percent));

    /// <summary>A fraction prints as a percentage and a flat amount as itself; getting the two
    /// crossed reads "refunds 1200% stamina" or "restores 0.08 of your health".</summary>
    [Fact]
    public void UniqueEffectArgumentsFollowTheKind()
    {
        string[] heal = ItemPresentation.UniqueArgs(UniqueEffectKind.OnKillHeal, 0.08f, 1f, 0f, 0f, 0f);
        Assert.Equal("8%", heal[0]);

        string[] refund = ItemPresentation.UniqueArgs(UniqueEffectKind.DodgeRefund, 12f, 1f, 0f, 0f, 0f);
        Assert.Equal("12", refund[0]);

        string[] thorns = ItemPresentation.UniqueArgs(UniqueEffectKind.ThornsFlat, 14f, 1f, 0f, 0f, 0f);
        Assert.Equal("14", thorns[0]);

        string[] echo = ItemPresentation.UniqueArgs(UniqueEffectKind.SpellEcho, 0.5f, 0.15f, 0.3f, 2.5f, 4f);
        Assert.Equal(new[] { "50%", "15%", "30%", "2.5", "4" }, echo);
    }

    [Theory]
    [InlineData(8f, "8s")]
    [InlineData(1.5f, "1.5s")]
    [InlineData(120f, "2m")]
    [InlineData(150f, "2m 30s")]
    public void DurationsReadShort(float seconds, string expected) =>
        Assert.Equal(expected, ItemPresentation.Seconds(seconds));

    /// <summary>The effect icon is the hotbar's only at-a-glance read, so no two effects may share one.</summary>
    [Fact]
    public void EveryConsumableEffectHasItsOwnIcon()
    {
        ConsumableEffectKind[] kinds = Enum.GetValues<ConsumableEffectKind>();
        Assert.Equal(kinds.Length, kinds.Select(ItemPresentation.EffectIcon).Distinct().Count());
    }

    // --- Quantities and the buyback shelf -----------------------------------

    [Theory]
    [InlineData(5, 10, true, 5)]
    [InlineData(10, 10, true, 9)] // a split always leaves one behind
    [InlineData(10, 10, false, 10)]
    [InlineData(0, 10, false, 1)]
    [InlineData(3, 1, true, 1)]
    public void QuantityIsHeldToTheStack(int requested, int stack, bool keepOne, int expected) =>
        Assert.Equal(expected, ItemPresentation.ClampQuantity(requested, stack, keepOne));

    [Fact]
    public void TheBuybackShelfKeepsTheNewestAndForgetsTheRest()
    {
        var shelf = new List<int>();
        for (int i = 1; i <= 15; i++)
        {
            ItemPresentation.PushRecent(shelf, i, 12);
        }

        Assert.Equal(12, shelf.Count);
        Assert.Equal(15, shelf[0]);
        Assert.Equal(4, shelf[^1]);
    }

    /// <summary>The part of a stack's price charged for the part that fitted in the pack. Rounded
    /// up, and never more than the whole, so what is left on the shelf never has a negative price.</summary>
    [Fact]
    public void AShareOfAPriceRoundsUpAndNeverExceedsTheWhole()
    {
        Assert.Equal(10, ItemPresentation.ShareOf(10, 3, 3));
        Assert.Equal(0, ItemPresentation.ShareOf(10, 0, 3));
        Assert.Equal(4, ItemPresentation.ShareOf(10, 1, 3));
        Assert.Equal(7, ItemPresentation.ShareOf(10, 2, 3));
        Assert.Equal(10, ItemPresentation.ShareOf(10, 5, 3));
    }

    // --- Pad labels ---------------------------------------------------------

    [Fact]
    public void EveryHotbarSlotHasADistinctPadChord()
    {
        var labels = Enumerable.Range(0, HotbarComponent.SlotCount).Select(GameInput.HotbarPadLabel).ToList();

        Assert.Equal(HotbarComponent.SlotCount, GameInput.HotbarChordButtons.Length);
        Assert.Equal(labels.Count, labels.Distinct().Count());
        Assert.All(labels, l => Assert.StartsWith(GameInput.HotbarChordLabel + "+", l));
        Assert.DoesNotContain(labels, l => l.EndsWith("?", StringComparison.Ordinal));
        Assert.Equal("?", GameInput.HotbarPadLabel(HotbarComponent.SlotCount));
    }
}
