using System;

namespace Embervale.UI;

/// <summary>
/// The measurements of what a <see cref="UiPanel"/> draws around its frame, and what the frame
/// gives up for it. Pure, so the one viewport that has no room to spare (a handheld at 853x533
/// logical) is checked by a unit test instead of by a screenshot nobody took.
/// </summary>
public static class UiChromeRules
{
    /// <summary>Below this logical width a full-screen panel's gutter shrinks.</summary>
    public const float NarrowWidth = 1100f;

    /// <summary>The gutter round a full-screen panel on a desktop viewport.</summary>
    public const int WideGutter = 70;

    /// <summary>Height of the footer legend's row: one keycap or glyph and a caption.</summary>
    public const int LegendHeight = 24;

    /// <summary>Clear space kept above and below a strip drawn in a gutter.</summary>
    public const int ChromeMargin = UiTheme.Space2xs;

    /// <summary>The gutter between the view edge and a full-screen panel, for a logical viewport width.</summary>
    public static int Gutter(float viewportWidth) => viewportWidth < NarrowWidth ? UiTheme.SpaceLg : WideGutter;

    /// <summary>
    /// The space a full-screen panel leaves under itself: its gutter, or the legend's row and its
    /// margins where the gutter is too short to hold them.
    /// </summary>
    public static int BottomInset(int gutter, bool legend) =>
        legend ? Math.Max(gutter, LegendHeight + (ChromeMargin * 2)) : gutter;
}
