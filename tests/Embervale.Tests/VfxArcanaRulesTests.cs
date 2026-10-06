using System;
using Embervale.Magic.Vfx;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// The bounds that keep a breath, and a spell cast on oneself, from covering the frame. A dragon's
/// breath drew a body the size of its whole wedge and a wall of smoke behind it; these pin what
/// replaced them.
/// </summary>
public class VfxArcanaRulesTests
{
    public static readonly TheoryData<float, float> Breaths = new()
    {
        { 14f, 55f }, // dragon
        { 8f, 45f },  // drake
        { 11f, 80f }, // ash
        { 12f, 50f }, // elder word
        { 40f, 120f }, // nothing shipped: the bounds hold anyway
        { 0.5f, 10f },
    };

    private static float Slope(float angleDegrees) => MathF.Tan(angleDegrees * 0.5f * MathF.PI / 180f);

    [Theory]
    [MemberData(nameof(Breaths))]
    public void ABreathsBodyIsATongueAtTheMouthAndNeverTheWedge(float range, float angle)
    {
        float length = VfxArcanaRules.GoutLength(range);
        float girth = VfxArcanaRules.GoutGirth(length, Slope(angle));

        Assert.InRange(length, 1.5f, VfxArcanaRules.GoutMaxLength);
        Assert.InRange(girth, 0.6f, VfxArcanaRules.GoutMaxGirth);
        if (range >= 4f)
        {
            Assert.True(length <= range * 0.5f, "The body reaches past the middle of the wedge.");
        }
    }

    [Fact]
    public void ADragonsBreathBodyIsAFractionOfWhatFilledTheFrame()
    {
        // Before: 6.6 m across and 10.5 m long, looked straight down by whoever it was aimed at.
        float length = VfxArcanaRules.GoutLength(14f);
        float girth = VfxArcanaRules.GoutGirth(length, Slope(55f));
        Assert.True(girth <= 2f);
        Assert.True(length <= 5f);

        // Left out when the camera is near its far end, so it is never closer than this.
        Assert.True(VfxArcanaRules.GoutClearance >= 2f);
    }

    [Theory]
    [MemberData(nameof(Breaths))]
    public void GlintsStaySmallAtAnyDistanceDownTheWedge(float range, float angle)
    {
        for (int i = 1; i <= 3; i++)
        {
            float radius = VfxArcanaRules.GlintRadius(range * i / 4f, Slope(angle));
            Assert.InRange(radius, 0.18f, VfxArcanaRules.GlintMaxRadius);
        }
    }

    [Fact]
    public void TheLeanestTierDrawsNoGlintsAndEveryTierUpDrawsNoFewer()
    {
        Assert.Equal(0, VfxArcanaRules.Glints(VfxTier.Performance));
        int last = 0;
        foreach (VfxTier tier in Enum.GetValues<VfxTier>())
        {
            int glints = VfxArcanaRules.Glints(tier);
            Assert.True(glints >= last, $"{tier} draws fewer glints than the tier under it.");
            Assert.InRange(glints, 0, 3);
            last = glints;
        }
    }

    [Theory]
    [InlineData(1f)]
    [InlineData(1.6f)]
    [InlineData(4f)]
    public void ABreathsSmokeIsCappedOnEveryTierAndAbsentOnTheLeanOnes(float amount)
    {
        float last = 0f;
        foreach (VfxTier tier in Enum.GetValues<VfxTier>())
        {
            VfxBudget budget = VfxBudgetRules.For(tier);
            float smoke = VfxArcanaRules.HazeDensity(budget, amount, smoke: true);
            if (!budget.SecondaryDebris)
            {
                Assert.Equal(0f, smoke);
                Assert.Equal(0f, VfxArcanaRules.HazeDensity(budget, amount, smoke: false));
            }

            Assert.InRange(smoke, 0f, VfxArcanaRules.SmokeHazeCap);
            Assert.True(smoke >= last, $"{tier} draws less smoke than the tier under it.");
            last = smoke;

            // At the cap, one tick adds at most a handful of puffs out of the emitter's allocation.
            VfxBurstPreset preset = VfxBurstPresets.For(VfxParticles.Smoke);
            float puffs = VfxBurstPresets.Allocated(preset) * VfxBurstPresets.AmountRatio(Math.Max(smoke, 0.0001f));
            if (smoke > 0f)
            {
                Assert.True(puffs <= 6f, $"{tier}: {puffs} smoke puffs a tick.");
            }
        }
    }

    [Fact]
    public void HazeThatIsNotSmokeStillGrowsWithTheTier()
    {
        float medium = VfxArcanaRules.HazeDensity(VfxBudgetRules.For(VfxTier.Medium), 1f, smoke: false);
        float ultra = VfxArcanaRules.HazeDensity(VfxBudgetRules.For(VfxTier.Ultra), 1f, smoke: false);
        Assert.True(medium > 0f);
        Assert.True(ultra > medium);
        Assert.Equal(0f, VfxArcanaRules.HazeDensity(VfxBudgetRules.For(VfxTier.Ultra), 0f, smoke: false));
    }

    [Fact]
    public void AFloorRingReachesIntoAFirstPersonFrame()
    {
        // With the eye about 1.6 m up and a vertical field of view near 70 degrees, level ground
        // enters the frame roughly 1.6 / tan(35 deg) = 2.3 m out. The ring has to run well past that.
        float entersFrame = 1.6f / MathF.Tan(35f * MathF.PI / 180f);
        Assert.True(VfxArcanaRules.FloorReach(inside: true) >= entersFrame * 1.5f);
        Assert.True(VfxArcanaRules.FloorReach(inside: false) < VfxArcanaRules.FloorReach(inside: true));
        Assert.Equal(VfxArcanaRules.FloorReachOutside, VfxArcanaRules.FloorReach(inside: false));
    }

    [Fact]
    public void OnlyTheTwoLowestTiersAreLean()
    {
        Assert.True(VfxArcanaRules.IsLean(VfxTier.Performance));
        Assert.True(VfxArcanaRules.IsLean(VfxTier.Low));
        Assert.False(VfxArcanaRules.IsLean(VfxTier.Medium));
        Assert.False(VfxArcanaRules.IsLean(VfxTier.High));
        Assert.False(VfxArcanaRules.IsLean(VfxTier.Ultra));
    }
}
