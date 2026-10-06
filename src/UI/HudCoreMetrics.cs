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

    /// <summary>Width of one hotbar cell where the bottom bar has room. Above
    /// <c>UiTheme.ControlHeight</c>: it is pressed.</summary>
    public const float HotbarCell = 72f;

    /// <summary>...and at <see cref="NarrowWidth"/>, still above <c>UiTheme.ControlHeight</c>.</summary>
    public const float HotbarCellNarrow = 56f;

    /// <summary>The least height of a hotbar cell: the line that holds the key and the count, and
    /// under it the smallest icon. A cell is square wherever its width allows that, so it is only
    /// taller than it is wide on the narrowest layouts.</summary>
    public const float HotbarCellHeightMin = 64f;

    /// <summary>The line at the top of a hotbar cell: the key's cap on the left, the count on the right.</summary>
    public const float HotbarHead = 24f;

    /// <summary>A painted item icon in a full-size hotbar cell.</summary>
    public const float HotbarIcon = 40f;

    /// <summary>The effect glyph a cell falls back to when its item has no painted icon. Smaller than
    /// the painting, because that cell also carries the item's name.</summary>
    public const float HotbarGlyph = 24f;

    /// <summary>...and in a cell too short to hold that glyph over a line of text.</summary>
    public const float HotbarGlyphNarrow = 16f;

    /// <summary>The lock-on reticle.</summary>
    public const float ReticleSize = 28f;

    /// <summary>Side of the minimap plot at the reference width and below.</summary>
    public const float MinimapMin = 186f;

    public const float MinimapMax = 232f;

    /// <summary>Side of the minimap plot at <see cref="NarrowWidth"/>.</summary>
    public const float MinimapNarrow = 140f;

    /// <summary>The minimap's frame, each side: the keyline and the lighter line inside it.</summary>
    public const float MinimapFrame = 2f;

    /// <summary>The narrowest layout the HUD is fitted to: a Steam Deck at UI scale 1.5.</summary>
    public const float NarrowWidth = 853f;

    /// <summary>Below this layout width the vitals, five full cells and the full minimap no longer
    /// fit side by side on the bottom bar, so the cells and the plot give way in proportion down to
    /// <see cref="NarrowWidth"/>. The vitals keep their width: it has a floor in <see cref="HudMetrics"/>.</summary>
    public const float RoomyWidth = 980f;

    /// <summary>The narrowest the compass strip is drawn.</summary>
    public const float CompassNarrow = 200f;

    /// <summary>Height of the compass strip: marks, letters, rule and the distance line under it.</summary>
    public const float CompassHeight = 50f;

    /// <summary>The narrowest a wrapping line of tracker text may be squeezed by what shares its row.</summary>
    public const float TrackerTextMin = 110f;

    /// <summary>Side of the minimap plot for a layout width: its old literal at the reference width,
    /// growing in proportion above it up to a cap, like every <see cref="HudMetrics"/> width, and
    /// giving way below <see cref="RoomyWidth"/> so the bottom bar still fits.</summary>
    public static float MinimapSide(float layoutWidth)
    {
        if (layoutWidth > 0f && layoutWidth < RoomyWidth)
        {
            return Narrowed(layoutWidth, MinimapNarrow, MinimapMin);
        }

        float side = MinimapMin * layoutWidth / HudMetrics.ReferenceWidth;
        return float.IsFinite(side) ? MathF.Round(Math.Clamp(side, MinimapMin, MinimapMax)) : MinimapMin;
    }

    /// <summary>Width of a hotbar cell for a layout width: <see cref="HotbarCell"/> wherever the
    /// bottom bar has room (and before the layout is known), narrower below <see cref="RoomyWidth"/>.</summary>
    public static float HotbarCellWidth(float layoutWidth) =>
        layoutWidth > 0f && layoutWidth < RoomyWidth
            ? Narrowed(layoutWidth, HotbarCellNarrow, HotbarCell)
            : HotbarCell;

    /// <summary>Height of a hotbar cell that is <paramref name="cellWidth"/> wide: square, down to
    /// <see cref="HotbarCellHeightMin"/>. A painted icon is its own label (the tooltip has the name),
    /// so the two lines of name that made the cell a tall dark box are gone.</summary>
    public static float HotbarCellHeight(float cellWidth) =>
        float.IsFinite(cellWidth) ? MathF.Max(cellWidth, HotbarCellHeightMin) : HotbarCell;

    /// <summary>Side of the painted icon in a cell <paramref name="cellHeight"/> tall: what the head
    /// line and the cell's inset leave, and never more than <see cref="HotbarIcon"/>.</summary>
    public static float HotbarIconSide(float cellHeight) =>
        Math.Clamp(cellHeight - HotbarHead - (2f * UiTheme.Space2xs), HotbarGlyphNarrow, HotbarIcon);

    /// <summary>Side of the fallback glyph in a cell <paramref name="cellHeight"/> tall, leaving a
    /// line for the name under it.</summary>
    public static float HotbarGlyphSide(float cellHeight) =>
        cellHeight >= HotbarCell ? HotbarGlyph : HotbarGlyphNarrow;

    /// <summary>
    /// The least width the bottom bar needs at a layout width: the vitals, the hotbar and the framed
    /// minimap, the gaps between them (two, counted as the four the bar once had, which leaves it a
    /// little slack) and the safe margin either side. Nothing
    /// lays out from this; it is the sum the sizes above are held to, so the minimap is never pushed
    /// off the right edge.
    /// </summary>
    public static float BottomBarMinimum(float layoutWidth) =>
        (2f * UiTheme.SpaceLg) + HudMetrics.VitalsWidth(layoutWidth) + (4f * UiTheme.SpaceMd)
        + (Items.HotbarComponent.SlotCount * HotbarCellWidth(layoutWidth))
        + ((Items.HotbarComponent.SlotCount - 1) * UiTheme.SpaceXs)
        + MinimapSide(layoutWidth) + (2f * MinimapFrame);

    /// <summary>
    /// Width of the compass strip for a layout width: <see cref="HudMetrics.CompassWidth"/>, less
    /// whatever would otherwise run under the quest tracker. The strip is centred, so the tracker on
    /// its right bounds it on both sides, which also keeps it off the clock on its left.
    /// </summary>
    public static float CompassWidth(float layoutWidth)
    {
        float full = HudMetrics.CompassWidth(layoutWidth);
        float between = layoutWidth - (2f * (UiTheme.SpaceLg + HudMetrics.TrackerWidth(layoutWidth) + UiTheme.SpaceMd));
        return layoutWidth > 0f && float.IsFinite(between)
            ? MathF.Round(Math.Clamp(between, CompassNarrow, full))
            : full;
    }

    /// <summary>
    /// Whether the boss bar, centred, would reach the quest tracker at this layout width. The bar has
    /// a floor it does not shrink below (<see cref="HudMetrics.BossBarMin"/>: it is read from across
    /// a fight), so on a narrow layout the two meet, and there the tracker steps aside for as long as
    /// the boss frame is up. A fight for your life is not when you reread your errands.
    /// </summary>
    public static bool BossBarMeetsTracker(float layoutWidth)
    {
        if (!(layoutWidth > 0f) || !float.IsFinite(layoutWidth))
        {
            return false;
        }

        float barRight = (layoutWidth / 2f) + (HudMetrics.BossBarWidth(layoutWidth) / 2f);
        float trackerLeft = layoutWidth - UiTheme.SpaceLg - HudMetrics.TrackerWidth(layoutWidth);
        return barRight + UiTheme.SpaceMd > trackerLeft;
    }

    private static float Narrowed(float layoutWidth, float atNarrow, float atRoomy)
    {
        float t = Math.Clamp((layoutWidth - NarrowWidth) / (RoomyWidth - NarrowWidth), 0f, 1f);
        return MathF.Floor(atNarrow + ((atRoomy - atNarrow) * t));
    }

    /// <summary>
    /// The fixed width of a "current/max" reading set at <paramref name="fontSize"/>: room for four
    /// digits either side of the stroke, so the number never shuffles the bar beside it as it changes.
    /// </summary>
    public static float ReadingWidth(int fontSize) => MathF.Ceiling(Math.Max(fontSize, 1) * 5.2f);
}
