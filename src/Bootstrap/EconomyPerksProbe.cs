using System;
using System.Collections.Generic;
using Embervale.Crafting;
using Embervale.Items;
using Embervale.Player;
using Embervale.Progression;
using Godot;

namespace Embervale.Bootstrap;

/// <summary>
/// The economy half of the perks probe in <c>--lifecycle</c>: the authored social, crafter and rogue perks
/// reach <see cref="PerkQuery"/> through the real <c>.tres</c> parse; a perked XP grant, a perked craft and
/// the saved craft serial behave on the live components; and an empty-save <c>Load</c> zeroes the serial.
/// Everything it touches is restored from a snapshot. Unit tests cannot reach this: the components are
/// Godot nodes.
/// </summary>
internal static class EconomyPerksProbe
{
    private static readonly string[] PerkIds =
    {
        "perk.silver_tongue", "perk.road_wise", "perk.fair_dealer", "perk.appraiser", "perk.quick_study",
        "perk.thrifty_hands", "perk.salvager", "perk.reclaimer", "perk.master_artisan", "perk.cutpurse", "perk.fortune",
    };

    public static void Verify(PlayerCharacter player, PerksComponent perks, Action<bool, string> check)
    {
        foreach (string id in PerkIds)
        {
            if (PerkDatabase.Get(id) is not { } perk || !perks.GrantFree(perk))
            {
                check(false, $"economy perks probe: perk '{id}' is unauthored or could not be granted.");
                perks.Load(new Godot.Collections.Dictionary());
                return;
            }

            while (perks.GrantFree(perk))
            {
            }
        }

        float haggle = PerkQuery.Of(player, PerkEffectKind.HaggleChanceBonus);
        check(Near(haggle, 15f), "economy perks probe: silver tongue did not give +15 haggle.");
        check(PerkEffectMath.HaggleChance(30, haggle) == 45 && PerkEffectMath.HaggleChance(0, haggle) == 0,
            "economy perks probe: a haggle perk changed a 30 chance wrongly or opened a 0 chance.");
        check(Near(PerkEffectMath.BuyFactor(PerkQuery.Of(player, PerkEffectKind.BuyDiscount)), 0.97f),
            "economy perks probe: fair dealer did not give 0.97 buy.");
        check(Near(PerkEffectMath.SellFactor(PerkQuery.Of(player, PerkEffectKind.SellBonus)), 1.05f),
            "economy perks probe: appraiser and cutpurse did not stack to 1.05 sell.");
        check(Near(PerkEffectMath.ServiceFactor(PerkQuery.Of(player, PerkEffectKind.ServicePriceMult)), 0.96f),
            "economy perks probe: road wise did not give 0.96 service.");
        check(Near(PerkQuery.Of(player, PerkEffectKind.XpGainMult), 0.06f), "economy perks probe: quick study did not give +6% xp.");
        check(PerkEffectMath.SaveChancePercent(PerkQuery.Of(player, PerkEffectKind.MaterialSaveChance)) == 20,
            "economy perks probe: thrifty hands and master artisan did not give 20%.");
        check(Near(PerkQuery.Of(player, PerkEffectKind.SalvageYieldBonus), 0.25f),
            "economy perks probe: salvager and reclaimer did not give +25% salvage.");
        check(Near(PerkQuery.Of(player, PerkEffectKind.LootQuality), 0.15f), "economy perks probe: fortune did not give +0.15 loot quality.");

        VerifyXp(player, check);
        VerifyCraft(player, check);

        perks.Load(new Godot.Collections.Dictionary());
        check(PerkEffectMath.ScaleXp(10, PerkQuery.Of(player, PerkEffectKind.XpGainMult)) == 10 && perks.Effects.IsEmpty,
            "economy perks probe: Load of an empty save kept economy perk effects.");
    }

    /// <summary>A grant of 10 xp lands as 11 with +6%, through the real <c>AddXp</c>.</summary>
    private static void VerifyXp(PlayerCharacter player, Action<bool, string> check)
    {
        if (player.GetComponent<ProgressionComponent>() is not { } progression)
        {
            check(false, "economy perks probe: the player has no progression.");
            return;
        }

        Godot.Collections.Dictionary snapshot = progression.Save();
        int level = progression.Level;
        int before = progression.CurrentXp;
        progression.AddXp(10);
        check(progression.Level != level || progression.CurrentXp - before == 11,
            $"economy perks probe: a 10 xp grant with +6% landed as {progression.CurrentXp - before}, not 11.");
        progression.Load(snapshot);
    }

    /// <summary>A craft on a serial the roll saves hands one unit of the largest ingredient back; on a serial it
    /// does not, nothing comes back; the serial counts crafts, is saved, and an empty save zeroes it.</summary>
    private static void VerifyCraft(PlayerCharacter player, Action<bool, string> check)
    {
        if (player.GetComponent<CraftingComponent>() is not { } crafting
            || player.GetComponent<InventoryComponent>() is not { } pack)
        {
            check(false, "economy perks probe: the player has no crafting or inventory.");
            return;
        }

        CraftingRecipeResource? recipe = FindRecipe(out string materialId);
        if (recipe == null)
        {
            check(false, "economy perks probe: no authored recipe has a plain output and an ingredient of 2 or more.");
            return;
        }

        int saving = -1;
        int sparing = -1;
        for (int serial = 0; serial < 1000 && (saving < 0 || sparing < 0); serial++)
        {
            if (MaterialSaving.Saves(serial, recipe.Id, 20))
            {
                saving = saving < 0 ? serial : saving;
            }
            else
            {
                sparing = sparing < 0 ? serial : sparing;
            }
        }

        check(saving >= 0 && sparing >= 0, "economy perks probe: the 20% roll never saved, or always did, in 1000 serials.");
        if (saving < 0 || sparing < 0)
        {
            return;
        }

        Godot.Collections.Dictionary packSnapshot = pack.Save();
        Godot.Collections.Dictionary craftSnapshot = crafting.Save();
        crafting.Learn(recipe.Id);

        int left = CraftOnce(crafting, pack, recipe, materialId, saving);
        check(left == 1, $"economy perks probe: a saving craft left {left} unit(s) of '{materialId}', not 1.");
        check(crafting.Save()["crafts"].AsInt32() == saving + 1, "economy perks probe: a craft did not advance the saved serial.");

        left = CraftOnce(crafting, pack, recipe, materialId, sparing);
        check(left == 0, $"economy perks probe: a non-saving craft left {left} unit(s) of '{materialId}', not 0.");

        // Replace semantics: a save with no serial must zero a live one, not keep it.
        crafting.Load(new Godot.Collections.Dictionary { ["crafts"] = 7 });
        check(crafting.Save()["crafts"].AsInt32() == 7, "economy perks probe: a saved craft serial did not load.");
        crafting.Load(new Godot.Collections.Dictionary());
        check(crafting.Save()["crafts"].AsInt32() == 0, "economy perks probe: Load of an empty save kept the live craft serial.");

        crafting.Load(craftSnapshot);
        pack.Load(packSnapshot);
    }

    /// <summary>Crafts once on <paramref name="serial"/> from a clean slate of the recipe's materials and
    /// returns how many units of <paramref name="materialId"/> are left (the output is removed again), or -1
    /// when the craft was refused.</summary>
    private static int CraftOnce(
        CraftingComponent crafting, InventoryComponent pack, CraftingRecipeResource recipe, string materialId, int serial)
    {
        foreach (RecipeIngredient ingredient in recipe.IngredientList())
        {
            int held = pack.CountOf(ingredient.ItemId);
            if (held > 0)
            {
                pack.RemoveItem(ingredient.ItemId, held);
            }

            if (ItemDatabase.Get(ingredient.ItemId) is { } item)
            {
                pack.AddItem(item, ingredient.Quantity);
            }
        }

        crafting.Load(new Godot.Collections.Dictionary { ["crafts"] = serial, ["known"] = crafting.Save()["known"] });
        if (!crafting.Craft(recipe, recipe.Station))
        {
            return -1;
        }

        int left = pack.CountOf(materialId);
        pack.RemoveItem(recipe.OutputItemId, Math.Max(1, recipe.OutputQuantity));
        return left;
    }

    /// <summary>The first recipe with a plain (not rolled) output and an ingredient needing 2 or more; the
    /// material is the one <see cref="MaterialSaving.SavedIngredient"/> picks.</summary>
    private static CraftingRecipeResource? FindRecipe(out string materialId)
    {
        materialId = string.Empty;
        foreach (CraftingRecipeResource recipe in RecipeDatabase.All)
        {
            if (recipe.OutputRarity != ItemRarity.Common || ItemDatabase.Get(recipe.OutputItemId) is null)
            {
                continue;
            }

            List<RecipeIngredient> ingredients = recipe.IngredientList();
            var quantities = new List<int>();
            foreach (RecipeIngredient ingredient in ingredients)
            {
                quantities.Add(ingredient.Quantity);
            }

            int index = MaterialSaving.SavedIngredient(quantities);
            if (index >= 0)
            {
                materialId = ingredients[index].ItemId;
                return recipe;
            }
        }

        return null;
    }

    private static bool Near(float a, float b) => Math.Abs(a - b) < 0.001f;
}
