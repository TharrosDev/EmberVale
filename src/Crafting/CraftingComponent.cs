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

    /// <summary>
    /// Everything a craft needs except the materials: the recipe exists, the entity knows it, the
    /// station will take it, and its output is a real item. Split out for 38Q — a master's commission
    /// asks exactly this and then <em>sells</em> the missing half, so the two questions had to stop
    /// being one.
    /// </summary>
    public bool CanMake(CraftingRecipeResource? recipe, CraftingStationType station)
    {
        return recipe != null
            && Knows(recipe.Id)
            && StationAccepts(recipe.Station, station)
            && ItemDatabase.Get(recipe.OutputItemId) != null;
    }

    /// <summary>Whether the recipe can be crafted right now at the given station.</summary>
    public bool CanCraft(CraftingRecipeResource? recipe, CraftingStationType station) =>
        CanMake(recipe, station) && HasIngredients(recipe!);

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
    /// Has a master make <paramref name="recipe"/> for <paramref name="totalPrice"/> (Phase 38Q): he
    /// supplies every ingredient the pack is short of, then crafts it as normal.
    ///
    /// ⚠️ <b>THE GOLD IS TAKEN LAST, INVERTING THE HOUSE RULE, AND FOR THE REASON THE HOUSE RULE
    /// EXISTS.</b> <c>ServiceComponent</c> charges before every other verb because none of them can
    /// fail once paid — a bed, a flag, a taught recipe. This one fails whenever the pack has no room
    /// for the piece, and <see cref="Craft"/> already rolls itself back cleanly when it does. Charging
    /// first would therefore be the only way in the whole battery to lose the money for nothing.
    ///
    /// ⚠️ <b><paramref name="totalPrice"/> is passed in rather than computed here</b>, and that is
    /// deliberate: the window quotes a number and this charges one, and they must be the same number.
    /// It comes from <c>EconomyReport.CommissionCost</c>, which is also what <c>--validate</c> proves
    /// no output can be sold for more than.
    /// </summary>
    public bool Commission(CraftingRecipeResource? recipe, CraftingStationType station, int totalPrice)
    {
        if (recipe == null || _inventory == null || !CanMake(recipe, station))
        {
            return false;
        }

        if (totalPrice > 0 && _inventory.CountOf(GameIds.Currency.Gold) < totalPrice)
        {
            return false; // the prompt and the button have both already said so
        }

        // The materials he supplies go into the pack so that Craft consumes them the ordinary way.
        // Handing over a finished piece without them would be a second crafting path, and the two
        // would drift the first time a recipe grew an ingredient.
        var supplied = new List<(string ItemId, int Quantity)>();
        foreach (RecipeIngredient ingredient in recipe.IngredientList())
        {
            int missing = ingredient.Quantity - _inventory.CountOf(ingredient.ItemId);
            if (missing <= 0)
            {
                continue;
            }

            if (ItemDatabase.Get(ingredient.ItemId) is not { } material ||
                _inventory.AddItem(material, missing) < missing)
            {
                // A pack too full to hold the materials is refused whole. Anything already handed over
                // goes back first: leaving it would be a free half-order, which is the mirror of the
                // bug Craft's own rollback exists to prevent.
                Return(supplied);
                Log.Warn($"Commission '{recipe.Id}': no room for the materials; the order was refused.");
                return false;
            }

            supplied.Add((ingredient.ItemId, missing));
        }

        if (!Craft(recipe, station))
        {
            Return(supplied);
            return false; // Craft has already returned the player's own ingredients
        }

        if (totalPrice > 0)
        {
            _inventory.RemoveItem(GameIds.Currency.Gold, totalPrice);
        }

        return true;
    }

    /// <summary>Takes back materials the master supplied for an order that did not complete.</summary>
    private void Return(List<(string ItemId, int Quantity)> supplied)
    {
        foreach ((string itemId, int quantity) in supplied)
        {
            _inventory!.RemoveItem(itemId, quantity);
        }
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
                if (ItemDatabase.Get(ingredient.ItemId) is { } material &&
                    _inventory.AddItem(material, recovered) < recovered)
                {
                    // Salvage is irreversible by design — the item is already gone — so a full pack
                    // cannot be refused the way a craft can. It can at least stop being silent:
                    // freeing the item's slot makes room for one material type, so a recipe
                    // recovering two can overflow by one and drop it without a word.
                    Log.Warn($"Deconstruct: no room for all the recovered '{material.Id}'; some was lost.");
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
