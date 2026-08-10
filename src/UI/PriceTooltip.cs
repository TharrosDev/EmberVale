using System.Text;
using Embervale.Economy;
using Embervale.Localization;

namespace Embervale.UI;

/// <summary>
/// A <see cref="PriceQuote"/> as the player reads it (Phase 38U) — one line per reason, the running
/// gold on each. Separate from <see cref="PriceBreakdown"/> because that file may not touch Godot
/// (the test project throws constructing one) and <see cref="Loc"/> reads the catalogue through it.
///
/// ⚠️ <b>Every line is formatted with the same two substitutions</b>: <c>{0}</c> is the line's own
/// argument — a trade, a percentage, a quantity — and <c>{1}</c> is the price after that step. A row
/// that needs only one of them simply does not mention the other, which is why there is no per-key
/// branch here and no way for a new line to arrive without a home.
/// </summary>
public static class PriceTooltip
{
    /// <summary>The breakdown as tooltip text, newest step last. Empty for an empty quote rather than
    /// a stray header — a tooltip with nothing under it is worse than no tooltip.</summary>
    public static string Render(PriceQuote quote)
    {
        if (quote.Lines.Count == 0)
        {
            return string.Empty;
        }

        var text = new StringBuilder();
        for (int i = 0; i < quote.Lines.Count; i++)
        {
            PriceLine line = quote.Lines[i];
            if (i > 0)
            {
                text.Append('\n');
            }

            text.Append(Loc.TF(line.Key, line.Arg, line.Running));
        }

        return text.ToString();
    }
}
