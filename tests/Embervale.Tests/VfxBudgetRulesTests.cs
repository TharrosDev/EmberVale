using System;
using Embervale.Magic.Vfx;
using Embervale.Settings;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// The spell-effect budget table, pinned to the numbers the design states, and the mapping from the
/// saved settings to a tier. The mapping is the part that bites: <c>Settings.RenderQuality</c> is
/// saved in the order its tiers were added, not the order the player reads them.
/// </summary>
public class VfxBudgetRulesTests
{
    [Fact]
    public void TierOrdinals_AreTheVisualOrder()
    {
        Assert.Equal(0, (int)VfxTier.Performance);
        Assert.Equal(1, (int)VfxTier.Low);
        Assert.Equal(2, (int)VfxTier.Medium);
        Assert.Equal(3, (int)VfxTier.High);
        Assert.Equal(4, (int)VfxTier.Ultra);
        Assert.Equal(5, VfxBudgetRules.TierCount);
        Assert.Equal(Enum.GetValues<VfxTier>().Length, VfxBudgetRules.TierCount);
    }

    [Theory]
    [InlineData(VfxTier.Performance, 0.25f, 2, 0, false, 0, false, 0f, 6, 0, VfxTrail.CoreAndHalo, 12, 25f)]
    [InlineData(VfxTier.Low, 0.45f, 3, 0, false, 0, false, 0f, 8, 0, VfxTrail.Short, 20, 35f)]
    [InlineData(VfxTier.Medium, 0.7f, 5, 0, false, 6, true, 1f, 12, 1, VfxTrail.Normal, 32, 50f)]
    [InlineData(VfxTier.High, 1.0f, 8, 0, true, 12, true, 1f, 16, 2, VfxTrail.Long, 48, 70f)]
    [InlineData(VfxTier.Ultra, 1.5f, 12, 1, true, 24, true, 2f, 24, 3, VfxTrail.LongWithSparks, 64, 90f)]
    public void Table_IsTheDesignTable(
        VfxTier tier, float particles, int lights, int shadowed, bool distortion, int marks, bool debris,
        float debrisScale, int segments, int branches, VfxTrail trail, int live, float distance)
    {
        Assert.Equal(
            new VfxBudget(particles, lights, shadowed, distortion, marks, debris, debrisScale, segments, branches,
                trail, live, distance),
            VfxBudgetRules.For(tier));
    }

    [Fact]
    public void EveryBudgetGrowsWithTheTier()
    {
        for (int i = 1; i < VfxBudgetRules.TierCount; i++)
        {
            VfxBudget lower = VfxBudgetRules.For((VfxTier)(i - 1));
            VfxBudget higher = VfxBudgetRules.For((VfxTier)i);

            Assert.True(higher.ParticleMultiplier > lower.ParticleMultiplier);
            Assert.True(higher.MaxLights > lower.MaxLights);
            Assert.True(higher.LiveEffects > lower.LiveEffects);
            Assert.True(higher.FullDetailDistance > lower.FullDetailDistance);
            Assert.True(higher.BoltSegments > lower.BoltSegments);
            Assert.True(higher.GroundMarks >= lower.GroundMarks);
            Assert.True(higher.Trail >= lower.Trail);
            Assert.True(higher.ShadowedLights <= higher.MaxLights);
        }
    }

    [Fact]
    public void For_ClampsATierOutsideTheTable()
    {
        Assert.Equal(VfxBudgetRules.For(VfxTier.Performance), VfxBudgetRules.For((VfxTier)(-3)));
        Assert.Equal(VfxBudgetRules.For(VfxTier.Ultra), VfxBudgetRules.For((VfxTier)42));
    }

    [Theory]
    [InlineData(GraphicsMath.Low, VfxTier.Low)]
    [InlineData(GraphicsMath.Medium, VfxTier.Medium)]
    [InlineData(GraphicsMath.High, VfxTier.High)]
    [InlineData(GraphicsMath.Ultra, VfxTier.Ultra)]
    [InlineData(GraphicsMath.Performance, VfxTier.Performance)]
    [InlineData(-1, VfxTier.Medium)]
    [InlineData(99, VfxTier.Medium)]
    public void FromRenderQuality_IsALookupNotACast(int renderQuality, VfxTier expected)
    {
        Assert.Equal(expected, VfxBudgetRules.FromRenderQuality(renderQuality));
    }

    [Fact]
    public void FromRenderQuality_FollowsTheMenuOrder()
    {
        // The preset the player reads as cheapest is the cheapest effect tier, and so on up.
        for (int i = 0; i < GraphicsMath.UiOrder.Length; i++)
        {
            Assert.Equal((VfxTier)i, VfxBudgetRules.FromRenderQuality(GraphicsMath.UiOrder[i]));
        }

        // Saved 4 is Performance, the lowest: a cast would have made it the richest.
        Assert.Equal(VfxTier.Performance, VfxBudgetRules.FromRenderQuality(4));
        Assert.Equal(VfxTier.Low, VfxBudgetRules.FromRenderQuality(0));
    }

    [Theory]
    [InlineData(-1, GraphicsMath.Ultra, VfxTier.Ultra)]          // follow the preset
    [InlineData(-1, GraphicsMath.Performance, VfxTier.Performance)]
    [InlineData(0, GraphicsMath.Ultra, VfxTier.Performance)]     // the player's own choice wins
    [InlineData(4, GraphicsMath.Performance, VfxTier.Ultra)]
    [InlineData(2, GraphicsMath.Low, VfxTier.Medium)]
    [InlineData(5, GraphicsMath.High, VfxTier.High)]             // out of range follows the preset
    [InlineData(-7, GraphicsMath.Low, VfxTier.Low)]
    public void Resolve_PrefersTheOverride(int spellEffects, int renderQuality, VfxTier expected)
    {
        Assert.Equal(expected, VfxBudgetRules.Resolve(spellEffects, renderQuality));
    }

    [Fact]
    public void TheSettingsDefault_FollowsThePreset()
    {
        // Frozen: ResourceSaver omits a value equal to its default, so every settings file written
        // before this option existed reads as "follow the preset".
        Assert.Equal(-1, VfxBudgetRules.FollowPreset);
    }

    [Fact]
    public void DropdownRows_RoundTripEverySavedValue()
    {
        Assert.Equal(0, VfxBudgetRules.DropdownIndex(VfxBudgetRules.FollowPreset));
        Assert.Equal(0, VfxBudgetRules.DropdownIndex(17));
        for (int saved = -1; saved <= 4; saved++)
        {
            Assert.Equal(saved, VfxBudgetRules.FromDropdownIndex(VfxBudgetRules.DropdownIndex(saved)));
        }

        Assert.Equal(4, VfxBudgetRules.FromDropdownIndex(99));
        Assert.Equal(-1, VfxBudgetRules.FromDropdownIndex(-4));
    }

    [Theory]
    [InlineData(VfxTier.Medium, 0f, VfxDetail.Full)]
    [InlineData(VfxTier.Medium, 50f, VfxDetail.Full)]
    [InlineData(VfxTier.Medium, 50.1f, VfxDetail.FlareOnly)]
    [InlineData(VfxTier.Medium, 75f, VfxDetail.FlareOnly)]
    [InlineData(VfxTier.Medium, 75.1f, VfxDetail.None)]
    [InlineData(VfxTier.Performance, 30f, VfxDetail.FlareOnly)]
    [InlineData(VfxTier.Performance, 38f, VfxDetail.None)]
    [InlineData(VfxTier.Ultra, 89f, VfxDetail.Full)]
    public void DetailAt_ThinsThenCulls(VfxTier tier, float distance, VfxDetail expected)
    {
        Assert.Equal(expected, VfxBudgetRules.DetailAt(tier, distance));
    }

    [Fact]
    public void OverBudget_AtTheLiveEffectCeiling()
    {
        Assert.False(VfxBudgetRules.OverBudget(VfxTier.Performance, 11));
        Assert.True(VfxBudgetRules.OverBudget(VfxTier.Performance, 12));
        Assert.False(VfxBudgetRules.OverBudget(VfxTier.Ultra, 63));
        Assert.True(VfxBudgetRules.OverBudget(VfxTier.Ultra, 64));
    }

    [Fact]
    public void PickRecycle_TakesTheOldestThatIsNotThePlayers()
    {
        var live = new (double Age, bool Player)[] { (9d, true), (2d, false), (5d, false), (1d, false) };
        Assert.Equal(2, VfxBudgetRules.PickRecycle(live));

        // Only the player's left: the oldest of those.
        var mine = new (double Age, bool Player)[] { (1d, true), (4d, true), (3d, true) };
        Assert.Equal(1, VfxBudgetRules.PickRecycle(mine));

        Assert.Equal(-1, VfxBudgetRules.PickRecycle(Array.Empty<(double, bool)>()));
    }

    [Fact]
    public void Quality_AppliesAndResets()
    {
        try
        {
            VfxQuality.Apply(VfxBudgetRules.FollowPreset, GraphicsMath.Performance, reducedMotion: true);
            Assert.Equal(VfxTier.Performance, VfxQuality.Tier);
            Assert.Equal(VfxBudgetRules.For(VfxTier.Performance), VfxQuality.Budget);
            Assert.Equal(VfxBudgetRules.Richness(VfxTier.Performance), VfxQuality.Rich);
            Assert.True(VfxQuality.ReducedMotion);

            VfxQuality.Apply(4, GraphicsMath.Performance, reducedMotion: false);
            Assert.Equal(VfxTier.Ultra, VfxQuality.Tier);
            Assert.Equal(VfxBudgetRules.For(VfxTier.Ultra), VfxQuality.Budget);
            Assert.Equal(VfxBudgetRules.Richness(VfxTier.Ultra), VfxQuality.Rich);
        }
        finally
        {
            VfxQuality.Reset();
        }

        // The default tier is the default preset's.
        Assert.Equal(VfxBudgetRules.FromRenderQuality(GraphicsMath.Medium), VfxQuality.Tier);
        Assert.Equal(VfxBudgetRules.For(VfxTier.Medium), VfxQuality.Budget);
        Assert.False(VfxQuality.ReducedMotion);
    }
}
