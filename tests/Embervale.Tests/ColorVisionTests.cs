using System;
using Embervale.Combat;
using Embervale.Items;
using Embervale.UI;
using Godot;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// Phase 37.5G: colour-vision adaptation.
///
/// These tests assert the **property**, not the arithmetic. Pinning matrix outputs would only
/// prove the numbers have not been retyped; what has to hold is that a pair of colours a deficient
/// viewer would confuse ends up *further apart to that viewer* after adaptation than before. That
/// is the whole point of daltonizing, and it is the thing a plausible-looking wrong sign or a
/// swapped matrix row would silently break.
/// </summary>
public class ColorVisionTests
{
    private static readonly ColorVisionMode[] Deficiencies =
    {
        ColorVisionMode.Deuteranopia,
        ColorVisionMode.Protanopia,
        ColorVisionMode.Tritanopia,
    };

    /// <summary>How far apart two colours look *to the affected viewer*. This is the measure that
    /// matters — separation a trichromat can see is not the question.</summary>
    private static float PerceivedGap(Color a, Color b, ColorVisionMode mode) =>
        ColorVision.Distance(ColorVision.Simulate(a, mode), ColorVision.Simulate(b, mode));

    private static void AssertSeparationImproves(Color a, Color b, ColorVisionMode mode, string pair)
    {
        float before = PerceivedGap(a, b, mode);
        float after = PerceivedGap(
            ColorVision.Daltonize(a, mode),
            ColorVision.Daltonize(b, mode),
            mode);

        Assert.True(
            after > before,
            $"{pair} under {mode}: adaptation moved them from {before:0.0000} to {after:0.0000} apart — " +
            "daltonization must increase separation, never reduce it");
    }

    /// <summary>
    /// Good vs Bad is the single most confusable pair in the UI, and it is the one carrying "this
    /// worked" against "this did not" — on stat deltas, quest state, faction standing and
    /// companion loyalty.
    /// </summary>
    [Theory]
    [InlineData(ColorVisionMode.Deuteranopia)]
    [InlineData(ColorVisionMode.Protanopia)]
    public void GoodAndBadSeparateForRedGreenDeficiencies(ColorVisionMode mode)
    {
        // The raw tokens, not the adapted properties — adapting twice would over-shift, and the
        // test needs the same inputs UiTheme.Adapt receives.
        var good = new Color(0.55f, 0.68f, 0.44f);
        var bad = new Color(0.82f, 0.42f, 0.36f);
        AssertSeparationImproves(good, bad, mode, "Good/Bad");
    }

    /// <summary>The rarity ramp leans on hue between Uncommon (sage) and the tiers around it. It
    /// also carries a luminance ordering and a frame-width channel, but this is the one that a
    /// colourblind player would otherwise lose entirely.</summary>
    [Theory]
    [InlineData(ColorVisionMode.Deuteranopia)]
    [InlineData(ColorVisionMode.Protanopia)]
    public void AdjacentRarityTiersSeparateForRedGreenDeficiencies(ColorVisionMode mode)
    {
        AssertSeparationImproves(
            ItemRarities.Color(ItemRarity.Common),
            ItemRarities.Color(ItemRarity.Uncommon),
            mode,
            "Common/Uncommon");
    }

    /// <summary>Nature (verdigris) against Frost (pale ice) is the school pair a red-green viewer
    /// is most likely to merge, and school colour is how a spell card says what it is.</summary>
    [Theory]
    [InlineData(ColorVisionMode.Deuteranopia)]
    [InlineData(ColorVisionMode.Protanopia)]
    public void NatureAndFrostSeparateForRedGreenDeficiencies(ColorVisionMode mode)
    {
        AssertSeparationImproves(
            Embervale.Magic.SpellSchools.Color(DamageType.Nature),
            Embervale.Magic.SpellSchools.Color(DamageType.Frost),
            mode,
            "Nature/Frost");
    }

    /// <summary>None is a true identity, not an approximate one. Every colour in the UI passes
    /// through <c>UiTheme.Adapt</c>, so a rounding drift here would recolour the whole palette for
    /// the overwhelming majority of players who have this turned off.</summary>
    [Fact]
    public void NoneLeavesEveryColourExactlyAsAuthored()
    {
        foreach (Color color in new[]
                 {
                     new Color(0.55f, 0.68f, 0.44f),
                     new Color(0.82f, 0.42f, 0.36f),
                     new Color(0.99f, 0.86f, 0.55f),
                     Colors.White,
                     Colors.Black,
                 })
        {
            Assert.Equal(color, ColorVision.Daltonize(color, ColorVisionMode.None));
            Assert.Equal(color, ColorVision.Simulate(color, ColorVisionMode.None));
        }
    }

    /// <summary>Adaptation must stay inside the displayable range. An out-of-gamut channel clips,
    /// and a clipped channel is exactly where two colours silently merge again.</summary>
    [Theory]
    [InlineData(ColorVisionMode.Deuteranopia)]
    [InlineData(ColorVisionMode.Protanopia)]
    [InlineData(ColorVisionMode.Tritanopia)]
    public void AdaptationStaysInGamut(ColorVisionMode mode)
    {
        foreach (ItemRarity rarity in Enum.GetValues<ItemRarity>())
        {
            Color adapted = ColorVision.Daltonize(ItemRarities.Color(rarity), mode);
            Assert.InRange(adapted.R, 0f, 1f);
            Assert.InRange(adapted.G, 0f, 1f);
            Assert.InRange(adapted.B, 0f, 1f);
        }
    }

    /// <summary>Alpha is not a colour channel and must survive untouched — several semantic tokens
    /// are used at partial alpha for frames and washes.</summary>
    [Theory]
    [InlineData(ColorVisionMode.Deuteranopia)]
    [InlineData(ColorVisionMode.Tritanopia)]
    public void AlphaIsPreserved(ColorVisionMode mode)
    {
        var translucent = new Color(0.55f, 0.68f, 0.44f, 0.35f);
        Assert.Equal(0.35f, ColorVision.Daltonize(translucent, mode).A, 5);
    }

    /// <summary>
    /// With no <c>SettingsService</c> registered — boot, and this test harness — the theme must
    /// report the untouched palette. Every contrast pin in <c>UiContrastTests</c> depends on it, so
    /// a regression here would silently audit the wrong colours.
    /// </summary>
    [Fact]
    public void ThemeFallsBackToTheAuthoredPaletteWithoutSettings()
    {
        Assert.Equal(ColorVisionMode.None, UiTheme.VisionMode);
        Assert.False(UiTheme.HighContrast);
        Assert.Equal(new Color(0.55f, 0.68f, 0.44f), UiTheme.Good);
    }

    /// <summary>The text-scale seam must never breach the 12 px legibility floor, whatever the
    /// setting says. A text-size control that can make text unreadable is not accessibility.</summary>
    [Fact]
    public void FontSizeNeverFallsBelowTheLegibilityFloor()
    {
        Assert.True(UiTheme.FontSize(UiTheme.CaptionFontSize) >= UiTheme.CaptionFontSize);
        Assert.True(UiTheme.FontSize(1) >= UiTheme.CaptionFontSize);
    }
}
