using System;
using System.Collections.Generic;
using System.Linq;

namespace Embervale.UI;

/// <summary>
/// The pure half of the trade screens (vendor, crafting, storage, appraisal, contract board): how
/// wide their columns are, how many of a thing can be bought, why a purchase is refused, the order
/// a recipe list reads in and what a haulage posting risks. Plain values in and out, so each is
/// pinned by <c>TradeRulesTests</c> instead of by a screenshot.
/// </summary>
public static class TradeRules
{
    // --- Layout -------------------------------------------------------------

    /// <summary>The narrowest a detail column may be and still hold an item card's stat grid.</summary>
    public const float DetailMin = 232f;

    /// <summary>The widest a detail column grows: past this the card is a billboard.</summary>
    public const float DetailMax = 360f;

    /// <summary>
    /// The width of the detail column beside two item lists, for the width a full-screen panel has
    /// to lay out in (<c>UiTheme.UsableWidth</c>). Three tenths of the page, held between
    /// <see cref="DetailMin"/> and <see cref="DetailMax"/>; the lists share whatever is left.
    /// </summary>
    public static float DetailWidth(float usable) => Math.Clamp(usable * 0.30f, DetailMin, DetailMax);

    /// <summary>The width each of the two lists beside a detail column gets, after the gaps between
    /// the three columns. Never negative.</summary>
    public static float ListWidth(float usable, float gap) =>
        Math.Max(0f, (usable - DetailWidth(usable) - (gap * 2f)) * 0.5f);

    /// <summary>The crafting screen's middle column (ingredients and verbs).</summary>
    public static float IngredientWidth(float usable) => Math.Clamp(usable * 0.27f, 200f, 320f);

    // --- Buying -------------------------------------------------------------

    /// <summary>The most a single order may be, whatever the purse and the shelf say.</summary>
    public const int MaxOrder = 99;

    /// <summary>
    /// How many of one ware the player could buy in one press: what the purse covers, what the
    /// shelf holds (<paramref name="remaining"/> below zero is a shelf that never runs out) and
    /// what one stack takes, capped at <see cref="MaxOrder"/>. Room in the pack is not predicted;
    /// the order stops at the first unit that does not fit.
    /// </summary>
    public static int MaxBuy(int unitPrice, int purse, int remaining, int maxStack)
    {
        int cap = Math.Min(MaxOrder, Math.Max(1, maxStack));
        int shelf = remaining < 0 ? cap : remaining;
        int afford = unitPrice <= 0 ? cap : Math.Max(0, purse) / unitPrice;
        return Math.Max(0, Math.Min(cap, Math.Min(shelf, afford)));
    }

    /// <summary>Why a ware cannot be bought.</summary>
    public enum BuyRefusal
    {
        None,

        /// <summary>The row is kept back for standing, a stake or a story beat.</summary>
        Locked,
        SoldOut,
        CannotAfford,
    }

    /// <summary>
    /// The one reason shown for a refused purchase. The lock is named before the price and the
    /// empty shelf before the purse: a player who cannot have this at any amount of gold must not
    /// be told to come back with more of it.
    /// </summary>
    public static BuyRefusal RefusalOf(bool lockOpen, bool available, bool affordable) =>
        !lockOpen ? BuyRefusal.Locked
        : !available ? BuyRefusal.SoldOut
        : !affordable ? BuyRefusal.CannotAfford
        : BuyRefusal.None;

    /// <summary>The gold still missing for a price; zero when the purse covers it.</summary>
    public static int Shortfall(int price, int purse) => Math.Max(0, price - Math.Max(0, purse));

    /// <summary>A counter's spread as two whole percentages of an item's worth: what it asks and
    /// what it pays.</summary>
    public static (int Asks, int Pays) Spread(float buyMarkup, float sellFraction) =>
        ((int)Math.Round(buyMarkup * 100f), (int)Math.Round(sellFraction * 100f));

    // --- Crafting -----------------------------------------------------------

    /// <summary>What a recipe list sorts by.</summary>
    public readonly record struct RecipeKey(bool Pinned, bool Craftable, string Name);

    /// <summary>
    /// A recipe list in reading order: the pinned recipe, then what can be made now, then the
    /// rest, each group by name. Total, so a rebuild never reshuffles rows under the cursor.
    /// </summary>
    public static List<T> OrderRecipes<T>(IEnumerable<T> recipes, Func<T, RecipeKey> key) =>
        recipes
            .OrderByDescending(r => key(r).Pinned)
            .ThenByDescending(r => key(r).Craftable)
            .ThenBy(r => key(r).Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => key(r).Name, StringComparer.Ordinal)
            .ToList();

    /// <summary>Where one ingredient stands.</summary>
    public enum IngredientState
    {
        /// <summary>Held in full: a tick.</summary>
        Enough,

        /// <summary>Not enough held: a cross.</summary>
        Short,

        /// <summary>Not enough held, and a master is supplying the rest on the bill: a plus.</summary>
        Supplied,
    }

    public static IngredientState IngredientOf(int have, int need, bool commission) =>
        have >= need ? IngredientState.Enough
        : commission ? IngredientState.Supplied
        : IngredientState.Short;

    /// <summary>The index <paramref name="delta"/> steps from <paramref name="current"/> among
    /// <paramref name="count"/> tabs, wrapping at both ends.</summary>
    public static int StepTab(int current, int delta, int count) =>
        count <= 0 ? 0 : (((current + delta) % count) + count) % count;

    // --- Contracts ----------------------------------------------------------

    /// <summary>What a haulage posting asks the player to put up with. Each is shown as a chip
    /// with its own words; none is a colour alone.</summary>
    [Flags]
    public enum ContractRisk
    {
        None = 0,

        /// <summary>The goods are contraband: carrying them is itself the risk.</summary>
        Contraband = 1,

        /// <summary>The board turns over within a day.</summary>
        ClosingSoon = 2,

        /// <summary>The player is not carrying enough yet.</summary>
        Short = 4,
    }

    public static ContractRisk RisksOf(bool contraband, int daysLeft, int have, int need, bool filled)
    {
        if (filled)
        {
            return ContractRisk.None; // nothing is at stake on a posting already paid
        }

        ContractRisk risks = ContractRisk.None;
        if (contraband)
        {
            risks |= ContractRisk.Contraband;
        }

        if (daysLeft <= 1)
        {
            risks |= ContractRisk.ClosingSoon;
        }

        if (have < need)
        {
            risks |= ContractRisk.Short;
        }

        return risks;
    }
}
