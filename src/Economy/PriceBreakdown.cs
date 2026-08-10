using System;
using System.Collections.Generic;
using Embervale.Factions;

namespace Embervale.Economy;

/// <summary>
/// One step of a price, as the player reads it: a locale key, the thing that step names (a trade,
/// a percentage, a quantity), and the gold figure <em>after</em> that step has been applied.
/// </summary>
/// <param name="Key">A <c>shop.line.*</c> key from <see cref="PriceBreakdown"/>'s constants.</param>
/// <param name="Arg">The line's single substitution, or empty when it takes none.</param>
/// <param name="Running">The price once this step has been applied — gold, never a factor.</param>
public readonly record struct PriceLine(string Key, string Arg, int Running);

/// <summary>
/// A price and the reasons for it. <see cref="Total"/> is what the button charges;
/// <see cref="Unit"/> is what one of the thing costs, which is the same number for everything except
/// a stack (38H's saturated payout is a decaying sum, not a multiply).
/// </summary>
public readonly record struct PriceQuote(IReadOnlyList<PriceLine> Lines, int Unit, int Total);

/// <summary>
/// Why a price is what it is (Phase 38U). Pure and Godot-free like <see cref="ShopPricing"/>,
/// <see cref="RegionDemand"/> and <see cref="HaggleRules"/>, and for the same reason: the test
/// project throws constructing any Godot object, so this is the half that can be pinned.
///
/// ⚠️ <b>THIS IS THE CHARGE, NOT A COMMENTARY ON IT.</b> Every surface below hands the player
/// <c>Total</c> and charges <c>Total</c>. The obvious shape — a display-only breakdown beside the
/// existing expression, kept honest by a validator rule — is two expressions of one number, and a
/// rule to hold them together is the rung above the one that works. Here the explanation
/// <em>cannot</em> disagree with the bill, because there is only one of them.
///
/// <b>Nothing new is multiplied here.</b> Each builder composes the functions that already shipped
/// (<see cref="ShopPricing.MarkupFor"/>, <see cref="ShopPricing.SellFractionFor"/>,
/// <see cref="ShopStock.SaturatedPayout"/>, <see cref="ConsignmentRules"/>,
/// <see cref="CommissionRules.Cost"/>, <see cref="TravelFee.For"/>) and reports the number after
/// each — so <c>NOW.md</c>'s invariant 4 ("what is this a spread over?") answers <em>nothing, it is
/// not a spread</em>, 38F's <c>NoCombinationOfMultipliersLetsSellingBeatBuying</c> sweep needs no new
/// entry, and the 38A clamps cover it untouched. The last line's <see cref="PriceLine.Running"/> is
/// the total by construction: the intermediate factors are accumulated in the <em>same order</em> the
/// shipped helpers multiply them, so the two agree bit for bit rather than approximately.
///
/// ⚠️ <b>The local value handed in must come from <c>PriceView.Today</c></b> (38T's carry). A
/// breakdown built from the authored <c>.tres</c> would calmly explain the wrong number on exactly
/// the days a supply shock exists for, which is worse than no breakdown at all.
/// </summary>
public static class PriceBreakdown
{
    /// <summary>What the thing is worth before anywhere or anyone has had an opinion about it.</summary>
    public const string KeyBase = "shop.line.base";

    /// <summary>The cell is awash in this trade, so it is worth less here (38G).</summary>
    public const string KeyLocalSurplus = "shop.line.local_surplus";

    /// <summary>The cell is short of this trade, so it is worth more here (38G).</summary>
    public const string KeyLocalDemand = "shop.line.local_demand";

    /// <summary>The same two, when a <em>supply shock</em> is what put the tag in that list (38T). A
    /// separate pair rather than an extra clause because one of them expires and the other does
    /// not — the player is being told which of the two kinds of fact they are looking at.</summary>
    public const string KeyShockSurplus = "shop.line.shock_surplus";

    /// <inheritdoc cref="KeyShockSurplus"/>
    public const string KeyShockDemand = "shop.line.shock_demand";

    /// <summary>The merchant's own margin, before anything about the player (38A).</summary>
    public const string KeyMarkup = "shop.line.markup";

    /// <summary>What the merchant pays out of the item's local value, before anything else (38A).</summary>
    public const string KeyFraction = "shop.line.fraction";

    /// <summary>What the player's standing with the merchant's faction does (38C).</summary>
    public const string KeyStanding = "shop.line.standing";

    /// <summary>The merchant's expert trade — keener buying, keener paying (38F).</summary>
    public const string KeySpecialty = "shop.line.specialty";

    /// <summary>Today's struck bargain (38S).</summary>
    public const string KeyHaggle = "shop.line.haggle";

    /// <summary>A whole stack at one price each — the unsaturated multiply (38H).</summary>
    public const string KeyStack = "shop.line.stack";

    /// <summary>A whole stack with the merchant's appetite falling across its own units (38H). ⚠️ It
    /// is a <em>sum</em>, not a multiply, which is the one place in the game where the quoted unit
    /// price times the quantity is the wrong number.</summary>
    public const string KeyGlut = "shop.line.glut";

    /// <summary>What the broker puts it on the shelf for, before her cut (38P).</summary>
    public const string KeyShelf = "shop.line.shelf";

    /// <summary>The house's commission on a consignment (38P).</summary>
    public const string KeyHouseCut = "shop.line.house_cut";

    /// <summary>The master's fee for the work itself (38Q).</summary>
    public const string KeyLabour = "shop.line.labour";

    /// <summary>Materials the master has to supply because the player did not (38Q).</summary>
    public const string KeyMaterial = "shop.line.material";

    /// <summary>A jump inside the region you are standing in (38C).</summary>
    public const string KeyTravelLocal = "shop.line.travel_local";

    /// <summary>A jump across a realm boundary (38C).</summary>
    public const string KeyTravelCross = "shop.line.travel_cross";

    /// <summary>A jump to a holding the player owns, which is free (38C/37A).</summary>
    public const string KeyTravelOwned = "shop.line.travel_owned";

    /// <summary>
    /// Every key a breakdown can emit. It exists so <c>ContentValidator</c> can prove the whole set
    /// resolves rather than only the ones today's authored data happens to reach — a shock line, a
    /// glut line and a broker's cut are all unreachable at the town square, so a validator that
    /// walked the shops would pass while three tooltips showed a raw key.
    /// </summary>
    public static readonly IReadOnlyList<string> AllKeys = new[]
    {
        KeyBase, KeyLocalSurplus, KeyLocalDemand, KeyShockSurplus, KeyShockDemand, KeyMarkup,
        KeyFraction, KeyStanding, KeySpecialty, KeyHaggle, KeyStack, KeyGlut, KeyShelf, KeyHouseCut,
        KeyLabour, KeyMaterial, KeyTravelLocal, KeyTravelCross, KeyTravelOwned,
    };

    /// <summary>
    /// What the player pays for one of a merchant's wares, and why. <paramref name="localValue"/> is
    /// <c>ShopResource.LocalValue(value, tags, PriceView.Today)</c> — passed in rather than resolved
    /// here because that call needs Godot and this file may not.
    /// </summary>
    public static PriceQuote Buy(
        int baseValue,
        int localValue,
        string localTag,
        bool shocked,
        float markup,
        ReputationTier tier,
        bool specialty,
        bool haggled)
    {
        var lines = new List<PriceLine> { new(KeyBase, string.Empty, baseValue) };
        AddLocal(lines, baseValue, localValue, localTag, shocked);

        float running = markup;
        lines.Add(new(KeyMarkup, Percent(markup), ShopPricing.BuyPrice(localValue, running)));

        // ⚠️ Multiplied in ShopPricing.MarkupFor's order, so the last Running below is Total exactly
        // rather than within a rounding of it. Reordering these three is a silent off-by-one-gold bug.
        if (tier != ReputationTier.Neutral)
        {
            float tierFactor = ShopPricing.PriceMultiplierFor(tier);
            running *= tierFactor;
            lines.Add(new(KeyStanding, Percent(tierFactor), ShopPricing.BuyPrice(localValue, running)));
        }

        if (specialty)
        {
            running *= ShopPricing.SpecialtyBuyDiscount;
            lines.Add(new(
                KeySpecialty,
                Percent(ShopPricing.SpecialtyBuyDiscount),
                ShopPricing.BuyPrice(localValue, running)));
        }

        if (haggled)
        {
            running *= HaggleRules.BuyFactor(true);
            lines.Add(new(
                KeyHaggle, Percent(HaggleRules.BuyDiscount), ShopPricing.BuyPrice(localValue, running)));
        }

        int total = ShopPricing.BuyPrice(
            localValue, ShopPricing.MarkupFor(markup, tier, specialty, haggled));

        return new PriceQuote(lines, total, total);
    }

    /// <summary>
    /// What a merchant pays for a stack, and why. The last line is the stack rather than the unit,
    /// because that is the number on the button — and when the merchant is glutted it is a
    /// <see cref="ShopStock.SaturatedPayout"/> sum that the unit price cannot be multiplied into.
    /// </summary>
    public static PriceQuote Sell(
        int baseValue,
        int localValue,
        string localTag,
        bool shocked,
        float fraction,
        bool specialty,
        bool haggled,
        int quantity,
        int absorbed,
        int restockDays)
    {
        var lines = new List<PriceLine> { new(KeyBase, string.Empty, baseValue) };
        AddLocal(lines, baseValue, localValue, localTag, shocked);

        float running = fraction;
        // ⚠️ A plain percentage, not a signed delta. A sell fraction of 0.4 is "they pay 40% of that",
        // and rendering it the way the markup and the standing ramp are rendered would print "-60%",
        // which reads as a penalty on top of a spread rather than as the spread itself.
        lines.Add(new(KeyFraction, Cut(fraction), ShopPricing.SellPrice(localValue, running)));

        // ShopPricing.SellFractionFor's order, for the reason Buy above records. Standing is
        // deliberately absent from this side and always has been (38C) — there is no line for it here
        // because there is no factor for it.
        if (specialty)
        {
            running *= ShopPricing.SpecialtySellBonus;
            lines.Add(new(
                KeySpecialty,
                Percent(ShopPricing.SpecialtySellBonus),
                ShopPricing.SellPrice(localValue, running)));
        }

        if (haggled)
        {
            running *= HaggleRules.SellFactor(true);
            lines.Add(new(
                KeyHaggle, Percent(HaggleRules.SellBonus), ShopPricing.SellPrice(localValue, running)));
        }

        int unit = ShopPricing.SellPrice(
            localValue, ShopPricing.SellFractionFor(fraction, specialty, haggled));
        int total = ShopStock.SaturatedPayout(unit, absorbed, quantity, restockDays);

        if (ShopStock.SaturationMultiplier(absorbed, restockDays) < 1f)
        {
            lines.Add(new(KeyGlut, quantity.ToString(), total));
        }
        else if (quantity > 1)
        {
            lines.Add(new(KeyStack, quantity.ToString(), total));
        }

        return new PriceQuote(lines, unit, total);
    }

    /// <summary>What a broker's shelf will hand back, and what she keeps (38P). She fronts nothing, so
    /// no purse and no saturation reach this — a stack lists for the multiply.</summary>
    public static PriceQuote Consign(
        int baseValue,
        int localValue,
        string localTag,
        bool shocked,
        float consignFraction,
        float commission,
        int quantity)
    {
        var lines = new List<PriceLine> { new(KeyBase, string.Empty, baseValue) };
        AddLocal(lines, baseValue, localValue, localTag, shocked);

        int gross = ConsignmentRules.Gross(localValue, consignFraction);
        lines.Add(new(KeyShelf, Cut(consignFraction), gross));

        int unit = ConsignmentRules.Net(gross, commission);
        lines.Add(new(KeyHouseCut, Cut(commission), unit));

        int safeQuantity = Math.Max(1, quantity);
        int total = unit * safeQuantity;
        if (safeQuantity > 1)
        {
            lines.Add(new(KeyStack, safeQuantity.ToString(), total));
        }

        return new PriceQuote(lines, unit, total);
    }

    /// <summary>
    /// What a master charges to make a thing, split into the work and each material the player failed
    /// to bring (38Q). ⚠️ <b>The split is the whole point of putting this here</b>: the window quoted
    /// one figure, so a player who had walked in with half the materials could not see that they had
    /// saved anything.
    /// </summary>
    public static PriceQuote Commission(
        int labourFee, IReadOnlyList<(string Name, int UnitValue, int Missing, float Markup)> shortfall)
    {
        int labour = Math.Max(0, labourFee);
        var lines = new List<PriceLine> { new(KeyLabour, string.Empty, labour) };

        // Accumulated exactly as CommissionRules.Cost accumulates it, including the long widening it
        // added so a 32-bit wrap cannot hand back a negative price — which is a payment.
        long running = labour;
        var costed = new List<(int UnitValue, int Missing, float Markup)>(shortfall.Count);
        foreach ((string name, int unitValue, int missing, float markup) in shortfall)
        {
            costed.Add((unitValue, missing, markup));
            if (missing <= 0)
            {
                continue;
            }

            running += (long)ShopPricing.BuyPrice(unitValue, markup) * missing;
            lines.Add(new(
                KeyMaterial, $"{name} x{missing}", (int)Math.Clamp(running, 0, int.MaxValue)));
        }

        int total = CommissionRules.Cost(labourFee, costed);

        return new PriceQuote(lines, total, total);
    }

    /// <summary>What a fast-travel jump costs and which of <see cref="TravelFee"/>'s three cases it
    /// is. One line, because the fee has one reason — the gate still applies: a number the player is
    /// charged says why.</summary>
    public static PriceQuote Travel(bool ownedHolding, bool crossRegion)
    {
        int fee = TravelFee.For(ownedHolding, crossRegion);
        string key = ownedHolding ? KeyTravelOwned : crossRegion ? KeyTravelCross : KeyTravelLocal;

        return new PriceQuote(new[] { new PriceLine(key, string.Empty, fee) }, fee, fee);
    }

    /// <summary>
    /// The place's opinion, when it has one. Silent when the local value equals the base — the town
    /// square and the Embermarket author nothing on purpose, and a line saying "prices are normal
    /// here" is the noise <c>BuildLocalTrade</c> already refuses to print.
    /// </summary>
    private static void AddLocal(
        List<PriceLine> lines, int baseValue, int localValue, string localTag, bool shocked)
    {
        if (localValue == baseValue)
        {
            return;
        }

        bool scarce = localValue > baseValue;
        string key = shocked
            ? scarce ? KeyShockDemand : KeyShockSurplus
            : scarce ? KeyLocalDemand : KeyLocalSurplus;

        lines.Add(new PriceLine(key, localTag, localValue));
    }

    /// <summary>A factor as the signed percentage the player reads on the standing caption — derived
    /// from the multiplier rather than written out again, so the two cannot disagree.</summary>
    private static string Percent(float factor) =>
        ((int)Math.Round((factor - 1f) * 100f, MidpointRounding.AwayFromZero)).ToString("+0;-0;0");

    /// <summary>A commission as a plain percentage — it is a slice taken, not a delta applied, so the
    /// sign the standing ramp needs would read as a discount here.</summary>
    private static string Cut(float commission) =>
        ((int)Math.Round(Math.Clamp(commission, 0f, 1f) * 100f, MidpointRounding.AwayFromZero))
            .ToString();
}
