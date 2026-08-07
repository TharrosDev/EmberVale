using Embervale.Economy;
using Embervale.Factions;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// Pins when a shop restocks and how good its leveled pool rolls (Phase 38B). Both are arithmetic
/// whose failure mode is *nothing visibly happening* — a restock that never fires and a quality ramp
/// that reads as flat look identical to the feature being unimplemented, so neither is safe to leave
/// to play-testing. The quickload case below is the one that would otherwise ship: it needs a save,
/// a reload and a day boundary to reproduce by hand, and it costs one comparison to get right.
/// </summary>
public class ShopStockTests
{
    [Fact]
    public void ARestockIsDueOnTheAuthoredDayAndNotBefore()
    {
        Assert.False(ShopStock.IsRestockDue(lastRestockDay: 3, currentDay: 4, restockDays: 2));
        Assert.True(ShopStock.IsRestockDue(lastRestockDay: 3, currentDay: 5, restockDays: 2));
        Assert.True(ShopStock.IsRestockDue(lastRestockDay: 3, currentDay: 9, restockDays: 2));
    }

    [Fact]
    public void ADailyShopRestocksEveryDay()
    {
        Assert.False(ShopStock.IsRestockDue(7, 7, 1));
        Assert.True(ShopStock.IsRestockDue(7, 8, 1));
    }

    [Fact]
    public void AShopWithNoIntervalNeverRestocks()
    {
        // Legal only when every row is unlimited — the validator enforces that pairing, and this makes
        // the arithmetic agree with it rather than restocking a shop that authored no clock.
        Assert.False(ShopStock.IsRestockDue(0, 9999, restockDays: 0));
        Assert.False(ShopStock.IsRestockDue(0, 9999, restockDays: -3));
    }

    [Fact]
    public void AClockBehindTheStampRestocksRatherThanFreezing()
    {
        // The quickload case. A load rewinds the world clock while the service may still hold a stamp
        // from the timeline being abandoned; comparing only forwards would leave that shop's stock
        // frozen for the rest of the run, which looks exactly like restock being broken.
        Assert.True(ShopStock.IsRestockDue(lastRestockDay: 40, currentDay: 2, restockDays: 1));
        Assert.True(ShopStock.IsRestockDue(lastRestockDay: 40, currentDay: 2, restockDays: 10));
    }

    [Fact]
    public void AFreshShopIsDueImmediately()
    {
        // The service stamps int.MinValue before the first stock; nothing may make that arithmetic
        // overflow into "not due", or a shop would open empty forever.
        Assert.True(ShopStock.IsRestockDue(int.MinValue, currentDay: 0, restockDays: 1));
    }

    [Fact]
    public void LevelOneRollsTheFloorQuality()
    {
        // A level-1 player must not be handed the ramp's benefit, and the service falls back to level 1
        // when there is no player at all — so this value is also the no-player case.
        Assert.Equal(0f, ShopStock.QualityForLevel(1));
        Assert.Equal(0f, ShopStock.QualityForLevel(0));
        Assert.Equal(0f, ShopStock.QualityForLevel(-5));
    }

    [Fact]
    public void QualityClimbsWithLevelAndThenStops()
    {
        Assert.True(ShopStock.QualityForLevel(5) > ShopStock.QualityForLevel(2));
        Assert.True(ShopStock.QualityForLevel(10) > ShopStock.QualityForLevel(5));

        // Clamped, and this is not cosmetic: quality drives LootRarity.Select, so an unbounded ramp
        // turns a general-goods stall into a Legendary vending machine for a high-level player.
        Assert.Equal(1f, ShopStock.QualityForLevel(ShopStock.QualityCapLevel));
        Assert.Equal(1f, ShopStock.QualityForLevel(ShopStock.QualityCapLevel + 50));
        Assert.Equal(1f, ShopStock.QualityForLevel(9999));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(12)]
    [InlineData(20)]
    [InlineData(60)]
    public void QualityStaysInTheUnitRange(int level)
    {
        // LootGenerator adds this to a table's own QualityBonus; anything outside 0..1 would push
        // LootRarity's tier boost somewhere its own tests never cover.
        float quality = ShopStock.QualityForLevel(level);
        Assert.InRange(quality, 0f, 1f);
    }

    [Fact]
    public void AnUnlimitedPurseCoversAnything()
    {
        // The sentinel, not a large number: a merchant who authors no purse must not be beatable by a
        // sufficiently rich player, and 0 is a genuinely broke merchant rather than an unlimited one.
        Assert.True(ShopStock.CanCover(ShopStock.UnlimitedPurse, 999_999));
        Assert.Equal(ShopStock.UnlimitedPurse, ShopStock.AfterSpend(ShopStock.UnlimitedPurse, 500));
        Assert.False(ShopStock.CanCover(0, 1));
    }

    [Fact]
    public void AMerchantCanSpendExactlyWhatTheyHave()
    {
        // The boundary a player hits when fencing one last item: covering it exactly must succeed, and
        // it must leave the purse at 0 rather than at -1, which would silently make them unlimited.
        Assert.True(ShopStock.CanCover(120, 120));
        Assert.Equal(0, ShopStock.AfterSpend(120, 120));
        Assert.False(ShopStock.CanCover(119, 120));
    }

    [Fact]
    public void SpendingNeverDrivesAPurseNegative()
    {
        Assert.Equal(0, ShopStock.AfterSpend(10, 999));
        Assert.Equal(50, ShopStock.AfterSpend(50, 0));
        Assert.Equal(50, ShopStock.AfterSpend(50, -20));
    }

    [Fact]
    public void ARefundNeverMintsTheMerchantMoney()
    {
        // A sale that debits the purse and then fails to take the goods has to hand the gold back, and
        // the clamp is what stops that path being a way to top a merchant up past what they authored.
        Assert.Equal(250, ShopStock.AfterRefund(purse: 200, amount: 50, authoredPurse: 250));
        Assert.Equal(250, ShopStock.AfterRefund(purse: 240, amount: 100, authoredPurse: 250));
        Assert.Equal(ShopStock.UnlimitedPurse, ShopStock.AfterRefund(ShopStock.UnlimitedPurse, 50, 0));
    }

    [Fact]
    public void AppetiteFallsToAFloorAndStaysThere()
    {
        Assert.Equal(1f, ShopStock.SaturationMultiplier(absorbed: 0, restockDays: 1));

        // The grace band is full price to its last unit, and the slope starts from there rather than
        // from zero — one honest haul must not be docked for being a haul.
        Assert.Equal(
            1f, ShopStock.SaturationMultiplier(ShopStock.SaturationGrace - 1, restockDays: 1));
        Assert.Equal(
            1f, ShopStock.SaturationMultiplier(ShopStock.SaturationGrace, restockDays: 1));
        Assert.True(
            ShopStock.SaturationMultiplier(ShopStock.SaturationGrace + 1, restockDays: 1) < 1f);
        Assert.Equal(
            ShopStock.SaturationFloor,
            ShopStock.SaturationMultiplier(
                ShopStock.SaturationGrace + ShopStock.SaturationSpan, restockDays: 1));

        // Past the span it holds rather than continuing down: saturation is a slope, never a refusal,
        // and a payout that reached zero would be item loss dressed as a market.
        Assert.Equal(
            ShopStock.SaturationFloor,
            ShopStock.SaturationMultiplier(ShopStock.SaturationSpan * 100, restockDays: 1));

        // Monotone all the way down — a bump anywhere in the ramp is a unit that pays more than the one
        // before it, which is an ordering the player can farm.
        float previous = 1f;
        for (int absorbed = 0; absorbed <= ShopStock.SaturationSpan * 2; absorbed++)
        {
            float current = ShopStock.SaturationMultiplier(absorbed, restockDays: 1);
            Assert.True(current <= previous, $"appetite rose at {absorbed}");
            previous = current;
        }
    }

    [Fact]
    public void AShopThatNeverRestocksNeverSaturates()
    {
        // Nothing would ever clear it, so the decay would be permanent — a markdown that lasts the rest
        // of the run and reads as the merchant being broken rather than as a mechanic.
        Assert.Equal(1f, ShopStock.SaturationMultiplier(absorbed: 500, restockDays: 0));
        Assert.Equal(120, ShopStock.SaturatedPayout(unitPrice: 6, absorbed: 500, quantity: 20, restockDays: 0));
    }

    /// <summary>
    /// ⚠️ The test 38H exists to pass. A stack's payout must decay across its <em>own</em> units, so
    /// selling twenty at once pays exactly what selling them one at a time pays. Price a stack at a
    /// single pre-sale multiplier instead and "dump everything in one click" becomes strictly optimal —
    /// saturation would then punish only the player who sells tidily, which is the opposite of the point.
    /// </summary>
    [Fact]
    public void StackSaleMatchesSellingOneAtATime()
    {
        const int UnitPrice = 7;
        const int RestockDays = 1;

        for (int quantity = 1; quantity <= 40; quantity++)
        {
            int drip = 0;
            for (int sold = 0; sold < quantity; sold++)
            {
                drip += ShopStock.SaturatedPayout(UnitPrice, absorbed: sold, quantity: 1, RestockDays);
            }

            int atOnce = ShopStock.SaturatedPayout(UnitPrice, absorbed: 0, quantity, RestockDays);
            Assert.Equal(drip, atOnce);
        }
    }

    [Fact]
    public void AHaulInsideTheGraceBandPaysExactlyWhatItDidBeforeSaturationExisted()
    {
        // 38A's arithmetic was unitPrice * quantity, and inside the grace band that is still exactly what
        // a player gets. This is the assertion that keeps 38H a pressure on repeat volume rather than a
        // silent markdown on every sale in the game.
        Assert.Equal(
            6 * ShopStock.SaturationGrace,
            ShopStock.SaturatedPayout(
                unitPrice: 6, absorbed: 0, quantity: ShopStock.SaturationGrace, restockDays: 1));

        Assert.Equal(0, ShopStock.SaturatedPayout(unitPrice: 0, absorbed: 0, quantity: 10, restockDays: 1));
        Assert.Equal(0, ShopStock.SaturatedPayout(unitPrice: 6, absorbed: 0, quantity: 0, restockDays: 1));
    }

    [Fact]
    public void BeyondTheGraceBandVolumeCostsSomething()
    {
        // The mechanic has to actually bite, or it is a constant wearing a curve's clothes. Twice the
        // grace band must pay measurably less than twice its price.
        int quantity = ShopStock.SaturationGrace * 2;
        int paid = ShopStock.SaturatedPayout(unitPrice: 6, absorbed: 0, quantity, restockDays: 1);

        Assert.True(paid < 6 * quantity);
        Assert.True(paid > 6 * ShopStock.SaturationGrace);
    }

    [Fact]
    public void AGluttedMerchantStillPaysSomething()
    {
        // The floor is a floor, not zero: a row that paid nothing would be refused as worthless (38A),
        // and turning "I have plenty" into "this is worthless" loses the mechanic's meaning entirely.
        Assert.True(ShopStock.SaturatedPayout(unitPrice: 10, absorbed: 999, quantity: 1, restockDays: 1) > 0);

        // ⚠️ The case that actually bites: a goblin hide pays a single coin, so any multiplier below 1
        // floors it to nothing and the sale is refused. Cheap high-volume goods are precisely what this
        // mechanic is about, so they must stay sellable at every point on the ramp.
        for (int taken = 0; taken < 200; taken++)
        {
            Assert.True(
                ShopStock.SaturatedPayout(unitPrice: 1, absorbed: taken, quantity: 1, restockDays: 1) > 0,
                $"a one-coin item became unsellable after {taken} absorbed");
        }
    }

    // --- 38I: stock gates and merchant investment ---------------------------

    [Fact]
    public void AnUngatedRowIsOpenToAnyone()
    {
        // The defaults ARE the ungated case — Hated is the bottom of the reputation ramp, an empty flag
        // is no flag, and zero rungs is no stake. If this ever fails, every shop authored before 38I has
        // silently locked itself.
        Assert.Equal(
            StockLock.Open,
            ShopStock.LockOf(
                ReputationTier.Hated, string.Empty, 0,
                standing: ReputationTier.Hated, hasFlag: false, invested: 0));
    }

    [Fact]
    public void EachGateHoldsItsOwnRowShut()
    {
        Assert.Equal(
            StockLock.Flag,
            ShopStock.LockOf(ReputationTier.Hated, "flag.x", 0, ReputationTier.Allied, false, 9));

        Assert.Equal(
            StockLock.Standing,
            ShopStock.LockOf(ReputationTier.Honored, string.Empty, 0, ReputationTier.Friendly, true, 9));

        Assert.Equal(
            StockLock.Investment,
            ShopStock.LockOf(ReputationTier.Hated, string.Empty, 2, ReputationTier.Allied, true, 1));
    }

    [Fact]
    public void MeetingTheGateOpensTheRow()
    {
        // Each gate in its exact boundary state: the flag held, standing *equal* to the requirement
        // (not above it), and the last rung just bought. An off-by-one on any of these is a shelf the
        // player has earned and still cannot buy from.
        Assert.Equal(
            StockLock.Open,
            ShopStock.LockOf(ReputationTier.Honored, "flag.x", 2, ReputationTier.Honored, true, 2));
    }

    [Fact]
    public void TheStoryGateIsReportedBeforeStandingAndStandingBeforeGold()
    {
        // ⚠️ The order is the feature, and it is PropertyClaim.Resolve's rule: never send a player off
        // to earn coin for something a story beat is holding shut. A row behind all three reports the
        // flag; with the flag held it reports standing; only then does it ask for the stake.
        Assert.Equal(
            StockLock.Flag,
            ShopStock.LockOf(ReputationTier.Allied, "flag.x", 3, ReputationTier.Hated, false, 0));

        Assert.Equal(
            StockLock.Standing,
            ShopStock.LockOf(ReputationTier.Allied, "flag.x", 3, ReputationTier.Hated, true, 0));

        Assert.Equal(
            StockLock.Investment,
            ShopStock.LockOf(ReputationTier.Allied, "flag.x", 3, ReputationTier.Allied, true, 0));
    }

    [Fact]
    public void AStakePaysOutOnlyTheRungsActuallyHeld()
    {
        int[] ladder = { 150, 300 };

        Assert.Equal(0, ShopStock.PurseBonusThrough(ladder, invested: 0));
        Assert.Equal(150, ShopStock.PurseBonusThrough(ladder, invested: 1));
        Assert.Equal(450, ShopStock.PurseBonusThrough(ladder, invested: 2));

        // A save carrying more rungs than the shop still authors is a content edit, not corruption:
        // the player keeps what exists rather than crashing the restock that reads it.
        Assert.Equal(450, ShopStock.PurseBonusThrough(ladder, invested: 7));
    }

    [Fact]
    public void AnInvestedPurseRefillsHigher()
    {
        Assert.Equal(300, ShopStock.PurseAfterInvestment(authoredPurse: 300, bonus: 0));
        Assert.Equal(450, ShopStock.PurseAfterInvestment(authoredPurse: 300, bonus: 150));
    }

    [Fact]
    public void AnUnlimitedPurseStaysUnlimitedNoMatterTheStake()
    {
        // ⚠️ The case worth a test: adding a bonus to a merchant who authors no purse would make her
        // FINITE — a downgrade the player paid gold for, and the exact opposite of what a stake
        // promises. --validate rejects the authoring; this proves the arithmetic is safe anyway.
        Assert.Equal(ShopStock.UnlimitedPurse, ShopStock.PurseAfterInvestment(0, 400));
        Assert.Equal(ShopStock.UnlimitedPurse, ShopStock.PurseAfterInvestment(ShopStock.UnlimitedPurse, 400));
    }

    [Fact]
    public void AnInvestedPurseStillCoversAndSpendsLikeAnyOther()
    {
        // The stake feeds the same three purse functions 38C already had; nothing about a raised purse
        // is a special case downstream, which is what keeps the refund clamp honest.
        int purse = ShopStock.PurseAfterInvestment(authoredPurse: 300, bonus: 150);

        Assert.True(ShopStock.CanCover(purse, 450));
        Assert.False(ShopStock.CanCover(purse, 451));
        Assert.Equal(0, ShopStock.AfterSpend(purse, 450));

        // ⚠️ A refunded sale must clamp to the *invested* ceiling, not the authored one, or a failed
        // sale would quietly erase what the stake bought.
        Assert.Equal(450, ShopStock.AfterRefund(purse: 0, amount: 450, authoredPurse: purse));
        Assert.Equal(300, ShopStock.AfterRefund(purse: 0, amount: 450, authoredPurse: 300));
    }
}
