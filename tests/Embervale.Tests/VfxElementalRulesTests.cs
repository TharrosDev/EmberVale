using System;
using Embervale.Magic.Vfx;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// What the fire, frost and lightning special cases ask each tier to draw. The Glacial Bulwark was a
/// flat lit panel and a struck target's linger was empty; these pin what replaced them, and that the
/// leanest tier paid for none of it.
/// </summary>
public class VfxElementalRulesTests
{
    public static readonly TheoryData<VfxTier> Tiers = new()
    {
        VfxTier.Performance, VfxTier.Low, VfxTier.Medium, VfxTier.High, VfxTier.Ultra,
    };

    [Theory]
    [MemberData(nameof(Tiers))]
    public void AWallOfIceIsAlwaysARowOfCrystals(VfxTier tier)
    {
        Assert.InRange(VfxElementalRules.PrismCount(tier), VfxElementalRules.MinPrisms, VfxElementalRules.MaxPrisms);
    }

    [Fact]
    public void ARicherTierNeverStandsFewerCrystalsOrLessSnow()
    {
        VfxTier[] tiers = Enum.GetValues<VfxTier>();
        for (int i = 1; i < tiers.Length; i++)
        {
            Assert.True(VfxElementalRules.PrismCount(tiers[i]) >= VfxElementalRules.PrismCount(tiers[i - 1]));
            Assert.True(VfxElementalRules.SnowLayers(tiers[i]) >= VfxElementalRules.SnowLayers(tiers[i - 1]));
        }
    }

    [Fact]
    public void TheLeanestTierPaysForNothingNew()
    {
        // Three crystals where it drew two flat spires and a stream of glints, one emitter of snow
        // as before, and no arcs left on what lightning struck.
        Assert.Equal(3, VfxElementalRules.PrismCount(VfxTier.Performance));
        Assert.Equal(1, VfxElementalRules.SnowLayers(VfxTier.Performance));
        Assert.False(VfxElementalRules.StruckCrackle(VfxTier.Performance));
        Assert.True(VfxElementalRules.StruckCrackle(VfxTier.Low));
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(0.5f)]
    [InlineData(1f)]
    public void TheTopOfTheWallIsBrokenAndTallestInTheMiddle(float roll)
    {
        float middle = VfxElementalRules.PrismHeight(0f, roll);
        float end = VfxElementalRules.PrismHeight(0.5f, roll);
        Assert.True(middle > end + 0.2f, "The crystals at the ends stand as tall as the middle.");

        // Every crystal clears the sheet of ice between them, and none towers over the wall.
        for (float across = -0.5f; across <= 0.5f; across += 0.125f)
        {
            float tall = VfxElementalRules.PrismHeight(across, roll);
            Assert.InRange(tall, VfxElementalRules.PrismWebHeight + 0.05f, 1.3f);
        }

        Assert.Equal(VfxElementalRules.PrismHeight(-0.3f, roll), VfxElementalRules.PrismHeight(0.3f, roll), 4);
    }

    [Fact]
    public void ACrystalHasDepthAndStaysOnItsLine()
    {
        Assert.InRange(VfxElementalRules.PrismDepthRatio, 0.5f, 1f);
        Assert.InRange(VfxElementalRules.PrismMaxDepth, 0.4f, 0.8f);
        Assert.InRange(VfxElementalRules.MaxPrismLean, 5f, 20f);
        Assert.InRange(VfxElementalRules.StruckCrackleSeconds, 0.8f, 2f);
    }

    [Fact]
    public void AStrikeIsSizedToItsHitAndFrostAndMistStayLow()
    {
        // A lightning blast's first ring runs to 1.2 times its size: it lands on the reach of the
        // dash's hit, give or take a hand, and never past it by more.
        Assert.InRange(VfxElementalRules.DashClapScale * 1.2f, 0.9f, 1.1f);

        // The patch of frost under a Rime Shard is there for about two seconds (2.4 times its creep).
        Assert.InRange(VfxElementalRules.RimeFrostSeconds * 2.4f, 1.8f, 2.4f);

        // Mist a caster stands in is never drawn larger than its preset.
        Assert.InRange(VfxElementalRules.ZoneMistScale, 0.6f, 1f);
    }
}
