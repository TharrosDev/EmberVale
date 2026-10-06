using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Embervale.Core;
using Embervale.Core.Diagnostics;
using Embervale.Core.Events;
using Embervale.Economy;
using Embervale.Entities;
using Embervale.Items;
using Embervale.Loot;
using Embervale.Progression;
using Embervale.Save;
using Godot;

namespace Embervale.Crafting;

/// <summary>What salvaging one item returns: the materials, the experience, whether a recipe was
/// reversed (otherwise it is generic scrap), and the station that would reverse one when this
/// station cannot (<see cref="CraftingStationType.Hand"/> = none would).</summary>
public readonly record struct SalvagePlan(
    IReadOnlyList<(ItemResource Item, int Quantity)> Materials,
    int Xp,
    bool FromRecipe,
    CraftingStationType BetterStation);

/// <summary>What a reforge costs and whether it can be done. <see cref="BlockKey"/> is the locale
/// key of the reason it cannot (empty when it can).</summary>
public readonly record struct ReforgeQuote(
    int Gold, ItemResource? Material, int Materials, string BlockKey, IReadOnlyList<PriceLine> Lines)
{
    public bool Allowed => BlockKey.Length == 0;

    /// <summary>The gold as a <see cref="PriceQuote"/>, so the window renders the bill with the same
    /// <c>PriceTooltip</c> every other price in the game uses.</summary>
    public PriceQuote Bill => new(Lines, Gold, Gold);
}

/// <summary>
/// The crafting brain for an entity (the player): the recipes it knows, its crafting skill, and the
/// acts of crafting, salvaging and reforging. A craft validates the station, the skill and the
/// ingredients, consumes the inputs from the sibling <see cref="InventoryComponent"/> (pack and
/// material bag alike, through the inventory's own API) and adds the output. Equippable outputs
/// carry a workmanship <see cref="CraftQuality"/> derived from the saved skill
/// (<see cref="CraftingSkill"/>), and those flagged with a rarity roll affixes through the
/// <see cref="LootGenerator"/>, so smithing produces gear in the same system as drops.
///
/// Known recipes, the craft serial, the skill experience, the reforge serial, the per-item reroll
/// ledger and the pinned recipe persist via <see cref="ISaveable"/>; recipes themselves live in the
/// <see cref="RecipeDatabase"/>.
/// </summary>
[GlobalClass]
public partial class CraftingComponent : EntityComponent, ISaveable
{
    /// <summary>Save key: crafting experience (additive; absent = 0).</summary>
    public const string SkillXpKey = "skill_xp";

    /// <summary>Save key: completed reforges, the serial a reroll derives its outcome from.</summary>
    public const string ReforgesKey = "reforges";

    /// <summary>Save key: the reroll ledger, an array of <c>{fp, n}</c> rows.</summary>
    public const string RerollsKey = "rerolls";

    /// <summary>Save key: the pinned recipe id (absent = none).</summary>
    public const string PinnedKey = "pinned";

    /// <summary>The most one bulk order makes.</summary>
    public const int MaxBulk = 99;

    public const string BlockStation = "craft.reforge.block.station";
    public const string BlockGold = "craft.reforge.block.gold";
    public const string BlockMaterials = "craft.reforge.block.materials";
    public const string BlockMaxLevel = "craft.reforge.block.max_level";
    public const string BlockRarity = "craft.reforge.block.rarity";
    public const string BlockNoAffix = "craft.reforge.block.no_affix";
    public const string BlockNoPool = "craft.reforge.block.no_pool";
    public const string BlockNotHeld = "craft.reforge.block.not_held";

    /// <summary>Every reason a reforge can be refused, for <c>--validate</c> to require a locale row
    /// for (the keys are chosen at runtime, so no source scan can see them used).</summary>
    public static readonly IReadOnlyList<string> BlockKeys = new[]
    {
        BlockStation, BlockGold, BlockMaterials, BlockMaxLevel, BlockRarity, BlockNoAffix, BlockNoPool, BlockNotHeld,
    };

    public const string LineWork = "craft.reforge.line.work";
    public const string LineRerolls = "craft.reforge.line.rerolls";
    public const string LineGain = "craft.reforge.line.gain";

    /// <summary>The bill lines a reforge quote can carry (same reason as <see cref="BlockKeys"/>).</summary>
    public static readonly IReadOnlyList<string> LineKeys = new[] { LineWork, LineRerolls, LineGain };

    /// <summary>Items the reroll ledger remembers. Old rows fall off the front, so the ledger cannot
    /// grow with a long game; forgetting one only makes that item's next reroll cheaper.</summary>
    private const int RerollLedgerCap = 128;

    /// <summary>Recipe ids the entity starts knowing (authored by the factory/scene).</summary>
    [Export]
    public Godot.Collections.Array<string> StartingRecipeIds { get; set; } = new();

    private readonly HashSet<string> _known = new();

    /// <summary>How many times each item has had an affix rerolled, keyed by
    /// <see cref="Fingerprint"/>. Kept here, in this component's own save, so the rising price
    /// survives a reload without the item needing a field of its own.</summary>
    private readonly List<(string Fingerprint, int Count)> _rerolls = new();

    private InventoryComponent? _inventory;
    private EquipmentComponent? _equipment;

    /// <summary>Completed crafts so far: the serial <see cref="MaterialSaving"/> and
    /// <see cref="CraftingSkill.Roll"/> derive their rolls from. Saved, so a quickload replays a craft's
    /// outcome instead of rerolling it.</summary>
    private int _crafts;

    private int _skillXp;
    private int _reforges;
    private string _pinned = string.Empty;

    // Recipes learned since the last toast. A lesson teaches a whole list in one call, and one toast
    // per recipe would bury the screen, so they are counted here and announced once, deferred.
    private int _learnedPending;
    private string _learnedLast = string.Empty;

    public string SaveId => SaveKey("crafting");

    public IReadOnlyCollection<string> KnownRecipes => _known;

    /// <summary>Crafting experience earned so far.</summary>
    public int SkillXp => _skillXp;

    /// <summary>The crafting rank that experience buys (<see cref="CraftingSkill.RankOf"/>).</summary>
    public int SkillRank => CraftingSkill.RankOf(_skillXp);

    /// <summary>The piece the most recent successful craft produced (runtime only, never saved), so
    /// the window can say what workmanship came out.</summary>
    public ItemInstance? LastCrafted { get; private set; }

    /// <summary>The recipe the player pinned to the top of the crafting window, or empty.</summary>
    public string PinnedRecipeId => _pinned;

    protected override void OnInitialize()
    {
        _inventory = Entity!.GetComponent<InventoryComponent>();
        _equipment = Entity.GetComponent<EquipmentComponent>();
        SeedStartingRecipes();
        RegisterSaveable();
    }

    protected override void OnTeardown()
    {
        SaveManager.Instance?.Unregister(this);
    }

    private void SeedStartingRecipes()
    {
        foreach (string id in StartingRecipeIds)
        {
            if (RecipeDatabase.Get(id) != null)
            {
                _known.Add(id);
            }
        }
    }

    public bool Knows(string recipeId) => _known.Contains(recipeId);

    /// <summary>Pins <paramref name="recipeId"/> (or clears the pin with an empty id).</summary>
    public void SetPinned(string recipeId)
    {
        _pinned = RecipeDatabase.Get(recipeId) != null ? recipeId : string.Empty;
    }

    // --- Learning -----------------------------------------------------------

    /// <summary>Teaches a new recipe (from a trainer's lesson or a recipe scroll).</summary>
    public bool Learn(string recipeId)
    {
        if (RecipeDatabase.Get(recipeId) == null || !_known.Add(recipeId))
        {
            return false;
        }

        if (Entity != null)
        {
            EventBus.Instance?.Publish(new RecipeLearnedEvent(Entity, recipeId));

            _learnedLast = recipeId;
            if (_learnedPending++ == 0)
            {
                CallDeferred(MethodName.AnnounceLearned);
            }
        }

        return true;
    }

    /// <summary>One toast for everything learned this frame: the recipe's name for a single one, a
    /// plain "new recipes" line for a lesson's worth. A load never reaches here: it fills the set
    /// directly, because a load restores state and does not narrate one.</summary>
    private void AnnounceLearned()
    {
        int count = _learnedPending;
        string last = _learnedLast;
        _learnedPending = 0;
        _learnedLast = string.Empty;

        if (count == 1 && RecipeDatabase.Get(last) is { } recipe)
        {
            EventBus.Instance?.Publish(new Narrative.StoryToastRequestedEvent("craft.toast.recipe_learned", recipe.NameKey));
        }
        else if (count > 1)
        {
            EventBus.Instance?.Publish(new Narrative.StoryToastRequestedEvent(
                "craft.toast.recipes_learned", "craft.toast.recipes_learned_detail"));
        }
    }

    /// <summary>The recipe a recipe scroll teaches, or null when <paramref name="itemId"/> is not
    /// one.</summary>
    public static CraftingRecipeResource? ScrollRecipe(string itemId)
    {
        if (string.IsNullOrEmpty(itemId))
        {
            return null;
        }

        foreach (CraftingRecipeResource recipe in RecipeDatabase.All)
        {
            if (recipe.ScrollItemId == itemId)
            {
                return recipe;
            }
        }

        return null;
    }

    /// <summary>Whether <paramref name="scroll"/> is a recipe scroll this crafter could still learn
    /// from.</summary>
    public bool CanStudy(ItemInstance? scroll) =>
        scroll != null && ScrollRecipe(scroll.TemplateId) is { } recipe && !Knows(recipe.Id);

    /// <summary>
    /// Uses a recipe scroll: consumes one and teaches its recipe. A scroll for a recipe already
    /// known is refused and kept, so it can still be sold. The unit is secured before the recipe is
    /// taught, the order <see cref="InventoryComponent.Consume"/> uses, so a stale instance teaches
    /// nothing.
    /// </summary>
    public bool StudyScroll(ItemInstance? scroll)
    {
        if (_inventory == null || scroll == null || !CanStudy(scroll))
        {
            return false;
        }

        CraftingRecipeResource recipe = ScrollRecipe(scroll.TemplateId)!;
        return _inventory.RemoveOneInstance(scroll) != null && Learn(recipe.Id);
    }

    // --- Crafting -----------------------------------------------------------

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

    /// <summary>Whether the crafter's own rank reaches <paramref name="recipe"/>'s tier.</summary>
    public bool HasSkillFor(CraftingRecipeResource recipe) => SkillRank >= CraftingSkill.RequiredRank(recipe.Tier);

    /// <summary>
    /// Everything a craft needs except the materials and the skill: the recipe exists, the entity
    /// knows it, the station will take it, and its output is a real item. Split out for 38Q — a
    /// master's commission asks exactly this and then <em>sells</em> the missing half. The skill is
    /// left out for the same reason: it is the master's hands doing the work.
    /// </summary>
    public bool CanMake(CraftingRecipeResource? recipe, CraftingStationType station)
    {
        return recipe != null
            && Knows(recipe.Id)
            && StationAccepts(recipe.Station, station)
            && ItemDatabase.Get(recipe.OutputItemId) != null;
    }

    /// <summary>Whether the recipe can be crafted right now, by the crafter, at the given station.</summary>
    public bool CanCraft(CraftingRecipeResource? recipe, CraftingStationType station) =>
        CanMake(recipe, station) && HasSkillFor(recipe!) && HasIngredients(recipe!);

    /// <summary>How many times in a row the recipe could be crafted from what is held now
    /// (0 when it cannot be crafted at all), capped at <see cref="MaxBulk"/>. Room for the output is
    /// not predicted here: <see cref="Craft(CraftingRecipeResource, CraftingStationType, int)"/>
    /// stops cleanly at the first piece that does not fit.</summary>
    public int MaxCraftable(CraftingRecipeResource? recipe, CraftingStationType station)
    {
        if (_inventory == null || !CanCraft(recipe, station))
        {
            return 0;
        }

        int most = MaxBulk;
        foreach (RecipeIngredient ingredient in recipe!.IngredientList())
        {
            most = Mathf.Min(most, _inventory.CountOf(ingredient.ItemId) / ingredient.Quantity);
        }

        return most;
    }

    /// <summary>Crafts the recipe once: consumes ingredients and adds the output. Returns false
    /// if it isn't currently craftable.</summary>
    public bool Craft(CraftingRecipeResource? recipe, CraftingStationType station) =>
        CraftOne(recipe, station, commissioned: false);

    /// <summary>
    /// Crafts the recipe up to <paramref name="count"/> times and returns how many were made. Each
    /// piece is a whole craft with its own rollback, so an order that runs out of room or material
    /// part-way keeps every finished piece and loses nothing for the one that failed.
    /// </summary>
    public int Craft(CraftingRecipeResource? recipe, CraftingStationType station, int count)
    {
        int made = 0;
        int wanted = Mathf.Clamp(count, 0, MaxBulk);
        while (made < wanted && CraftOne(recipe, station, commissioned: false))
        {
            made++;
        }

        return made;
    }

    private bool CraftOne(CraftingRecipeResource? recipe, CraftingStationType station, bool commissioned)
    {
        if (recipe == null || _inventory == null)
        {
            return false;
        }

        bool ready = commissioned
            ? CanMake(recipe, station) && HasIngredients(recipe)
            : CanCraft(recipe, station);
        if (!ready)
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
        int rankBefore = SkillRank;

        // Ingredients are pre-validated above, so these removals all succeed.
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
        if (template is EquippableItemResource equippable && !template.IsStackable)
        {
            // A piece of gear is unique: it carries the workmanship of the hands that made it, and
            // above Common it rolls affixes. A master's commission is the plain piece off his own
            // pattern: Common, Standard, no affixes (see Commission for why that is load-bearing).
            CraftQuality quality = commissioned
                ? CraftQuality.Standard
                : CraftingSkill.Roll(rankBefore, recipe.Tier, _crafts, recipe.Id);
            float luck = commissioned ? 0f : CraftingSkill.AffixLuck(rankBefore, recipe.Tier);
            ItemRarity rarity = commissioned ? ItemRarity.Common : recipe.OutputRarity;

            for (int i = 0; i < quantity; i++)
            {
                ItemInstance made = rarity != ItemRarity.Common
                    ? LootGenerator.RollAffixed(
                        equippable, rarity, itemLevel: template.ItemLevel, quality: quality, luck: luck)
                    : new ItemInstance(template) { Quality = quality };
                if (_inventory.AddInstance(made, 1) < 1)
                {
                    break;
                }

                placed.Add(made);
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

        ReturnSavedMaterial(recipe);
        _crafts++;
        LastCrafted = placed[0];

        if (!commissioned)
        {
            GainSkill(CraftingSkill.XpForCraft(recipe.Tier, rankBefore));
        }

        if (Entity != null)
        {
            EventBus.Instance?.Publish(new ItemCraftedEvent(Entity, recipe.Id, recipe.OutputItemId, quantity));
        }

        return true;
    }

    /// <summary>Adds crafting experience and announces a new rank.</summary>
    private void GainSkill(int xp)
    {
        if (xp <= 0)
        {
            return;
        }

        int before = SkillRank;
        _skillXp = (int)System.Math.Min((long)_skillXp + xp, int.MaxValue);
        int after = SkillRank;
        if (after > before && Entity != null)
        {
            EventBus.Instance?.Publish(new Narrative.StoryToastRequestedEvent(
                "craft.toast.rank_up", CraftingSkill.RankNameKey(after)));
        }
    }

    /// <summary>
    /// A material-saving perk hands one unit of the recipe's largest ingredient back after a completed craft
    /// (<see cref="MaterialSaving"/> says whether and which). Runs after the output is safely placed, so it can
    /// never turn a refused craft into a gain; a pack with no room for the unit simply forgoes it.
    /// </summary>
    private void ReturnSavedMaterial(CraftingRecipeResource recipe)
    {
        int chance = PerkEffectMath.SaveChancePercent(PerkQuery.Of(Entity, PerkEffectKind.MaterialSaveChance));
        if (chance <= 0 || !MaterialSaving.Saves(_crafts, recipe.Id, chance))
        {
            return;
        }

        List<RecipeIngredient> ingredients = recipe.IngredientList();
        var quantities = new List<int>(ingredients.Count);
        foreach (RecipeIngredient ingredient in ingredients)
        {
            quantities.Add(ingredient.Quantity);
        }

        int index = MaterialSaving.SavedIngredient(quantities);
        if (index >= 0 && ItemDatabase.Get(ingredients[index].ItemId) is { } material)
        {
            _inventory!.AddItem(material, 1);
            Log.Info($"Crafting '{recipe.Id}' saved a unit of '{material.Id}'.");
        }
    }

    /// <summary>
    /// Has a master make <paramref name="recipe"/> for <paramref name="totalPrice"/> (Phase 38Q): he
    /// supplies every ingredient the pack is short of, then crafts it as normal.
    ///
    /// ⚠️ <b>THE GOLD IS TAKEN LAST, INVERTING THE HOUSE RULE, AND FOR THE REASON THE HOUSE RULE
    /// EXISTS.</b> <c>ServiceComponent</c> charges before every other verb because none of them can
    /// fail once paid — a bed, a flag, a taught recipe. This one fails whenever the pack has no room
    /// for the piece, and the craft already rolls itself back cleanly when it does. Charging
    /// first would therefore be the only way in the whole battery to lose the money for nothing.
    ///
    /// ⚠️ <b><paramref name="totalPrice"/> is passed in rather than computed here</b>, and that is
    /// deliberate: the window quotes a number and this charges one, and they must be the same number.
    /// It comes from <c>EconomyReport.CommissionCost</c>, which is also what <c>--validate</c> proves
    /// no output can be sold for more than.
    ///
    /// ⚠️ <b>A commissioned piece is the plain item: Common, Standard workmanship, no affixes, and it
    /// pays no crafting experience.</b> That proof prices the output at its template value, and an
    /// instance is worth more than its template the moment it carries a rarity, an affix or fine
    /// workmanship (<see cref="ItemInstance.Value"/>): a Rare commission would be worth about
    /// twice the figure <c>--validate</c> checked. Rarity and workmanship are what the player's own hands add.
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

        // The materials he supplies go into the pack so that the craft consumes them the ordinary
        // way. Handing over a finished piece without them would be a second crafting path, and the
        // two would drift the first time a recipe grew an ingredient.
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
                // bug the craft's own rollback exists to prevent.
                Return(supplied);
                Log.Warn($"Commission '{recipe.Id}': no room for the materials; the order was refused.");
                return false;
            }

            supplied.Add((ingredient.ItemId, missing));
        }

        if (!CraftOne(recipe, station, commissioned: true))
        {
            Return(supplied);
            return false; // the craft has already returned the player's own ingredients
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
    /// The refund always fits — the ingredients were in this same inventory a moment ago, and
    /// removing them freed at least as much room as putting them back needs.
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
            // A recipe that makes several at once is never reversed: one unit would hand back the
            // materials of the whole batch.
            if (recipe.OutputItemId == itemId && recipe.Station != CraftingStationType.Hand &&
                recipe.OutputQuantity <= 1 && StationAccepts(recipe.Station, station))
            {
                return recipe;
            }
        }

        return null;
    }

    /// <summary>Whether <paramref name="instance"/> can be salvaged — only gear/equipment (weapons,
    /// armor, accessories) the crafter holds or wears, and never a locked piece: a lock is the
    /// player saying "not this one", and salvage cannot be undone. A recipe isn't required:
    /// recipe-less gear salvages into generic scrap.</summary>
    public bool CanDeconstruct(ItemInstance? instance, CraftingStationType station)
    {
        // Stackable gear (ammunition) is not salvageable. A sheaf of twenty arrows is one craft, and
        // breaking it down an arrow at a time would pay scrap and experience twenty times over for
        // the plank that made them: an endless loop the moment arrows became craftable.
        if (instance is not { IsEquippable: true } || instance.Locked || instance.Template.IsStackable)
        {
            return false;
        }

        return (_inventory != null && _inventory.CountOf(instance.TemplateId) > 0)
            || (_equipment != null && _equipment.IsInstanceEquipped(instance));
    }

    /// <summary>
    /// What salvaging <paramref name="instance"/> at <paramref name="station"/> returns.
    ///
    /// ⚠️ <b>The one computation of a salvage's yield.</b> <see cref="Deconstruct"/> hands out
    /// exactly this and the crafting window previews exactly this; the window used to repeat the
    /// arithmetic, which is how a preview and a payout drift apart.
    ///
    /// The station matters: a recipe is reversed only where it is made, so a forged blade broken
    /// down at a workbench is scrap, and <see cref="SalvagePlan.BetterStation"/> names the station
    /// that would have recovered its metal.
    /// </summary>
    public SalvagePlan PlanSalvage(ItemInstance instance, CraftingStationType station)
    {
        var materials = new List<(ItemResource Item, int Quantity)>();
        float yieldBonus = PerkQuery.Of(Entity, PerkEffectKind.SalvageYieldBonus);
        CraftingRecipeResource? recipe = DeconstructionRecipe(instance.TemplateId, station);
        CraftingStationType better = CraftingStationType.Hand;

        if (recipe != null)
        {
            foreach (RecipeIngredient ingredient in recipe.IngredientList())
            {
                int recovered = Deconstruction.RecoveredQuantity(ingredient.Quantity, yieldBonus);

                // Never force-deref a content lookup: a recipe whose ingredient item was deleted skips
                // that material rather than crashing the salvage.
                if (recovered > 0 && ItemDatabase.Get(ingredient.ItemId) is { } material)
                {
                    materials.Add((material, recovered));
                }
            }
        }
        else
        {
            // No recipe to reverse here — return generic scrap so any item is still worth salvaging.
            int scrap = Deconstruction.ScrapYield(instance.Rarity, yieldBonus);
            if (scrap > 0 && ItemDatabase.Get(GameIds.Items.Scrap) is { } scrapItem)
            {
                materials.Add((scrapItem, scrap));
            }

            foreach (CraftingRecipeResource other in RecipeDatabase.All)
            {
                if (other.OutputItemId == instance.TemplateId && other.Station != CraftingStationType.Hand &&
                    other.OutputQuantity <= 1)
                {
                    better = other.Station;
                    break;
                }
            }
        }

        int xp = PerkEffectMath.ScaleCraftXp(
            Deconstruction.Xp(instance.Template.Value, instance.Rarity), PerkQuery.Of(Entity, PerkEffectKind.CraftXpMult));
        return new SalvagePlan(materials, xp, recipe != null, better);
    }

    /// <summary>Deconstructs one of <paramref name="instance"/>: consumes it and returns what
    /// <see cref="PlanSalvage"/> promises, plus XP. Returns false for a locked item, one the player
    /// doesn't hold, or a pack with no room for the materials — in which case nothing is consumed.</summary>
    public bool Deconstruct(ItemInstance? instance, CraftingStationType station)
    {
        if (instance == null || _inventory == null || !CanDeconstruct(instance, station))
        {
            return false;
        }

        SalvagePlan plan = PlanSalvage(instance, station);

        // Salvaging equipped gear takes it off first (back into the inventory) so the consume below
        // is uniform — and its stat bonuses are cleanly removed by the unequip.
        bool worn = _equipment != null && _equipment.IsInstanceEquipped(instance);
        if (worn && !_equipment!.UnequipInstance(instance))
        {
            return false;
        }

        if (_inventory.RemoveOneInstance(instance) == null)
        {
            return false;
        }

        // Materials go to the material bag when the inventory has one, where they always fit. Without
        // a bag a full pack can refuse them, and salvage used to shrug and lose the difference: the
        // item is put back instead and the salvage refused, so nothing is ever destroyed for nothing.
        var given = new List<(ItemResource Item, int Quantity)>();
        foreach ((ItemResource material, int quantity) in plan.Materials)
        {
            int added = _inventory.AddItem(material, quantity);
            if (added > 0)
            {
                given.Add((material, added));
            }

            if (added < quantity)
            {
                foreach ((ItemResource back, int backQuantity) in given)
                {
                    _inventory.RemoveItem(back.Id, backQuantity);
                }

                _inventory.AddInstance(instance, 1);
                if (worn)
                {
                    _equipment!.Equip(instance);
                }

                Log.Warn($"Deconstruct: no room for the recovered '{material.Id}'; the salvage was refused.");
                return false;
            }
        }

        ForgetRerolls(instance);
        Entity?.GetComponent<ProgressionComponent>()?.AddXp(plan.Xp);

        if (Entity != null)
        {
            EventBus.Instance?.Publish(new ItemDeconstructedEvent(Entity, instance.TemplateId, plan.Xp));
        }

        return true;
    }

    /// <summary>The junk-marked gear a salvage-all would break down here (a snapshot).</summary>
    public List<ItemStack> SalvageableJunk(CraftingStationType station)
    {
        var junk = new List<ItemStack>();
        if (_inventory == null)
        {
            return junk;
        }

        foreach (ItemStack stack in _inventory.JunkStacks())
        {
            if (CanDeconstruct(stack.Instance, station))
            {
                junk.Add(stack);
            }
        }

        return junk;
    }

    /// <summary>Salvages every junk-marked piece of gear and returns how many were broken down. Each
    /// goes through <see cref="Deconstruct"/>, so a piece with no room for its materials is skipped
    /// and kept rather than lost. Locked items are never junk (the inventory guarantees it).</summary>
    public int SalvageAllJunk(CraftingStationType station)
    {
        int salvaged = 0;
        foreach (ItemStack stack in SalvageableJunk(station))
        {
            for (int i = stack.Quantity; i > 0 && Deconstruct(stack.Instance, station); i--)
            {
                salvaged++;
            }
        }

        return salvaged;
    }

    // --- Reforging (Forge only) ---------------------------------------------

    /// <summary>The ingot a reforge of <paramref name="instance"/> consumes: its tier's metal, or
    /// the best lower-tier ingot the item database actually has.</summary>
    public static ItemResource? ReforgeMaterial(ItemInstance instance)
    {
        int tier = ReforgeRules.MaterialTier(instance.Template.Tier, instance.EffectiveItemLevel);
        for (int t = tier; t >= 1; t--)
        {
            if (ItemDatabase.Get(ReforgeRules.MaterialFor(t)) is { } material)
            {
                return material;
            }
        }

        return null;
    }

    /// <summary>How many times <paramref name="instance"/> has had an affix rerolled.</summary>
    public int RerollsOn(ItemInstance instance)
    {
        string fingerprint = Fingerprint(instance);
        foreach ((string key, int count) in _rerolls)
        {
            if (key == fingerprint)
            {
                return count;
            }
        }

        return 0;
    }

    /// <summary>What rerolling one affix of <paramref name="instance"/> costs now.</summary>
    public ReforgeQuote RerollQuote(ItemInstance instance, CraftingStationType station)
    {
        int prior = RerollsOn(instance);
        string block = !instance.HasAffixes ? BlockNoAffix : string.Empty;
        int gold = ReforgeRules.RerollGold(instance.Value, prior);
        var lines = new List<PriceLine> { new(LineWork, string.Empty, ReforgeRules.RerollBaseGold(instance.Value)) };
        if (prior > 0)
        {
            lines.Add(new PriceLine(LineRerolls, prior.ToString(CultureInfo.InvariantCulture), gold));
        }

        return Quote(instance, station, gold, ReforgeRules.RerollMaterials(prior), block, lines);
    }

    /// <summary>What the next upgrade level of <paramref name="instance"/> costs.</summary>
    public ReforgeQuote UpgradeQuote(ItemInstance instance, CraftingStationType station)
    {
        int target = instance.UpgradeLevel + 1;
        ItemInstance after = instance.Copy();
        after.UpgradeLevel = target;
        string block = ReforgeRules.CanUpgrade(instance.UpgradeLevel) ? string.Empty : BlockMaxLevel;
        int gold = ReforgeRules.UpgradeGold(instance.Value, after.Value, target);
        var lines = new List<PriceLine>
        {
            new(LineWork, string.Empty, ReforgeRules.UpgradeBaseGold(instance.Value, target)),
            new(LineGain, string.Empty, gold),
        };
        return Quote(instance, station, gold, ReforgeRules.UpgradeMaterials(target), block, lines);
    }

    /// <summary>What promoting <paramref name="instance"/> one rarity costs.</summary>
    public ReforgeQuote PromoteQuote(ItemInstance instance, CraftingStationType station)
    {
        string block = string.Empty;
        if (!ReforgeRules.CanPromote(instance.Rarity))
        {
            block = BlockRarity;
        }
        else if (AffixPool(instance, instance.Rarity + 1, skipIndex: -1).Count == 0)
        {
            block = BlockNoPool;
        }

        int after = ReforgeRules.InstanceValue(
            instance.Template.Value, instance.Affixes.Count + 1, instance.Rarity + 1, instance.Quality, instance.UpgradeLevel);
        int gold = ReforgeRules.PromoteGold(instance.Value, after);
        var lines = new List<PriceLine>
        {
            new(LineWork, string.Empty, ReforgeRules.PromoteBaseGold(instance.Value)),
            new(LineGain, string.Empty, gold),
        };
        return Quote(instance, station, gold, ReforgeRules.PromoteMaterials(instance.Rarity), block, lines);
    }

    private ReforgeQuote Quote(
        ItemInstance instance, CraftingStationType station, int gold, int materials, string block, List<PriceLine> lines)
    {
        ItemResource? material = ReforgeMaterial(instance);
        if (block.Length == 0)
        {
            if (station != CraftingStationType.Forge)
            {
                block = BlockStation;
            }
            else if (!instance.IsEquippable || !Holds(instance))
            {
                block = BlockNotHeld;
            }
            else if (_inventory!.CountOf(GameIds.Currency.Gold) < gold)
            {
                block = BlockGold;
            }
            else if (material == null || _inventory.CountOf(material.Id) < materials)
            {
                block = BlockMaterials;
            }
        }

        return new ReforgeQuote(gold, material, materials, block, lines);
    }

    private bool Holds(ItemInstance instance)
    {
        if (_inventory == null)
        {
            return false;
        }

        if (_equipment != null && _equipment.IsInstanceEquipped(instance))
        {
            return true;
        }

        foreach (ItemStack stack in _inventory.AllStacks)
        {
            if (ReferenceEquals(stack.Instance, instance))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Rerolls the affix at <paramref name="affixIndex"/> into a different one the item could have
    /// rolled, and returns the reforged item (null when refused). Its value does not change, so the
    /// fee buys a different item, never a dearer one.
    ///
    /// ⚠️ The outcome is seeded from the item and the saved reforge serial, so a quickload replays
    /// the same result instead of offering another pull for the same gold.
    /// </summary>
    public ItemInstance? RerollAffix(ItemInstance instance, int affixIndex, CraftingStationType station)
    {
        ReforgeQuote quote = RerollQuote(instance, station);
        if (!quote.Allowed || affixIndex < 0 || affixIndex >= instance.Affixes.Count)
        {
            return null;
        }

        RandomNumberGenerator rng = ReforgeRng(instance);
        AffixDefinition? pick = PickAffix(AffixPool(instance, instance.Rarity, affixIndex), rng);
        if (pick == null)
        {
            return null;
        }

        var affixes = new List<ItemAffix>(instance.Affixes);
        affixes[affixIndex] = pick.Roll(rng, 0.5f);
        ItemInstance reforged = Reforged(instance, instance.Rarity, affixes);

        int prior = RerollsOn(instance);
        if (!Swap(instance, reforged))
        {
            return null;
        }

        Pay(quote);
        ForgetRerolls(instance);
        RememberRerolls(reforged, prior + 1);
        _reforges++;
        return reforged;
    }

    /// <summary>Raises <paramref name="instance"/>'s upgrade level by one. The item keeps its
    /// identity (the level is a field on it), and a worn piece is taken off and put back on so its
    /// stat bonuses are re-applied at the new level.</summary>
    public bool Upgrade(ItemInstance instance, CraftingStationType station)
    {
        ReforgeQuote quote = UpgradeQuote(instance, station);
        if (!quote.Allowed)
        {
            return false;
        }

        bool worn = _equipment != null && _equipment.IsInstanceEquipped(instance);
        if (worn && !_equipment!.UnequipInstance(instance))
        {
            return false; // no room to take it off; nothing was charged
        }

        instance.UpgradeLevel++;
        if (worn)
        {
            _equipment!.Equip(instance);
        }

        Pay(quote);
        _reforges++;
        return true;
    }

    /// <summary>Promotes <paramref name="instance"/> one rarity (Uncommon to Rare, Rare to Epic),
    /// adding one new affix, and returns the reforged item (null when refused).</summary>
    public ItemInstance? Promote(ItemInstance instance, CraftingStationType station)
    {
        ReforgeQuote quote = PromoteQuote(instance, station);
        if (!quote.Allowed)
        {
            return null;
        }

        ItemRarity rarity = instance.Rarity + 1;
        RandomNumberGenerator rng = ReforgeRng(instance);
        AffixDefinition? pick = PickAffix(AffixPool(instance, rarity, skipIndex: -1), rng);
        if (pick == null)
        {
            return null;
        }

        var affixes = new List<ItemAffix>(instance.Affixes) { pick.Roll(rng, 0.5f) };
        ItemInstance reforged = Reforged(instance, rarity, affixes);

        int prior = RerollsOn(instance);
        if (!Swap(instance, reforged))
        {
            return null;
        }

        Pay(quote);
        ForgetRerolls(instance);
        RememberRerolls(reforged, prior);
        _reforges++;
        return reforged;
    }

    /// <summary>The affixes that could replace the one at <paramref name="skipIndex"/> (or join the
    /// item, when it is -1) at <paramref name="rarity"/>: the loot generator's pool, less the affix
    /// being replaced and anything sharing a stat or a group with one that stays.</summary>
    private static List<AffixDefinition> AffixPool(ItemInstance instance, ItemRarity rarity, int skipIndex)
    {
        var pool = new List<AffixDefinition>();
        if (instance.Equippable is not { } equippable)
        {
            return pool;
        }

        foreach (AffixDefinition candidate in AffixDatabase.ApplicableTo(equippable, rarity, instance.EffectiveItemLevel))
        {
            bool clash = false;
            for (int i = 0; i < instance.Affixes.Count && !clash; i++)
            {
                ItemAffix held = instance.Affixes[i];
                if (i == skipIndex)
                {
                    clash = held.Id == candidate.Id;
                    continue;
                }

                string group = AffixDatabase.Get(held.Id)?.Group ?? string.Empty;
                clash = held.Id == candidate.Id || held.Stat == candidate.Stat ||
                    (candidate.Group.Length > 0 && candidate.Group == group);
            }

            if (!clash)
            {
                pool.Add(candidate);
            }
        }

        return pool;
    }

    /// <summary>Whether the affix at <paramref name="affixIndex"/> has anything to be rerolled into.</summary>
    public bool CanRerollAffix(ItemInstance instance, int affixIndex) =>
        affixIndex >= 0 && affixIndex < instance.Affixes.Count &&
        AffixPool(instance, instance.Rarity, affixIndex).Count > 0;

    private static AffixDefinition? PickAffix(List<AffixDefinition> pool, RandomNumberGenerator rng)
    {
        float total = 0f;
        foreach (AffixDefinition affix in pool)
        {
            total += Mathf.Max(0f, affix.Weight);
        }

        if (pool.Count == 0)
        {
            return null;
        }

        float roll = rng.Randf() * total;
        foreach (AffixDefinition affix in pool)
        {
            roll -= Mathf.Max(0f, affix.Weight);
            if (roll <= 0f)
            {
                return affix;
            }
        }

        return pool[^1];
    }

    private RandomNumberGenerator ReforgeRng(ItemInstance instance)
    {
        ulong seed = ((ulong)StableRoll.Seed(Fingerprint(instance)) << 32) | (uint)_reforges;
        return new RandomNumberGenerator { Seed = seed };
    }

    private static ItemInstance Reforged(ItemInstance source, ItemRarity rarity, List<ItemAffix> affixes)
    {
        return new ItemInstance(source.Template, rarity, affixes)
        {
            Quality = source.Quality,
            UpgradeLevel = source.UpgradeLevel,
            ItemLevel = source.ItemLevel,
            Locked = source.Locked,
            Junk = source.Junk,
        };
    }

    /// <summary>Replaces <paramref name="old"/> with <paramref name="replacement"/> wherever it is
    /// held: in the pack, or worn (taken off, swapped, put back on). False changes nothing.</summary>
    private bool Swap(ItemInstance old, ItemInstance replacement)
    {
        if (_inventory == null)
        {
            return false;
        }

        bool worn = _equipment != null && _equipment.IsInstanceEquipped(old);
        if (worn && !_equipment!.UnequipInstance(old))
        {
            return false;
        }

        if (_inventory.RemoveOneInstance(old) == null)
        {
            return false;
        }

        // The slot the old piece left is the slot the new one takes, so this add cannot fail; the
        // guard is here so that if it ever does, the player gets the original back.
        if (_inventory.AddInstance(replacement, 1) < 1)
        {
            _inventory.AddInstance(old, 1);
            if (worn)
            {
                _equipment!.Equip(old);
            }

            return false;
        }

        if (worn)
        {
            _equipment!.Equip(replacement);
        }

        return true;
    }

    private void Pay(ReforgeQuote quote)
    {
        if (quote.Gold > 0)
        {
            _inventory!.RemoveItem(GameIds.Currency.Gold, quote.Gold);
        }

        if (quote.Material != null && quote.Materials > 0)
        {
            _inventory!.RemoveItem(quote.Material.Id, quote.Materials);
        }
    }

    /// <summary>What identifies an item to the reroll ledger: its template, rarity, workmanship,
    /// rolled level and exact affixes. The upgrade level is left out so upgrading does not reset the
    /// count. Two identical pieces share a row, which only ever makes the second one dearer.</summary>
    private static string Fingerprint(ItemInstance instance)
    {
        var text = new StringBuilder(instance.TemplateId);
        text.Append('|').Append((int)instance.Rarity)
            .Append('|').Append((int)instance.Quality)
            .Append('|').Append(instance.ItemLevel);
        foreach (ItemAffix affix in instance.Affixes)
        {
            text.Append('|').Append(affix.Id).Append(':')
                .Append(affix.Value.ToString("R", CultureInfo.InvariantCulture));
        }

        return text.ToString();
    }

    private void RememberRerolls(ItemInstance instance, int count)
    {
        if (count <= 0)
        {
            return;
        }

        _rerolls.Add((Fingerprint(instance), count));
        while (_rerolls.Count > RerollLedgerCap)
        {
            _rerolls.RemoveAt(0);
        }
    }

    private void ForgetRerolls(ItemInstance instance)
    {
        string fingerprint = Fingerprint(instance);
        _rerolls.RemoveAll(row => row.Fingerprint == fingerprint);
    }

    // --- ISaveable ----------------------------------------------------------

    public Godot.Collections.Dictionary Save()
    {
        var known = new Godot.Collections.Array();
        foreach (string id in _known)
        {
            known.Add(id);
        }

        var data = new Godot.Collections.Dictionary { ["known"] = known, ["crafts"] = _crafts };

        // Additive keys, written only off their default, so a save that uses none of them is exactly
        // what it was before they existed.
        if (_skillXp > 0)
        {
            data[SkillXpKey] = _skillXp;
        }

        if (_reforges > 0)
        {
            data[ReforgesKey] = _reforges;
        }

        if (_rerolls.Count > 0)
        {
            var rows = new Godot.Collections.Array();
            foreach ((string fingerprint, int count) in _rerolls)
            {
                rows.Add(new Godot.Collections.Dictionary { ["fp"] = fingerprint, ["n"] = count });
            }

            data[RerollsKey] = rows;
        }

        if (_pinned.Length > 0)
        {
            data[PinnedKey] = _pinned;
        }

        return data;
    }

    public void Load(Godot.Collections.Dictionary data)
    {
        // Replaced, never merged: every fact below is reset first and an absent key lands on its
        // default, so a save that predates a key (or holds 0) zeroes a live value.
        _crafts = data.TryGetValue("crafts", out Variant craftsVar) ? Mathf.Max(0, craftsVar.AsInt32()) : 0;
        _skillXp = data.TryGetValue(SkillXpKey, out Variant xpVar) ? Mathf.Max(0, xpVar.AsInt32()) : 0;
        _reforges = data.TryGetValue(ReforgesKey, out Variant reforgesVar) ? Mathf.Max(0, reforgesVar.AsInt32()) : 0;
        _learnedPending = 0;
        _learnedLast = string.Empty;

        _rerolls.Clear();
        if (data.TryGetValue(RerollsKey, out Variant rerollsVar))
        {
            foreach (Variant row in rerollsVar.AsGodotArray())
            {
                Godot.Collections.Dictionary entry = row.AsGodotDictionary();
                if (entry.TryGetValue("fp", out Variant fp) && entry.TryGetValue("n", out Variant n) && n.AsInt32() > 0)
                {
                    _rerolls.Add((fp.AsString(), n.AsInt32()));
                }
            }
        }

        // The recipe set used to survive a save with no "known" key: the early return sat above the
        // Clear, so a quickload into such a save kept every recipe learned on the abandoned timeline.
        _known.Clear();
        if (data.TryGetValue("known", out Variant knownVar))
        {
            foreach (Variant id in knownVar.AsGodotArray())
            {
                string recipeId = id.AsString();
                if (RecipeDatabase.Get(recipeId) != null)
                {
                    _known.Add(recipeId);
                }
            }
        }

        // Starting recipes are authored data every character knows, not state: seeding them again
        // gives a save with no list its defaults, and gives an older save the starting recipes added
        // since it was written (nothing else could ever teach those).
        SeedStartingRecipes();

        string pinned = data.TryGetValue(PinnedKey, out Variant pinnedVar) ? pinnedVar.AsString() : string.Empty;
        _pinned = RecipeDatabase.Get(pinned) != null ? pinned : string.Empty;
    }
}
