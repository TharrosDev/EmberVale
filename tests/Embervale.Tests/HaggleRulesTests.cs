using Embervale.Economy;
using Embervale.Factions;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// The negotiation's arithmetic (Phase 38S). As with the bones in 38R2, this is the half of the
/// feature that can be tested at all — the ledger is a Godot node and the press needs a human — so the
/// three guarantees the design rests on are pinned here or nowhere: the outcome is derived, it is
/// stable across processes, and it cannot break the spread.
/// </summary>
public class HaggleRulesTests
{
    private const string Shop = "shop.ember_crown.goods";

    /// <summary>The property the design exists for: a reload replays the merchant's mood, never rerolls
    /// it. If this fails, a quickload is a re-roll and the one-attempt-a-day ledger stops mattering.</summary>
    [Fact]
    public void Succeeds_IsDeterministic()
    {
        for (int day = 0; day < 50; day++)
        {
            Assert.Equal(
                HaggleRules.Succeeds(day, Shop, 50),
                HaggleRules.Succeeds(day, Shop, 50));
        }
    }

    /// <summary>⚠️ The reason <c>StableRoll</c> hand-writes FNV-1a instead of calling
    /// <c>string.GetHashCode()</c>: .NET randomises string hashing per process, so this literal would
    /// drift between runs and the same day would price differently after a restart. A hard-coded
    /// expectation catches that; a self-consistent one never would.</summary>
    [Fact]
    public void Succeeds_IsStableAcrossProcesses()
    {
        Assert.Equal("YYYNNYNYNNNNNYNNNYNY", Sequence(Shop, days: 20, chance: 50));
    }

    /// <summary>Two counters in the same town must not share a mood, or "come back tomorrow" becomes
    /// advice about the whole realm rather than about one merchant.</summary>
    [Fact]
    public void Succeeds_DiffersBetweenMerchantsOnTheSameDay()
    {
        Assert.NotEqual(
            Sequence(Shop, days: 20, chance: 50),
            Sequence("shop.embermarket.ironmonger", days: 20, chance: 50));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-40)]
    public void Succeeds_IsAlwaysFalseWithoutAChance(int chance)
    {
        for (int day = 0; day < 20; day++)
        {
            Assert.False(HaggleRules.Succeeds(day, Shop, chance));
        }
    }

    [Fact]
    public void Succeeds_IsAlwaysTrueAtAHundred()
    {
        for (int day = 0; day < 20; day++)
        {
            Assert.True(HaggleRules.Succeeds(day, Shop, 100));
        }
    }

    /// <summary>An authored chance has to mean roughly what it says over a campaign, or the number in
    /// the offer line is a lie the player cannot check. Loose bounds: this pins bias, not a distribution.</summary>
    [Fact]
    public void Succeeds_HonoursItsAuthoredOddsOverAYear()
    {
        int struck = 0;
        for (int day = 0; day < 365; day++)
        {
            if (HaggleRules.Succeeds(day, Shop, 30))
            {
                struck++;
            }
        }

        Assert.InRange(struck, 80, 140);   // 30% of 365 is 110
    }

    /// <summary>Both factors are exactly <c>1</c> when no deal was struck — the call sites have no
    /// branch of their own, so an unhaggled price must come out bit-for-bit unchanged. This is also
    /// what keeps <c>--economy</c> byte-identical across the sub-phase.</summary>
    [Fact]
    public void NoDealChangesNoPrice()
    {
        Assert.Equal(1f, HaggleRules.BuyFactor(struck: false));
        Assert.Equal(1f, HaggleRules.SellFactor(struck: false));
        Assert.Equal(
            ShopPricing.MarkupFor(1.5f, ReputationTier.Allied, specialty: true),
            ShopPricing.MarkupFor(1.5f, ReputationTier.Allied, specialty: true, haggled: false));
        Assert.Equal(
            ShopPricing.SellFractionFor(0.4f, specialty: true),
            ShopPricing.SellFractionFor(0.4f, specialty: true, haggled: false));
    }

    /// <summary>A struck deal is worth having on both sides — a haggle that moved nothing at either
    /// counter would validate and price correctly and be entirely imperceptible, which is 38G's
    /// parking notice and the failure this sub-phase is most exposed to.</summary>
    [Fact]
    public void ADealMovesBothSidesOfTheSpread()
    {
        Assert.True(
            ShopPricing.BuyPrice(100, ShopPricing.MarkupFor(1.5f, ReputationTier.Neutral, haggled: true)) <
            ShopPricing.BuyPrice(100, ShopPricing.MarkupFor(1.5f, ReputationTier.Neutral)));

        Assert.True(
            ShopPricing.SellPrice(100, ShopPricing.SellFractionFor(0.4f, false, haggled: true)) >
            ShopPricing.SellPrice(100, ShopPricing.SellFractionFor(0.4f, false)));
    }

    /// <summary>
    /// ⚠️ 38R2's carried lesson, asked directly: the haggle stacks with the standing ramp, so the
    /// interesting case is <b>Allied</b> and not Neutral. Even there the 38A clamps hold — a merchant
    /// still never sells below an item's value nor pays above it — which is why a haggle needed no new
    /// safety argument and a commission did.
    /// </summary>
    [Fact]
    public void AHaggleAtAlliedStandingStillCannotBreakTheSpread()
    {
        foreach (int value in new[] { 1, 2, 7, 25, 100, 9999 })
        {
            int buy = ShopPricing.BuyPrice(
                value, ShopPricing.MarkupFor(1.5f, ReputationTier.Allied, specialty: true, haggled: true));
            int sell = ShopPricing.SellPrice(
                value, ShopPricing.SellFractionFor(0.62f, specialty: true, haggled: true));

            Assert.True(buy >= value, $"buy {buy} < value {value}");
            Assert.True(sell <= value, $"sell {sell} > value {value}");
        }
    }

    private static string Sequence(string shopId, int days, int chance)
    {
        var result = new System.Text.StringBuilder(days);
        for (int day = 0; day < days; day++)
        {
            result.Append(HaggleRules.Succeeds(day, shopId, chance) ? 'Y' : 'N');
        }

        return result.ToString();
    }
}
