using System.Collections.Generic;
using Embervale.Core;
using Embervale.Enemies;
using Embervale.Items;
using Embervale.Loot;
using Embervale.Stats;
using Godot;

namespace Embervale.Debugging;

/// <summary>
/// <see cref="ItemValidator"/>: affix and loot-table rules. Owned by the loot lane alone; its rules
/// go here and nowhere else. <c>RequireKey</c> in <c>ItemValidator.cs</c> is shared.
///
/// <c>ContentValidator.ValidateLootTables</c> already checks the tables directly under
/// <c>data/loot</c> for load failures, emptiness and unknown item ids. This file adds what it does
/// not: the sub-folders (<c>tiers/</c>, <c>bosses/</c>), the shape of every row, nested references,
/// the affix catalogue, and the boss tables.
/// </summary>
public static partial class ItemValidator
{
    private const string LootRoot = "res://data/loot";

    /// <summary>The highest item level the game generates (the last tier's ceiling).</summary>
    private const int MaxItemLevel = AffixDefinition.MaxScaledLevel;

    private static void CollectLoot(List<string> issues)
    {
        CollectAffixes(issues);
        CollectAffixCoverage(issues);

        var tables = new Dictionary<string, LootTable>();
        LoadLootTables(LootRoot, topLevel: true, tables, issues);
        foreach (KeyValuePair<string, LootTable> pair in tables)
        {
            CollectLootTable(pair.Key, pair.Value, issues);
        }

        CollectBossTables(issues);

        if (ResidentResources.Load<LootTable>(ContainerLootComponent.DefaultTablePath) == null)
        {
            issues.Add($"the cache loot table '{ContainerLootComponent.DefaultTablePath}' is missing, so every chest rolls nothing");
        }

        foreach (AffixEffect effect in new[] { AffixEffect.HealthRegen, AffixEffect.StaminaRegen, AffixEffect.ManaRegen })
        {
            RequireKey(ItemAffix.EffectKeyFor(effect), "tooltip key", $"affix effect '{effect}'", issues);
        }
    }

    // --- Affixes ------------------------------------------------------------

    private static void CollectAffixes(List<string> issues)
    {
        var labels = new Dictionary<string, string>();
        foreach (AffixDefinition affix in AffixDatabase.All)
        {
            string what = $"affix '{affix.Id}'";
            if (!affix.Id.StartsWith("affix."))
            {
                issues.Add($"{what} must start with 'affix.'");
            }

            if (affix.Label.Length == 0)
            {
                issues.Add($"{what} has an empty Label, so the item it rolls on gets no name fragment");
            }
            else if (labels.TryGetValue(affix.Label, out string? other))
            {
                issues.Add($"{what} shares the Label '{affix.Label}' with '{other}'");
            }
            else
            {
                labels[affix.Label] = affix.Id;
            }

            if (affix.MinValue <= 0f || affix.MaxValue < affix.MinValue)
            {
                issues.Add($"{what} has the value range {affix.MinValue}..{affix.MaxValue}; it must be positive with min <= max");
            }

            if (affix.Weight <= 0f)
            {
                issues.Add($"{what} has weight {affix.Weight}, so it can never be picked");
            }

            if (affix.MinRarity < ItemRarity.Uncommon)
            {
                issues.Add($"{what} has MinRarity Common, a rarity that carries no affixes");
            }

            if (affix.MinItemLevel < 0 || affix.MinItemLevel > MaxItemLevel)
            {
                issues.Add($"{what} has MinItemLevel {affix.MinItemLevel}, outside 0..{MaxItemLevel}");
            }

            if (!affix.ForWeapons && !affix.ForArmor && !affix.ForAccessories)
            {
                issues.Add($"{what} fits no gear family");
            }

            // A percent affix is a fraction. One that can reach 100% at the level cap is an
            // authoring slip (12 typed where 0.12 was meant) far more often than a design.
            float cap = AffixDefinition.ScaleForLevel(affix.MaxValue, MaxItemLevel, affix.GrowthPerLevel);
            if (affix.ReadsAsPercent && cap >= 1f)
            {
                issues.Add($"{what} reads as a percentage but reaches {cap * 100f:0}% at item level {MaxItemLevel}");
            }

            if (affix.Effect != AffixEffect.Stat)
            {
                if (affix.ModifierType != ModifierType.Flat || affix.IsPercent)
                {
                    issues.Add($"{what} is a regeneration affix and must be a flat amount per second");
                }

                StatType resource = affix.Effect switch
                {
                    AffixEffect.HealthRegen => StatType.Health,
                    AffixEffect.StaminaRegen => StatType.Stamina,
                    _ => StatType.Mana,
                };
                if (affix.Stat != resource)
                {
                    issues.Add($"{what} regenerates {resource} but names the stat {affix.Stat}");
                }
            }
        }
    }

    /// <summary>
    /// Every gear family must be able to fill every rarity's affix count, both on a level-less roll
    /// (crafting, shops) and at the level cap, and must have a choice of signature affixes so that
    /// two Legendaries of one family are not forced into the same first affix.
    /// </summary>
    private static void CollectAffixCoverage(List<string> issues)
    {
        (GearFamily Family, EquipmentSlot Slot)[] families =
        {
            (GearFamily.Weapon, EquipmentSlot.MainHand),
            (GearFamily.Armor, EquipmentSlot.Chest),
            (GearFamily.Accessory, EquipmentSlot.Ring),
        };
        ItemRarity[] rarities = { ItemRarity.Uncommon, ItemRarity.Rare, ItemRarity.Epic, ItemRarity.Legendary };

        foreach ((GearFamily family, EquipmentSlot slot) in families)
        {
            var probe = new EquippableItemResource { Slot = slot };
            foreach (int level in new[] { 0, MaxItemLevel })
            {
                foreach (ItemRarity rarity in rarities)
                {
                    // Distinct picks, as the generator counts them: one per exclusivity group, one
                    // per stat or effect outside a group.
                    var distinct = new HashSet<string>();
                    foreach (AffixDefinition affix in AffixDatabase.ApplicableTo(probe, rarity, level))
                    {
                        distinct.Add(affix.Group.Length > 0 ? $"group:{affix.Group}" : $"{affix.Effect}:{affix.Stat}");
                    }

                    int needed = LootRarity.AffixCount(rarity);
                    if (distinct.Count < needed)
                    {
                        issues.Add($"{family} gear has {distinct.Count} distinct affixes at {rarity}, item level {level}; a {rarity} item rolls {needed}");
                    }
                }

                int signature = 0;
                foreach (AffixDefinition affix in AffixDatabase.ApplicableTo(probe, ItemRarity.Legendary, level))
                {
                    if (affix.MinRarity >= LootGenerator.SignatureRarity)
                    {
                        signature++;
                    }
                }

                if (signature < 2)
                {
                    issues.Add($"{family} gear has {signature} signature affixes (MinRarity {LootGenerator.SignatureRarity} or higher) at item level {level}; it needs at least 2 for Legendaries to differ");
                }
            }
        }
    }

    // --- Loot tables --------------------------------------------------------

    /// <summary>Loads every table under <paramref name="directory"/>, recursively. A top-level file
    /// that fails to load is ContentValidator's to report; one in a sub-folder is reported here.</summary>
    private static void LoadLootTables(string directory, bool topLevel, Dictionary<string, LootTable> tables,
        List<string> issues)
    {
        if (!DirAccess.DirExistsAbsolute(directory))
        {
            return;
        }

        foreach (string file in DirAccess.GetFilesAt(directory))
        {
            string name = file.EndsWith(".remap") ? file[..^6] : file;
            if (!name.EndsWith(".tres"))
            {
                continue;
            }

            string path = $"{directory}/{name}";
            if (ResidentResources.Load<LootTable>(path) is { } table)
            {
                tables[path] = table;
            }
            else if (!topLevel)
            {
                issues.Add($"loot table '{path}' failed to load");
            }
        }

        foreach (string sub in DirAccess.GetDirectoriesAt(directory))
        {
            LoadLootTables($"{directory}/{sub}", topLevel: false, tables, issues);
        }
    }

    private static void CollectLootTable(string path, LootTable table, List<string> issues)
    {
        string what = $"loot table '{path}'";
        bool topLevel = path.LastIndexOf('/') == LootRoot.Length;

        if (table.Tier < 0 || table.Tier > LootTiers.MaxTier)
        {
            issues.Add($"{what} has tier {table.Tier}, outside 0..{LootTiers.MaxTier}");
        }

        if (table.MinItemLevel < 0 || table.MaxItemLevel < 0 ||
            (table.MinItemLevel > 0 && table.MaxItemLevel > 0 && table.MinItemLevel > table.MaxItemLevel))
        {
            issues.Add($"{what} has the item level limits {table.MinItemLevel}..{table.MaxItemLevel}");
        }

        if (table.GoldChance < 0f || table.GoldChance > 1f)
        {
            issues.Add($"{what} has GoldChance {table.GoldChance}, outside 0..1");
        }

        if (table.GoldMin < 0 || table.GoldMin > table.GoldMax)
        {
            issues.Add($"{what} has the gold range {table.GoldMin}..{table.GoldMax}");
        }

        if (!topLevel && table.Entries.Count == 0 && table.GoldChance <= 0f)
        {
            issues.Add($"{what} is empty (no entries and no gold)");
        }

        var groupWeights = new Dictionary<string, float>();
        int index = 0;
        foreach (Variant element in table.Entries)
        {
            string row = $"{what} row {index++}";
            LootEntry? entry = element.As<LootEntry>();
            if (entry == null)
            {
                issues.Add($"{row} is not a LootEntry");
                continue;
            }

            CollectLootEntry(row, entry, topLevel, issues);

            if (entry.Group.Length > 0)
            {
                groupWeights.TryGetValue(entry.Group, out float sum);
                groupWeights[entry.Group] = sum + Mathf.Max(0f, entry.Weight);
            }
        }

        foreach (KeyValuePair<string, float> group in groupWeights)
        {
            if (group.Value <= 0f)
            {
                issues.Add($"{what} group '{group.Key}' has no weight, so its pick is uniform by accident");
            }
        }

        if (NestingDepth(table, table.Tier, new HashSet<LootTable>()) < 0)
        {
            issues.Add($"{what} nests itself, directly or through another table");
        }
    }

    private static void CollectLootEntry(string row, LootEntry entry, bool topLevel, List<string> issues)
    {
        if (entry.DropChance < 0f || entry.DropChance > 1f)
        {
            issues.Add($"{row} has DropChance {entry.DropChance}, outside 0..1");
        }

        if (entry.MinQuantity < 0 || entry.MaxQuantity < 1 || entry.MinQuantity > entry.MaxQuantity)
        {
            issues.Add($"{row} has the quantity range {entry.MinQuantity}..{entry.MaxQuantity}");
        }

        if (entry.Weight < 0f)
        {
            issues.Add($"{row} has a negative Weight");
        }

        if (entry.MinLevel < 0 || entry.MaxLevel < 0 ||
            (entry.MinLevel > 0 && entry.MaxLevel > 0 && entry.MinLevel > entry.MaxLevel))
        {
            issues.Add($"{row} has the level gate {entry.MinLevel}..{entry.MaxLevel}");
        }

        if (entry.IsNested)
        {
            if (entry.ItemId.Length > 0)
            {
                issues.Add($"{row} names both the item '{entry.ItemId}' and the table '{entry.TablePath}'; the item is ignored");
            }

            CollectNestedPath(row, entry.TablePath, issues);
            return;
        }

        if (entry.ItemId.Length == 0)
        {
            issues.Add($"{row} names neither an item nor a table");
            return;
        }

        ItemResource? item = ItemDatabase.Get(entry.ItemId);
        if (item == null)
        {
            // ContentValidator reports unknown ids in the top-level tables; do not say it twice.
            if (!topLevel)
            {
                issues.Add($"{row} references unknown item '{entry.ItemId}'");
            }

            return;
        }

        if (entry.RollAffixes && item is not EquippableItemResource)
        {
            issues.Add($"{row} rolls affixes on '{entry.ItemId}', which is not equippable");
        }

        if (entry.MinRarity > ItemRarity.Common && !(entry.RollAffixes && item is EquippableItemResource))
        {
            issues.Add($"{row} sets a rarity floor on '{entry.ItemId}' but does not roll it, so the floor does nothing");
        }
    }

    /// <summary>A nested path must resolve to a loot table: as written when it has no tier token,
    /// and for every one of the six tiers when it does.</summary>
    private static void CollectNestedPath(string row, string tablePath, List<string> issues)
    {
        bool tiered = tablePath.Contains(LootTiers.TierToken);
        for (int tier = LootTiers.MinTier; tier <= (tiered ? LootTiers.MaxTier : LootTiers.MinTier); tier++)
        {
            string resolved = LootTiers.ResolvePath(tablePath, tier);
            if (!ResourceLoader.Exists(resolved) || ResidentResources.Load<LootTable>(resolved) == null)
            {
                issues.Add($"{row} nests '{resolved}', which is not a loot table");
            }
        }
    }

    /// <summary>How deep <paramref name="table"/> nests, or -1 when it reaches itself. Nested paths
    /// are followed at <paramref name="tier"/> (the first tier when the table pins none).</summary>
    private static int NestingDepth(LootTable table, int tier, HashSet<LootTable> open)
    {
        if (!open.Add(table))
        {
            return -1;
        }

        int deepest = 0;
        foreach (Variant element in table.Entries)
        {
            if (element.As<LootEntry>() is not { IsNested: true } entry ||
                ResidentResources.Load<LootTable>(LootTiers.ResolvePath(entry.TablePath, tier)) is not { } nested)
            {
                continue;
            }

            int depth = NestingDepth(nested, LootTiers.IsTier(nested.Tier) ? nested.Tier : tier, open);
            if (depth < 0)
            {
                return -1;
            }

            deepest = Mathf.Max(deepest, depth + 1);
        }

        open.Remove(table);
        return deepest;
    }

    // --- Bosses -------------------------------------------------------------

    /// <summary>
    /// Every boss archetype drops from a table. A story boss (its fight records a defeat flag and
    /// is not a duel that ends in a withdrawal) must leave a reward chest whose table pins a tier
    /// and guarantees a once-per-save Legendary: the signature piece of the first kill.
    /// </summary>
    private static void CollectBossTables(List<string> issues)
    {
        foreach (EnemyArchetypeResource archetype in EnemyArchetypeDatabase.All)
        {
            if (!archetype.IsBoss)
            {
                continue;
            }

            string what = $"boss archetype '{archetype.Id}'";
            if (archetype.LootTablePath.Length == 0)
            {
                issues.Add($"{what} has no loot table");
                continue;
            }

            if (ResidentResources.Load<LootTable>(archetype.LootTablePath) is not { } table)
            {
                // A missing file is already reported by ContentValidator's archetype check.
                continue;
            }

            BossResource? fight = BossDatabase.Get(archetype.BossId);
            bool storyBoss = fight is { DefeatFlagId.Length: > 0, WithdrawHealthFraction: <= 0f };
            if (!storyBoss)
            {
                continue;
            }

            if (!table.DropsAsChest)
            {
                issues.Add($"{what} is a story boss but its table '{archetype.LootTablePath}' does not leave a reward chest");
            }

            if (!LootTiers.IsTier(table.Tier))
            {
                issues.Add($"{what} table '{archetype.LootTablePath}' does not pin a tier, so its loot would follow whatever realm the fight happens in");
            }

            bool signature = false;
            foreach (Variant element in table.Entries)
            {
                if (element.As<LootEntry>() is { OncePerSave: true, RollAffixes: true, MinRarity: ItemRarity.Legendary } entry
                    && ItemDatabase.Get(entry.ItemId) is EquippableItemResource)
                {
                    signature = true;
                }
            }

            if (!signature)
            {
                issues.Add($"{what} table '{archetype.LootTablePath}' has no once-per-save Legendary row for its signature item");
            }
        }
    }
}
