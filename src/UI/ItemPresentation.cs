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

    /// <summary>
    /// Spends up to <paramref name="quantity"/> of the credit held under <paramref name="key"/> and
    /// returns how many units were NOT covered by it. A vendor's buyback uses it to tell goods that
    /// are new to the counter from goods it has already been sold once.
    /// </summary>
    public static int SpendCredit<TKey>(Dictionary<TKey, int> credits, TKey key, int quantity)
        where TKey : notnull
    {
        if (quantity <= 0)
        {
            return 0;
        }

        int held = credits.GetValueOrDefault(key);
        int spent = Math.Min(held, quantity);
        if (spent >= held)
        {
            credits.Remove(key);
        }
        else
        {
            credits[key] = held - spent;
        }

        return quantity - spent;
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

    // --- The slot's marks and the detail card's anatomy (2026-10 UI upgrade) ----

    /// <summary>
    /// The **non-colour** rarity channel on a slot: how many ticks sit in its corner. None for Common,
    /// one more per tier above it, so a rarity reads by counting when the frame colours cannot be told
    /// apart. <see cref="UiTheme.RarityBorderWidth"/> is the other half (the frame thickens at Epic).
    /// </summary>
    public static int RarityTicks(ItemRarity rarity) => Math.Clamp((int)rarity, 0, 4);

    /// <summary>
    /// Whether a name may be set in the carved display face. UI_STYLE keeps Cinzel to labels of three
    /// words or fewer: it has no lower case, and "Reinforced Steel Greatsword of the Bear" in capitals
    /// is a wall. A longer name takes the interface face at the same size.
    /// </summary>
    public static bool UsesDisplayFace(string? name) =>
        !string.IsNullOrWhiteSpace(name) &&
        name.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length <= 3;

    /// <summary>What an item's one big number measures.</summary>
    public enum HeroKind
    {
        None,
        Damage,
        Armor,
        Health,
        Stamina,
        Mana,
    }

    /// <summary>The one number a detail card leads with, or <see cref="HeroKind.None"/> when the item has
    /// no single number worth leading with (a ring, a buff potion, a pelt).</summary>
    public readonly record struct HeroNumber(HeroKind Kind, float Value)
    {
        public static readonly HeroNumber None = new(HeroKind.None, 0f);
    }

    /// <summary>
    /// Picks the hero number from plain facts: a weapon leads with its damage, other gear with the
    /// armour it grants, a restoring consumable with the amount it restores. Everything else has none:
    /// a ring's bonuses are not comparable to each other, so promoting one of them would be a guess.
    /// </summary>
    public static HeroNumber Hero(float? weaponDamage, float armor, ConsumableEffectKind? effect, float amount)
    {
        if (weaponDamage is { } damage && damage > 0f)
        {
            return new HeroNumber(HeroKind.Damage, damage);
        }

        if (armor > 0f)
        {
            return new HeroNumber(HeroKind.Armor, armor);
        }

        if (amount <= 0f)
        {
            return HeroNumber.None;
        }

        return effect switch
        {
            ConsumableEffectKind.Heal => new HeroNumber(HeroKind.Health, amount),
            ConsumableEffectKind.RestoreStamina => new HeroNumber(HeroKind.Stamina, amount),
            ConsumableEffectKind.RestoreMana => new HeroNumber(HeroKind.Mana, amount),
            _ => HeroNumber.None,
        };
    }

    /// <summary>
    /// The hero number's change against what is worn: positive is an improvement. Null when the two are
    /// not the same measure (a shield against an off-hand dagger), because a damage number minus an
    /// armour number is not a fact. An empty slot counts as zero of the candidate's own measure.
    /// </summary>
    public static float? HeroDelta(HeroNumber candidate, HeroNumber? worn)
    {
        if (candidate.Kind == HeroKind.None)
        {
            return null;
        }

        if (worn is not { } rival)
        {
            return candidate.Value;
        }

        return rival.Kind == candidate.Kind ? candidate.Value - rival.Value : null;
    }

    /// <summary>One stat line of a detail card: what the item grants and, when it is being compared, how
    /// that differs from what is worn. <paramref name="Worn"/> is the worn item's own total.</summary>
    public readonly record struct StatRow(StatType Stat, float Value, float Worn, float Delta);

    /// <summary>
    /// The card's stat rows, in stat order. Not comparing: one row per stat the item grants. Comparing
    /// (<paramref name="worn"/> may still be null, which is an empty slot): the union of both sides, so
    /// a stat the swap would lose shows as a row of its own with a negative delta.
    /// </summary>
    public static IReadOnlyList<StatRow> StatRows(
        IEnumerable<(StatType Stat, float Value, ModifierType Type)> candidate,
        IEnumerable<(StatType Stat, float Value, ModifierType Type)>? worn,
        bool comparing)
    {
        Dictionary<StatType, float> mine = Totals(candidate);
        Dictionary<StatType, float> theirs = comparing && worn != null
            ? Totals(worn)
            : new Dictionary<StatType, float>();

        var rows = new List<StatRow>();
        foreach (StatType stat in mine.Keys.Union(theirs.Keys))
        {
            float value = mine.GetValueOrDefault(stat);
            float other = theirs.GetValueOrDefault(stat);
            if (value != 0f || other != 0f)
            {
                rows.Add(new StatRow(stat, value, other, comparing ? value - other : 0f));
            }
        }

        rows.Sort((a, b) => a.Stat.CompareTo(b.Stat));
        return rows;
    }

    /// <summary>The members of <paramref name="held"/> that were not in <paramref name="known"/>: what
    /// came into the pack since it was last looked at. Order is the order held.</summary>
    public static List<T> NewSince<T>(IEnumerable<T> held, ICollection<T> known)
    {
        var fresh = new List<T>();
        foreach (T item in held)
        {
            if (!known.Contains(item))
            {
                fresh.Add(item);
            }
        }

        return fresh;
    }

    // --- ItemInstance adapters ------------------------------------------------

    public static SortKey KeyOf(ItemInstance instance) =>
        new(instance.DisplayName, (int)instance.Rarity, instance.Weight, instance.Value, (int)instance.Type,
            instance.EffectiveItemLevel);

    public static IReadOnlyList<(StatType Stat, float Delta)> Compare(ItemInstance candidate, ItemInstance? equipped) =>
        Compare(candidate.StatBonuses(), equipped?.StatBonuses());

    /// <summary>The hero number of a real item (<see cref="Hero"/>).</summary>
    public static HeroNumber HeroOf(ItemInstance instance)
    {
        float armor = 0f;
        foreach ((StatType stat, float value, ModifierType _) in instance.StatBonuses())
        {
            if (stat == StatType.Armor)
            {
                armor += value;
            }
        }

        var consumable = instance.Template as ConsumableItemResource;
        float amount = consumable == null ? 0f
            : consumable.Effect == ConsumableEffectKind.Heal ? consumable.EffectiveHeal : consumable.Magnitude;
        return Hero(instance.Equippable?.Weapon?.BaseDamage, armor, consumable?.Effect, amount);
    }

    public static IReadOnlyList<StatRow> StatRows(ItemInstance candidate, ItemInstance? worn, bool comparing) =>
        StatRows(candidate.StatBonuses(), worn?.StatBonuses(), comparing);
}
