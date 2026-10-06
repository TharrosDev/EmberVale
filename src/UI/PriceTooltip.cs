using System.Collections.Generic;
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

    /// <summary>The same breakdown one reason at a time, for a screen that prints it under an item's
    /// card instead of hiding it behind a hover: a pad has no pointer to hover with.</summary>
    public static IEnumerable<string> Lines(PriceQuote quote)
    {
        foreach (PriceLine line in quote.Lines)
        {
            yield return Loc.TF(line.Key, line.Arg, line.Running);
        }
    }

    /// <summary>
    /// The breakdown as a ledger: what each step is, and the gold after it, as two columns. A step's
    /// words come from its <c>.label</c> key (the line's key with the gold left off); a step with
    /// no such key keeps its whole sentence and an empty second column. Only the two free jumps
    /// read "no charge": a sale that pays nothing, or a thing worth nothing, is still 0g.
    /// </summary>
    public static IEnumerable<(string Label, string Value)> Rows(PriceQuote quote)
    {
        foreach (PriceLine line in quote.Lines)
        {
            string labelKey = line.Key + ".label";
            if (!Loc.Has(labelKey))
            {
                yield return (Loc.TF(line.Key, line.Arg, line.Running), string.Empty);
                continue;
            }

            yield return (
                Loc.TF(labelKey, line.Arg),
                line.Key is PriceBreakdown.KeyTravelOwned or PriceBreakdown.KeyTravelMounted
                    ? Loc.T("shop.line.free")
                    : Loc.TF("shop.price", line.Running));
        }
    }

    /// <summary>
    /// The buy/sell spread in one sentence: what a counter asks for a thing and what it pays for the
    /// same thing, as percentages of its worth here. It answers the question every first sale raises,
    /// and it is why the two halves of a shop window never quote one number.
    /// </summary>
    public static string Spread(int asksPercent, int paysPercent) =>
        Loc.TF("shop.line.spread", asksPercent, paysPercent);
}
