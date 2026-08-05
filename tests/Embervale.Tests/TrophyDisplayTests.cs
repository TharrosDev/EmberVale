using Embervale.Housing;
using Embervale.Items;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// Pins <see cref="TrophyDisplay"/> — the gate a display stand opens on and the floor it accepts
/// (Phase 37D). Fourth in the <see cref="PropertyClaimTests"/> / <see cref="PropertyStorageTests"/> /
/// <see cref="PlacementCheckTests"/> line. The rarity boundary is the half worth pinning: it is read
/// by the stand and by the panel's Store button, and a drift between them would let the UI offer a
/// transfer the stand then refuses.
/// </summary>
public class TrophyDisplayTests
{
    [Fact]
    public void AClaimedHoldingsStandOpens()
    {
        Assert.Equal(TrophyOutcome.Open, TrophyDisplay.Resolve(propertyKnown: true, owned: true));
    }

    [Fact]
    public void AnUnclaimedHoldingsStandIsRefusedAsNotYours()
    {
        Assert.Equal(TrophyOutcome.NotOwned, TrophyDisplay.Resolve(propertyKnown: true, owned: false));
    }

    [Fact]
    public void AnUnknownPropertyIsReportedBeforeOwnership()
    {
        // Same ordering call PropertyStorage makes: an unresolvable id is an authoring fault, and
        // reporting it as "not yours" would hide a typo behind a refusal the player could act on.
        Assert.Equal(
            TrophyOutcome.UnknownProperty, TrophyDisplay.Resolve(propertyKnown: false, owned: false));
    }

    [Fact]
    public void AnUnknownPropertyStaysUnknownEvenIfOwnershipSaysOtherwise()
    {
        Assert.Equal(
            TrophyOutcome.UnknownProperty, TrophyDisplay.Resolve(propertyKnown: false, owned: true));
    }

    [Theory]
    [InlineData(ItemRarity.Common)]
    [InlineData(ItemRarity.Uncommon)]
    [InlineData(ItemRarity.Rare)]
    public void OrdinaryLootIsNotATrophy(ItemRarity rarity)
    {
        // Rare is the boundary's near side and the one that matters: a stand that took Rare gear
        // would fill up with the contents of an ordinary afternoon.
        Assert.False(TrophyDisplay.CanDisplay(rarity));
    }

    [Theory]
    [InlineData(ItemRarity.Epic)]
    [InlineData(ItemRarity.Legendary)]
    public void EpicAndBetterEarnTheirPlinth(ItemRarity rarity)
    {
        Assert.True(TrophyDisplay.CanDisplay(rarity));
    }

    [Fact]
    public void TheIronHeartQualifies()
    {
        // The relic Phase 28D grants for the first boss kill is the trophy this feature exists for;
        // it is Legendary, so the rarity floor takes it with no per-item authoring at all.
        Assert.True(TrophyDisplay.CanDisplay(ItemRarity.Legendary));
        Assert.Equal(ItemRarity.Epic, TrophyDisplay.MinimumRarity);
    }
}
