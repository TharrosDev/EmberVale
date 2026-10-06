using System;
using System.Collections.Generic;
using Embervale.Core;
using Embervale.Crafting;
using Embervale.Economy;
using Embervale.Items;
using Embervale.UI;

namespace Embervale.Debugging;

/// <summary>
/// <see cref="ItemValidator"/>: recipe, station, quality and upgrade rules. Owned by the crafting
/// lane alone; its rules go here and nowhere else. <c>RequireKey</c> in <c>ItemValidator.cs</c> is
/// shared.
///
/// <c>ContentValidator</c> already proves that a recipe's items exist, that every recipe is seeded
/// or taught (never both), and that no commission pays for itself; none of that is repeated. What
/// is checked here is what those rules cannot see: a recipe's own shape, the new tier and scroll
/// fields, materials that lead nowhere, the reforge price table, and the locale keys the crafting
/// window chooses at runtime.
/// </summary>
public static partial class ItemValidator
{
    private static void CollectCrafting(List<string> issues)
    {
        CollectRecipeShapes(issues);
        CollectMaterialSinks(issues);
        CollectReforgeTable(issues);
        CollectCraftingKeys(issues);
    }

    /// <summary>Quantities, output rarity, tier and the scroll link of every recipe.</summary>
    private static void CollectRecipeShapes(List<string> issues)
    {
        var seeded = new HashSet<string>(GameIds.Recipes.Starting);
        var scrolls = new Dictionary<string, string>();

        foreach (CraftingRecipeResource recipe in RecipeDatabase.All)
        {
            string what = $"recipe '{recipe.Id}'";

            if (recipe.OutputQuantity <= 0)
            {
                issues.Add($"{what} makes {recipe.OutputQuantity} of its output; a craft must make at least one");
            }

            // IngredientList drops a row with no item or a quantity under one, so a typo there is a
            // recipe that silently asks for less than its author wrote. Compare against the raw rows.
            List<RecipeIngredient> ingredients = recipe.IngredientList();
            if (ingredients.Count != recipe.Ingredients.Count)
            {
                issues.Add(
                    $"{what} authors {recipe.Ingredients.Count} ingredient row(s) but only {ingredients.Count} are " +
                    "usable; every row needs an item id and a quantity above zero");
            }

            if (ingredients.Count == 0)
            {
                issues.Add($"{what} has no ingredients, so it would make its output from nothing");
            }

            var seenIngredients = new HashSet<string>();
            foreach (RecipeIngredient ingredient in ingredients)
            {
                if (!seenIngredients.Add(ingredient.ItemId))
                {
                    issues.Add(
                        $"{what} lists ingredient '{ingredient.ItemId}' twice; the have/need check counts each " +
                        "row alone, so merge them into one row");
                }

                if (ingredient.ItemId == recipe.OutputItemId)
                {
                    issues.Add($"{what} consumes its own output '{recipe.OutputItemId}'");
                }
            }

            if (recipe.Tier < 0 || recipe.Tier > CraftingSkill.MaxTier)
            {
                issues.Add($"{what} has Tier {recipe.Tier}; it must be 0 (legacy) to {CraftingSkill.MaxTier}");
            }

            CollectOutputRarity(recipe, what, issues);

            if (recipe.ScrollItemId.Length > 0)
            {
                if (ItemDatabase.Get(recipe.ScrollItemId) == null)
                {
                    issues.Add($"{what} names scroll '{recipe.ScrollItemId}', which is not an item");
                }

                if (scrolls.TryGetValue(recipe.ScrollItemId, out string? other))
                {
                    issues.Add(
                        $"{what} and recipe '{other}' both name scroll '{recipe.ScrollItemId}'; a scroll " +
                        "teaches exactly one recipe");
                }
                else
                {
                    scrolls[recipe.ScrollItemId] = recipe.Id;
                }

                if (seeded.Contains(recipe.Id))
                {
                    issues.Add(
                        $"{what} is a starting recipe and also names scroll '{recipe.ScrollItemId}'; every " +
                        "player already knows it, so the scroll could never be used");
                }
            }
        }
    }

    /// <summary>
    /// A recipe's <see cref="CraftingRecipeResource.OutputRarity"/> is only read for a piece of
    /// gear that does not stack; anywhere else it colours the recipe card for a rarity the craft
    /// will not produce. The forge never makes a Legendary, and a tiered recipe never rolls below
    /// its output's own rarity (the legacy recipes predate that rule and are left alone).
    /// </summary>
    private static void CollectOutputRarity(CraftingRecipeResource recipe, string what, List<string> issues)
    {
        if (!Enum.IsDefined(recipe.OutputRarity))
        {
            issues.Add($"{what} has OutputRarity {(int)recipe.OutputRarity}, which is not an item rarity");
            return;
        }

        if (recipe.OutputRarity == ItemRarity.Legendary)
        {
            issues.Add($"{what} crafts at Legendary; legendaries are found, never made");
        }

        if (ItemDatabase.Get(recipe.OutputItemId) is not { } output)
        {
            return; // ContentValidator.ValidateRecipes already reports the missing output
        }

        bool rollsRarity = output is EquippableItemResource && !output.IsStackable;
        if (!rollsRarity && recipe.OutputRarity != ItemRarity.Common)
        {
            issues.Add(
                $"{what} sets OutputRarity {recipe.OutputRarity} on '{output.Id}', which is not a piece of " +
                "unstackable gear; the rarity would be shown and never rolled, so it must be Common");
        }

        if (rollsRarity && recipe.Tier > 0 && recipe.OutputRarity < output.Rarity)
        {
            issues.Add(
                $"{what} crafts '{output.Id}' at {recipe.OutputRarity}, below the item's own rarity " +
                $"{output.Rarity}");
        }

        if (recipe.Tier > 0 && output.Tier > 0 && recipe.Tier != output.Tier)
        {
            issues.Add(
                $"{what} is tier {recipe.Tier} but its output '{output.Id}' is tier {output.Tier}; the " +
                "crafting rank it asks for would not match what it makes");
        }
    }

    /// <summary>
    /// Every crafting material goes somewhere: into a recipe, or over a counter. A material that is
    /// in no recipe and that no merchant will take is dead weight the player can only drop.
    /// </summary>
    private static void CollectMaterialSinks(List<string> issues)
    {
        var used = new HashSet<string>();
        foreach (CraftingRecipeResource recipe in RecipeDatabase.All)
        {
            foreach (RecipeIngredient ingredient in recipe.IngredientList())
            {
                used.Add(ingredient.ItemId);
            }
        }

        foreach (ItemResource item in ItemDatabase.All.Values)
        {
            if (item.Type != ItemType.Material || used.Contains(item.Id))
            {
                continue;
            }

            List<string> tags = item.TagList();
            bool sold = false;
            foreach (ShopResource shop in ShopDatabase.All)
            {
                if (TradeTags.Accepts(tags, shop.AcceptedTagList()))
                {
                    sold = true;
                    break;
                }
            }

            if (!sold)
            {
                issues.Add(
                    $"material '{item.Id}' is an ingredient of no recipe and no shop accepts its trade tags " +
                    $"({string.Join(", ", tags)}); give it a recipe or a buyer");
            }
        }
    }

    /// <summary>
    /// The reforge price table: fees and material counts never fall as a piece is worked further,
    /// no fee can be earned back by selling the result, every tier's ingot exists, and the pure
    /// value formula the rules are proved against still matches a real item's value.
    /// </summary>
    private static void CollectReforgeTable(List<string> issues)
    {
        int[] values = { 1, 10, 50, 200, 1000, 5000, 20000 };
        foreach (int value in values)
        {
            for (int prior = 0; prior <= ReforgeRules.MaxRerollSteps + 2; prior++)
            {
                if (ReforgeRules.RerollGold(value, prior) <= 0 || ReforgeRules.RerollMaterials(prior) <= 0)
                {
                    issues.Add($"reforge: reroll {prior + 1} of an item worth {value} is free");
                }

                if (ReforgeRules.RerollGold(value, prior + 1) < ReforgeRules.RerollGold(value, prior) ||
                    ReforgeRules.RerollMaterials(prior + 1) < ReforgeRules.RerollMaterials(prior))
                {
                    issues.Add($"reforge: reroll {prior + 2} of an item worth {value} costs less than reroll {prior + 1}");
                }
            }

            int previousGold = 0;
            for (int level = 0; level < ItemUpgrades.MaxLevel; level++)
            {
                int before = ReforgeRules.InstanceValue(value, 0, ItemRarity.Common, CraftQuality.Standard, level);
                int after = ReforgeRules.InstanceValue(value, 0, ItemRarity.Common, CraftQuality.Standard, level + 1);
                int gold = ReforgeRules.UpgradeGold(before, after, level + 1);
                if (gold <= previousGold)
                {
                    issues.Add($"reforge: upgrade level {level + 1} of an item worth {value} costs {gold}, not more than level {level}'s {previousGold}");
                }

                if (ReforgeRules.Exploitable(gold, before, after))
                {
                    issues.Add($"reforge: upgrade level {level + 1} of an item worth {value} costs {gold} and adds {after - before} value; it could be sold at a profit");
                }

                if (level > 0 && ReforgeRules.UpgradeMaterials(level + 1) < ReforgeRules.UpgradeMaterials(level))
                {
                    issues.Add($"reforge: upgrade level {level + 1} asks for less metal than level {level}");
                }

                previousGold = gold;
            }

            foreach (ItemRarity rarity in new[] { ItemRarity.Uncommon, ItemRarity.Rare })
            {
                int affixes = (int)rarity;
                int before = ReforgeRules.InstanceValue(value, affixes, rarity, CraftQuality.Standard, 0);
                int after = ReforgeRules.InstanceValue(value, affixes + 1, rarity + 1, CraftQuality.Standard, 0);
                int gold = ReforgeRules.PromoteGold(before, after);
                if (ReforgeRules.Exploitable(gold, before, after))
                {
                    issues.Add($"reforge: promoting a {rarity} item worth {value} costs {gold} and adds {after - before} value; it could be sold at a profit");
                }
            }
        }

        if (ReforgeRules.PromoteMaterials(ItemRarity.Rare) < ReforgeRules.PromoteMaterials(ItemRarity.Uncommon))
        {
            issues.Add("reforge: promoting a Rare item asks for less metal than promoting an Uncommon one");
        }

        if (ItemUpgrades.StatMultiplier(ItemUpgrades.MaxLevel) <= ItemUpgrades.StatMultiplier(0) ||
            CraftQualities.StatMultiplier(CraftQuality.Masterwork) <= CraftQualities.StatMultiplier(CraftQuality.Standard))
        {
            issues.Add("reforge: an upgrade level or a Masterwork piece is worth no more than a plain one");
        }

        for (int tier = 1; tier <= CraftingSkill.MaxTier; tier++)
        {
            string materialId = ReforgeRules.MaterialFor(tier);
            if (ItemDatabase.Get(materialId) == null)
            {
                issues.Add($"reforge: tier {tier} gear is reforged with '{materialId}', which is not an item");
            }
        }

        // ReforgeRules.InstanceValue restates ItemInstance.Value so the rules can be unit-tested
        // without an engine. Hold the two together on real items, at the workmanship extremes.
        foreach (ItemResource item in ItemDatabase.All.Values)
        {
            if (item is not EquippableItemResource || item.IsStackable)
            {
                continue;
            }

            foreach (CraftQuality quality in new[] { CraftQuality.Standard, CraftQuality.Masterwork })
            {
                var instance = new ItemInstance(item, ItemRarity.Rare)
                {
                    Quality = quality,
                    UpgradeLevel = ItemUpgrades.MaxLevel,
                };
                int expected = ReforgeRules.InstanceValue(item.Value, 0, ItemRarity.Rare, quality, ItemUpgrades.MaxLevel);
                if (instance.Value != expected)
                {
                    issues.Add(
                        $"reforge: ReforgeRules.InstanceValue says a {quality} '{item.Id}' is worth {expected} but " +
                        $"ItemInstance.Value says {instance.Value}; the two formulas have drifted");
                    return; // one drift is every item's drift; say it once
                }
            }
        }
    }

    /// <summary>The locale rows the crafting systems reach by a computed key.</summary>
    private static void CollectCraftingKeys(List<string> issues)
    {
        foreach (CraftingRecipeResource recipe in RecipeDatabase.All)
        {
            RequireKey(recipe.NameKey, "name key", $"recipe '{recipe.Id}'", issues);
        }

        for (int rank = 0; rank <= CraftingSkill.MaxRank; rank++)
        {
            RequireKey(CraftingSkill.RankNameKey(rank), "title", $"crafting rank {rank}", issues);
        }

        foreach (CraftQuality quality in Enum.GetValues<CraftQuality>())
        {
            RequireKey(CraftingPanel.QualityKey(quality), "name", $"workmanship '{quality}'", issues);
        }

        foreach (CraftingStationType station in Enum.GetValues<CraftingStationType>())
        {
            RequireKey(CraftingStations.LabelKey(station), "name", $"crafting station '{station}'", issues);
        }

        foreach (string key in CraftingComponent.BlockKeys)
        {
            RequireKey(key, "reason", "a refused reforge's", issues);
        }

        foreach (string key in CraftingComponent.LineKeys)
        {
            RequireKey(key, "line", "a reforge bill's", issues);
        }

        foreach (string key in CraftingPanel.CategoryKeys)
        {
            RequireKey(key, "name", "a crafting filter's", issues);
        }

        foreach (string key in CraftingPanel.ComputedKeys)
        {
            RequireKey(key, "text", "the crafting window's", issues);
        }
    }
}
