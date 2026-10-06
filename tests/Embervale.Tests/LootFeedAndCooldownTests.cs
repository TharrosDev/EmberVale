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
}
