using System.Collections.Generic;
using Embervale.Items;
using Godot;

namespace Embervale.Loot;

/// <summary>One generated drop: an instance and how many of it.</summary>
public readonly record struct LootDrop(ItemInstance Instance, int Quantity);

/// <summary>
/// Turns a <see cref="LootTable"/> into concrete <see cref="LootDrop"/>s. For each
/// entry it rolls the drop chance and quantity; equippable entries flagged for
/// affixes get a rolled rarity (<see cref="LootRarity"/>) and a set of affixes drawn
/// from the <see cref="AffixDatabase"/>, with values scaled by rarity and the
/// table's quality bonus. Gold is appended as a final mundane drop.
///
/// Pure data-in/data-out: spawning the drops into the world is the caller's job
/// (see <see cref="LootComponent"/>).
/// </summary>
public static class LootGenerator
{
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
        var drops = new List<LootDrop>();
        if (table == null)
        {
            return drops;
        }

        float quality = table.QualityBonus + extraQuality + luck;

        foreach (Variant element in table.Entries)
        {
            LootEntry? entry = element.As<LootEntry>();
            if (entry == null || string.IsNullOrEmpty(entry.ItemId))
            {
                continue;
            }

            if (rng.Randf() > entry.DropChance)
            {
                continue;
            }

            ItemResource? template = ItemDatabase.Get(entry.ItemId);
            if (template == null)
            {
                continue;
            }

            int quantity = entry.MinQuantity >= entry.MaxQuantity
                ? entry.MinQuantity
                : rng.RandiRange(entry.MinQuantity, entry.MaxQuantity);
            if (quantity <= 0)
            {
                continue;
            }

            ItemInstance instance = entry.RollAffixes && template is EquippableItemResource equippable
                ? RollEquippable(equippable, rng, quality, itemLevel)
                : ItemInstance.Plain(template);

            // Rolled gear is unique — emit one drop per unit so each keeps its roll.
            if (instance.IsStackable)
            {
                drops.Add(new LootDrop(instance, quantity));
            }
            else
            {
                drops.Add(new LootDrop(instance, 1));
                bool rerollEach = entry.RollAffixes && template is EquippableItemResource;
                for (int i = 1; i < quantity; i++)
                {
                    ItemInstance extra = rerollEach
                        ? RollEquippable((EquippableItemResource)template, rng, quality, itemLevel)
                        : ItemInstance.Plain(template);
                    drops.Add(new LootDrop(extra, 1));
                }
            }
        }

        AppendGold(table, rng, drops);
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
    /// <see cref="ItemInstance.ItemLevel"/> and gating the affix pool. 0, the default, is a
    /// level-less roll (nothing stamped, ungated affixes only).</param>
    /// <param name="quality">The workmanship stamped on <see cref="ItemInstance.Quality"/>. Only
    /// the crafting bench passes anything but <see cref="CraftQuality.Standard"/>.</param>
    /// <param name="luck">Added to <paramref name="valueQuality"/> (the sum is clamped to 0..1 by
    /// the value blend).</param>
    public static ItemInstance RollAffixed(EquippableItemResource template, ItemRarity rarity, float valueQuality = 0.5f,
        int itemLevel = 0, CraftQuality quality = CraftQuality.Standard, float luck = 0f)
    {
        return Stamp(Roll(template, rarity, SharedRng, valueQuality + luck, itemLevel), itemLevel, quality);
    }

    private static ItemInstance RollEquippable(EquippableItemResource template, RandomNumberGenerator rng, float quality,
        int itemLevel)
    {
        ItemRarity rarity = LootRarity.Roll(rng, quality);
        return Stamp(Roll(template, rarity, rng, RarityQuality(rarity, quality), itemLevel), itemLevel, CraftQuality.Standard);
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
        List<ItemAffix> affixes = RollAffixes(pool, count, rng, valueQuality);
        return new ItemInstance(template, rarity, affixes);
    }

    private static ItemInstance Stamp(ItemInstance instance, int itemLevel, CraftQuality quality)
    {
        instance.ItemLevel = System.Math.Max(0, itemLevel);
        instance.Quality = quality;
        return instance;
    }

    /// <summary>Picks up to <paramref name="count"/> distinct affixes (no repeated stat, no two
    /// from one non-empty <see cref="AffixDefinition.Group"/>) from the pool by weight, then rolls
    /// each one's value.</summary>
    private static List<ItemAffix> RollAffixes(List<AffixDefinition> pool, int count,
        RandomNumberGenerator rng, float valueQuality)
    {
        var rolled = new List<ItemAffix>();
        if (pool.Count == 0)
        {
            return rolled;
        }

        var candidates = new List<AffixDefinition>(pool);
        var usedStats = new HashSet<Stats.StatType>();
        var usedGroups = new HashSet<string>();

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

            if (!usedStats.Add(pick.Stat))
            {
                continue;
            }

            if (!string.IsNullOrEmpty(pick.Group))
            {
                usedGroups.Add(pick.Group);
            }

            rolled.Add(pick.Roll(rng, valueQuality));
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
