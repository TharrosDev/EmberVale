using System;
using System.Collections.Generic;
using System.Linq;
using Embervale.Items;
using Embervale.Stats;

namespace Embervale.UI;

/// <summary>
/// The pure half of the item vocabulary (Phase 37.5C): what glyph a category takes, how a backpack
/// orders, and how a candidate item compares to what is already worn.
///
/// Every entry point here takes **plain values rather than <see cref="ItemInstance"/>**, and that
/// shape is deliberate: an <c>ItemInstance</c> wraps a Godot <c>Resource</c>, the test project
/// forbids constructing Godot objects, and logic that can only be exercised by running the game is
/// logic nothing checks. The comparison maths is where that matters most — a sign error there
/// silently tells the player a downgrade is an upgrade, and it would look completely reasonable on
/// screen. The <c>ItemInstance</c> overloads at the bottom are thin adapters.
/// </summary>
public static class ItemPresentation
{
    /// <summary>
    /// The category glyph shown in an item slot.
    ///
    /// **Why glyphs and not icons.** <c>ItemResource.Icon</c> has existed since Phase 5 and, as of
    /// 37.5C, **not one of the 26 authored items sets it and nothing in the codebase read it** — it
    /// was dead scaffolding. A literal icon grid would therefore have been 26 empty boxes, which is
    /// strictly worse than the text list it replaced. A glyph grid carries real information today:
    /// silhouette says category, colour says rarity, frame thickness says tier.
    ///
    /// These are deliberately the most widely-covered shapes in Unicode (Geometric Shapes plus the
    /// black star) rather than prettier pictographs — a missing glyph renders as a .notdef box, and
    /// an inventory full of tofu is a worse failure than a plain triangle. <c>ItemSlot</c> prefers a
    /// real <c>Icon</c> whenever one is finally authored, so this is a floor, not a ceiling.
    /// </summary>
    public static string Glyph(ItemType type) => type switch
    {
        ItemType.Consumable => "●",
        ItemType.Weapon => "▲",
        ItemType.Armor => "■",
        ItemType.Material => "▬",
        ItemType.Quest => "★",
        _ => "◆",
    };

    /// <summary>The sort orders the backpack offers.</summary>
    public enum SortOrder
    {
        Name,
        Rarity,
        Weight,
        Value,
    }

    /// <summary>The four facts a sort needs. Exists so <see cref="Sort{T}"/> can be exercised
    /// without an <see cref="ItemInstance"/>.</summary>
    public readonly record struct SortKey(string Name, int Rarity, float Weight, int Value);

    /// <summary>
    /// Orders a backpack for display. Rarity and value descend (the interesting end first); name
    /// and weight ascend.
    ///
    /// Every comparison falls through to the name, which makes the order **total**. That is not
    /// tidiness: this panel rebuilds on every inventory change, and under a partial order two items
    /// of equal rarity may swap places on each rebuild — so the grid would reshuffle under the
    /// player's cursor every time they picked up a coin.
    /// </summary>
    public static IEnumerable<T> Sort<T>(IEnumerable<T> items, SortOrder order, Func<T, SortKey> key)
    {
        return order switch
        {
            SortOrder.Rarity => items.OrderByDescending(i => key(i).Rarity).ThenBy(i => key(i).Name, StringComparer.Ordinal),
            SortOrder.Weight => items.OrderBy(i => key(i).Weight).ThenBy(i => key(i).Name, StringComparer.Ordinal),
            SortOrder.Value => items.OrderByDescending(i => key(i).Value).ThenBy(i => key(i).Name, StringComparer.Ordinal),
            _ => items.OrderBy(i => key(i).Name, StringComparer.Ordinal),
        };
    }

    /// <summary>
    /// The stat difference between a candidate and whatever occupies its slot, as the player would
    /// read it: **positive means equipping is an improvement**.
    ///
    /// Both sides are summed by stat first. An item can carry the same stat from its template bonus
    /// *and* from a rolled affix — a sword with +2 Power and a "+3 Power" prefix — and comparing
    /// entry by entry would report two separate deltas for one stat and get the sign of the pair
    /// wrong whenever they disagreed.
    ///
    /// A stat present on only one side still appears, with the missing side counted as zero. That
    /// is the case the player most needs to see: it is what an empty slot looks like, and it is
    /// what losing a stat entirely looks like. Stats where the two sides agree exactly are dropped,
    /// because a delta of zero is noise.
    ///
    /// ⚠️ <c>ModifierType</c> is deliberately ignored. Every equippable in the game carries flat
    /// bonuses, affixes are flat, and summing a flat +5 with a percentage +5% would be arithmetic
    /// nonsense presented as a fact. If percentage gear is ever authored, this must split the two
    /// and show them as separate rows rather than quietly adding them.
    /// </summary>
    public static IReadOnlyList<(StatType Stat, float Delta)> Compare(
        IEnumerable<(StatType Stat, float Value, ModifierType Type)> candidate,
        IEnumerable<(StatType Stat, float Value, ModifierType Type)>? equipped)
    {
        Dictionary<StatType, float> mine = Totals(candidate);
        Dictionary<StatType, float> theirs = Totals(equipped ?? Enumerable.Empty<(StatType, float, ModifierType)>());

        var result = new List<(StatType, float)>();
        foreach (StatType stat in mine.Keys.Union(theirs.Keys))
        {
            float delta = mine.GetValueOrDefault(stat) - theirs.GetValueOrDefault(stat);
            if (delta != 0f)
            {
                result.Add((stat, delta));
            }
        }

        result.Sort((a, b) => a.Item1.CompareTo(b.Item1));
        return result;
    }

    private static Dictionary<StatType, float> Totals(
        IEnumerable<(StatType Stat, float Value, ModifierType Type)> bonuses)
    {
        var totals = new Dictionary<StatType, float>();
        foreach ((StatType stat, float value, ModifierType _) in bonuses)
        {
            totals[stat] = totals.GetValueOrDefault(stat) + value;
        }

        return totals;
    }

    // --- ItemInstance adapters ------------------------------------------------

    public static SortKey KeyOf(ItemInstance instance) =>
        new(instance.DisplayName, (int)instance.Rarity, instance.Weight, instance.Value);

    public static IReadOnlyList<(StatType Stat, float Delta)> Compare(ItemInstance candidate, ItemInstance? equipped) =>
        Compare(candidate.StatBonuses(), equipped?.StatBonuses());
}
