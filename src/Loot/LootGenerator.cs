using System;
using System.Collections.Generic;
using Embervale.Core;
using Embervale.Core.Diagnostics;
using Embervale.Items;
using Godot;

namespace Embervale.Loot;

/// <summary>One generated drop: an instance and how many of it.</summary>
public readonly record struct LootDrop(ItemInstance Instance, int Quantity);

/// <summary>
/// Where a roll comes from: the realm tier it happens in, who is looting, and how lucky they are.
/// Every member defaults to "unknown", and an empty context is exactly the level-less roll the
/// generator made before it existed.
/// </summary>
public readonly record struct LootContext
{
    /// <summary>Realm tier 1..6 the roll happens in; 0 = none (a table may still pin its own).</summary>
    public int Tier { get; init; }

    /// <summary>Forces every rolled piece to this item level; 0 lets the tier's band decide.</summary>
    public int ItemLevel { get; init; }

    /// <summary>The looting character's level; 0 = unknown. Drops land near it, inside the band.</summary>
    public int FinderLevel { get; init; }

    /// <summary>The source's own quality (an elite, a shop's level curve), added to the table's.</summary>
    public float ExtraQuality { get; init; }

    /// <summary>The looter's luck (perks), added to the quality as well.</summary>
    public float Luck { get; init; }

    /// <summary>Rarity floor for every piece rolled.</summary>
    public ItemRarity MinRarity { get; init; }

    /// <summary>The looter's saved history: drives bad-luck protection and once-per-save drops.
    /// Null rolls without either.</summary>
    public LootLedger? Ledger { get; init; }
}

/// <summary>
/// Turns a <see cref="LootTable"/> into concrete <see cref="LootDrop"/>s. For each
/// entry it rolls the drop chance and quantity; equippable entries flagged for
/// affixes get a rolled rarity (<see cref="LootRarity"/>) and a set of affixes drawn
/// from the <see cref="AffixDatabase"/>, with values scaled by rarity, the
/// table's quality bonus and the item level the piece is generated at. Gold is appended as a
/// final mundane drop.
///
/// A roll made with a <see cref="LootContext"/> is level-aware: the tier decides the level band
/// (<see cref="LootTiers"/>), nested rows reach the tier's own pools, and the looter's
/// <see cref="LootLedger"/> supplies bad-luck protection (<see cref="PityRules"/>).
///
/// Pure data-in/data-out: spawning the drops into the world is the caller's job
/// (see <see cref="LootComponent"/>).
/// </summary>
public static class LootGenerator
{
    /// <summary>The rarity from which an item's first affix is drawn from the high-rarity pool.</summary>
    public const ItemRarity SignatureRarity = ItemRarity.Epic;

    /// <summary>How deep a table may nest tables. Two is all the authored content uses; the cap is
    /// what makes a table that names itself a warning instead of a stack overflow.</summary>
    private const int MaxNesting = 4;

    private static readonly RandomNumberGenerator SharedRng = CreateRng();

    private static RandomNumberGenerator CreateRng()
    {
        var rng = new RandomNumberGenerator();
        rng.Randomize();
        return rng;
    }

    /// <summary>Rolls a table on the shared generator; see the overload taking one.</summary>
    public static List<LootDrop> Generate(LootTable? table, float extraQuality = 0f, int itemLevel = 0, float luck = 0f)
    {
        return Generate(table, SharedRng, extraQuality, itemLevel, luck);
    }

    /// <summary>
    /// Rolls a table into drops.
    /// </summary>
    /// <param name="table">The table to roll; null yields no drops.</param>
    /// <param name="rng">The generator to roll on (a seeded one makes the result reproducible).</param>
    /// <param name="extraQuality">Added to the table's own <c>QualityBonus</c>; shifts the rarity
    /// roll and nudges affix values.</param>
    /// <param name="itemLevel">The level rolled gear is generated at: it is stamped on each rolled
    /// <see cref="ItemInstance.ItemLevel"/> and gates the affix pool
    /// (<see cref="AffixDefinition.MinItemLevel"/>). 0, the default, is the level-less roll every
    /// pre-ics caller made: nothing stamped, ungated affixes only.</param>
    /// <param name="luck">The finder's luck (perks, a unique effect). It is simply added to the
    /// quality here; it is a separate argument so a caller never has to fold a character's luck into
    /// a place's quality itself.</param>
    public static List<LootDrop> Generate(LootTable? table, RandomNumberGenerator rng, float extraQuality = 0f,
        int itemLevel = 0, float luck = 0f)
    {
        return Generate(table, rng, new LootContext { ExtraQuality = extraQuality, ItemLevel = itemLevel, Luck = luck });
    }

    /// <summary>Rolls a table for a described source on the shared generator.</summary>
    public static List<LootDrop> Generate(LootTable? table, in LootContext context)
    {
        return Generate(table, SharedRng, context);
    }

    /// <summary>
    /// Rolls a table for a described source: the realm tier it happens in, who is looting and how
    /// lucky they are. This is the level-aware entry point; the overloads above are the same roll
    /// with an otherwise empty context.
    /// </summary>
    public static List<LootDrop> Generate(LootTable? table, RandomNumberGenerator rng, in LootContext context)
    {
        var drops = new List<LootDrop>();
        if (table == null)
        {
            return drops;
        }

        var state = new RollState(
            Tier: context.Tier,
            Quality: context.ExtraQuality + context.Luck,
            ForcedLevel: Math.Max(0, context.ItemLevel),
            FinderLevel: Math.Max(0, context.FinderLevel),
            LevelFloor: 0,
            LevelCeiling: 0,
            Floor: context.MinRarity,
            Ledger: context.Ledger);
        RollTable(table, rng, state, drops, depth: 0);
        return drops;
    }

    /// <summary>
    /// Rolls a specific equippable at a forced rarity (crafting, guaranteed rewards, chest
    /// legendaries). Affix values still vary within their ranges.
    /// </summary>
    /// <param name="template">The equippable to roll a copy of.</param>
    /// <param name="rarity">The forced rarity; it decides the affix count.</param>
    /// <param name="valueQuality">0..1 bias of each affix value toward its maximum.</param>
    /// <param name="itemLevel">The level the copy is generated at: stamped on
    /// <see cref="ItemInstance.ItemLevel"/>, gating the affix pool and scaling affix values. 0, the
    /// default, is a level-less roll (nothing stamped, ungated affixes only, unscaled values).</param>
    /// <param name="quality">The workmanship stamped on <see cref="ItemInstance.Quality"/>. Only
    /// the crafting bench passes anything but <see cref="CraftQuality.Standard"/>.</param>
    /// <param name="luck">Added to <paramref name="valueQuality"/> (the sum is clamped to 0..1 by
    /// the value blend).</param>
    public static ItemInstance RollAffixed(EquippableItemResource template, ItemRarity rarity, float valueQuality = 0.5f,
        int itemLevel = 0, CraftQuality quality = CraftQuality.Standard, float luck = 0f)
    {
        return Stamp(Roll(template, rarity, SharedRng, valueQuality + luck, itemLevel), itemLevel, quality);
    }

    /// <summary>Everything one table roll carries down into the tables it nests.</summary>
    private readonly record struct RollState(
        int Tier, float Quality, int ForcedLevel, int FinderLevel, int LevelFloor, int LevelCeiling,
        ItemRarity Floor, LootLedger? Ledger)
    {
        /// <summary>The level entry gates are read against (0 on a level-less roll).</summary>
        public int ReferenceLevel => ForcedLevel > 0 ? ForcedLevel : LootTiers.ReferenceLevel(Tier, FinderLevel);
    }

    private static void RollTable(LootTable table, RandomNumberGenerator rng, RollState outer, List<LootDrop> drops,
        int depth)
    {
        RollState state = outer with
        {
            // A table that pins a tier (a boss) wins over the realm it is rolled in.
            Tier = LootTiers.IsTier(table.Tier) ? table.Tier : outer.Tier,
            Quality = outer.Quality + table.QualityBonus,
            LevelFloor = table.MinItemLevel > 0 ? table.MinItemLevel : outer.LevelFloor,
            LevelCeiling = table.MaxItemLevel > 0 ? table.MaxItemLevel : outer.LevelCeiling,
        };

        HashSet<string>? rolledGroups = null;
        foreach (Variant element in table.Entries)
        {
            LootEntry? entry = element.As<LootEntry>();
            if (entry == null)
            {
                continue;
            }

            if (entry.Group.Length > 0)
            {
                // The whole group is one roll, made where its first row sits.
                rolledGroups ??= new HashSet<string>();
                if (!rolledGroups.Add(entry.Group))
                {
                    continue;
                }

                entry = PickFromGroup(table, entry.Group, rng, state);
                if (entry == null)
                {
                    continue;
                }
            }
            else if (!IsEligible(entry, state))
            {
                continue;
            }

            RollEntry(entry, rng, state, drops, depth);
        }

        AppendGold(table, rng, drops);
    }

    /// <summary>A row can drop at all: it names something, the roll's level is inside its gate, and
    /// a once-per-save row has not been claimed.</summary>
    private static bool IsEligible(LootEntry entry, RollState state)
    {
        if (!entry.IsNested && string.IsNullOrEmpty(entry.ItemId))
        {
            return false;
        }

        if (!entry.AllowsLevel(state.ReferenceLevel))
        {
            return false;
        }

        return !entry.OncePerSave || state.Ledger == null || !state.Ledger.HasClaimed(OnceKey(entry));
    }

    private static string OnceKey(LootEntry entry) => entry.IsNested ? entry.TablePath : entry.ItemId;

    /// <summary>Picks one eligible member of a group by weight (uniformly when no member has any).</summary>
    private static LootEntry? PickFromGroup(LootTable table, string group, RandomNumberGenerator rng, RollState state)
    {
        var members = new List<LootEntry>();
        float total = 0f;
        foreach (Variant element in table.Entries)
        {
            // A member naming an item that does not exist is not a pick: choosing it would turn a
            // guaranteed group into an empty drop.
            if (element.As<LootEntry>() is { } member && member.Group == group && IsEligible(member, state)
                && (member.IsNested || ItemDatabase.Get(member.ItemId) != null))
            {
                members.Add(member);
                total += Mathf.Max(0f, member.Weight);
            }
        }

        if (members.Count == 0)
        {
            return null;
        }

        if (total <= 0f)
        {
            return members[rng.RandiRange(0, members.Count - 1)];
        }

        float pick = rng.Randf() * total;
        foreach (LootEntry member in members)
        {
            pick -= Mathf.Max(0f, member.Weight);
            if (pick <= 0f)
            {
                return member;
            }
        }

        return members[^1];
    }

    private static void RollEntry(LootEntry entry, RandomNumberGenerator rng, RollState state, List<LootDrop> drops,
        int depth)
    {
        if (rng.Randf() > entry.DropChance)
        {
            return;
        }

        int quantity = entry.MinQuantity >= entry.MaxQuantity
            ? entry.MinQuantity
            : rng.RandiRange(entry.MinQuantity, entry.MaxQuantity);
        if (quantity <= 0)
        {
            return;
        }

        ItemRarity floor = entry.MinRarity > state.Floor ? entry.MinRarity : state.Floor;

        if (entry.IsNested)
        {
            string path = LootTiers.ResolvePath(entry.TablePath, state.Tier);
            if (depth >= MaxNesting || ResidentResources.Load<LootTable>(path) is not { } nested)
            {
                Log.Warn($"A loot table row could not roll nested table '{path}' (depth {depth}); skipped.");
                return;
            }

            RollState inner = state with { Floor = floor };
            for (int i = 0; i < quantity; i++)
            {
                RollTable(nested, rng, inner, drops, depth + 1);
            }

            Claim(entry, state);
            return;
        }

        ItemResource? template = ItemDatabase.Get(entry.ItemId);
        if (template == null)
        {
            return;
        }

        bool rolled = entry.RollAffixes && template is EquippableItemResource;
        ItemInstance instance = rolled
            ? RollEquippable((EquippableItemResource)template, rng, state, floor)
            : ItemInstance.Plain(template);

        // Rolled gear is unique — emit one drop per unit so each keeps its roll.
        if (instance.IsStackable)
        {
            drops.Add(new LootDrop(instance, quantity));
        }
        else
        {
            drops.Add(new LootDrop(instance, 1));
            for (int i = 1; i < quantity; i++)
            {
                ItemInstance extra = rolled
                    ? RollEquippable((EquippableItemResource)template, rng, state, floor)
                    : ItemInstance.Plain(template);
                drops.Add(new LootDrop(extra, 1));
            }
        }

        Claim(entry, state);
    }

    private static void Claim(LootEntry entry, RollState state)
    {
        if (entry.OncePerSave)
        {
            state.Ledger?.Claim(OnceKey(entry));
        }
    }

    private static ItemInstance RollEquippable(EquippableItemResource template, RandomNumberGenerator rng,
        RollState state, ItemRarity floor)
    {
        // Bad-luck protection: a dry streak adds quality to this roll and, once it has run long
        // enough, forbids anything below Rare. Only a roll made for a looter with a ledger counts.
        int dry = state.Ledger?.DryStreak ?? 0;
        ItemRarity rarity = PityRules.Apply(LootRarity.Roll(rng, state.Quality + PityRules.BonusQuality(dry)), dry);
        state.Ledger?.RecordRoll(rarity);

        // Floors come after the streak is recorded, so a guaranteed boss legendary does not reset
        // the luck of ordinary drops. A template's own rarity is a floor too: an item authored
        // Epic never drops as a Common copy of itself.
        if (floor > rarity)
        {
            rarity = floor;
        }

        if (template.Rarity > rarity)
        {
            rarity = template.Rarity;
        }

        int level = ItemLevelFor(template, rng, state);
        return Stamp(Roll(template, rarity, rng, RarityQuality(rarity, state.Quality), level), level, CraftQuality.Standard);
    }

    /// <summary>The level one rolled piece is generated at: the forced level, or a roll inside the
    /// tier's band, held within the table's own limits and the template's tier. 0 on a level-less
    /// roll (no tier, nothing forced), which draws nothing from the generator.</summary>
    private static int ItemLevelFor(ItemResource template, RandomNumberGenerator rng, RollState state)
    {
        int level = state.ForcedLevel > 0
            ? state.ForcedLevel
            : LootTiers.IsTier(state.Tier) ? LootTiers.RollItemLevel(state.Tier, state.FinderLevel, rng.Randf()) : 0;
        if (level <= 0)
        {
            return 0;
        }

        if (state.LevelFloor > 0)
        {
            level = Math.Max(level, state.LevelFloor);
        }

        if (state.LevelCeiling > 0)
        {
            level = Math.Min(level, state.LevelCeiling);
        }

        return LootTiers.ClampToItemTier(level, template.Tier);
    }

    private static ItemInstance Roll(EquippableItemResource template, ItemRarity rarity, RandomNumberGenerator rng,
        float valueQuality, int itemLevel)
    {
        int count = LootRarity.AffixCount(rarity);
        if (count <= 0)
        {
            return new ItemInstance(template, rarity);
        }

        List<AffixDefinition> pool = AffixDatabase.ApplicableTo(template, rarity, itemLevel);
        List<ItemAffix> affixes = RollAffixes(pool, count, rng, valueQuality, itemLevel, rarity);
        return new ItemInstance(template, rarity, affixes);
    }

    private static ItemInstance Stamp(ItemInstance instance, int itemLevel, CraftQuality quality)
    {
        instance.ItemLevel = Math.Max(0, itemLevel);
        instance.Quality = quality;
        return instance;
    }

    /// <summary>Picks up to <paramref name="count"/> distinct affixes (no repeated stat or effect,
    /// no two from one non-empty <see cref="AffixDefinition.Group"/>) from the pool by weight, then
    /// rolls each one's value at <paramref name="itemLevel"/>.
    ///
    /// An Epic or Legendary item draws its first affix from the affixes that only exist at
    /// <see cref="SignatureRarity"/> and above, when the pool has any. Without that, four picks from
    /// a pool dominated by common affixes made every Legendary the same handful of stats.</summary>
    private static List<ItemAffix> RollAffixes(List<AffixDefinition> pool, int count,
        RandomNumberGenerator rng, float valueQuality, int itemLevel, ItemRarity rarity)
    {
        var rolled = new List<ItemAffix>();
        if (pool.Count == 0)
        {
            return rolled;
        }

        var candidates = new List<AffixDefinition>(pool);
        var usedStats = new HashSet<(AffixEffect, Stats.StatType)>();
        var usedGroups = new HashSet<string>();

        if (rarity >= SignatureRarity)
        {
            List<AffixDefinition> signature = candidates.FindAll(def => def.MinRarity >= SignatureRarity);
            if (signature.Count > 0 && WeightedPick(signature, rng) is { } first)
            {
                candidates.Remove(first);
                usedStats.Add((first.Effect, first.Stat));
                if (!string.IsNullOrEmpty(first.Group))
                {
                    usedGroups.Add(first.Group);
                }

                rolled.Add(first.Roll(rng, valueQuality, itemLevel));
            }
        }

        while (rolled.Count < count && candidates.Count > 0)
        {
            AffixDefinition? pick = WeightedPick(candidates, rng);
            if (pick == null)
            {
                break;
            }

            candidates.Remove(pick);
            if (!string.IsNullOrEmpty(pick.Group) && usedGroups.Contains(pick.Group))
            {
                continue;
            }

            if (!usedStats.Add((pick.Effect, pick.Stat)))
            {
                continue;
            }

            if (!string.IsNullOrEmpty(pick.Group))
            {
                usedGroups.Add(pick.Group);
            }

            rolled.Add(pick.Roll(rng, valueQuality, itemLevel));
        }

        return rolled;
    }

    private static AffixDefinition? WeightedPick(List<AffixDefinition> candidates, RandomNumberGenerator rng)
    {
        float total = 0f;
        foreach (AffixDefinition def in candidates)
        {
            total += Mathf.Max(0f, def.Weight);
        }

        if (total <= 0f)
        {
            return candidates.Count > 0 ? candidates[rng.RandiRange(0, candidates.Count - 1)] : null;
        }

        float pick = rng.Randf() * total;
        foreach (AffixDefinition def in candidates)
        {
            pick -= Mathf.Max(0f, def.Weight);
            if (pick <= 0f)
            {
                return def;
            }
        }

        return candidates[^1];
    }

    /// <summary>Higher rarity rolls bias affix values upward.</summary>
    private static float RarityQuality(ItemRarity rarity, float tableQuality)
    {
        float rarityFraction = (int)rarity / (float)(int)ItemRarity.Legendary;
        return Mathf.Clamp(rarityFraction + (tableQuality * 0.25f), 0f, 1f);
    }

    private static void AppendGold(LootTable table, RandomNumberGenerator rng, List<LootDrop> drops)
    {
        if (table.GoldMax <= 0 || rng.Randf() > table.GoldChance)
        {
            return;
        }

        ItemResource? gold = ItemDatabase.Get(table.GoldItemId);
        if (gold == null)
        {
            return;
        }

        int amount = table.GoldMin >= table.GoldMax
            ? table.GoldMax
            : rng.RandiRange(table.GoldMin, table.GoldMax);
        if (amount > 0)
        {
            drops.Add(new LootDrop(ItemInstance.Plain(gold), amount));
        }
    }
}
