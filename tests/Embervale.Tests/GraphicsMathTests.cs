using Embervale.Settings;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// Pins the graphics-option rules that are easy to break quietly: the saved quality ints (a settings
/// file from before the Performance tier must keep naming the tier it named), how an override
/// resolves against a preset, and the frame cap a menu gets.
/// </summary>
public class GraphicsMathTests
{
    [Theory]
    [InlineData(0, "Low")]
    [InlineData(1, "Medium")]
    [InlineData(2, "High")]
    [InlineData(3, "Ultra")]
    [InlineData(4, "Performance")]
    public void SavedTierInts_KeepTheirMeaning(int saved, string name)
    {
        Assert.Equal(name, GraphicsMath.TierName(saved));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(5)]
    [InlineData(99)]
    public void UnknownSavedTier_ReadsAsMedium(int saved)
    {
        Assert.Equal(GraphicsMath.Medium, GraphicsMath.ClampTier(saved));
    }

    [Fact]
    public void UiOrder_IsCheapestFirst_AndRoundTripsEveryTier()
    {
        Assert.Equal(new[] { 4, 0, 1, 2, 3 }, GraphicsMath.UiOrder);
        for (int tier = 0; tier < GraphicsMath.TierCount; tier++)
        {
            Assert.Equal(tier, GraphicsMath.TierFromUiIndex(GraphicsMath.UiIndexOfTier(tier)));
        }
    }

    [Fact]
    public void AbsentOverrides_AreThePurePreset()
    {
        GraphicsOverrides none = GraphicsOverrides.None;
        Assert.False(none.IsCustom);
        Assert.True(GraphicsMath.Resolve(none.AmbientOcclusion, true));
        Assert.False(GraphicsMath.Resolve(none.AmbientOcclusion, false));
        Assert.Equal(3, GraphicsMath.Resolve(none.AntiAliasing, 3));
        Assert.Equal(0.6f, GraphicsMath.ResolveScale(none.RenderScale, 0.6f));
    }

    [Fact]
    public void AChangedControl_IsCustom_AndSettingItBackIsNot()
    {
        int changed = GraphicsMath.Override(value: false, preset: true);
        Assert.Equal(0, changed);
        Assert.True((GraphicsOverrides.None with { Glow = changed }).IsCustom);

        int restored = GraphicsMath.Override(value: true, preset: true);
        Assert.Equal(-1, restored);
        Assert.False((GraphicsOverrides.None with { Glow = restored }).IsCustom);

        Assert.Equal(-1, GraphicsMath.Override(2, 2));
        Assert.Equal(1, GraphicsMath.Override(1, 2));
        Assert.Equal(0f, GraphicsMath.OverrideScale(0.8f, 0.8f));
        Assert.Equal(0.7f, GraphicsMath.OverrideScale(0.7f, 0.8f));
    }

    [Theory]
    [InlineData(0.2f, 0.5f)]
    [InlineData(0.75f, 0.75f)]
    [InlineData(3f, 1f)]
    public void RenderScaleOverride_IsClampedToTheSupportedRange(float saved, float expected)
    {
        Assert.Equal(expected, GraphicsMath.ResolveScale(saved, 1f));
    }

    [Fact]
    public void ShadowOverride_NamesAnotherPresetsBundle()
    {
        // No override and Off both read the active tier; Off is reported separately.
        Assert.Equal(GraphicsMath.High, GraphicsMath.ShadowSourceTier(-1, GraphicsMath.High));
        Assert.Equal(GraphicsMath.High, GraphicsMath.ShadowSourceTier(0, GraphicsMath.High));
        Assert.False(GraphicsMath.ShadowsEnabled(0));
        Assert.True(GraphicsMath.ShadowsEnabled(-1));

        Assert.Equal(GraphicsMath.Performance, GraphicsMath.ShadowSourceTier(1, GraphicsMath.Ultra));
        Assert.Equal(GraphicsMath.Ultra, GraphicsMath.ShadowSourceTier(5, GraphicsMath.Performance));

        // A preset's own choice resolves back to itself, so it is never stored as an override.
        for (int tier = 0; tier < GraphicsMath.TierCount; tier++)
        {
            int choice = GraphicsMath.PresetShadowChoice(tier);
            Assert.Equal(tier, GraphicsMath.ShadowSourceTier(choice, GraphicsMath.Medium));
            Assert.Equal(-1, GraphicsMath.Override(choice, GraphicsMath.PresetShadowChoice(tier)));
        }
    }

    [Theory]
    [InlineData(0, false, 0)]
    [InlineData(144, false, 144)]
    [InlineData(-5, false, 0)]
    [InlineData(0, true, 60)]
    [InlineData(144, true, 60)]
    [InlineData(60, true, 60)]
    [InlineData(40, true, 40)]
    [InlineData(30, true, 30)]
    public void FpsCap_IsTheSavedCapInPlay_AndNeverAboveSixtyInAMenu(int saved, bool inMenu, int expected)
    {
        Assert.Equal(expected, GraphicsMath.FpsCap(saved, inMenu));
    }
}
