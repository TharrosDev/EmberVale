using Embervale.Enemies;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// Pins the bestiary's reveal rule (Phase 34G). The service that counts kills and the panel that
/// draws them both need a live tree; this threshold is the part that decides what the player is
/// allowed to read, so it is pinned here.
/// </summary>
public class BestiaryStageTests
{
    [Theory]
    [InlineData(0, 5, BestiaryStage.Unseen)]
    [InlineData(1, 5, BestiaryStage.Sighted)]
    [InlineData(4, 5, BestiaryStage.Sighted)]
    [InlineData(5, 5, BestiaryStage.Known)]     // the boundary is inclusive
    [InlineData(99, 5, BestiaryStage.Known)]
    public void Of_WalksUnseenThenSightedThenKnown(int kills, int toKnow, BestiaryStage expected)
    {
        Assert.Equal(expected, BestiaryStages.Of(kills, toKnow));
    }

    /// <summary>A one-kill threshold is how a boss is authored — you only ever fight it once, so the
    /// single kill must open the whole page rather than stranding it on Sighted forever.</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(0)]
    [InlineData(-3)]
    public void Of_ThresholdOfOneOrLess_SkipsSighted(int toKnow)
    {
        Assert.Equal(BestiaryStage.Unseen, BestiaryStages.Of(0, toKnow));
        Assert.Equal(BestiaryStage.Known, BestiaryStages.Of(1, toKnow));
    }

    /// <summary>Nothing you have never killed may leak its lore, whatever the threshold says —
    /// a zero or negative count is always Unseen.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Of_NoKills_IsAlwaysUnseen(int kills)
    {
        Assert.Equal(BestiaryStage.Unseen, BestiaryStages.Of(kills, 5));
        Assert.Equal(BestiaryStage.Unseen, BestiaryStages.Of(kills, 1));
        Assert.Equal(BestiaryStage.Unseen, BestiaryStages.Of(kills, 0));
    }
}
