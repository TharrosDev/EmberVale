using System;
using System.Collections.Generic;

namespace Embervale.Economy;

/// <summary>
/// What a good is worth <em>where the player is standing</em> (Phase 38G) — the last multiplier the
/// economy arc needed, and the only one that can make carrying goods between two settlements pay.
/// Pure and Godot-free like <see cref="ShopPricing"/>, <see cref="HaggleRules"/> and
/// <see cref="WagerRules"/>, for the same reason: the test project cannot construct a Godot object.
///
/// ⚠️ <b>IT MOVES THE VALUE, NOT THE SPREAD, AND THAT IS THE WHOLE DIFFERENCE.</b> Every earlier
/// multiplier in the arc — standing, the specialty premium, a haggle — is a factor on a markup or a
/// fraction, and <c>ShopPricing</c>'s clamps therefore hold <c>sell &lt;= value &lt;= buy</c> at each
/// counter, which is exactly why no route could ever pay (38N1's finding, printed by
/// <see cref="EconomyReport"/> in its own output). This one produces a <b>local value</b> that both
/// sides of one counter then spread over — so that invariant survives untouched <em>at a shop</em>,
/// while two shops in different places can finally disagree about what a sack of grain is worth.
///
/// ⚠️ <b>Symmetry is structural here, not a rule to remember.</b> 38F's carried warning was that demand
/// applied to one side moves the <em>ratio</em> rather than the price. There is no one side to apply it
/// to: the function returns a value, and a value has no sides.
///
/// <b>A tag in neither list prices at the realm reference.</b> The town square and the Embermarket
/// author nothing on purpose — a multiplier everywhere is a multiplier nowhere, and the two districts
/// the player learns prices in are the baseline the mine and the coast are read against.
/// </summary>
public static class RegionDemand
{
    /// <summary>
    /// What a locally abundant good is worth here. Twenty metres from the seam, ore is cheap.
    ///
    /// ⚠️ <b>The pair of factors has to clear the widest specialty spread in the realm or nothing ever
    /// pays.</b> Buying at a specialist (markup 1.5 × 0.95) and selling to one (fraction 0.62 × 1.25)
    /// loses about 46% — a ratio of ~1.84 — so <c>DemandFactor / SurplusFactor</c> must beat that with
    /// room for integer rounding, which eats a whole route at low values. 1.50 / 0.62 is 2.42.
    /// </summary>
    public const float SurplusFactor = 0.62f;

    /// <summary>What a locally scarce good is worth here. Nothing grows in a hole in the ground.
    /// See <see cref="SurplusFactor"/> for why the pair is as wide as it is.</summary>
    public const float DemandFactor = 1.50f;

    /// <summary>
    /// The value of a good at one place. <paramref name="surplus"/> and <paramref name="demand"/> are
    /// the trade tags authored on the cell; an item wearing a tag in neither list is unaffected.
    ///
    /// ⚠️ <b>Surplus is answered first and a tag in both lists is treated as surplus</b>, deliberately:
    /// it is authoring nonsense, <c>--validate</c> refuses it, and a refusal is worth more than a
    /// resolution rule nobody can remember. This branch exists so a hand-edited <c>.tres</c> still
    /// prices deterministically, the same reason <c>ShopPricing</c> clamps a markup the validator
    /// already rejects.
    ///
    /// Floors at <c>1</c> for anything that had a value, like <see cref="ShopPricing.BuyPrice"/>: a
    /// surplus must never round a cheap good down to worthless, which would make it free to buy and
    /// impossible to sell.
    /// </summary>
    public static int ValueAt(
        int baseValue,
        IReadOnlyList<string> itemTags,
        IReadOnlyList<string> surplus,
        IReadOnlyList<string> demand)
    {
        if (baseValue <= 0 || itemTags.Count == 0)
        {
            return Math.Max(0, baseValue);
        }

        float factor = 1f;
        if (Wears(itemTags, surplus))
        {
            factor = SurplusFactor;
        }
        else if (Wears(itemTags, demand))
        {
            factor = DemandFactor;
        }

        if (factor == 1f)
        {
            return baseValue;
        }

        return Math.Max(1, (int)Math.Round(baseValue * factor, MidpointRounding.AwayFromZero));
    }

    /// <summary>
    /// <b>Which</b> of the item's tags the cell has an opinion about, or empty for none — the same scan
    /// <see cref="Wears"/> does, returning the tag instead of a boolean.
    ///
    /// It exists for 38U: a breakdown line that says a price moved must name <em>what</em> moved it, and
    /// re-deriving the match at the call site is a second copy of the rule this file owns. The cell's
    /// list is walked in its authored order, so the answer is the same one <see cref="ValueAt"/> acted on.
    /// </summary>
    public static string MatchedTag(IReadOnlyList<string> itemTags, IReadOnlyList<string> cellTags)
    {
        for (int i = 0; i < cellTags.Count; i++)
        {
            for (int j = 0; j < itemTags.Count; j++)
            {
                if (string.Equals(itemTags[j], cellTags[i], StringComparison.Ordinal))
                {
                    return cellTags[i];
                }
            }
        }

        return string.Empty;
    }

    /// <summary>Whether any of the item's tags appears in the cell's list. Small lists both — the
    /// vocabulary is closed and a settlement authors a handful — so a nested scan beats a set.</summary>
    private static bool Wears(IReadOnlyList<string> itemTags, IReadOnlyList<string> cellTags) =>
        MatchedTag(itemTags, cellTags).Length > 0;
}
