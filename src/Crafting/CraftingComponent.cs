using System.Collections.Generic;
using Embervale.Core.Diagnostics;
using Embervale.Core.Events;
using Embervale.Entities;
using Embervale.Items;
using Embervale.Loot;
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

    public string SaveId => SaveKey("crafting");

    public IReadOnlyCollection<string> KnownRecipes => _known;

    protected override void OnInitialize()
    {
        _inventory = Entity!.GetComponent<InventoryComponent>();

        foreach (string id in StartingRecipeIds)
        {
            if (RecipeDatabase.Get(id) != null)
            {
                _known.Add(id);
            }
        }

        SaveManager.Instance?.Register(this);
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

        // Ingredients are pre-validated by CanCraft, so these removals all succeed.
        foreach (RecipeIngredient ingredient in recipe.IngredientList())
        {
            _inventory.RemoveItem(ingredient.ItemId, ingredient.Quantity);
        }

        ItemResource? template = ItemDatabase.Get(recipe.OutputItemId);
        if (template == null)
        {
            // CanCraft already guards this, but never force-deref a content lookup: a
            // recipe whose output item was deleted must fail cleanly, not crash mid-craft.
            Log.Warn($"Recipe '{recipe.Id}' output item '{recipe.OutputItemId}' is missing; craft aborted.");
            return false;
        }

        int quantity = Mathf.Max(1, recipe.OutputQuantity);

        if (template is EquippableItemResource equippable && recipe.OutputRarity != ItemRarity.Common)
        {
            // Crafted gear rolls affixes; each piece is unique, so add them individually.
            for (int i = 0; i < quantity; i++)
            {
                _inventory.AddInstance(LootGenerator.RollAffixed(equippable, recipe.OutputRarity), 1);
            }
        }
        else
        {
            _inventory.AddInstance(ItemInstance.Plain(template), quantity);
        }

        if (Entity != null)
        {
            EventBus.Instance?.Publish(new ItemCraftedEvent(Entity, recipe.Id, recipe.OutputItemId, quantity));
        }

        return true;
    }

    private static bool StationAccepts(CraftingStationType required, CraftingStationType open)
    {
        return required == CraftingStationType.Hand || required == open;
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
