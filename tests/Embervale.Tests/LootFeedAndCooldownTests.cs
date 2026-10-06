using System.Linq;
using Embervale.UI;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// The two small clocks behind the HUD's item feedback: the pickup feed's merge window and the
/// hotbar's cooldown sweep. Both are timing rules that only misbehave at an edge - a line that never
/// leaves, a sweep that never ends - which is what these pin.
/// </summary>
public class LootFeedAndCooldownTests
{
    // --- Pickup feed --------------------------------------------------------

    [Fact]
    public void RapidPickupsOfOneItemBecomeOneLine()
    {
        var feed = new LootFeedMerger();
        feed.Add("item.ore", "Iron Ore", 0, 2, now: 0.0);
        feed.Add("item.ore", "Iron Ore", 0, 5, now: 0.2);

        Assert.Empty(feed.Flush(0.3)); // still inside the quiet window of the second pickup

        LootFeedLine line = Assert.Single(feed.Flush(0.2 + LootFeedMerger.Window + 0.01));
        Assert.Equal(7, line.Quantity);
        Assert.Equal("Iron Ore", line.Name);
        Assert.False(feed.HasPending);
    }

    [Fact]
    public void DifferentItemsLeaveInTheOrderTheyWerePickedUp()
    {
        var feed = new LootFeedMerger();
        feed.Add("item.b", "B", 2, 1, now: 0.0);
        feed.Add("item.a", "A", 0, 1, now: 0.1);

        Assert.Equal(new[] { "B", "A" }, feed.Flush(5.0).Select(l => l.Name));
    }

    /// <summary>A steady trickle keeps resetting the quiet window, so the hold has a ceiling.</summary>
    [Fact]
    public void AConstantStreamStillReportsAfterTheMaxHold()
    {
        var feed = new LootFeedMerger();
        double now = 0.0;
        while (now < LootFeedMerger.MaxHold)
        {
            feed.Add("item.gold", "Gold", 0, 1, now);
            now += 0.2;
        }

        Assert.NotEmpty(feed.Flush(now));
    }

    [Fact]
    public void NothingIsHeldForAnEmptyPickup()
    {
        var feed = new LootFeedMerger();
        feed.Add("item.ore", "Iron Ore", 0, 0, now: 0.0);
        feed.Add(string.Empty, "Nameless", 0, 3, now: 0.0);

        Assert.False(feed.HasPending);
    }

    // --- Cooldown sweep -----------------------------------------------------

    [Fact]
    public void ACooldownRunsDownAndThenIsGone()
    {
        var clock = new CooldownClock();
        clock.Start("potion", 10.0);

        Assert.True(clock.AnyRunning);
        Assert.Equal(1.0, clock.Fraction("potion"), 3);

        clock.Advance(4.0);
        Assert.Equal(6.0, clock.Remaining("potion"), 3);
        Assert.Equal(0.6, clock.Fraction("potion"), 3);

        clock.Advance(6.0);
        Assert.Equal(0.0, clock.Remaining("potion"), 3);
        Assert.False(clock.AnyRunning);
    }

    [Fact]
    public void KeysCoolDownApartAndARestartStartsOver()
    {
        var clock = new CooldownClock();
        clock.Start("potion", 10.0);
        clock.Advance(5.0);
        clock.Start("food", 2.0);

        Assert.Equal(5.0, clock.Remaining("potion"), 3);
        Assert.Equal(2.0, clock.Remaining("food"), 3);
        Assert.Equal(0.0, clock.Remaining("elixir"), 3);

        clock.Start("potion", 10.0);
        Assert.Equal(10.0, clock.Remaining("potion"), 3);
    }

    /// <summary>A consumable with no cooldown, or with no key, must never put a sweep on the bar.</summary>
    [Fact]
    public void AZeroLengthOrNamelessCooldownStartsNothing()
    {
        var clock = new CooldownClock();
        clock.Start("potion", 0.0);
        clock.Start(string.Empty, 5.0);

        Assert.False(clock.AnyRunning);

        clock.Start("potion", 5.0);
        clock.Clear();
        Assert.False(clock.AnyRunning);
    }
}
