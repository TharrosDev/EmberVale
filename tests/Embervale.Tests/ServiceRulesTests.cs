using Embervale.Economy;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// Pins the two decisions every service makes (Phase 38D): which refusal to say, and where the clock
/// lands after a rest.
///
/// The rest arithmetic is the reason this file exists. <c>WorldClock.SetTimeOfDay</c> advances the day
/// only for an hour of 24 or more and otherwise just rewinds the hour, so an inn that asked for its
/// authored <c>RestHour</c> directly would appear to work while never advancing <c>Day</c> — silently
/// freezing 38B's shop restock clock and anything else keyed to a date. Nothing about that failure would
/// point at the inn, and no amount of reading the one-line call site would reveal it.
/// </summary>
public class ServiceRulesTests
{
    private const int Price = 250;

    [Fact]
    public void AnAffordableUnheldServiceIsGranted()
    {
        Assert.Equal(
            ServiceOutcome.Granted,
            ServiceRules.Resolve(known: true, hostile: false, alreadyHeld: false, price: Price, goldHeld: Price));
    }

    [Fact]
    public void ExactlyEnoughGoldIsEnough()
    {
        Assert.Equal(ServiceOutcome.Granted, ServiceRules.Resolve(true, false, false, 250, 250));
        Assert.Equal(ServiceOutcome.CannotAfford, ServiceRules.Resolve(true, false, false, 250, 249));
    }

    [Fact]
    public void AlreadyHeldIsReportedBeforeThePrice()
    {
        // Deliberate ordering. Telling a player who already owns the mount to go and earn 400 gold sends
        // them after the wrong thing — the same mistake 37A's deed ordering exists to avoid.
        Assert.Equal(
            ServiceOutcome.AlreadyHeld,
            ServiceRules.Resolve(known: true, hostile: false, alreadyHeld: true, price: Price, goldHeld: 0));
    }

    [Fact]
    public void HostilityOutranksEverythingSayable()
    {
        // Someone who will not serve you has no price to quote, and no account to tell you about.
        Assert.Equal(
            ServiceOutcome.Hostile,
            ServiceRules.Resolve(known: true, hostile: true, alreadyHeld: true, price: Price, goldHeld: Price));
    }

    [Fact]
    public void AnUnknownServiceOutranksEvenHostility()
    {
        // An unresolvable id is an authoring fault, not a refusal; the prompt stays silent rather than
        // inventing a reason, which is what PropertyStorageComponent does for a bad PropertyId.
        Assert.Equal(
            ServiceOutcome.Unknown,
            ServiceRules.Resolve(known: false, hostile: true, alreadyHeld: true, price: Price, goldHeld: 0));
    }

    [Fact]
    public void AFreeServiceIsAlwaysAffordable()
    {
        Assert.Equal(ServiceOutcome.Granted, ServiceRules.Resolve(true, false, false, price: 0, goldHeld: 0));
    }

    [Fact]
    public void RestingToALaterHourStaysOnTheSameDay()
    {
        // A nap: 06:00 to 08:00 is two hours forward and no date change.
        Assert.Equal(8f, ServiceRules.RestTarget(6f, 8));
        Assert.Equal(2, ServiceRules.RestHours(6f, 8));
    }

    [Fact]
    public void RestingPastMidnightAsksForTomorrow()
    {
        // THE regression guard. 20:00 → 08:00 must be asked for as 32, because 8 would rewind the hour
        // and leave Day untouched.
        Assert.Equal(32f, ServiceRules.RestTarget(20f, 8));
        Assert.Equal(12, ServiceRules.RestHours(20f, 8));
    }

    [Fact]
    public void RestingAtTheTargetHourPassesAWholeDay()
    {
        // An inn is never a no-op: paying at exactly 08:00 for a rest until 08:00 buys tomorrow, not
        // nothing. Returning 8 here would take the player's gold and stop the clock.
        Assert.Equal(32f, ServiceRules.RestTarget(8f, 8));
        Assert.Equal(24, ServiceRules.RestHours(8f, 8));
    }

    [Fact]
    public void TheRestTargetAlwaysMovesTheClockForward()
    {
        // Swept, because the whole point is that no starting hour can produce a backwards jump.
        for (int hour = 0; hour < 24; hour++)
        {
            float target = ServiceRules.RestTarget(hour, 8);
            Assert.True(target > hour, $"resting at {hour}:00 asked for {target}, which is not forward");
            Assert.True(target < hour + 25f, $"resting at {hour}:00 asked for {target}, more than a day");
        }
    }

    [Fact]
    public void AnOutOfRangeRestHourIsClampedRatherThanTrusted()
    {
        // --validate rejects one, but a hand-edited .tres must not be able to hurl the clock somewhere
        // arbitrary — the same defensive posture PropertyClaim takes with a negative price.
        Assert.Equal(23f + 24f, ServiceRules.RestTarget(23f, 99));
        Assert.Equal(24f, ServiceRules.RestTarget(0f, -5));
    }
}
