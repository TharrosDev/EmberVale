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
    public void ADiscAtTheFirstPersonCastersFeetStaysUnderTheRadiusThatIsCutBack()
    {
        // Eye 1.6 m over the middle of the disc: it is drawn at the floor of the cut, where the
        // old four fifths of the ring's reach was more than half bright.
        float inside = VfxArcanaRules.SelfDisc(inside: true, 1.4f);
        Assert.Equal(VfxArcanaRules.SelfDiscInside, inside);
        Assert.Equal(VfxScreenRules.SelfRingFloor, VfxScreenRules.SelfRing(0f, 1.6f, inside), 3);
        Assert.True(VfxScreenRules.SelfRing(0f, 1.6f, VfxArcanaRules.FloorReachInside * 0.8f) > 0.5f);
        Assert.Equal(1.4f, VfxArcanaRules.SelfDisc(inside: false, 1.4f));
    }

    [Fact]
    public void TheLeanestTierAddsNothingToASwarmAndEveryTierUpAddsNoFewer()
    {
        Assert.Equal(0, VfxArcanaRules.SwarmInsects(VfxTier.Performance));
        int last = 0;
        foreach (VfxTier tier in Enum.GetValues<VfxTier>())
        {
            int insects = VfxArcanaRules.SwarmInsects(tier);
            int streaks = VfxArcanaRules.ImplosionStreaks(tier);
            Assert.True(insects >= last, $"{tier} circles fewer insects than the tier below.");
            Assert.InRange(insects, 0, VfxMotifRules.MaxCount);
            Assert.InRange(streaks, 3, VfxMotifRules.MaxCount);
            last = insects;
        }
    }

    [Fact]
    public void TheLeanTiersSpaceOutWhatHangsAndBurnOnlyWhereThereIsAScorch()
    {
        Assert.Equal(3, VfxArcanaRules.AshEvery(VfxTier.Performance));
        Assert.Equal(1, VfxArcanaRules.AshEvery(VfxTier.Ultra));
        foreach (VfxTier tier in Enum.GetValues<VfxTier>())
        {
            // Nothing is left burning on a tier that leaves no scorch for it to burn on.
            if (VfxBudgetRules.For(tier).GroundMarks <= 0)
            {
                Assert.Equal(0, VfxArcanaRules.BurnEvery(tier));
            }
        }

        Assert.Equal(1, VfxArcanaRules.BurnEvery(VfxTier.High));
    }

    [Theory]
    [InlineData(0, 5, false)]
    [InlineData(-2, 5, false)]
    [InlineData(1, int.MinValue, true)]
    [InlineData(3, 9, true)]
    [InlineData(3, 10, false)]
    [InlineData(2, -1, false)] // a negative roll is read without its sign: 0x7FFFFFFF is odd
    public void OneTickInSoManyFalls(int every, int roll, bool expected)
    {
        Assert.Equal(expected, VfxArcanaRules.Falls(every, roll));
    }

    [Fact]
    public void OverManyTicksAThirdOfThemFall()
    {
        int fell = 0;
        var random = new Random(12345);
        for (int i = 0; i < 3000; i++)
        {
            if (VfxArcanaRules.Falls(3, random.Next(int.MinValue, int.MaxValue)))
            {
                fell++;
            }
        }

        Assert.InRange(fell, 850, 1150);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MaxValue)]
    [InlineData(int.MinValue)]
    [InlineData(0x00ABC000)]
    public void TheLitPatchStaysInsideTheWedge(int roll)
    {
        Assert.InRange(VfxArcanaRules.PatchAlong(roll), 0.2f, 0.9f);
    }

    [Fact]
    public void TheCameraIsInABreathOnlyDownItsWedge()
    {
        float slope = Slope(55f);

        // The player a dragon is breathing at, eight metres down the axis.
        Assert.True(VfxArcanaRules.InBreath(8f, 0.5f, 14f, slope));

        // Behind the mouth, beside the wedge, and past its end.
        Assert.False(VfxArcanaRules.InBreath(-2f, 0f, 14f, slope));
        Assert.False(VfxArcanaRules.InBreath(8f, 9f, 14f, slope));
        Assert.False(VfxArcanaRules.InBreath(20f, 0f, 14f, slope));

        // At the mouth itself nothing has been thrown yet.
        Assert.False(VfxArcanaRules.InBreath(0.5f, 0f, 14f, slope));
    }

    [Fact]
    public void ABreathIsDrawnFromAMouthOnlyWhenTheMouthIsWhereTheBreathStarts()
    {
        // A dragon's jaw a metre and a half ahead of its aim origin.
        Assert.True(VfxArcanaRules.MouthIsUsable(1.5f, 12.6f, 14f));

        // A bone found a long way from where the rule breathes from, or one already at the far end.
        Assert.False(VfxArcanaRules.MouthIsUsable(9f, 12f, 14f));
        Assert.False(VfxArcanaRules.MouthIsUsable(1f, 3f, 14f));
        Assert.False(VfxArcanaRules.MouthIsUsable(0f, 0f, 0f));
    }

    [Theory]
    [InlineData(0.1f)]
    [InlineData(0.8f)]
    [InlineData(2.5f)]
    [InlineData(9f)]
    public void TheSigilUnderAMarkedBodyIsWiderThanTheBodyAndNeverHuge(float width)
    {
        float radius = VfxArcanaRules.MarkSigilRadius(width);
        Assert.InRange(radius, 0.9f, 2.2f);
        if (width <= 2.5f)
        {
            Assert.True(radius > width * 0.5f, "The body hides its own sigil.");
        }
    }

    [Theory]
    [MemberData(nameof(Breaths))]
    public void TheLitPatchOnTheLeanestTierIsNoWiderThanItWas(float range, float angle)
    {
        float slope = Slope(angle);
        for (int roll = 0; roll < 4096; roll += 97)
        {
            float wide = range * VfxArcanaRules.PatchAlong(roll << 12) * slope * 0.85f;
            float lean = VfxArcanaRules.PatchRadius(VfxTier.Performance, wide);
            Assert.InRange(lean, 0.9f, VfxArcanaRules.PatchLeanRadius);
            foreach (VfxTier tier in Enum.GetValues<VfxTier>())
            {
                float radius = VfxArcanaRules.PatchRadius(tier, wide);
                Assert.InRange(radius, 0.9f, 3.2f);
                Assert.True(radius >= lean, $"{tier} lights less ground than the leanest tier.");
            }
        }
    }

    [Fact]
    public void ABreathersOwnFeetAreTheFloorUnlessItIsInTheAir()
    {
        // Standing on the terrain, on a bridge a little above it, or under it in a dungeon.
        Assert.Equal(10f, VfxArcanaRules.BreathFloor(10f, 10f, 9.2f, hasGround: true));
        Assert.Equal(12f, VfxArcanaRules.BreathFloor(12f, 10f, 9.2f, hasGround: true));
        Assert.Equal(-6f, VfxArcanaRules.BreathFloor(-6f, 10f, 9.2f, hasGround: true));

        // Hovering: the terrain under the patch, never the air under the dragon.
        Assert.Equal(9.2f, VfxArcanaRules.BreathFloor(22f, 10f, 9.2f, hasGround: true));

        // Nothing to ask (the sandbox): as it always was.
        Assert.Equal(22f, VfxArcanaRules.BreathFloor(22f, 0f, 0f, hasGround: false));
    }

    [Fact]
    public void APitchedBreathStopsWhereItMeetsTheFloor()
    {
        // Level: all of what was asked for.
        Assert.Equal(9f, VfxArcanaRules.GroundReach(9f, 1f, 0f, 3f), 3);

        // From twelve metres up at 45 degrees it lands twelve metres out, however long the wedge.
        float level = MathF.Sqrt(0.5f);
        Assert.Equal(12f, VfxArcanaRules.GroundReach(40f, level, level, 12f), 2);
        Assert.Equal(4f * level, VfxArcanaRules.GroundReach(4f, level, level, 12f), 3);

        // Never behind the mouth, and never further than the axis laid flat.
        Assert.Equal(0f, VfxArcanaRules.GroundReach(-3f, 1f, 0f, 3f));
        Assert.True(VfxArcanaRules.GroundReach(9f, 0.4f, 0f, 3f) <= 9f * 0.4f + 0.001f);
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
