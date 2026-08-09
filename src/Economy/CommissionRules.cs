using System;
using System.Collections.Generic;

namespace Embervale.Economy;

/// <summary>
/// What a master smith charges to make something for you (Phase 38Q): a flat labour fee, plus the
/// materials you did not bring, at his own counter price. Pure and Godot-free for the reason
/// <see cref="ContrabandLaw"/> and <see cref="ShopStock"/> are — the test project throws an
/// <c>AccessViolationException</c> constructing any Godot object, and <c>RecipeIngredient</c> is one,
/// so the shortfall arrives here as plain numbers and the caller does the resolving.
///
/// <b>The fee has to buy something the free forge does not.</b> <c>town_hub</c> carries a public
/// <c>StationForge</c> twenty metres from the smith, so a master who only charges for labour on a
/// recipe the player can already supply is strictly worse than walking — the "correct, validated and
/// completely imperceptible" failure that got 38G parked. What he sells is the <em>materials</em>:
/// commissioning undercuts buying the finished piece off his shelf in exact proportion to what the
/// player already carries.
///
/// ⚠️ <b>THIS IS THE FIRST PRICE IN THE ECONOMY THE <see cref="ShopPricing"/> CLAMPS DO NOT PROTECT.</b>
/// Every earlier one was a spread over a single item's value, so <c>sell &lt;= value &lt;= buy</c> held by
/// construction (38F) and 38P's consignment inherited it by calling <see cref="ShopPricing.SellPrice"/>.
/// A commission spans <em>two different items</em> — ingredients in, output out — and crafting is meant
/// to add value, so nothing in the arithmetic stops <c>sell(output) &gt; buy(ingredients) + labour</c>.
/// That is a money printer with no cap and no cooldown. It is closed by the labour fee being large
/// enough, which is authored data, which is why <see cref="Exploitable"/> exists and
/// <c>--validate</c> runs it over every recipe the counter can reach.
/// </summary>
public static class CommissionRules
{
    /// <summary>
    /// What the master charges: <paramref name="labourFee"/> plus each missing unit at
    /// <see cref="ShopPricing.BuyPrice"/>.
    ///
    /// <b>It calls <c>BuyPrice</c> rather than repeating the multiply</b> — 38P's carried lesson in the
    /// form where it bites hardest, because this number is both quoted on screen and charged. A second
    /// rounding rule for the same question is how the quote and the bill drift apart.
    ///
    /// Each line carries <b>its own</b> markup — the master's shop markup already run through
    /// <see cref="ShopPricing.MarkupFor"/>, so his standing discount and his specialty reach a
    /// commission exactly as they reach his counter, with no second ramp to keep in step. Per line
    /// rather than per basket because 38F's specialty discount is a property of the <em>item</em>: a
    /// smith who is keen on metal is not thereby keen on the leather in the same recipe.
    ///
    /// ⚠️ Saturates rather than overflowing, as <see cref="ContrabandLaw.Fine"/> does: a 32-bit wrap
    /// would hand back a <em>negative</em> price, and a negative price is a payment.
    /// </summary>
    public static int Cost(int labourFee, IReadOnlyList<(int UnitValue, int Missing, float Markup)> shortfall)
    {
        long total = Math.Max(0, labourFee);

        for (int i = 0; i < shortfall.Count; i++)
        {
            (int unitValue, int missing, float markup) = shortfall[i];
            if (missing <= 0)
            {
                continue;
            }

            total += (long)ShopPricing.BuyPrice(unitValue, markup) * missing;
        }

        return (int)Math.Clamp(total, 0, int.MaxValue);
    }

    /// <summary>
    /// Whether commissioning this recipe and selling the result pays for itself — the money printer
    /// described on the type. <c>true</c> is a <b>content fault</b>, not a runtime state:
    /// <c>--validate</c> refuses it and no player can ever reach it.
    ///
    /// The comparison is deliberately <c>&gt;=</c>. Breaking even is not free money, but it is an
    /// unbounded loop of pressing a button for nothing, and the margin an author thinks they left is
    /// the margin a later standing discount removes — <see cref="ShopPricing.PriceMultiplierFor"/>
    /// takes 15% off the buy side at Exalted, and it is the labour fee's turn to absorb that.
    /// </summary>
    public static bool Exploitable(int commissionCost, int bestSellPrice, int outputQuantity) =>
        (long)Math.Max(0, bestSellPrice) * Math.Max(1, outputQuantity) >= commissionCost;
}
