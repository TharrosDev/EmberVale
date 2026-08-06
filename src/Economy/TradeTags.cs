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
/// promise rather than a feature — <c>contraband</c>, <c>food</c>, <c>fish</c>, <c>textile</c>,
/// <c>tome</c>, <c>map</c>, <c>livestock</c> and <c>fuel</c> are all named in the economy arc and each
/// arrives with the sub-phase that authors something wearing it.
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

    private static readonly HashSet<string> _all = new()
    {
        Metal, Ore, Leather, Pelt,
        Herb, Reagent, Potion,
        Gem, Jewelry, Weapon, Armor, Arcane, Tool, Furnishing,
        Trophy, Relic, Luxury,
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
    /// </summary>
    public static bool Accepts(
        IReadOnlyCollection<string> itemTags, IReadOnlyCollection<string> acceptedTags)
    {
        if (acceptedTags.Count == 0 || itemTags.Count == 0)
        {
            return true;
        }

        return Overlaps(itemTags, acceptedTags);
    }

    /// <summary>Whether this is the merchant's own trade — any overlap at all. Drives the premium she
    /// pays and the keener price she asks (<see cref="ShopPricing.SellFractionFor"/> /
    /// <see cref="ShopPricing.MarkupFor"/>).</summary>
    public static bool IsSpecialty(
        IReadOnlyCollection<string> itemTags, IReadOnlyCollection<string> specialties) =>
        specialties.Count > 0 && itemTags.Count > 0 && Overlaps(itemTags, specialties);

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
