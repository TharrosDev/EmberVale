using System.Collections.Generic;
using Embervale.Economy;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// What a master smith charges to make something for you (Phase 38Q). One of these guards a
/// realm-wide invariant rather than a rounding rule — see
/// <see cref="ACommissionIsNeverCheaperThanWhatItProduces"/>.
/// </summary>
public class CommissionRulesTests
{
    private static IReadOnlyList<(int UnitValue, int Missing, float Markup)> Shortfall(
        params (int, int, float)[] entries) => new List<(int, int, float)>(entries);

    [Fact]
    public void SupplyingEverythingCostsOnlyTheLabourFee()
    {
        // The player who brings all the materials pays the smith for his hands and nothing else.
        Assert.Equal(40, CommissionRules.Cost(40, Shortfall()));
        Assert.Equal(40, CommissionRules.Cost(40, Shortfall((25, 0, 1.4f), (9, 0, 1.4f))));
    }

    [Fact]
    public void MissingMaterialsAreChargedAtTheMastersCounterPrice()
    {
        // 2x an ingot worth 25 at a 1.4 markup is 2 x 35, on top of 40 for the labour.
        Assert.Equal(35, ShopPricing.BuyPrice(25, 1.4f));
        Assert.Equal(110, CommissionRules.Cost(40, Shortfall((25, 2, 1.4f))));

        // A mixed basket sums per line rather than over a total, so each unit takes its own round-up —
        // the same order VendorPanel charges a shelf in.
        Assert.Equal(110 + 13, CommissionRules.Cost(40, Shortfall((25, 2, 1.4f), (9, 1, 1.4f))));
    }

    [Fact]
    public void EachLineCarriesItsOwnMarkup()
    {
        // 38F's specialty discount is a property of the item, not of the basket: a smith who is keen
        // on metal is not thereby keen on the leather in the same recipe. 25 at 1.33 is 34, 9 at 1.4
        // is 13 — priced separately, they do not average.
        Assert.Equal(40 + 34 + 13, CommissionRules.Cost(40, Shortfall((25, 1, 1.33f), (9, 1, 1.4f))));
    }

    [Fact]
    public void APriceIsNeverNegativeAndNeverWraps()
    {
        // Saturating rather than overflowing, as ContrabandLaw.Fine does: a 32-bit wrap would hand
        // back a negative price, and a negative price is a payment.
        Assert.Equal(0, CommissionRules.Cost(-500, Shortfall()));
        Assert.Equal(int.MaxValue, CommissionRules.Cost(int.MaxValue, Shortfall((int.MaxValue, 99, 4f))));

        // A negative quantity is authoring nonsense, not a discount.
        Assert.Equal(40, CommissionRules.Cost(40, Shortfall((25, -3, 1.4f))));
    }

    [Fact]
    public void ACommissionIsNeverCheaperThanWhatItProduces()
    {
        // ⚠️ THE LOAD-BEARING ONE, and the first price in the economy the ShopPricing clamps do not
        // protect. A commission spans two different items — ingredients in, output out — and crafting
        // is meant to add value, so nothing in the arithmetic stops sell(output) beating the bill.
        // Commission it, sell it, repeat, forever. Only the labour fee closes it, and only --validate
        // can check that the authored fee is big enough.
        Assert.True(CommissionRules.Exploitable(commissionCost: 40, bestSellPrice: 62, outputQuantity: 1));
        Assert.False(CommissionRules.Exploitable(commissionCost: 90, bestSellPrice: 62, outputQuantity: 1));

        // Output quantity is the half that is easy to miss: a recipe yielding five of something cheap
        // is the same loop at a fifth of the price per press.
        Assert.True(CommissionRules.Exploitable(commissionCost: 90, bestSellPrice: 20, outputQuantity: 5));

        // Breaking even counts as exploitable. It is not free money, but it is an unbounded loop of
        // pressing a button for nothing, and the margin an author left is the margin a standing
        // discount removes.
        Assert.True(CommissionRules.Exploitable(commissionCost: 62, bestSellPrice: 62, outputQuantity: 1));
    }
}
