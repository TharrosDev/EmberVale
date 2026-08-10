using System.Collections.Generic;
using Embervale.Economy;
using Embervale.Factions;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// Why a price is what it is (Phase 38U).
///
/// ⚠️ <b>The load-bearing tests here are the ones that pin a quote's <c>Total</c> to the expression
/// the surface charged before this sub-phase existed</b> — see
/// <see cref="BuyTotalIsTheShippedExpressionAtEveryStandingAndCombination"/>. 38U merged the
/// explanation and the charge into one value on purpose, which is only safe while the merged value is
/// provably the old one: a breakdown that quietly reprices the realm would be indistinguishable from a
/// breakdown that explains it, and <c>--economy</c> only prints the standing landscape.
///
/// The second theme is that <b>the last line's <c>Running</c> is the total</b>. The intermediate
/// factors are accumulated in <c>ShopPricing</c>'s own multiplication order so the two agree exactly
/// rather than within a gold — reordering those three multiplies is a silent off-by-one and nothing
/// else in the repo would catch it.
/// </summary>
public class PriceBreakdownTests
{
    private static readonly ReputationTier[] AllTiers =
    {
        ReputationTier.Hated, ReputationTier.Hostile, ReputationTier.Unfriendly, ReputationTier.Neutral,
        ReputationTier.Friendly, ReputationTier.Honored, ReputationTier.Allied,
    };

    [Fact]
    public void BuyTotalIsTheShippedExpressionAtEveryStandingAndCombination()
    {
        // The anti-drift pin: VendorPanel used to compute exactly this and now takes quote.Total.
        foreach (ReputationTier tier in AllTiers)
        {
            foreach (bool specialty in new[] { false, true })
            {
                foreach (bool haggled in new[] { false, true })
                {
                    foreach (int local in new[] { 1, 7, 40, 137, 999 })
                    {
                        PriceQuote quote = PriceBreakdown.Buy(
                            local, local, string.Empty, false, 1.5f, tier, specialty, haggled);

                        Assert.Equal(
                            ShopPricing.BuyPrice(
                                local, ShopPricing.MarkupFor(1.5f, tier, specialty, haggled)),
                            quote.Total);
                    }
                }
            }
        }
    }

    [Fact]
    public void BuyLastLineIsTheTotal()
    {
        // ⚠️ If this fails, the factors are being accumulated in a different order from
        // ShopPricing.MarkupFor and the tooltip's bottom line disagrees with the button by a gold.
        foreach (ReputationTier tier in AllTiers)
        {
            foreach (bool specialty in new[] { false, true })
            {
                foreach (bool haggled in new[] { false, true })
                {
                    PriceQuote quote = PriceBreakdown.Buy(
                        60, 60, string.Empty, false, 1.5f, tier, specialty, haggled);

                    Assert.Equal(quote.Total, quote.Lines[^1].Running);
                }
            }
        }
    }

    [Fact]
    public void SellTotalIsTheSaturatedPayoutAndUnitIsTheSellPrice()
    {
        PriceQuote quote = PriceBreakdown.Sell(
            100, 100, string.Empty, false, 0.4f, specialty: true, haggled: true,
            quantity: 5, absorbed: 0, restockDays: 3);

        int unit = ShopPricing.SellPrice(100, ShopPricing.SellFractionFor(0.4f, true, true));
        Assert.Equal(unit, quote.Unit);
        Assert.Equal(ShopStock.SaturatedPayout(unit, 0, 5, 3), quote.Total);
        Assert.Equal(quote.Total, quote.Lines[^1].Running);
    }

    [Fact]
    public void AGluttedStackIsASumAndSaysSo()
    {
        // ⚠️ The one place in the game where unit price x quantity is the WRONG number (38H). A
        // breakdown that multiplied would explain a figure the merchant is not paying.
        const int absorbed = 40;
        PriceQuote quote = PriceBreakdown.Sell(
            100, 100, string.Empty, false, 0.4f, false, false,
            quantity: 6, absorbed: absorbed, restockDays: 3);

        Assert.True(ShopStock.SaturationMultiplier(absorbed, 3) < 1f, "the fixture must be glutted");
        Assert.NotEqual(quote.Unit * 6, quote.Total);
        Assert.Equal(PriceBreakdown.KeyGlut, quote.Lines[^1].Key);
    }

    [Fact]
    public void AnUngluttedSingleItemGetsNoStackLineAtAll()
    {
        PriceQuote quote = PriceBreakdown.Sell(
            100, 100, string.Empty, false, 0.4f, false, false,
            quantity: 1, absorbed: 0, restockDays: 3);

        Assert.Equal(quote.Unit, quote.Total);
        Assert.DoesNotContain(quote.Lines, line => line.Key == PriceBreakdown.KeyStack);
        Assert.DoesNotContain(quote.Lines, line => line.Key == PriceBreakdown.KeyGlut);
    }

    [Fact]
    public void ConsignTotalIsTheNetTimesTheStack()
    {
        PriceQuote quote = PriceBreakdown.Consign(
            100, 100, string.Empty, false, consignFraction: 0.85f, commission: 0.2f, quantity: 4);

        int net = ConsignmentRules.Net(ConsignmentRules.Gross(100, 0.85f), 0.2f);
        Assert.Equal(net, quote.Unit);
        Assert.Equal(net * 4, quote.Total);
    }

    [Fact]
    public void CommissionLinesSumToTheCharge()
    {
        var named = new List<(string Name, int UnitValue, int Missing, float Markup)>
        {
            ("Iron Ingot", 12, 3, 1.5f),
            ("Leather Strap", 5, 0, 1.5f),   // brought by the player — no line, no cost
            ("Ember Resin", 30, 1, 1.425f),
        };
        var costed = new List<(int UnitValue, int Missing, float Markup)>();
        foreach ((string _, int value, int missing, float markup) in named)
        {
            costed.Add((value, missing, markup));
        }

        PriceQuote quote = PriceBreakdown.Commission(40, named);

        Assert.Equal(CommissionRules.Cost(40, costed), quote.Total);
        Assert.Equal(quote.Total, quote.Lines[^1].Running);

        // Labour plus the two SHORT materials — the one the player carried is silent, which is the
        // whole reason the split is on screen at all.
        Assert.Equal(3, quote.Lines.Count);
        Assert.Equal(PriceBreakdown.KeyLabour, quote.Lines[0].Key);
        Assert.Equal(40, quote.Lines[0].Running);
    }

    [Fact]
    public void TravelIsTheFeeAndNamesWhichCaseItIs()
    {
        Assert.Equal(TravelFee.LocalFee, PriceBreakdown.Travel(false, false).Total);
        Assert.Equal(PriceBreakdown.KeyTravelLocal, PriceBreakdown.Travel(false, false).Lines[0].Key);
        Assert.Equal(TravelFee.CrossRegionFee, PriceBreakdown.Travel(false, true).Total);
        Assert.Equal(PriceBreakdown.KeyTravelCross, PriceBreakdown.Travel(false, true).Lines[0].Key);

        // Owned wins over cross-region, exactly as TravelFee.For orders the two.
        Assert.Equal(0, PriceBreakdown.Travel(true, true).Total);
        Assert.Equal(PriceBreakdown.KeyTravelOwned, PriceBreakdown.Travel(true, true).Lines[0].Key);
    }

    [Fact]
    public void APlaceWithNoOpinionSaysNothingAboutItself()
    {
        // The town square and the Embermarket author nothing on purpose (38G): "prices are normal
        // here" is the noise BuildLocalTrade already refuses to print.
        PriceQuote quote = PriceBreakdown.Buy(
            100, 100, string.Empty, false, 1.5f, ReputationTier.Neutral, false, false);

        Assert.DoesNotContain(quote.Lines, line => line.Key == PriceBreakdown.KeyLocalDemand);
        Assert.DoesNotContain(quote.Lines, line => line.Key == PriceBreakdown.KeyLocalSurplus);
    }

    [Fact]
    public void AShockedTagIsNamedDifferentlyFromASettledOne()
    {
        // ⚠️ 38T's carry, made testable: one of the four reasons a price moved EXPIRES, and a player
        // cannot plan against a line that does not say which kind of fact it is.
        PriceQuote settled = PriceBreakdown.Buy(
            100, 150, "raw ore", shocked: false, 1.5f, ReputationTier.Neutral, false, false);
        PriceQuote shocked = PriceBreakdown.Buy(
            100, 150, "raw ore", shocked: true, 1.5f, ReputationTier.Neutral, false, false);

        Assert.Equal(PriceBreakdown.KeyLocalDemand, settled.Lines[1].Key);
        Assert.Equal(PriceBreakdown.KeyShockDemand, shocked.Lines[1].Key);
        Assert.Equal(settled.Total, shocked.Total);   // the same money, a different reason for it
    }

    [Fact]
    public void ASurplusAndAScarcityAreToldApartByDirection()
    {
        PriceQuote cheap = PriceBreakdown.Buy(
            100, 62, "raw ore", false, 1.5f, ReputationTier.Neutral, false, false);
        PriceQuote dear = PriceBreakdown.Buy(
            100, 150, "raw ore", false, 1.5f, ReputationTier.Neutral, false, false);

        Assert.Equal(PriceBreakdown.KeyLocalSurplus, cheap.Lines[1].Key);
        Assert.Equal(PriceBreakdown.KeyLocalDemand, dear.Lines[1].Key);
    }

    [Fact]
    public void NeutralStandingAddsNoLineBecauseItChangesNothing()
    {
        PriceQuote quote = PriceBreakdown.Buy(
            100, 100, string.Empty, false, 1.5f, ReputationTier.Neutral, false, false);

        Assert.DoesNotContain(quote.Lines, line => line.Key == PriceBreakdown.KeyStanding);
        Assert.Equal(2, quote.Lines.Count);   // base and the merchant's margin, nothing else
    }

    [Fact]
    public void EveryKeyABuilderCanEmitIsDeclaredInAllKeys()
    {
        // ⚠️ AllKeys is what --validate walks, so a line added to a builder and forgotten here would
        // ship a raw key to the player with every automated check passing. This closes the loop from
        // the other end: the builders' output must be a subset of the declared contract.
        var emitted = new List<PriceLine>();
        emitted.AddRange(PriceBreakdown.Buy(
            100, 62, "ore", true, 1.5f, ReputationTier.Allied, true, true).Lines);
        emitted.AddRange(PriceBreakdown.Buy(
            100, 150, "ore", false, 1.5f, ReputationTier.Hated, false, false).Lines);
        emitted.AddRange(PriceBreakdown.Sell(
            100, 100, string.Empty, false, 0.4f, true, true, 6, 40, 3).Lines);
        emitted.AddRange(PriceBreakdown.Sell(
            100, 100, string.Empty, false, 0.4f, false, false, 4, 0, 3).Lines);
        emitted.AddRange(PriceBreakdown.Consign(
            100, 100, string.Empty, false, 0.85f, 0.2f, 3).Lines);
        emitted.AddRange(PriceBreakdown.Commission(
            40, new List<(string, int, int, float)> { ("Iron Ingot", 12, 2, 1.5f) }).Lines);
        emitted.AddRange(PriceBreakdown.Travel(false, false).Lines);
        emitted.AddRange(PriceBreakdown.Travel(false, true).Lines);
        emitted.AddRange(PriceBreakdown.Travel(true, false).Lines);

        foreach (PriceLine line in emitted)
        {
            Assert.Contains(line.Key, PriceBreakdown.AllKeys);
        }
    }
}
