using System;
using Godot;

namespace Embervale.UI;

/// <summary>
/// WCAG 2.x contrast arithmetic (30.5K). The legibility audit is executable: unit tests pin
/// every text-bearing token pair in <see cref="UiTheme"/> to AA (≥4.5:1) and every meaningful
/// non-text pair (bar fills) to ≥3:1, so a future palette retune cannot silently regress
/// readability. Alpha is ignored — tokens are compared as composited, opaque colours.
/// </summary>
public static class UiContrast
{
    /// <summary>WCAG relative luminance of an sRGB colour.</summary>
    public static double Luminance(Color color) =>
        (0.2126 * Linear(color.R)) + (0.7152 * Linear(color.G)) + (0.0722 * Linear(color.B));

    /// <summary>WCAG contrast ratio between two colours (order-independent, 1..21).</summary>
    public static double Ratio(Color a, Color b)
    {
        double la = Luminance(a);
        double lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    private static double Linear(float channel) =>
        channel <= 0.03928f ? channel / 12.92 : Math.Pow((channel + 0.055) / 1.055, 2.4);
}
