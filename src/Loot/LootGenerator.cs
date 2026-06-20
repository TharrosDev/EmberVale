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

    public static List<LootDrop> Generate(LootTable? table, float extraQuality = 0f)
    {
        return Generate(table, SharedRng, extraQuality);
    }

    public static List<LootDrop> Generate(LootTable? table, RandomNumberGenerator rng, float extraQuality = 0f)
    {
        var drops = new List<LootDrop>();
        if (table == null)
        {
            return drops;
        }

        float quality = table.QualityBonus + extraQuality;

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
                ? RollEquippable(equippable, rng, quality)
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
                        ? RollEquippable((EquippableItemResource)template, rng, quality)
                        : ItemInstance.Plain(template);
                    drops.Add(new LootDrop(extra, 1));
                }
            }
        }

        AppendGold(table, rng, drops);
        return drops;
    }

    /// <summary>
    /// Rolls a specific equippable at a forced rarity (handy for seeding demo loot
    /// or guaranteed rewards). Affix values still vary within their ranges.
    /// </summary>
    public static ItemInstance RollAffixed(EquippableItemResource template, ItemRarity rarity, float valueQuality = 0.5f)
    {
        int count = LootRarity.AffixCount(rarity);
        if (count <= 0)
        {
            return new ItemInstance(template, rarity);
        }

        List<AffixDefinition> pool = AffixDatabase.ApplicableTo(template, rarity);
        List<ItemAffix> affixes = RollAffixes(pool, count, SharedRng, valueQuality);
        return new ItemInstance(template, rarity, affixes);
    }

    private static ItemInstance RollEquippable(EquippableItemResource template, RandomNumberGenerator rng, float quality)
    {
        ItemRarity rarity = LootRarity.Roll(rng, quality);
        int count = LootRarity.AffixCount(rarity);
        if (count <= 0)
        {
            return new ItemInstance(template, rarity);
        }

        List<AffixDefinition> pool = AffixDatabase.ApplicableTo(template, rarity);
        List<ItemAffix> affixes = RollAffixes(pool, count, rng, RarityQuality(rarity, quality));
        return new ItemInstance(template, rarity, affixes);
    }

    /// <summary>Picks up to <paramref name="count"/> distinct affixes (no repeated
    /// stat) from the pool by weight, then rolls each one's value.</summary>
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

        while (rolled.Count < count && candidates.Count > 0)
        {
            AffixDefinition? pick = WeightedPick(candidates, rng);
            if (pick == null)
            {
                break;
            }

            candidates.Remove(pick);
            if (!usedStats.Add(pick.Stat))
            {
                continue;
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
