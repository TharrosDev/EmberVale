using Embervale.Economy;
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
}
