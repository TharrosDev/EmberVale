using System.Collections.Generic;
using Embervale.Core;
using Embervale.Core.Diagnostics;
using Embervale.Core.Events;
using Embervale.Entities;
using Embervale.Items;
using Embervale.Loot;
using Embervale.Progression;
using Embervale.Save;
using Godot;

namespace Embervale.Crafting;

/// <summary>
/// The crafting brain for an entity (the player): the recipes it knows and the act of
/// crafting — validate the station + ingredients, consume the inputs from the sibling
/// <see cref="InventoryComponent"/>, and add the output. Equippable outputs flagged with
/// a rarity roll affixes through the <see cref="LootGenerator"/>, so smithing produces
/// gear in the same system as drops.
///
/// Known recipes persist via <see cref="ISaveable"/> (a learnable set, seeded from
/// <see cref="StartingRecipeIds"/>); recipes themselves live in the
/// <see cref="RecipeDatabase"/>.
/// </summary>
[GlobalClass]
public partial class CraftingComponent : EntityComponent, ISaveable
{
    /// <summary>Recipe ids the entity starts knowing (authored by the factory/scene).</summary>
    [Export]
    public Godot.Collections.Array<string> StartingRecipeIds { get; set; } = new();

    private readonly HashSet<string> _known = new();

    private InventoryComponent? _inventory;
    private EquipmentComponent? _equipment;

    public string SaveId => SaveKey("crafting");

    public IReadOnlyCollection<string> KnownRecipes => _known;

    protected override void OnInitialize()
    {
        _inventory = Entity!.GetComponent<InventoryComponent>();
        _equipment = Entity.GetComponent<EquipmentComponent>();

        foreach (string id in StartingRecipeIds)
        {
            if (RecipeDatabase.Get(id) != null)
            {
                _known.Add(id);
            }
        }

        RegisterSaveable();
    }

    protected override void OnTeardown()
    {
        SaveManager.Instance?.Unregister(this);
    }

    public bool Knows(string recipeId) => _known.Contains(recipeId);

    /// <summary>Teaches a new recipe (e.g. from a recipe book or trainer).</summary>
    public bool Learn(string recipeId)
    {
        if (RecipeDatabase.Get(recipeId) == null || !_known.Add(recipeId))
        {
            return false;
        }

        if (Entity != null)
        {
            EventBus.Instance?.Publish(new RecipeLearnedEvent(Entity, recipeId));
        }

        return true;
    }

    /// <summary>True if the inventory holds every ingredient in the required amount.</summary>
    public bool HasIngredients(CraftingRecipeResource recipe)
    {
        if (_inventory == null)
        {
            return false;
        }

        foreach (RecipeIngredient ingredient in recipe.IngredientList())
        {
            if (_inventory.CountOf(ingredient.ItemId) < ingredient.Quantity)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Whether the recipe can be crafted right now at the given station.</summary>
    public bool CanCraft(CraftingRecipeResource? recipe, CraftingStationType station)
    {
        return recipe != null
            && Knows(recipe.Id)
            && StationAccepts(recipe.Station, station)
            && ItemDatabase.Get(recipe.OutputItemId) != null
            && HasIngredients(recipe);
    }

    /// <summary>Crafts the recipe: consumes ingredients and adds the output. Returns false
    /// if it isn't currently craftable.</summary>
    public bool Craft(CraftingRecipeResource? recipe, CraftingStationType station)
    {
        if (recipe == null || _inventory == null || !CanCraft(recipe, station))
        {
            return false;
        }

        // Resolved BEFORE anything is consumed. CanCraft already guards it, but this branch used to
        // sit after the removals, so the one path written to "fail cleanly" was the one that ate the
        // player's materials and handed back nothing.
        ItemResource? template = ItemDatabase.Get(recipe.OutputItemId);
        if (template == null)
        {
            Log.Warn($"Recipe '{recipe.Id}' output item '{recipe.OutputItemId}' is missing; craft aborted.");
            return false;
        }

        int quantity = Mathf.Max(1, recipe.OutputQuantity);

        // Ingredients are pre-validated by CanCraft, so these removals all succeed.
        foreach (RecipeIngredient ingredient in recipe.IngredientList())
        {
            _inventory.RemoveItem(ingredient.ItemId, ingredient.Quantity);
        }

        // AddInstance returns what actually fit, and a full pack can take none of it. Consuming
        // first and adding second therefore destroyed the ingredients and dropped the output in
        // silence — crafting was the one place in the codebase that did not honour the rule
        // StoragePanel.Transfer and PlacementDirector.Remove both state outright: a full pack must
        // never be a reason something evaporates. Anything short of the whole output is rolled back.
        var placed = new List<ItemInstance>();
        if (template is EquippableItemResource equippable && recipe.OutputRarity != ItemRarity.Common)
        {
            // Crafted gear rolls affixes; each piece is unique, so add them individually.
            for (int i = 0; i < quantity; i++)
            {
                ItemInstance rolled = LootGenerator.RollAffixed(equippable, recipe.OutputRarity);
                if (_inventory.AddInstance(rolled, 1) < 1)
                {
                    break;
                }

                placed.Add(rolled);
            }
        }
        else
        {
            ItemInstance plain = ItemInstance.Plain(template);
            for (int i = _inventory.AddInstance(plain, quantity); i > 0; i--)
            {
                placed.Add(plain);
            }
        }

        if (placed.Count < quantity)
        {
            Rollback(recipe, placed, plain: template);
            Log.Warn($"Recipe '{recipe.Id}': no room for the output; the craft was refused and the ingredients returned.");
            return false;
        }

        if (Entity != null)
        {
            EventBus.Instance?.Publish(new ItemCraftedEvent(Entity, recipe.Id, recipe.OutputItemId, quantity));
        }

        return true;
    }

    /// <summary>
    /// Undoes a partial craft: pulls back whatever output did land and returns the ingredients.
    /// The refund always fits — the ingredients were in this same pack a moment ago, and removing
    /// them freed at least as much room as putting them back needs.
    /// </summary>
    private void Rollback(CraftingRecipeResource recipe, List<ItemInstance> placed, ItemResource plain)
    {
        foreach (ItemInstance instance in placed)
        {
            // Rolled pieces are unique, so they leave by reference; a stackable output leaves by id.
            // The same split StoragePanel.Transfer documents, and for the same reason.
            if (instance.IsStackable)
            {
                _inventory!.RemoveItem(plain.Id, 1);
            }
            else
            {
                _inventory!.RemoveOneInstance(instance);
            }
        }

        foreach (RecipeIngredient ingredient in recipe.IngredientList())
        {
            if (ItemDatabase.Get(ingredient.ItemId) is { } item)
            {
                _inventory!.AddItem(item, ingredient.Quantity);
            }
        }
    }

    /// <summary>Whether a recipe authored for the <paramref name="required"/> station can be crafted at
    /// the currently <paramref name="open"/> station: hand recipes craft anywhere, otherwise the station
    /// must match exactly. Pure and side-effect-free (exposed for unit coverage of the station gate).</summary>
    public static bool StationAccepts(CraftingStationType required, CraftingStationType open)
    {
        return required == CraftingStationType.Hand || required == open;
    }

    // --- Deconstruction (the inverse of crafting) ---------------------------

    /// <summary>The station recipe whose output is <paramref name="itemId"/>, if one exists at the
    /// open station — the "blueprint" deconstruction reverses to salvage the item. <c>Hand</c> recipes
    /// don't deconstruct (there's no station to do it at).</summary>
    public CraftingRecipeResource? DeconstructionRecipe(string itemId, CraftingStationType station)
    {
        if (string.IsNullOrEmpty(itemId))
        {
            return null;
        }

        foreach (CraftingRecipeResource recipe in RecipeDatabase.All)
        {
            if (recipe.OutputItemId == itemId && recipe.Station != CraftingStationType.Hand &&
                StationAccepts(recipe.Station, station))
            {
                return recipe;
            }
        }

        return null;
    }

    /// <summary>Whether <paramref name="instance"/> can be salvaged — only gear/equipment (weapons,
    /// armor, accessories). Materials, consumables and the like aren't salvageable. A recipe isn't
    /// required: recipe-less gear salvages into generic scrap.</summary>
    public bool CanDeconstruct(ItemInstance? instance, CraftingStationType station)
    {
        if (instance is not { IsEquippable: true })
        {
            return false;
        }

        return (_inventory != null && _inventory.CountOf(instance.TemplateId) > 0)
            || (_equipment != null && _equipment.IsInstanceEquipped(instance));
    }

    /// <summary>Deconstructs one of <paramref name="instance"/>: consumes it and returns its recipe's
    /// materials (a floored fraction) — or, for a recipe-less item, generic scrap — plus XP. Returns
    /// false only for currency or an item the player doesn't actually hold.</summary>
    public bool Deconstruct(ItemInstance? instance, CraftingStationType station)
    {
        if (instance is not { IsEquippable: true } || _inventory == null)
        {
            return false;
        }

        CraftingRecipeResource? recipe = DeconstructionRecipe(instance.TemplateId, station);

        // Salvaging equipped gear takes it off first (back into the inventory) so the consume below
        // is uniform — and its stat bonuses are cleanly removed by the unequip.
        if (_inventory.RemoveOneInstance(instance) == null)
        {
            if (_equipment == null || !_equipment.UnequipInstance(instance) ||
                _inventory.RemoveOneInstance(instance) == null)
            {
                return false;
            }
        }

        if (recipe != null)
        {
            foreach (RecipeIngredient ingredient in recipe.IngredientList())
            {
                int recovered = Deconstruction.RecoveredQuantity(ingredient.Quantity);
                if (recovered <= 0)
                {
                    continue;
                }

                // Never force-deref a content lookup: a recipe whose ingredient item was deleted skips
                // that material rather than crashing the salvage.
                if (ItemDatabase.Get(ingredient.ItemId) is { } material)
                {
                    _inventory.AddItem(material, recovered);
                }
            }
        }
        else
        {
            // No recipe to reverse — return generic scrap so any item is still worth salvaging.
            int scrap = Deconstruction.ScrapYield(instance.Rarity);
            if (scrap > 0 && ItemDatabase.Get(GameIds.Items.Scrap) is { } scrapItem)
            {
                _inventory.AddItem(scrapItem, scrap);
            }
        }

        int xp = Deconstruction.Xp(instance.Template.Value, instance.Rarity);
        Entity?.GetComponent<ProgressionComponent>()?.AddXp(xp);

        if (Entity != null)
        {
            EventBus.Instance?.Publish(new ItemDeconstructedEvent(Entity, instance.TemplateId, xp));
        }

        return true;
    }

    // --- ISaveable ----------------------------------------------------------

    public Godot.Collections.Dictionary Save()
    {
        var known = new Godot.Collections.Array();
        foreach (string id in _known)
        {
            known.Add(id);
        }

        return new Godot.Collections.Dictionary { ["known"] = known };
    }

    public void Load(Godot.Collections.Dictionary data)
    {
        if (!data.TryGetValue("known", out Variant knownVar))
        {
            return;
        }

        _known.Clear();
        foreach (Variant id in knownVar.AsGodotArray())
        {
            string recipeId = id.AsString();
            if (RecipeDatabase.Get(recipeId) != null)
            {
                _known.Add(recipeId);
            }
        }
    }
}
