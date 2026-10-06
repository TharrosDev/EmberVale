using System;
using System.Collections.Generic;
using Embervale.Core;
using Embervale.Crafting;
using Embervale.Economy;
using Embervale.Enemies;
using Embervale.Items;
using Embervale.Loot;
using Embervale.Magic;
using Embervale.Quests;
using Embervale.Stats;
using Godot;

namespace Embervale.Debugging;

/// <summary>
/// <see cref="ItemValidator"/>: coverage rules for the authored catalogue (items, sets, unique
/// effects against <c>tools/items/catalogue.py</c>). Owned by the content lane alone; its rules go
/// here and nowhere else. <c>RequireKey</c> in <c>ItemValidator.cs</c> is shared.
///
/// What it holds the generated content to: every planned id is a loaded resource, an item's level
/// sits inside its tier's band, an equippable's stats and price land on <see cref="ItemBudget"/>,
/// a weapon item carries a weapon of its class, a consumable and a unique effect have the numbers
/// their kind reads, a set can actually be worn whole, a recipe scroll teaches a recipe that exists,
/// and every planned equippable has somewhere to come from.
/// </summary>
public static partial class ItemValidator
{
    private const string ContentLootDirectory = "res://data/loot";

    private static void CollectContent(List<string> issues)
    {
        ContentCoverage(issues);
        ContentTierBands(issues);
        ContentBudgets(issues);
        ContentConsumables(issues);
        ContentUniqueNumbers(issues);
        ContentSetShape(issues);
        ContentRecipeScrolls(issues);
        ContentObtainability(issues);
    }

    private static void ContentCoverage(List<string> issues)
    {
        foreach (string id in ItemCatalogueIds.ItemIds)
        {
            if (ItemDatabase.Get(id) == null)
            {
                issues.Add($"catalogue item '{id}' has no resource in data/items; run python tools/gen_items.py");
            }
        }

        foreach (string id in ItemCatalogueIds.SetIds)
        {
            if (ItemSetDatabase.Get(id) == null)
            {
                issues.Add($"catalogue set '{id}' has no resource in data/item_sets; run python tools/gen_items.py");
            }
        }

        foreach (string id in ItemCatalogueIds.UniqueEffectIds)
        {
            if (UniqueEffectDatabase.Get(id) == null)
            {
                issues.Add($"catalogue unique effect '{id}' has no resource in data/unique_effects; run python tools/gen_items.py");
            }
        }
    }

    /// <summary>Every tiered item, the twelve retrofitted originals included.</summary>
    private static void ContentTierBands(List<string> issues)
    {
        foreach (ItemResource item in ItemDatabase.All.Values)
        {
            if (item.Tier <= 0 || item.ItemLevel <= 0)
            {
                continue; // legacy, or a material or scroll that carries a tier and no level
            }

            (int low, int high) = ItemBudget.TierBand(item.Tier);
            if (item.ItemLevel < low || item.ItemLevel > high)
            {
                issues.Add($"item '{item.Id}' has item level {item.ItemLevel}, outside tier {item.Tier}'s band {low}..{high}");
            }

            if (item.RequiredLevel > item.ItemLevel)
            {
                issues.Add($"item '{item.Id}' needs level {item.RequiredLevel} to use, above its own item level {item.ItemLevel}");
            }
        }
    }

    /// <summary>Stats, price and weapon of every planned equippable against <see cref="ItemBudget"/>.
    /// The originals are not held to it: they predate the budget and their numbers are in saves.</summary>
    private static void ContentBudgets(List<string> issues)
    {
        foreach (string id in ItemCatalogueIds.ItemIds)
        {
            if (ItemDatabase.Get(id) is not EquippableItemResource item)
            {
                continue;
            }

            float budget = ItemBudget.Points(item.ItemLevel, item.Slot, item.TwoHanded, item.Rarity);
            float spent = item.BonusManaRegen * ItemBudget.ManaRegenWeight + item.BonusStaminaRegen * ItemBudget.StaminaRegenWeight;
            foreach ((StatType stat, float amount) in item.StatBonuses())
            {
                spent += amount * ItemBudget.Weight(stat);
            }

            if (!ItemBudget.WithinBudget(spent, budget))
            {
                issues.Add($"item '{id}' spends {spent:0.#} stat points against a budget of {budget:0.#} " +
                           $"(item level {item.ItemLevel}, {item.Slot}, {item.Rarity}); more than a quarter off");
            }

            int value = ItemBudget.Value(item.ItemLevel, item.Rarity, item.Slot);
            if (Math.Abs(item.Value - value) > Math.Max(5, value * 0.03f))
            {
                issues.Add($"item '{id}' is valued at {item.Value} gold; item level {item.ItemLevel} at {item.Rarity} prices it at {value}");
            }

            bool expectsTwoHands = ItemBudget.IsTwoHandedClass(item.WeaponClass);
            if (item.TwoHanded != expectsTwoHands)
            {
                issues.Add($"item '{id}' is a {item.WeaponClass} with TwoHanded = {item.TwoHanded}");
            }

            if (item.Slot != EquipmentSlot.MainHand)
            {
                if (item.Weapon != null)
                {
                    issues.Add($"item '{id}' sits in {item.Slot} and carries a weapon, which only the main hand swaps in");
                }

                continue;
            }

            if (item.Weapon is not { } weapon)
            {
                issues.Add($"item '{id}' is a main-hand item with no Weapon; equipping it would leave the bare-hand default");
                continue;
            }

            float damage = ItemBudget.WeaponDamage(item.ItemLevel, item.WeaponClass, item.Rarity);
            if (damage <= 0f)
            {
                issues.Add($"item '{id}' is a main-hand item of class {item.WeaponClass}, which has no damage profile");
            }
            else if (Math.Abs(weapon.BaseDamage - damage) > damage * ItemBudget.Tolerance)
            {
                issues.Add($"item '{id}' swings for {weapon.BaseDamage:0.#}; a {item.WeaponClass} of item level " +
                           $"{item.ItemLevel} is budgeted {damage:0.#}");
            }

            if (weapon.IsRanged != (item.WeaponClass == WeaponClass.Bow))
            {
                issues.Add($"item '{id}' is a {item.WeaponClass} whose weapon has IsRanged = {weapon.IsRanged}");
            }
        }
    }

    private static void ContentConsumables(List<string> issues)
    {
        foreach (string id in ItemCatalogueIds.ItemIds)
        {
            if (ItemDatabase.Get(id) is not ConsumableItemResource item)
            {
                continue;
            }

            if (item.Effect != ConsumableEffectKind.Cure && item.Magnitude <= 0f)
            {
                issues.Add($"consumable '{id}' is a {item.Effect} with no magnitude; using it would do nothing");
            }

            if (item.Effect == ConsumableEffectKind.Buff && item.DurationSeconds <= 0f)
            {
                issues.Add($"consumable '{id}' is a buff with no duration");
            }

            if (item.CooldownSeconds < 0f || item.DurationSeconds < 0f)
            {
                issues.Add($"consumable '{id}' has a negative cooldown or duration");
            }

            foreach (string statusId in item.CureStatusIds)
            {
                if (StatusEffectDatabase.Get(statusId) == null)
                {
                    issues.Add($"consumable '{id}' cures unknown status '{statusId}'");
                }
            }
        }
    }

    private static void ContentUniqueNumbers(List<string> issues)
    {
        foreach (UniqueEffectResource effect in UniqueEffectDatabase.All)
        {
            string what = $"unique effect '{effect.Id}'";
            if (effect.Magnitude < 0f || effect.DurationSeconds < 0f || effect.CooldownSeconds < 0f)
            {
                issues.Add($"{what} has a negative magnitude, duration or cooldown");
            }

            if (effect.Chance <= 0f || effect.Chance > 1f)
            {
                issues.Add($"{what} has chance {effect.Chance}; it must be above 0 and at most 1");
            }

            switch (effect.Kind)
            {
                case UniqueEffectKind.OnHitStatus:
                    if (StatusEffectDatabase.Get(effect.StatusId) == null)
                    {
                        issues.Add($"{what} applies unknown status '{effect.StatusId}'");
                    }

                    break;
                case UniqueEffectKind.LowHealthPower:
                case UniqueEffectKind.CritExecute:
                    if (effect.Threshold <= 0f || effect.Threshold >= 1f)
                    {
                        issues.Add($"{what} is gated on health fraction {effect.Threshold}; it must be between 0 and 1");
                    }

                    ContentRequireMagnitude(effect, what, issues);
                    break;
                case UniqueEffectKind.ManaShield:
                case UniqueEffectKind.BlockReflect:
                case UniqueEffectKind.OnKillHeal:
                    ContentRequireMagnitude(effect, what, issues);
                    if (effect.Magnitude > 1f)
                    {
                        issues.Add($"{what} has magnitude {effect.Magnitude}; a {effect.Kind} reads it as a fraction of 1");
                    }

                    break;
                default:
                    ContentRequireMagnitude(effect, what, issues);
                    break;
            }
        }
    }

    private static void ContentRequireMagnitude(UniqueEffectResource effect, string what, List<string> issues)
    {
        if (effect.Magnitude <= 0f)
        {
            issues.Add($"{what} is a {effect.Kind} with no magnitude; it would do nothing");
        }
    }

    /// <summary>One item per slot, so two pieces that share a slot can never count together: the
    /// highest threshold must be reachable with the slots the set actually spans.</summary>
    private static void ContentSetShape(List<string> issues)
    {
        foreach (ItemSetResource set in ItemSetDatabase.All)
        {
            var slots = new HashSet<EquipmentSlot>();
            foreach (string pieceId in set.PieceIds)
            {
                if (ItemDatabase.Get(pieceId) is EquippableItemResource piece)
                {
                    slots.Add(piece.Slot);
                }
                else if (ItemDatabase.Get(pieceId) != null)
                {
                    issues.Add($"item set '{set.Id}' lists piece '{pieceId}', which cannot be equipped");
                }
            }

            foreach (ItemSetBonusResource bonus in set.Bonuses)
            {
                if (bonus != null && bonus.PiecesRequired > slots.Count)
                {
                    issues.Add($"item set '{set.Id}' has a bonus at {bonus.PiecesRequired} pieces, but its pieces " +
                               $"fill only {slots.Count} different slots");
                }
            }
        }
    }

    /// <summary>A scroll is <c>item.recipe_scroll.&lt;leaf&gt;</c> and teaches <c>recipe.&lt;leaf&gt;</c>.</summary>
    private static void ContentRecipeScrolls(List<string> issues)
    {
        foreach (string id in ItemCatalogueIds.ItemIds)
        {
            if (!id.StartsWith(ItemCatalogueIds.RecipeScrollPrefix, StringComparison.Ordinal))
            {
                continue;
            }

            string recipeId = ItemCatalogueIds.RecipeOfScroll(id);
            if (RecipeDatabase.Get(recipeId) == null)
            {
                issues.Add($"recipe scroll '{id}' teaches '{recipeId}', which is not a recipe");
            }
        }
    }

    /// <summary>
    /// Every planned equippable has a source: a loot table entry (monsters, chests and the shop gear
    /// pools all live in data/loot), a recipe output, a shop shelf, a quest reward or a boss reward.
    /// Checked by id against the loaded databases, so a source another system adds counts the moment
    /// it is authored. Gear with none of them is content no player can ever hold.
    /// </summary>
    private static void ContentObtainability(List<string> issues)
    {
        HashSet<string> sources = ContentObtainableIds();
        foreach (string id in ItemCatalogueIds.ItemIds)
        {
            if (ItemDatabase.Get(id) is not EquippableItemResource || sources.Contains(id))
            {
                continue;
            }

            bool signature = false;
            foreach (string signatureId in ItemCatalogueIds.BossSignatureIds)
            {
                signature |= signatureId == id;
            }

            issues.Add(signature
                ? $"item '{id}' is a Flamebearer's signature and nothing drops it: no loot table or boss reward names it"
                : $"item '{id}' cannot be obtained: no loot table, recipe, shop, quest or boss reward names it");
        }
    }

    private static HashSet<string> ContentObtainableIds()
    {
        var ids = new HashSet<string>();
        if (DirAccess.DirExistsAbsolute(ContentLootDirectory))
        {
            foreach (string file in DirAccess.GetFilesAt(ContentLootDirectory))
            {
                string name = file.EndsWith(".remap", StringComparison.Ordinal) ? file[..^6] : file;
                if (!name.EndsWith(".tres", StringComparison.Ordinal) ||
                    ResidentResources.Load<LootTable>($"{ContentLootDirectory}/{name}") is not { } table)
                {
                    continue; // ContentValidator.ValidateLootTables reports a table that fails to load
                }

                foreach (Variant element in table.Entries)
                {
                    if (element.As<LootEntry>() is { } entry && entry.DropChance > 0f)
                    {
                        ids.Add(entry.ItemId);
                    }
                }
            }
        }

        foreach (CraftingRecipeResource recipe in RecipeDatabase.All)
        {
            ids.Add(recipe.OutputItemId);
        }

        foreach (ShopResource shop in ShopDatabase.All)
        {
            foreach (ShopStockEntry entry in shop.StockList())
            {
                ids.Add(entry.ItemId);
            }
        }

        foreach (QuestResource quest in QuestDatabase.All)
        {
            foreach (Variant element in quest.RewardItems)
            {
                if (element.As<QuestItemReward>() is { } reward)
                {
                    ids.Add(reward.ItemId);
                }
            }
        }

        foreach (BossResource boss in BossDatabase.All)
        {
            ids.Add(boss.RewardItemId);
        }

        return ids;
    }
}

/// <summary>
/// The item stat budget: what an equippable of a given level, slot and rarity may carry, what a
/// weapon of a class swings for, and what the piece is worth. Pure arithmetic with no engine calls.
/// It is the C# mirror of the four functions at the top of <c>tools/gen_items.py</c>, which writes
/// the numbers; <see cref="ItemValidator"/> and <c>ItemCatalogueTests</c> recompute them here, so
/// retuning one side without the other fails the build rather than a play session.
///
/// A point is one point of Armor; <see cref="Weight"/> prices every other stat against it.
/// </summary>
public static class ItemBudget
{
    /// <summary>How far a written item may sit from its budget.</summary>
    public const float Tolerance = 0.25f;

    /// <summary>The rounding grain: a tiny budget may be off by this many points whatever the fraction.</summary>
    public const float Grain = 1.6f;

    public const float ManaRegenWeight = 8f;
    public const float StaminaRegenWeight = 6f;
    public const float TwoHandedShare = 1.6f;

    /// <summary>The (lowest, highest) item level of a realm tier; (0, 0) for a tier that does not exist.</summary>
    public static (int Low, int High) TierBand(int tier) => tier switch
    {
        1 => (1, 10),
        2 => (8, 20),
        3 => (18, 30),
        4 => (28, 40),
        5 => (36, 46),
        6 => (44, 50),
        _ => (0, 0),
    };

    /// <summary>Points a chest piece of this item level spends before rarity.</summary>
    public static float Base(int itemLevel) => 6f + 0.9f * itemLevel;

    /// <summary>The fraction of a chest piece's budget a slot gets.</summary>
    public static float SlotShare(EquipmentSlot slot) => slot switch
    {
        EquipmentSlot.Chest => 1f,
        EquipmentSlot.Legs => 0.75f,
        EquipmentSlot.Head => 0.6f,
        EquipmentSlot.Hands => 0.45f,
        EquipmentSlot.Feet => 0.45f,
        EquipmentSlot.OffHand => 0.6f,
        EquipmentSlot.MainHand => 0.5f,
        EquipmentSlot.Ring => 0.5f,
        EquipmentSlot.Amulet => 0.6f,
        EquipmentSlot.Ammo => 0.2f,
        _ => 0f,
    };

    public static float RarityFactor(ItemRarity rarity) => rarity switch
    {
        ItemRarity.Uncommon => 1.1f,
        ItemRarity.Rare => 1.25f,
        ItemRarity.Epic => 1.4f,
        ItemRarity.Legendary => 1.6f,
        _ => 1f,
    };

    /// <summary>The stat points an equippable may spend.</summary>
    public static float Points(int itemLevel, EquipmentSlot slot, bool twoHanded, ItemRarity rarity) =>
        Base(itemLevel) * SlotShare(slot) * (twoHanded ? TwoHandedShare : 1f) * RarityFactor(rarity);

    /// <summary>Points one unit of a stat costs (crit chance and move speed per 1.0).</summary>
    public static float Weight(StatType stat) => stat switch
    {
        StatType.Armor => 1f,
        StatType.Health => 0.4f,
        StatType.Stamina => 0.5f,
        StatType.Mana => 0.5f,
        StatType.PhysicalPower => 3f,
        StatType.SpellPower => 3f,
        StatType.CritChance => 500f,
        StatType.MoveSpeed => 25f,
        StatType.FireResist or StatType.FrostResist or StatType.LightningResist
            or StatType.ArcaneResist or StatType.NatureResist or StatType.NecroticResist => 0.5f,
        _ => 0f,
    };

    /// <summary>True when <paramref name="spent"/> is within a quarter (or one grain) of the budget.</summary>
    public static bool WithinBudget(float spent, float budget) =>
        Math.Abs(spent - budget) <= Math.Max(Tolerance * budget, Grain);

    /// <summary>Sword-equivalent damage multiplier of a class; 0 for a class that deals none.</summary>
    public static float ClassDamage(WeaponClass weaponClass) => weaponClass switch
    {
        WeaponClass.Sword => 1f,
        WeaponClass.Dagger => 0.72f,
        WeaponClass.Axe => 1.12f,
        WeaponClass.Mace => 1.08f,
        WeaponClass.Spear => 0.95f,
        WeaponClass.Staff => 0.8f,
        WeaponClass.Greatsword => 1.6f,
        WeaponClass.Bow => 1.12f,
        _ => 0f,
    };

    /// <summary>The base damage of a weapon of this level, class and rarity.</summary>
    public static float WeaponDamage(int itemLevel, WeaponClass weaponClass, ItemRarity rarity) =>
        (12f + 0.62f * itemLevel) * ClassDamage(weaponClass) * (1f + 0.04f * (int)rarity);

    public static bool IsTwoHandedClass(WeaponClass weaponClass) =>
        weaponClass is WeaponClass.Staff or WeaponClass.Greatsword or WeaponClass.Bow;

    /// <summary>Gold value of an equippable, from item level, rarity and slot alone.</summary>
    public static int Value(int itemLevel, ItemRarity rarity, EquipmentSlot slot)
    {
        float rarityFactor = rarity switch
        {
            ItemRarity.Uncommon => 1.25f,
            ItemRarity.Rare => 1.8f,
            ItemRarity.Epic => 2.6f,
            ItemRarity.Legendary => 4f,
            _ => 1f,
        };
        float slotFactor = slot switch
        {
            EquipmentSlot.Chest or EquipmentSlot.MainHand or EquipmentSlot.Amulet => 1f,
            EquipmentSlot.Legs or EquipmentSlot.OffHand => 0.8f,
            EquipmentSlot.Head => 0.7f,
            EquipmentSlot.Hands or EquipmentSlot.Feet => 0.55f,
            EquipmentSlot.Ring => 0.9f,
            EquipmentSlot.Ammo => 0.04f,
            _ => 0f,
        };
        float raw = (18f + 3.2f * itemLevel + 0.06f * itemLevel * itemLevel) * rarityFactor * slotFactor;
        return raw >= 100f ? (int)MathF.Round(raw / 5f) * 5 : Math.Max(1, (int)MathF.Round(raw));
    }
}

/// <summary>
/// The ids <c>tools/items/catalogue.py</c> plans, written here by <c>tools/gen_items.py</c> so the
/// validator and the tests can ask "is everything planned actually authored" without parsing Python.
/// Pure data.
/// </summary>
public static class ItemCatalogueIds
{
    public const string RecipeScrollPrefix = "item.recipe_scroll.";

    /// <summary>The recipe a scroll teaches: <c>item.recipe_scroll.x</c> teaches <c>recipe.x</c>.</summary>
    public static string RecipeOfScroll(string scrollItemId) =>
        scrollItemId.StartsWith(RecipeScrollPrefix, StringComparison.Ordinal)
            ? "recipe." + scrollItemId[RecipeScrollPrefix.Length..]
            : string.Empty;

    // --- BEGIN generated by tools/gen_items.py (do not edit by hand) ---
    /// <summary>Every item tools/items/catalogue.py plans, in catalogue order.</summary>
    public static readonly IReadOnlyList<string> ItemIds = new[]
    {
        "item.weapon.iron_sword",
        "item.weapon.iron_axe",
        "item.weapon.iron_mace",
        "item.weapon.iron_spear",
        "item.weapon.oak_staff",
        "item.weapon.iron_greatsword",
        "item.armor.homespun_hood",
        "item.armor.homespun_robe",
        "item.armor.homespun_wraps",
        "item.armor.homespun_trousers",
        "item.armor.homespun_shoes",
        "item.armor.rawhide_coif",
        "item.armor.rawhide_jerkin",
        "item.armor.rawhide_gloves",
        "item.armor.rawhide_breeches",
        "item.armor.rawhide_boots",
        "item.armor.iron_helm",
        "item.armor.iron_cuirass",
        "item.armor.iron_gauntlets",
        "item.armor.iron_greaves",
        "item.armor.iron_sabatons",
        "item.ring.copper",
        "item.amulet.copper_charm",
        "item.weapon.steel_dagger",
        "item.weapon.steel_axe",
        "item.weapon.steel_mace",
        "item.weapon.steel_spear",
        "item.weapon.frostpine_staff",
        "item.weapon.steel_greatsword",
        "item.weapon.frostpine_bow",
        "item.armor.frostwool_hood",
        "item.armor.frostwool_robe",
        "item.armor.frostwool_wraps",
        "item.armor.frostwool_trousers",
        "item.armor.frostwool_shoes",
        "item.armor.frosthide_coif",
        "item.armor.frosthide_jerkin",
        "item.armor.frosthide_gloves",
        "item.armor.frosthide_breeches",
        "item.armor.frosthide_boots",
        "item.armor.steel_helm",
        "item.armor.steel_cuirass",
        "item.armor.steel_gauntlets",
        "item.armor.steel_greaves",
        "item.armor.steel_sabatons",
        "item.armor.steel_shield",
        "item.ring.silver",
        "item.amulet.silver_pendant",
        "item.ammo.steel_arrows",
        "item.weapon.blacksteel_sword",
        "item.weapon.blacksteel_dagger",
        "item.weapon.blacksteel_axe",
        "item.weapon.blacksteel_mace",
        "item.weapon.blacksteel_spear",
        "item.weapon.cinderwood_staff",
        "item.weapon.blacksteel_greatsword",
        "item.weapon.cinderwood_bow",
        "item.armor.ashweave_hood",
        "item.armor.ashweave_robe",
        "item.armor.ashweave_wraps",
        "item.armor.ashweave_trousers",
        "item.armor.ashweave_shoes",
        "item.armor.ashhide_coif",
        "item.armor.ashhide_jerkin",
        "item.armor.ashhide_gloves",
        "item.armor.ashhide_breeches",
        "item.armor.ashhide_boots",
        "item.armor.blacksteel_helm",
        "item.armor.blacksteel_cuirass",
        "item.armor.blacksteel_gauntlets",
        "item.armor.blacksteel_greaves",
        "item.armor.blacksteel_sabatons",
        "item.armor.blacksteel_shield",
        "item.ring.obsidian",
        "item.amulet.obsidian_talisman",
        "item.ammo.blacksteel_arrows",
        "item.weapon.sunsteel_sword",
        "item.weapon.sunsteel_dagger",
        "item.weapon.sunsteel_axe",
        "item.weapon.sunsteel_mace",
        "item.weapon.sunsteel_spear",
        "item.weapon.goldenpalm_staff",
        "item.weapon.sunsteel_greatsword",
        "item.weapon.goldenpalm_bow",
        "item.armor.sunsilk_hood",
        "item.armor.sunsilk_robe",
        "item.armor.sunsilk_wraps",
        "item.armor.sunsilk_trousers",
        "item.armor.sunsilk_shoes",
        "item.armor.dunehide_coif",
        "item.armor.dunehide_jerkin",
        "item.armor.dunehide_gloves",
        "item.armor.dunehide_breeches",
        "item.armor.dunehide_boots",
        "item.armor.sunsteel_helm",
        "item.armor.sunsteel_cuirass",
        "item.armor.sunsteel_gauntlets",
        "item.armor.sunsteel_greaves",
        "item.armor.sunsteel_sabatons",
        "item.armor.sunsteel_shield",
        "item.ring.gold",
        "item.amulet.gold_medallion",
        "item.ammo.sunsteel_arrows",
        "item.weapon.moonsilver_sword",
        "item.weapon.moonsilver_dagger",
        "item.weapon.moonsilver_axe",
        "item.weapon.moonsilver_mace",
        "item.weapon.moonsilver_spear",
        "item.weapon.gloamwood_staff",
        "item.weapon.moonsilver_greatsword",
        "item.weapon.gloamwood_bow",
        "item.armor.gloamweave_hood",
        "item.armor.gloamweave_robe",
        "item.armor.gloamweave_wraps",
        "item.armor.gloamweave_trousers",
        "item.armor.gloamweave_shoes",
        "item.armor.duskhide_coif",
        "item.armor.duskhide_jerkin",
        "item.armor.duskhide_gloves",
        "item.armor.duskhide_breeches",
        "item.armor.duskhide_boots",
        "item.armor.moonsilver_helm",
        "item.armor.moonsilver_cuirass",
        "item.armor.moonsilver_gauntlets",
        "item.armor.moonsilver_greaves",
        "item.armor.moonsilver_sabatons",
        "item.armor.moonsilver_shield",
        "item.ring.moonsilver",
        "item.amulet.moonsilver_locket",
        "item.ammo.moonsilver_arrows",
        "item.weapon.starmetal_sword",
        "item.weapon.starmetal_dagger",
        "item.weapon.starmetal_axe",
        "item.weapon.starmetal_mace",
        "item.weapon.starmetal_spear",
        "item.weapon.starwood_staff",
        "item.weapon.starmetal_greatsword",
        "item.weapon.starwood_bow",
        "item.armor.starweave_hood",
        "item.armor.starweave_robe",
        "item.armor.starweave_wraps",
        "item.armor.starweave_trousers",
        "item.armor.starweave_shoes",
        "item.armor.skyhide_coif",
        "item.armor.skyhide_jerkin",
        "item.armor.skyhide_gloves",
        "item.armor.skyhide_breeches",
        "item.armor.skyhide_boots",
        "item.armor.starmetal_helm",
        "item.armor.starmetal_cuirass",
        "item.armor.starmetal_gauntlets",
        "item.armor.starmetal_greaves",
        "item.armor.starmetal_sabatons",
        "item.armor.starmetal_shield",
        "item.ring.starmetal",
        "item.amulet.starmetal_torc",
        "item.ammo.starmetal_arrows",
        "item.armor.emberguard_helm",
        "item.armor.emberguard_cuirass",
        "item.armor.emberguard_shield",
        "item.armor.frostfang_hunters_coif",
        "item.armor.frostfang_hunters_jerkin",
        "item.armor.frostfang_hunters_gloves",
        "item.weapon.frostfang_hunters_bow",
        "item.armor.stormcallers_hood",
        "item.armor.stormcallers_robe",
        "item.weapon.stormcallers_staff",
        "item.armor.ashen_reavers_helm",
        "item.armor.ashen_reavers_cuirass",
        "item.armor.ashen_reavers_greaves",
        "item.weapon.ashen_reavers_axe",
        "item.amulet.wildheart_totem",
        "item.ring.wildheart_band",
        "item.armor.wildheart_gloves",
        "item.armor.sunspire_templars_helm",
        "item.armor.sunspire_templars_cuirass",
        "item.armor.sunspire_templars_gauntlets",
        "item.armor.sunspire_templars_greaves",
        "item.armor.sunspire_templars_sabatons",
        "item.armor.gloamwardens_coif",
        "item.armor.gloamwardens_jerkin",
        "item.armor.gloamwardens_breeches",
        "item.weapon.gloamwardens_dagger",
        "item.armor.starfall_hood",
        "item.armor.starfall_robe",
        "item.armor.starfall_wraps",
        "item.armor.starfall_trousers",
        "item.armor.starfall_shoes",
        "item.weapon.crownbreaker",
        "item.weapon.stormcleaver",
        "item.weapon.packlords_talon",
        "item.weapon.prophets_pyre",
        "item.armor.hollow_diadem",
        "item.weapon.ashen_oath",
        "item.ring.unmakers_signet",
        "item.armor.mirrorguard",
        "item.weapon.farsight",
        "item.amulet.sages_reservoir",
        "item.amulet.magpies_charm",
        "item.weapon.emberpike",
        "item.armor.windrunner_treads",
        "item.armor.briarheart_cuirass",
        "item.armor.gravetenders_grips",
        "item.weapon.winters_bite",
        "item.ring.last_ember",
        "item.potion.health_greater",
        "item.potion.health_superior",
        "item.potion.stamina_lesser",
        "item.potion.mana_lesser",
        "item.potion.stamina_greater",
        "item.potion.mana_greater",
        "item.potion.stamina_superior",
        "item.potion.mana_superior",
        "item.potion.cure_lesser",
        "item.potion.cure_greater",
        "item.potion.cure_superior",
        "item.potion.resist_fire_lesser",
        "item.potion.resist_frost_lesser",
        "item.potion.resist_lightning_lesser",
        "item.potion.resist_arcane_lesser",
        "item.potion.resist_nature_lesser",
        "item.potion.resist_necrotic_lesser",
        "item.potion.resist_fire_greater",
        "item.potion.resist_frost_greater",
        "item.potion.resist_lightning_greater",
        "item.potion.resist_arcane_greater",
        "item.potion.resist_nature_greater",
        "item.potion.resist_necrotic_greater",
        "item.potion.resist_fire_superior",
        "item.potion.resist_frost_superior",
        "item.potion.resist_lightning_superior",
        "item.potion.resist_arcane_superior",
        "item.potion.resist_nature_superior",
        "item.potion.resist_necrotic_superior",
        "item.potion.might_lesser",
        "item.potion.might_greater",
        "item.potion.might_superior",
        "item.potion.insight_lesser",
        "item.potion.insight_greater",
        "item.potion.insight_superior",
        "item.potion.stoneskin_lesser",
        "item.potion.stoneskin_greater",
        "item.potion.stoneskin_superior",
        "item.food.grilled_catch",
        "item.food.hearth_stew",
        "item.food.oatcake",
        "item.food.eel_pie",
        "item.food.spiced_roast",
        "item.food.frostfang_broth",
        "item.food.ember_tea",
        "item.food.roe_toast",
        "item.material.blacksteel_ingot",
        "item.material.sunsteel_ingot",
        "item.material.moonsilver_ingot",
        "item.material.starmetal_ingot",
        "item.material.celestial_dust",
        "item.recipe_scroll.blacksteel_ingot",
        "item.recipe_scroll.sunsteel_ingot",
        "item.recipe_scroll.moonsilver_ingot",
        "item.recipe_scroll.starmetal_ingot",
        "item.recipe_scroll.blacksteel_sword",
        "item.recipe_scroll.cinderwood_bow",
        "item.recipe_scroll.cinderwood_staff",
        "item.recipe_scroll.blacksteel_cuirass",
        "item.recipe_scroll.ashhide_jerkin",
        "item.recipe_scroll.ashweave_robe",
        "item.recipe_scroll.sunsteel_sword",
        "item.recipe_scroll.goldenpalm_bow",
        "item.recipe_scroll.goldenpalm_staff",
        "item.recipe_scroll.sunsteel_cuirass",
        "item.recipe_scroll.dunehide_jerkin",
        "item.recipe_scroll.sunsilk_robe",
        "item.recipe_scroll.moonsilver_sword",
        "item.recipe_scroll.gloamwood_bow",
        "item.recipe_scroll.gloamwood_staff",
        "item.recipe_scroll.moonsilver_cuirass",
        "item.recipe_scroll.duskhide_jerkin",
        "item.recipe_scroll.gloamweave_robe",
        "item.recipe_scroll.starmetal_sword",
        "item.recipe_scroll.starwood_bow",
        "item.recipe_scroll.starwood_staff",
        "item.recipe_scroll.starmetal_cuirass",
        "item.recipe_scroll.skyhide_jerkin",
        "item.recipe_scroll.starweave_robe",
        "item.recipe_scroll.blacksteel_arrows",
        "item.recipe_scroll.health_greater",
        "item.recipe_scroll.health_superior",
        "item.recipe_scroll.stamina_greater",
        "item.recipe_scroll.stamina_superior",
        "item.recipe_scroll.mana_greater",
        "item.recipe_scroll.mana_superior",
        "item.recipe_scroll.cure_greater",
        "item.recipe_scroll.cure_superior",
        "item.recipe_scroll.spiced_roast",
        "item.recipe_scroll.frostfang_broth",
        "item.recipe_scroll.ember_tea",
    };

    /// <summary>Every planned item set.</summary>
    public static readonly IReadOnlyList<string> SetIds = new[]
    {
        "set.emberguard",
        "set.frostfang_hunter",
        "set.stormcaller",
        "set.ashen_reaver",
        "set.wildheart",
        "set.sunspire_templar",
        "set.gloamwarden",
        "set.starfall",
    };

    /// <summary>Every planned unique effect.</summary>
    public static readonly IReadOnlyList<string> UniqueEffectIds = new[]
    {
        "unique.crownbreaker",
        "unique.stormcleaver",
        "unique.packlords_hunger",
        "unique.prophets_pyre",
        "unique.hollow_diadem",
        "unique.ashen_oath",
        "unique.unmaking_touch",
        "unique.mirrorguard",
        "unique.farsight",
        "unique.sages_reservoir",
        "unique.magpies_luck",
        "unique.emberpike",
        "unique.windrunner",
        "unique.briarheart",
        "unique.gravetender",
        "unique.winters_bite",
        "unique.last_ember",
        "unique.reavers_fury",
        "unique.wildheart_feast",
        "unique.templars_rebuke",
        "unique.gloamwardens_step",
        "unique.starfall_echo",
    };

    /// <summary>The legendary each Flamebearer drops; boss-owned, so nothing sells them.</summary>
    public static readonly IReadOnlyList<string> BossSignatureIds = new[]
    {
        "item.armor.hollow_diadem",
        "item.ring.unmakers_signet",
        "item.weapon.ashen_oath",
        "item.weapon.crownbreaker",
        "item.weapon.packlords_talon",
        "item.weapon.prophets_pyre",
        "item.weapon.stormcleaver",
    };
    // --- END generated ---
}
