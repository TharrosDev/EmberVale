using System.Collections.Generic;

namespace Embervale.Economy;

/// <summary>
/// The closed vocabulary of trade tags (Phase 38F) and the two questions a merchant's identity hangs
/// off: will she take this at all, and is it her trade?
///
/// <b>Tags are not ids.</b> They carry no domain prefix and have no <c>docs/IDS.md</c> row — they are a
/// controlled vocabulary rather than a registry, which is why the list lives here in code where
/// <c>ContentValidator</c> can hold authored data to it. Adding one is a line here plus a
/// <c>trade.tag.&lt;tag&gt;</c> locale key.
///
/// Pure and Godot-free for the reason <see cref="ShopPricing"/> is: the test project cannot construct
/// Godot objects, so every parameter is a plain collection and the resource accessors do the converting.
///
/// <b>The vocabulary deliberately holds only tags with members today.</b> A tag nothing carries is a
/// promise rather than a feature — each arrives with the sub-phase that authors something wearing it.
/// 38L kept that bargain for five of the eight the arc had named: <c>food</c>, <c>fish</c>,
/// <c>textile</c>, <c>tome</c> and <c>fuel</c> landed with the Embermarket roster and its catalogue.
/// <c>contraband</c> arrives here in 38O with five goods wearing it the same day, and <c>map</c> and
/// <c>livestock</c> still wait for anything at all to wear them.
/// </summary>
public static class TradeTags
{
    // Raw materials and the trades that work them.
    public const string Metal = "metal";
    public const string Ore = "ore";
    public const string Leather = "leather";
    public const string Pelt = "pelt";

    // The apothecary's half of the world.
    public const string Herb = "herb";
    public const string Reagent = "reagent";
    public const string Potion = "potion";

    // Worked goods and valuables.
    public const string Gem = "gem";
    public const string Jewelry = "jewelry";
    public const string Weapon = "weapon";
    public const string Armor = "armor";
    public const string Arcane = "arcane";
    public const string Tool = "tool";
    public const string Furnishing = "furnishing";

    // What comes back from the wilds, and what should probably not be for sale at all.
    public const string Trophy = "trophy";
    public const string Relic = "relic";
    public const string Luxury = "luxury";

    // What a market sells that an adventurer's shop does not (38L). These five exist because the
    // Embermarket roster authored goods wearing them in the same sub-phase — a specialist whose whole
    // trade is one tag is only a specialist if that tag has a shelf behind it.
    public const string Food = "food";
    public const string Fish = "fish";
    public const string Textile = "textile";
    public const string Tome = "tome";
    public const string Fuel = "fuel";

    /// <summary>
    /// Goods no honest merchant will touch (Phase 38O) — smuggled, stolen, untaxed or forbidden.
    ///
    /// ⚠️ <b>This tag is the one exception to "both empties mean yes", and it is the only tag that
    /// changes what <see cref="Accepts"/> means.</b> Every other tag is a filter a shop may opt out
    /// of; this one is a door a shop must opt <em>in</em> to. See <see cref="Accepts"/>.
    /// </summary>
    public const string Contraband = "contraband";

    private static readonly HashSet<string> _all = new()
    {
        Metal, Ore, Leather, Pelt,
        Herb, Reagent, Potion,
        Gem, Jewelry, Weapon, Armor, Arcane, Tool, Furnishing,
        Trophy, Relic, Luxury,
        Food, Fish, Textile, Tome, Fuel,
        Contraband,
    };

    /// <summary>Every tag an item or a shop may author. The validator's whole authority.</summary>
    public static IReadOnlyCollection<string> All => _all;

    /// <summary>Whether a tag is in the vocabulary. A typo is a silently unsellable item, which is why
    /// <c>--validate</c> rejects one rather than shrugging.</summary>
    public static bool IsKnown(string tag) => !string.IsNullOrEmpty(tag) && _all.Contains(tag);

    /// <summary>
    /// Whether a merchant will take this at all.
    ///
    /// <b>Both empties mean yes, and that is the load-bearing decision.</b> An empty accepted list is a
    /// general store — a merchant who deals in everything is authored by saying nothing, which is also
    /// what every shop authored before 38F says. An untagged item is accepted everywhere, so a new item
    /// is never silently unsellable while its tags are still being decided.
    ///
    /// ⚠️ Both fail <em>open</em>, matching the inverted fail-safe the shops already use for a missing
    /// <c>ReputationComponent</c>: a half-built world trades normally. Failing closed would turn an
    /// authoring gap into a world where nothing can be sold, which reads as the feature being broken.
    ///
    /// <b>⚠️ <see cref="Contraband"/> inverts exactly that, and it is the only tag that does (38O).</b>
    /// Failing open is right for a filter and wrong for a prohibition: an empty accepted list is how a
    /// general store is authored, so under the rule above Aldreth would fence smuggled goods across the
    /// counter of the town's most respectable shop. So contraband <b>dominates</b> — an item wearing it
    /// is refused by every shop that does not name <see cref="Contraband"/> in its own accepted list,
    /// whatever else either list says. A stolen signet tagged <c>contraband</c> <em>and</em>
    /// <c>jewelry</c> is still refused by the jeweller, and that is the point: the fence is the only
    /// door, which is what makes finding one worth the walk.
    ///
    /// The exception is narrow on purpose. It is one branch here rather than a flag on
    /// <c>ShopResource</c> or a second refusal in the panel, so the vendor window, the sale itself and
    /// <c>EconomyReport</c>'s arbitrage table all inherit it from the one function they already share.
    /// </summary>
    public static bool Accepts(
        IReadOnlyCollection<string> itemTags, IReadOnlyCollection<string> acceptedTags)
    {
        if (Contains(itemTags, Contraband))
        {
            return Contains(acceptedTags, Contraband);
        }

        if (acceptedTags.Count == 0 || itemTags.Count == 0)
        {
            return true;
        }

        return Overlaps(itemTags, acceptedTags);
    }

    /// <summary>Whether this is contraband at all — the question the refusal text branches on, so the
    /// panel does not have to know the tag's name (38O).</summary>
    public static bool IsContraband(IReadOnlyCollection<string> itemTags) =>
        Contains(itemTags, Contraband);

    /// <summary>Whether a merchant deals in contraband — a fence. Authored, never inferred.</summary>
    public static bool IsFence(IReadOnlyCollection<string> acceptedTags) =>
        Contains(acceptedTags, Contraband);

    /// <summary>Whether this is the merchant's own trade — any overlap at all. Drives the premium she
    /// pays and the keener price she asks (<see cref="ShopPricing.SellFractionFor"/> /
    /// <see cref="ShopPricing.MarkupFor"/>).</summary>
    public static bool IsSpecialty(
        IReadOnlyCollection<string> itemTags, IReadOnlyCollection<string> specialties) =>
        specialties.Count > 0 && itemTags.Count > 0 && Overlaps(itemTags, specialties);

    private static bool Contains(IReadOnlyCollection<string> tags, string wanted)
    {
        foreach (string tag in tags)
        {
            if (tag == wanted)
            {
                return true;
            }
        }

        return false;
    }

    private static bool Overlaps(IReadOnlyCollection<string> left, IReadOnlyCollection<string> right)
    {
        foreach (string tag in left)
        {
            foreach (string candidate in right)
            {
                if (tag == candidate)
                {
                    return true;
                }
            }
        }

        return false;
    }
}
