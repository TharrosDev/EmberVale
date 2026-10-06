using Embervale.UI;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// The HUD's viewport-derived widths. The load-bearing claim is the first test: at the reference
/// width every widget is exactly the literal it replaced, the boss bar excepted.
/// </summary>
public class HudMetricsTests
{
    [Fact]
    public void AtTheReferenceWidth_EveryWidthIsTheLiteralItReplaced()
    {
        Assert.Equal(286f, HudMetrics.VitalsWidth(1280f));
        Assert.Equal(280f, HudMetrics.TrackerWidth(1280f));
        Assert.Equal(460f, HudMetrics.CompassWidth(1280f));
    }

    [Theory]
    [InlineData(853f)]
    [InlineData(640f)]
    [InlineData(0f)]
    [InlineData(float.NaN)]
    public void BelowTheReference_NothingShrinksUnderItsLiteral(float width)
    {
        Assert.Equal(286f, HudMetrics.VitalsWidth(width));
        Assert.Equal(280f, HudMetrics.TrackerWidth(width));
        Assert.Equal(460f, HudMetrics.CompassWidth(width));
        Assert.Equal(HudMetrics.BossBarMin, HudMetrics.BossBarWidth(width));
    }

    [Fact]
    public void AWiderLayout_GrowsEachWidthUpToItsCap()
    {
        Assert.Equal(358f, HudMetrics.VitalsWidth(1600f));
        Assert.Equal(350f, HudMetrics.TrackerWidth(1600f));
        Assert.Equal(575f, HudMetrics.CompassWidth(1600f));

        Assert.Equal(HudMetrics.VitalsMax, HudMetrics.VitalsWidth(3440f));
        Assert.Equal(HudMetrics.TrackerMax, HudMetrics.TrackerWidth(3440f));
        Assert.Equal(HudMetrics.CompassMax, HudMetrics.CompassWidth(3440f));
    }

    [Theory]
    [InlineData(853f, 420f)]
    [InlineData(1280f, 538f)]
    [InlineData(1600f, 672f)]
    [InlineData(3440f, 720f)]
    public void TheBossBar_IsAShareOfTheWidthBetweenItsFloorAndCeiling(float width, float expected)
    {
        Assert.Equal(expected, HudMetrics.BossBarWidth(width));
    }

    [Fact]
    public void Widths_NeverDecreaseAsTheLayoutWidens()
    {
        float vitals = 0f, tracker = 0f, compass = 0f, boss = 0f;
        for (float width = 400f; width <= 4000f; width += 40f)
        {
            Assert.True(HudMetrics.VitalsWidth(width) >= vitals);
            Assert.True(HudMetrics.TrackerWidth(width) >= tracker);
            Assert.True(HudMetrics.CompassWidth(width) >= compass);
            Assert.True(HudMetrics.BossBarWidth(width) >= boss);
            vitals = HudMetrics.VitalsWidth(width);
            tracker = HudMetrics.TrackerWidth(width);
            compass = HudMetrics.CompassWidth(width);
            boss = HudMetrics.BossBarWidth(width);
        }
    }

    [Fact]
    public void LayoutWidth_IsTheViewportLessTheSafeZoneOverTheScale()
    {
        Assert.Equal(1280f, HudMetrics.LayoutWidth(1280f, 1f, 0f));
        Assert.Equal(1024f, HudMetrics.LayoutWidth(1280f, 1.25f, 0f), 3);
        Assert.Equal(1152f, HudMetrics.LayoutWidth(1280f, 1f, 0.05f), 3);
        Assert.Equal(1280f, HudMetrics.LayoutWidth(1280f, 0f, 0f));
    }

    [Fact]
    public void WithNoScaleAndNoSafeZone_TheScaledRectIsTheFullRect()
    {
        Assert.Equal((0f, 0f, 1f, 1f), HudMetrics.ScaledAnchors(1f, 0f));
    }

    [Theory]
    [InlineData(1.5f, 0f)]
    [InlineData(0.75f, 0f)]
    [InlineData(1f, 0.1f)]
    [InlineData(1.25f, 0.05f)]
    public void TheScaledRect_CoversTheScreenLessTheSafeZoneOnceScaled(float scale, float safeZone)
    {
        (float left, float top, float right, float bottom) = HudMetrics.ScaledAnchors(scale, safeZone);

        // Scaled about its top-left corner, the rect's far edge lands at 1 - safeZone.
        Assert.Equal(safeZone, left);
        Assert.Equal(safeZone, top);
        Assert.Equal(1f - safeZone, left + (right - left) * scale, 4);
        Assert.Equal(1f - safeZone, top + (bottom - top) * scale, 4);
    }

    [Theory]
    [InlineData(1f, 0f, 160f)]
    [InlineData(1.5f, 0f, 240f)]
    [InlineData(0.75f, 0f, 120f)]
    [InlineData(1f, 0.1f, 232f)]
    [InlineData(1.5f, 0.05f, 276f)]
    public void AClearanceInHudUnits_LandsOnTheScreenWhereTheScaledHudDrawsIt(float scale, float safeZone, float expected)
    {
        const float Height = 720f;
        Assert.Equal(expected, HudMetrics.ScreenClearance(160f, Height, scale, safeZone), 3);

        // The same line through the scaled rect: its bottom edge on screen, less the scaled clearance.
        (_, float top, _, float bottom) = HudMetrics.ScaledAnchors(scale, safeZone);
        float bottomOnScreen = (top + (bottom - top) * scale) * Height;
        Assert.Equal(Height - bottomOnScreen + 160f * scale, HudMetrics.ScreenClearance(160f, Height, scale, safeZone), 2);
    }
}
