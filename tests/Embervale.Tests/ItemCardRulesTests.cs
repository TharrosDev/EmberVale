using System;
using System.Collections.Generic;
using System.Linq;
using Embervale.Items;
using Embervale.Stats;
using Embervale.UI;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// The pure rules behind the item slot's marks, the detail card's anatomy and the character screen's
/// tabs (2026-10 UI upgrade). The card is where a sign error tells the player a downgrade is an
/// upgrade, so the hero number and the stat rows are pinned here rather than looked at.
/// </summary>
public class ItemCardRulesTests
{
    private static (StatType, float, ModifierType) Flat(StatType stat, float value) => (stat, value, ModifierType.Flat);

    // --- Slot marks ---------------------------------------------------------

    [Fact]
    public void RarityTicksCountUpWithRarityAndCommonHasNone()
    {
        int[] ticks = Enum.GetValues<ItemRarity>().Select(ItemPresentation.RarityTicks).ToArray();
        Assert.Equal(0, ItemPresentation.RarityTicks(ItemRarity.Common));
        Assert.Equal(ticks.OrderBy(t => t), ticks);
        Assert.Equal(ticks.Length, ticks.Distinct().Count());
    }

    [Fact]
    public void EveryRarityIsCarriedByAChannelThatIsNotColour()
    {
        // Ticks separate every tier; the frame and the card's lit edge thicken at Epic as well.
        Assert.True(UiTheme.RarityEdgeWidth(ItemRarity.Epic) > UiTheme.RarityEdgeWidth(ItemRarity.Rare));
        Assert.True(ItemPresentation.RarityTicks(ItemRarity.Legendary) > ItemPresentation.RarityTicks(ItemRarity.Epic));
        Assert.Equal(5, Enum.GetValues<ItemRarity>().Select(ItemSlot.RarityKey).Distinct().Count());
    }

    [Fact]
    public void NewSinceListsOnlyWhatWasNotKnown()
    {
        var known = new HashSet<string> { "sword", "potion" };
        Assert.Equal(new[] { "helm", "ring" }, ItemPresentation.NewSince(new[] { "sword", "helm", "potion", "ring" }, known));
        Assert.Empty(ItemPresentation.NewSince(new[] { "sword" }, known));
        Assert.Equal(new[] { "sword" }, ItemPresentation.NewSince(new[] { "sword" }, new HashSet<string>()));
    }

    // --- The card -----------------------------------------------------------

    [Theory]
    [InlineData("Iron Sword", true)]
    [InlineData("Sword of Embers", true)]
    [InlineData("Reinforced Steel Greatsword of the Bear", false)]
    [InlineData("Keen Iron Sword of Haste", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void TheCarvedFaceIsKeptToShortNames(string? name, bool display)
    {
        Assert.Equal(display, ItemPresentation.UsesDisplayFace(name));
    }

    [Fact]
    public void HeroIsDamageForAWeaponArmourForOtherGearAndTheAmountForARestorative()
    {
        Assert.Equal(new ItemPresentation.HeroNumber(ItemPresentation.HeroKind.Damage, 24f), ItemPresentation.Hero(24f, 3f, null, 0f));
        Assert.Equal(new ItemPresentation.HeroNumber(ItemPresentation.HeroKind.Armor, 8f), ItemPresentation.Hero(null, 8f, null, 0f));
        Assert.Equal(ItemPresentation.HeroKind.Health, ItemPresentation.Hero(null, 0f, ConsumableEffectKind.Heal, 40f).Kind);
        Assert.Equal(ItemPresentation.HeroKind.Stamina, ItemPresentation.Hero(null, 0f, ConsumableEffectKind.RestoreStamina, 30f).Kind);
        Assert.Equal(ItemPresentation.HeroKind.Mana, ItemPresentation.Hero(null, 0f, ConsumableEffectKind.RestoreMana, 30f).Kind);
    }

    [Fact]
    public void HeroIsNoneWhereNoSingleNumberLeads()
    {
        // A ring, a buff potion, a cure, a pelt.
        Assert.Equal(ItemPresentation.HeroNumber.None, ItemPresentation.Hero(null, 0f, null, 0f));
        Assert.Equal(ItemPresentation.HeroNumber.None, ItemPresentation.Hero(null, 0f, ConsumableEffectKind.Buff, 12f));
        Assert.Equal(ItemPresentation.HeroNumber.None, ItemPresentation.Hero(null, 0f, ConsumableEffectKind.Cure, 0f));
        Assert.Equal(ItemPresentation.HeroNumber.None, ItemPresentation.Hero(0f, 0f, ConsumableEffectKind.Heal, 0f));
    }

    [Fact]
    public void HeroDeltaIsPositiveForAnImprovementAndCountsAnEmptySlotAsZero()
    {
        var mine = new ItemPresentation.HeroNumber(ItemPresentation.HeroKind.Damage, 24f);
        Assert.Equal(4f, ItemPresentation.HeroDelta(mine, new ItemPresentation.HeroNumber(ItemPresentation.HeroKind.Damage, 20f)));
        Assert.Equal(-6f, ItemPresentation.HeroDelta(mine, new ItemPresentation.HeroNumber(ItemPresentation.HeroKind.Damage, 30f)));
        Assert.Equal(24f, ItemPresentation.HeroDelta(mine, null));
    }

    [Fact]
    public void HeroDeltaRefusesToSubtractDifferentMeasures()
    {
        var dagger = new ItemPresentation.HeroNumber(ItemPresentation.HeroKind.Damage, 12f);
        var shield = new ItemPresentation.HeroNumber(ItemPresentation.HeroKind.Armor, 9f);
        Assert.Null(ItemPresentation.HeroDelta(dagger, shield));
        Assert.Null(ItemPresentation.HeroDelta(ItemPresentation.HeroNumber.None, shield));
    }

    [Fact]
    public void StatRowsWithoutAComparisonListOnlyWhatTheItemGrants()
    {
        var rows = ItemPresentation.StatRows(
            new[] { Flat(StatType.Armor, 6f), Flat(StatType.Health, 10f), Flat(StatType.Armor, 2f) },
            new[] { Flat(StatType.CritChance, 0.05f) },
            comparing: false);

        Assert.Equal(new[] { StatType.Health, StatType.Armor }, rows.Select(r => r.Stat));
        Assert.Equal(8f, rows.Single(r => r.Stat == StatType.Armor).Value);
        Assert.All(rows, r => Assert.Equal(0f, r.Delta));
    }

    [Fact]
    public void StatRowsWhenComparingKeepAStatTheSwapWouldLose()
    {
        var rows = ItemPresentation.StatRows(
            new[] { Flat(StatType.Armor, 6f) },
            new[] { Flat(StatType.Armor, 4f), Flat(StatType.CritChance, 0.05f) },
            comparing: true);

        ItemPresentation.StatRow armor = rows.Single(r => r.Stat == StatType.Armor);
        Assert.Equal(6f, armor.Value);
        Assert.Equal(4f, armor.Worn);
        Assert.Equal(2f, armor.Delta);

        ItemPresentation.StatRow crit = rows.Single(r => r.Stat == StatType.CritChance);
        Assert.Equal(0f, crit.Value);
        Assert.Equal(-0.05f, crit.Delta, 5);
    }

    [Fact]
    public void StatRowsAgainstAnEmptySlotAreAllGain()
    {
        var rows = ItemPresentation.StatRows(new[] { Flat(StatType.Armor, 6f) }, null, comparing: true);
        Assert.Equal(6f, Assert.Single(rows).Delta);
    }

    [Fact]
    public void ARarityNameStaysReadableOnItsOwnBand()
    {
        foreach (ItemRarity rarity in Enum.GetValues<ItemRarity>())
        {
            Godot.Color tint = ItemRarities.Color(rarity);
            Godot.Color band = UiTheme.PlateBandColor(tint);
            Assert.True(UiContrast.Ratio(tint, band) >= 4.5, $"{rarity} name on its band");
            Assert.True(UiContrast.Ratio(UiTheme.Dim, band) >= 4.5, $"type line on the {rarity} band");
        }
    }

    // --- Stat deltas and the sheet ------------------------------------------

    [Theory]
    [InlineData(StatType.Armor, 6f, "+6")]
    [InlineData(StatType.Armor, -2.5f, "-2.5")]
    [InlineData(StatType.CritChance, 0.02f, "+2%")]
    [InlineData(StatType.AttackSpeed, -0.05f, "-5%")]
    [InlineData(StatType.Health, 0f, "0")]
    public void DeltasAreSignedAndFractionsReadAsPercentages(StatType stat, float delta, string expected)
    {
        Assert.Equal(expected, StatsPresentation.FormatDelta(stat, delta));
    }

    [Fact]
    public void DerivedDeltaFollowsAPrimaryIntoTheStatsItBuys()
    {
        var result = StatsPresentation.DerivedDelta(new[] { (StatType.Strength, 2f) });

        float power = StatDerivation.Effects(StatType.Strength).Single(e => e.Stat == StatType.PhysicalPower).PerPoint;
        Assert.Equal(2f, result.Single(r => r.Stat == StatType.Strength).Delta);
        Assert.Equal(2f * power, result.Single(r => r.Stat == StatType.PhysicalPower).Delta, 4);
    }

    [Fact]
    public void DerivedDeltaAddsADirectBonusToADerivedOne()
    {
        // Vitality buys armour; a piece that also carries flat armour shows the sum.
        float perPoint = StatDerivation.Effects(StatType.Vitality).Single(e => e.Stat == StatType.Armor).PerPoint;
        var result = StatsPresentation.DerivedDelta(new[] { (StatType.Vitality, 10f), (StatType.Armor, 4f) });
        Assert.Equal(4f + (10f * perPoint), result.Single(r => r.Stat == StatType.Armor).Delta, 4);
    }

    [Fact]
    public void DerivedDeltaDropsWhatCancelsAndListsTheSheetFirst()
    {
        float perPoint = StatDerivation.Effects(StatType.Vitality).Single(e => e.Stat == StatType.Armor).PerPoint;
        var cancelled = StatsPresentation.DerivedDelta(new[] { (StatType.Vitality, 10f), (StatType.Armor, -10f * perPoint) });
        Assert.DoesNotContain(cancelled, r => r.Stat == StatType.Armor);

        // Health is not on the sheet's sections, so it follows everything that is.
        var ordered = StatsPresentation.DerivedDelta(new[] { (StatType.Health, 20f), (StatType.FireResist, 5f), (StatType.Strength, 1f) });
        Assert.Equal(StatType.Health, ordered[^1].Stat);
        Assert.Equal(StatType.Strength, ordered[0].Stat);
        Assert.Empty(StatsPresentation.DerivedDelta(Array.Empty<(StatType, float)>()));
    }

    [Fact]
    public void EachSectionLeadsWithOneOfItsOwnStats()
    {
        for (int i = 0; i < StatsPresentation.Sections.Length; i++)
        {
            StatType? hero = StatsPresentation.SectionHero(i, _ => 1f);
            Assert.NotNull(hero);
            Assert.Contains(hero!.Value, StatsPresentation.Sections[i].Stats);
        }

        Assert.Null(StatsPresentation.SectionHero(-1, _ => 1f));
        Assert.Null(StatsPresentation.SectionHero(StatsPresentation.Sections.Length, _ => 1f));
    }

    [Fact]
    public void OffenceLeadsWithWhicheverPowerTheCharacterHasMoreOf()
    {
        int offence = Array.FindIndex(StatsPresentation.Sections, s => s.Stats.Contains(StatType.PhysicalPower));
        Assert.Equal(StatType.SpellPower, StatsPresentation.SectionHero(offence, s => s == StatType.SpellPower ? 30f : 10f));
        Assert.Equal(StatType.PhysicalPower, StatsPresentation.SectionHero(offence, s => s == StatType.SpellPower ? 10f : 30f));
        Assert.Equal(StatType.PhysicalPower, StatsPresentation.SectionHero(offence, _ => 10f)); // a tie keeps the first listed

        int defence = Array.FindIndex(StatsPresentation.Sections, s => s.Stats.Contains(StatType.Armor));
        Assert.Equal(StatType.Armor, StatsPresentation.SectionHero(defence, s => s == StatType.FireResist ? 99f : 1f));
    }

    // --- The character screen's tab run -------------------------------------

    [Fact]
    public void TheTabRunVisitsPackMaterialsThenEachTopTabAndWraps()
    {
        const int Tabs = 4; // Gear, Progression, Perks, Guilds
        var visited = new List<(int Tab, bool Materials)>();
        int stop = InventoryTabRules.StopOf(0, false);
        for (int i = 0; i < Tabs + 1; i++)
        {
            visited.Add(InventoryTabRules.FromStop(stop));
            stop = InventoryTabRules.Step(stop, 1, Tabs);
        }

        Assert.Equal(new[] { (0, false), (0, true), (1, false), (2, false), (3, false) }, visited);
        Assert.Equal(InventoryTabRules.StopOf(0, false), stop); // wrapped
        Assert.Equal(InventoryTabRules.StopOf(3, false), InventoryTabRules.Step(InventoryTabRules.StopOf(0, false), -1, Tabs));
    }

    [Fact]
    public void AStopRoundTripsThroughItsTabAndView()
    {
        for (int stop = 0; stop < 5; stop++)
        {
            (int tab, bool materials) = InventoryTabRules.FromStop(stop);
            Assert.Equal(stop, InventoryTabRules.StopOf(tab, materials));
        }

        // Materials only means something on the Gear tab.
        Assert.Equal(InventoryTabRules.StopOf(2, false), InventoryTabRules.StopOf(2, true));
    }
}
