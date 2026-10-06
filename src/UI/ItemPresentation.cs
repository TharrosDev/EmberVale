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

        /// <summary>Category first (the <see cref="ItemType"/> order), best rarity first within it.</summary>
        Type,

        /// <summary>Highest item level first.</summary>
        Level,
    }

    /// <summary>The facts a sort needs. Exists so <see cref="Sort{T}"/> can be exercised
    /// without an <see cref="ItemInstance"/>.</summary>
    public readonly record struct SortKey(string Name, int Rarity, float Weight, int Value, int Type = 0, int Level = 0);

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
            SortOrder.Type => items.OrderBy(i => key(i).Type).ThenByDescending(i => key(i).Rarity).ThenBy(i => key(i).Name, StringComparer.Ordinal),
            SortOrder.Level => items.OrderByDescending(i => key(i).Level).ThenBy(i => key(i).Name, StringComparer.Ordinal),
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

    // --- Search, filters and the facts on the detail card (ics:inv-ui) ---------

    /// <summary>
    /// Whether an item answers a typed search. Every whitespace-separated term has to appear in at
    /// least one of the <paramref name="haystacks"/> (the name, the category, an affix line), in any
    /// case, so "iron sw" finds the Iron Sword and "sword iron" finds it too. An empty search matches
    /// everything: a cleared field must show the pack, not hide it.
    /// </summary>
    public static bool Matches(string? query, params string?[] haystacks)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return true;
        }

        foreach (string term in query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            bool found = false;
            foreach (string? hay in haystacks)
            {
                if (hay != null && hay.Contains(term, StringComparison.OrdinalIgnoreCase))
                {
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Whether a stack passes the category and gear-slot filters; null means "any".
    /// A slot filter hides everything that is not gear, since nothing else has a slot.</summary>
    public static bool PassesFilter(ItemType type, EquipmentSlot slot, ItemType? typeFilter, EquipmentSlot? slotFilter) =>
        (typeFilter is null || type == typeFilter) && (slotFilter is null || slot == slotFilter);

    /// <summary>
    /// The worn slots a candidate should be compared against. One slot for almost everything; for a
    /// ring, every ring slot the game has, because a ring can go on either hand and comparing it
    /// with only one of them hides the swap the player would actually make. Ring slots are found by
    /// name so a second one is picked up the day it is appended to <see cref="EquipmentSlot"/>.
    /// </summary>
    public static IReadOnlyList<EquipmentSlot> RivalSlots(EquipmentSlot slot, IEnumerable<EquipmentSlot>? known = null)
    {
        if (slot == EquipmentSlot.None)
        {
            return Array.Empty<EquipmentSlot>();
        }

        if (!IsRing(slot))
        {
            return new[] { slot };
        }

        return (known ?? Enum.GetValues<EquipmentSlot>()).Where(IsRing).Distinct().OrderBy(s => s).ToList();
    }

    private static bool IsRing(EquipmentSlot slot) =>
        slot.ToString().StartsWith(nameof(EquipmentSlot.Ring), StringComparison.Ordinal);

    /// <summary>Whether a level requirement is met; 0 or less means there is none.</summary>
    public static bool MeetsLevel(int requiredLevel, int playerLevel) => requiredLevel <= 0 || playerLevel >= requiredLevel;

    /// <summary>How many distinct pieces of a set are worn. Distinct, because two copies of one
    /// ring are one piece of the set, not two.</summary>
    public static int WornPieces(IEnumerable<string> pieceIds, IEnumerable<string> equippedIds)
    {
        var worn = new HashSet<string>(equippedIds, StringComparer.Ordinal);
        return pieceIds.Distinct(StringComparer.Ordinal).Count(worn.Contains);
    }

    /// <summary>A signed bonus as the player reads it: "+12", or "+8%" for a fraction.</summary>
    public static string BonusNumber(float value, bool percent)
    {
        string sign = value >= 0f ? "+" : string.Empty;
        return percent ? $"{sign}{value * 100f:0.#}%" : $"{sign}{value:0.#}";
    }

    /// <summary>
    /// The five arguments a unique effect's description takes, in the resource's placeholder order:
    /// magnitude, chance, threshold, duration, cooldown. Fractions print as percentages; the
    /// magnitude does too unless the kind names a flat amount (stamina refunded, thorns damage).
    /// </summary>
    public static string[] UniqueArgs(
        UniqueEffectKind kind, float magnitude, float chance, float threshold, float duration, float cooldown)
    {
        bool flat = kind is UniqueEffectKind.DodgeRefund or UniqueEffectKind.ThornsFlat;
        return new[]
        {
            flat ? $"{magnitude:0.#}" : Percent(magnitude),
            Percent(chance),
            Percent(threshold),
            $"{duration:0.#}",
            $"{cooldown:0.#}",
        };
    }

    private static string Percent(float fraction) => $"{fraction * 100f:0.#}%";

    /// <summary>A duration as a short reading: "8s", "1.5s", "2m", "2m 30s".</summary>
    public static string Seconds(float seconds)
    {
        if (seconds < 60f)
        {
            return $"{Math.Max(0f, seconds):0.#}s";
        }

        int whole = (int)Math.Round(seconds);
        return whole % 60 == 0 ? $"{whole / 60}m" : $"{whole / 60}m {whole % 60}s";
    }

    /// <summary>The icon a consumable effect takes on the hotbar and the detail card. Five
    /// effects, five different silhouettes: on the hotbar the shape is the only thing read at
    /// a glance, and colour alone would not survive a colour-vision mode.</summary>
    public static UiIcon.Kind EffectIcon(ConsumableEffectKind effect) => effect switch
    {
        ConsumableEffectKind.RestoreStamina => UiIcon.Kind.Stamina,
        ConsumableEffectKind.RestoreMana => UiIcon.Kind.Mana,
        ConsumableEffectKind.Buff => UiIcon.Kind.Armor,
        ConsumableEffectKind.Cure => UiIcon.Kind.Sun,
        _ => UiIcon.Kind.Health,
    };

    /// <summary>The quantity a split or a partial sale starts on and is held to: at least one,
    /// and never the whole stack when <paramref name="keepOne"/> (a split has to leave something
    /// behind or it is a move).</summary>
    public static int ClampQuantity(int requested, int stackQuantity, bool keepOne)
    {
        int max = Math.Max(1, keepOne ? stackQuantity - 1 : stackQuantity);
        return Math.Clamp(requested, 1, max);
    }

    /// <summary>Puts <paramref name="entry"/> at the front of a newest-first list and drops
    /// whatever falls off the end past <paramref name="capacity"/> (the buyback shelf).</summary>
    public static void PushRecent<T>(List<T> list, T entry, int capacity)
    {
        list.Insert(0, entry);
        if (list.Count > capacity)
        {
            list.RemoveRange(capacity, list.Count - capacity);
        }
    }

    /// <summary>The share of a stack's price that <paramref name="quantity"/> of its
    /// <paramref name="total"/> units carries, rounded up so buying a stack back in pieces never
    /// costs less than buying it whole.</summary>
    public static int ShareOf(int price, int quantity, int total)
    {
        if (total <= 0 || quantity >= total)
        {
            return price;
        }

        return quantity <= 0 ? 0 : (int)Math.Ceiling(price * (double)quantity / total);
    }

    // --- ItemInstance adapters ------------------------------------------------

    public static SortKey KeyOf(ItemInstance instance) =>
        new(instance.DisplayName, (int)instance.Rarity, instance.Weight, instance.Value, (int)instance.Type,
            instance.EffectiveItemLevel);

    public static IReadOnlyList<(StatType Stat, float Delta)> Compare(ItemInstance candidate, ItemInstance? equipped) =>
        Compare(candidate.StatBonuses(), equipped?.StatBonuses());
}
