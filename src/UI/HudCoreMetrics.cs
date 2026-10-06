using System;

namespace Embervale.UI;

/// <summary>
/// Sizes of the HUD's own widgets: bar heights, icon sizes, the hotbar cell and the minimap plot.
/// Pure, and the companion of <see cref="HudMetrics"/>, which holds the widths that follow the
/// layout. A HUD widget reads a size from here instead of carrying a pixel count of its own, so the
/// vitals, the party strip and the hotbar cannot drift apart one literal at a time.
/// </summary>
public static class HudCoreMetrics
{
    /// <summary>The health bar: the one read under pressure, so the tallest.</summary>
    public const float BarHeight = 12f;

    /// <summary>Stamina, mana, corruption and a companion's health.</summary>
    public const float BarMinorHeight = 8f;

    /// <summary>A progress line under something else: an objective's count, a spell's recovery.</summary>
    public const float BarThinHeight = 4f;

    /// <summary>The icon that leads a HUD row.</summary>
    public const float IconSize = 18f;

    /// <summary>The icon that leads a subordinate HUD row.</summary>
    public const float IconMinor = 16f;

    /// <summary>A bullet beside a line of caption text.</summary>
    public const float BulletSize = 12f;

    /// <summary>One hotbar cell, square. Above <c>UiTheme.ControlHeight</c>: it is pressed.</summary>
    public const float HotbarCell = 72f;

    /// <summary>A painted item icon in a hotbar cell.</summary>
    public const float HotbarIcon = 40f;

    /// <summary>The effect glyph a cell falls back to when its item has no painted icon.</summary>
    public const float HotbarGlyph = 24f;

    /// <summary>The lock-on reticle.</summary>
    public const float ReticleSize = 28f;

    /// <summary>Side of the minimap plot at the reference width and below.</summary>
    public const float MinimapMin = 186f;

    public const float MinimapMax = 232f;

    /// <summary>Height of the compass strip: marks, letters, rule and the distance line under it.</summary>
    public const float CompassHeight = 50f;

    /// <summary>The narrowest a wrapping line of tracker text may be squeezed by what shares its row.</summary>
    public const float TrackerTextMin = 110f;

    /// <summary>Side of the minimap plot for a layout width: its old literal at the reference width
    /// and below, growing in proportion above it up to a cap, like every <see cref="HudMetrics"/> width.</summary>
    public static float MinimapSide(float layoutWidth)
    {
        float side = MinimapMin * layoutWidth / HudMetrics.ReferenceWidth;
        return float.IsFinite(side) ? MathF.Round(Math.Clamp(side, MinimapMin, MinimapMax)) : MinimapMin;
    }

    /// <summary>
    /// The fixed width of a "current/max" reading set at <paramref name="fontSize"/>: room for four
    /// digits either side of the stroke, so the number never shuffles the bar beside it as it changes.
    /// </summary>
    public static float ReadingWidth(int fontSize) => MathF.Ceiling(Math.Max(fontSize, 1) * 5.2f);
}
