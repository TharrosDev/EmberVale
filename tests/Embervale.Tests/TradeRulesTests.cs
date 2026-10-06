using System.Collections.Generic;
using System.Linq;
using Embervale.UI;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// The pure rules behind the trade screens (2026-10 UI upgrade): the column widths that have to fit a
/// handheld, how many of a ware one press may buy, which refusal is named, the order a recipe list
/// reads in and what a haulage posting risks. A wrong answer here is a purchase that overdraws the
/// purse or a page that runs off an 853 px view, and neither shows in a log.
/// </summary>
public class TradeRulesTests
{
    // The gap between the three columns of a trade page, and the hairlines between them.
    private const float Gap = UiTheme.SpaceMd;

    // --- Layout -------------------------------------------------------------

    [Theory]
    [InlineData(769f)]   // 853 px wide: a Steam Deck at UI scale 1.5
    [InlineData(1104f)]  // 1280 px wide
    [InlineData(1744f)]  // 1920 px wide
    [InlineData(3264f)]  // 3440 px wide
    public void ThreeColumnsFitTheWidthTheyAreGiven(float usable)
    {
        float detail = TradeRules.DetailWidth(usable);
        float list = TradeRules.ListWidth(usable, Gap);

        Assert.InRange(detail, TradeRules.DetailMin, TradeRules.DetailMax);
        Assert.True(detail + (list * 2f) + (Gap * 2f) <= usable + 0.01f);
    }

    [Fact]
    public void AHandheldListStillHasRoomForASlotANameAndAPrice()
    {
        // 853x533 logical: gutter 24 a side and the panel padding leave 769 px. A row needs its slot
        // (34), its card margins (32), the scroll gutter (16), two gaps and a price; what is left for
        // the name must still be worth reading.
        float list = TradeRules.ListWidth(769f, Gap * 2f);
        float name = list - ItemSlot.CompactSize - (UiTheme.CompactPadX * 2f) - UiTheme.ScrollGutter - (UiTheme.SpaceSm * 2f) - 48f;
        Assert.True(name >= 80f, $"a handheld row leaves {name} px for the item's name");
    }

    [Fact]
    public void TheCraftingColumnsLeaveTheRecipeListTheLargestShareOnAHandheld()
    {
        const float usable = 769f;
        float recipes = usable - TradeRules.IngredientWidth(usable) - TradeRules.DetailWidth(usable) - (Gap * 4f);
        Assert.True(recipes >= TradeRules.IngredientWidth(usable));
        Assert.True(recipes >= 220f, $"the recipe list is {recipes} px wide");
    }

    [Fact]
    public void ColumnWidthsNeverShrinkAsThePageGrows()
    {
        float[] widths = { 400f, 769f, 1104f, 1744f, 3264f };
        Assert.Equal(widths.Select(TradeRules.DetailWidth).OrderBy(w => w), widths.Select(TradeRules.DetailWidth));
        Assert.Equal(widths.Select(TradeRules.IngredientWidth).OrderBy(w => w), widths.Select(TradeRules.IngredientWidth));
        Assert.Equal(0f, TradeRules.ListWidth(100f, Gap));
    }

    // --- Buying -------------------------------------------------------------

    [Theory]
    [InlineData(10, 95, -1, 20, 9)]    // the purse is the limit
    [InlineData(10, 1000, 3, 20, 3)]   // the shelf is the limit
    [InlineData(10, 1000, -1, 20, 20)] // one stack is the limit on a shelf that never runs out
    [InlineData(10, 5, -1, 20, 0)]     // cannot afford one
    [InlineData(10, 1000, 0, 20, 0)]   // sold out
    [InlineData(0, 0, -1, 5, 5)]       // a free ware is not a division by zero
    [InlineData(1, 100000, -1, 500, TradeRules.MaxOrder)]
    [InlineData(10, 1000, 4, 1, 1)]    // a single item fills a stack on its own
    public void MaxBuyIsTheSmallestOfPurseShelfAndStack(int price, int purse, int remaining, int maxStack, int expected)
    {
        Assert.Equal(expected, TradeRules.MaxBuy(price, purse, remaining, maxStack));
    }

    [Fact]
    public void MaxBuyNeverSpendsMoreThanThePurseHolds()
    {
        for (int price = 1; price <= 40; price += 3)
        {
            for (int purse = 0; purse <= 400; purse += 7)
            {
                int most = TradeRules.MaxBuy(price, purse, -1, 99);
                Assert.True(most * price <= purse);
                Assert.True(most == 99 || (most + 1) * price > purse);
            }
        }
    }

    [Fact]
    public void ARefusalNamesTheLockBeforeTheShelfAndTheShelfBeforeThePurse()
    {
        Assert.Equal(TradeRules.BuyRefusal.Locked, TradeRules.RefusalOf(lockOpen: false, available: false, affordable: false));
        Assert.Equal(TradeRules.BuyRefusal.SoldOut, TradeRules.RefusalOf(lockOpen: true, available: false, affordable: false));
        Assert.Equal(TradeRules.BuyRefusal.CannotAfford, TradeRules.RefusalOf(lockOpen: true, available: true, affordable: false));
        Assert.Equal(TradeRules.BuyRefusal.None, TradeRules.RefusalOf(lockOpen: true, available: true, affordable: true));
    }

    [Theory]
    [InlineData(50, 20, 30)]
    [InlineData(50, 50, 0)]
    [InlineData(50, 80, 0)]
    [InlineData(50, -5, 50)]
    public void ShortfallIsTheGoldStillMissing(int price, int purse, int expected)
    {
        Assert.Equal(expected, TradeRules.Shortfall(price, purse));
    }

    [Fact]
    public void TheSpreadIsReadAsWholePercentages()
    {
        Assert.Equal((150, 40), TradeRules.Spread(1.5f, 0.4f));
        Assert.Equal((125, 33), TradeRules.Spread(1.249f, 0.334f));
    }

    // --- Crafting -----------------------------------------------------------

    [Fact]
    public void RecipesReadPinnedThenCraftableThenTheRestEachByName()
    {
        var recipes = new List<TradeRules.RecipeKey>
        {
            new(false, false, "Steel Helm"),
            new(false, true, "Iron Sword"),
            new(true, false, "Ember Ring"),
            new(false, true, "bandage"),
            new(false, false, "Ash Cloak"),
        };

        List<TradeRules.RecipeKey> ordered = TradeRules.OrderRecipes(recipes, r => r);
        Assert.Equal(
            new[] { "Ember Ring", "bandage", "Iron Sword", "Ash Cloak", "Steel Helm" },
            ordered.Select(r => r.Name));
    }

    [Fact]
    public void RecipeOrderIsTotalSoARebuildNeverReshufflesTheList()
    {
        var recipes = new List<TradeRules.RecipeKey>
        {
            new(false, true, "Potion"), new(false, true, "potion"), new(false, true, "Arrow"), new(false, false, "Bow"),
        };

        List<string> forward = TradeRules.OrderRecipes(recipes, r => r).Select(r => r.Name).ToList();
        recipes.Reverse();
        Assert.Equal(forward, TradeRules.OrderRecipes(recipes, r => r).Select(r => r.Name));
    }

    [Theory]
    [InlineData(4, 4, false, TradeRules.IngredientState.Enough)]
    [InlineData(9, 4, true, TradeRules.IngredientState.Enough)]
    [InlineData(3, 4, false, TradeRules.IngredientState.Short)]
    [InlineData(3, 4, true, TradeRules.IngredientState.Supplied)]
    [InlineData(0, 1, true, TradeRules.IngredientState.Supplied)]
    public void AnIngredientIsEnoughShortOrSuppliedByAMaster(int have, int need, bool commission, TradeRules.IngredientState expected)
    {
        Assert.Equal(expected, TradeRules.IngredientOf(have, need, commission));
    }

    [Theory]
    [InlineData(0, 1, 3, 1)]
    [InlineData(2, 1, 3, 0)]
    [InlineData(0, -1, 3, 2)]
    [InlineData(1, -1, 3, 0)]
    [InlineData(0, 1, 0, 0)]
    public void SubTabsStepAndWrapAtBothEnds(int current, int delta, int count, int expected)
    {
        Assert.Equal(expected, TradeRules.StepTab(current, delta, count));
    }

    // --- Contracts ----------------------------------------------------------

    [Fact]
    public void APostingNamesEveryRiskItCarries()
    {
        Assert.Equal(TradeRules.ContractRisk.None, TradeRules.RisksOf(false, daysLeft: 3, have: 40, need: 40, filled: false));
        Assert.Equal(TradeRules.ContractRisk.Short, TradeRules.RisksOf(false, daysLeft: 3, have: 12, need: 40, filled: false));
        Assert.Equal(TradeRules.ContractRisk.ClosingSoon, TradeRules.RisksOf(false, daysLeft: 1, have: 40, need: 40, filled: false));
        Assert.Equal(
            TradeRules.ContractRisk.Contraband | TradeRules.ContractRisk.ClosingSoon | TradeRules.ContractRisk.Short,
            TradeRules.RisksOf(true, daysLeft: 0, have: 0, need: 5, filled: false));
    }

    [Fact]
    public void AFilledPostingRisksNothing()
    {
        Assert.Equal(TradeRules.ContractRisk.None, TradeRules.RisksOf(true, daysLeft: 0, have: 0, need: 5, filled: true));
    }
}
