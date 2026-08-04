using Embervale.Housing;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// Pins whether a holding can be claimed, and which refusal to say out loud (Phase 37A). Both the
/// deed's prompt and its interaction read this one function, so an order that drifts between them is
/// impossible by construction — and the order itself is the behaviour worth pinning.
/// </summary>
public class PropertyClaimTests
{
    private const int Price = 600;

    [Fact]
    public void AnEarnedAndAffordedDeedIsGranted()
    {
        Assert.Equal(
            ClaimOutcome.Granted,
            PropertyClaim.Resolve(owned: false, questDone: true, goldHeld: Price, priceGold: Price));
    }

    [Fact]
    public void ExactlyEnoughGoldIsEnough()
    {
        // Off-by-one on the affordability check is the kind of thing that only shows up as a player
        // standing at a post with the exact price in their pocket, being told no.
        Assert.Equal(
            ClaimOutcome.Granted,
            PropertyClaim.Resolve(false, true, goldHeld: 600, priceGold: 600));
        Assert.Equal(
            ClaimOutcome.TooExpensive,
            PropertyClaim.Resolve(false, true, goldHeld: 599, priceGold: 600));
    }

    [Fact]
    public void OwnershipWinsOverEverythingElse()
    {
        // A claimed holding reports as yours even with no gold and an unfinished quest — otherwise
        // the post would start asking a landlord for money again.
        Assert.Equal(
            ClaimOutcome.AlreadyOwned,
            PropertyClaim.Resolve(owned: true, questDone: false, goldHeld: 0, priceGold: Price));
    }

    [Fact]
    public void TheQuestGateIsReportedBeforeThePrice()
    {
        // Deliberate ordering: telling a player to go and earn 600 gold for something a quest is
        // holding shut anyway sends them off after the wrong thing.
        Assert.Equal(
            ClaimOutcome.QuestLocked,
            PropertyClaim.Resolve(owned: false, questDone: false, goldHeld: 0, priceGold: Price));
    }

    [Fact]
    public void AnUngatedDeedFallsThroughToItsPrice()
    {
        // The component passes questDone: true when no quest is authored.
        Assert.Equal(ClaimOutcome.TooExpensive, PropertyClaim.Resolve(false, true, 10, Price));
        Assert.Equal(ClaimOutcome.Granted, PropertyClaim.Resolve(false, true, Price, Price));
    }

    [Fact]
    public void AQuestEarnedHoldingWithNoPriceIsGrantedOutright()
    {
        // Price 0 means the quest was the cost.
        Assert.Equal(
            ClaimOutcome.Granted,
            PropertyClaim.Resolve(owned: false, questDone: true, goldHeld: 0, priceGold: 0));
    }

    [Fact]
    public void ANegativePriceNeverPaysThePlayer()
    {
        // The validator rejects one, but a hand-edited .tres must not turn a deed into an income.
        Assert.Equal(ClaimOutcome.Granted, PropertyClaim.Resolve(false, true, 0, priceGold: -500));
        Assert.Equal(0, PropertyClaim.PriceToCharge(-500));
    }

    [Fact]
    public void ThePriceChargedIsThePriceAuthored()
    {
        Assert.Equal(Price, PropertyClaim.PriceToCharge(Price));
        Assert.Equal(0, PropertyClaim.PriceToCharge(0));
    }
}
