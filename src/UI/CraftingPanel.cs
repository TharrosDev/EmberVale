using Embervale.Core;
using Embervale.Core.Events;
using Embervale.Crafting;
using Embervale.Entities;
using Embervale.Items;
using Embervale.Localization;
using Godot;

namespace Embervale.UI;

/// <summary>
/// The crafting window. It is event-driven: a <see cref="CraftingStationComponent"/>
/// publishes a <see cref="CraftingStationOpenedEvent"/> on interact, this panel resolves
/// the player's <see cref="CraftingComponent"/> + <see cref="InventoryComponent"/> and
/// lists the recipes that match the station (plus <c>Hand</c> recipes the player knows),
/// each with a have/need ingredient breakdown and a Craft button enabled only when it can
/// be made. Like the character screen it is modal — it frees the mouse and sets
/// <see cref="UiState.MenuOpen"/>. Rebuilt from a dirty flag in <c>_Process</c> (never
/// during a button signal) so crafting never frees its own button mid-callback.
/// </summary>
public partial class CraftingPanel : CanvasLayer
{
    private PanelContainer _panel = null!;
    private VBoxContainer _list = null!;

    private IEntity? _player;
    private CraftingComponent? _crafting;
    private InventoryComponent? _inventory;
    private CraftingStationType _station;
    private string _stationName = "Crafting";
    private bool _dirty;
    private bool _justOpened;
    private bool _salvageMode;

    public override void _Ready()
    {
        _panel = UiTheme.Panel();
        _panel.Visible = false;
        _panel.AnchorLeft = 0.5f;
        _panel.AnchorRight = 0.5f;
        _panel.OffsetLeft = -230;
        _panel.OffsetRight = 230;
        _panel.OffsetTop = 60;
        _panel.GrowHorizontal = Control.GrowDirection.Both;
        AddChild(_panel);

        MarginContainer margin = UiTheme.Padding(12);
        _panel.AddChild(margin);

        var scroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(436, 540),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        margin.AddChild(scroll);

        _list = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _list.AddThemeConstantOverride("separation", 4);
        scroll.AddChild(_list);

        EventBus.Instance?.Subscribe<CraftingStationOpenedEvent>(OnStationOpened);
        EventBus.Instance?.Subscribe<InventoryChangedEvent>(OnInventoryChanged);
        EventBus.Instance?.Subscribe<ItemCraftedEvent>(OnItemCrafted);
        EventBus.Instance?.Subscribe<ItemDeconstructedEvent>(OnItemDeconstructed);
    }

    public override void _ExitTree()
    {
        EventBus.Instance?.Unsubscribe<CraftingStationOpenedEvent>(OnStationOpened);
        EventBus.Instance?.Unsubscribe<InventoryChangedEvent>(OnInventoryChanged);
        EventBus.Instance?.Unsubscribe<ItemCraftedEvent>(OnItemCrafted);
        EventBus.Instance?.Unsubscribe<ItemDeconstructedEvent>(OnItemDeconstructed);
    }

    private void OnStationOpened(CraftingStationOpenedEvent e)
    {
        // Ignore a second station while one is open.
        if (_panel.Visible)
        {
            return;
        }

        _player = e.Player;
        _crafting = e.Player.GetComponent<CraftingComponent>();
        _inventory = e.Player.GetComponent<InventoryComponent>();
        _station = e.Station;
        _stationName = e.StationName;

        if (_crafting == null)
        {
            return;
        }

        _salvageMode = false;
        SetOpen(true);
        _dirty = true;

        // The same interact press that opened the station is still "just pressed" this
        // frame; swallow it so the close-on-interact below doesn't fire immediately.
        _justOpened = true;
    }

    private void OnInventoryChanged(InventoryChangedEvent e) => _dirty = true;

    private void OnItemCrafted(ItemCraftedEvent e) => _dirty = true;

    private void OnItemDeconstructed(ItemDeconstructedEvent e) => _dirty = true;

    public override void _Process(double delta)
    {
        if (!_panel.Visible)
        {
            return;
        }

        // Swallow the interact press that opened the station this frame.
        if (_justOpened)
        {
            _justOpened = false;
        }
        else if (Godot.Input.IsActionJustPressed(GameInput.Interact))
        {
            // A modal needs an easy out; the interact key both opens and closes it.
            Close();
            return;
        }

        if (_dirty)
        {
            Rebuild();
        }
    }

    private void Craft(CraftingRecipeResource recipe)
    {
        _crafting?.Craft(recipe, _station);
        _dirty = true; // rebuild next frame (events also flag it)
    }

    private void Deconstruct(ItemInstance instance)
    {
        _crafting?.Deconstruct(instance, _station);
        _dirty = true; // rebuild next frame (events also flag it)
    }

    private void SetMode(bool salvage)
    {
        _salvageMode = salvage;
        _dirty = true;
    }

    private void Close()
    {
        IEntity? player = _player;
        SetOpen(false);
        _player = null;
        _crafting = null;
        _inventory = null;

        if (player != null)
        {
            EventBus.Instance?.Publish(new CraftingStationClosedEvent(player));
        }
    }

    private void SetOpen(bool open)
    {
        _panel.Visible = open;
        if (open) UiState.Open(this); else UiState.Close(this);

        bool playing = GameManager.Instance is { IsPlaying: true };
        Godot.Input.MouseMode = UiState.MenuOpen || !playing
            ? Godot.Input.MouseModeEnum.Visible
            : Godot.Input.MouseModeEnum.Captured;
    }

    private void Rebuild()
    {
        _dirty = false;

        foreach (Node child in _list.GetChildren())
        {
            _list.RemoveChild(child);
            child.QueueFree();
        }

        _list.AddChild(UiTheme.Header($"{_stationName}   (E to close)"));

        if (_crafting == null)
        {
            return;
        }

        AddModeTabs();

        if (_salvageMode)
        {
            RebuildSalvage();
        }
        else
        {
            RebuildCraft();
        }
    }

    /// <summary>Craft / Salvage switch at the top of the panel; the active mode reads as the accent.</summary>
    private void AddModeTabs()
    {
        var tabs = new HBoxContainer();
        tabs.AddThemeConstantOverride("separation", 8);

        Button craftTab = UiTheme.Action(Loc.T("craft.mode_craft"));
        craftTab.Disabled = !_salvageMode; // the active tab is the disabled (highlighted) one
        craftTab.Pressed += () => SetMode(false);
        tabs.AddChild(craftTab);

        Button salvageTab = UiTheme.Action(Loc.T("craft.mode_salvage"));
        salvageTab.Disabled = _salvageMode;
        salvageTab.Pressed += () => SetMode(true);
        tabs.AddChild(salvageTab);

        _list.AddChild(tabs);
        _list.AddChild(new HSeparator());
    }

    private void RebuildCraft()
    {
        bool any = false;
        foreach (CraftingRecipeResource recipe in RecipeDatabase.All)
        {
            if (!_crafting!.Knows(recipe.Id) || !StationShows(recipe.Station))
            {
                continue;
            }

            any = true;
            AddRecipe(recipe);
        }

        if (!any)
        {
            _list.AddChild(UiTheme.Body(Loc.T("craft.recipes_none"), UiTheme.Dim));
        }
    }

    /// <summary>Lists every salvageable inventory stack and worn item — recipe-backed (returns
    /// materials) or not (returns generic scrap) — each with its yield preview and a Deconstruct button.</summary>
    private void RebuildSalvage()
    {
        bool any = false;

        // Loose inventory items first, then the gear the player is wearing (badged) — both salvageable.
        if (_inventory != null)
        {
            foreach (ItemStack stack in _inventory.Stacks)
            {
                if (_crafting!.CanDeconstruct(stack.Instance, _station))
                {
                    any = true;
                    AddSalvage(stack.Instance, stack.Quantity, equipped: false,
                        _crafting.DeconstructionRecipe(stack.Instance.TemplateId, _station));
                }
            }
        }

        EquipmentComponent? equipment = _player?.GetComponent<EquipmentComponent>();
        if (equipment != null)
        {
            foreach (ItemInstance instance in equipment.EquippedInstances)
            {
                if (_crafting!.CanDeconstruct(instance, _station))
                {
                    any = true;
                    AddSalvage(instance, 1, equipped: true,
                        _crafting.DeconstructionRecipe(instance.TemplateId, _station));
                }
            }
        }

        if (!any)
        {
            _list.AddChild(UiTheme.Body(Loc.T("craft.salvage_none"), UiTheme.Dim));
        }
    }

    private void AddSalvage(ItemInstance instance, int quantity, bool equipped, CraftingRecipeResource? recipe)
    {
        var titleRow = new HBoxContainer();
        titleRow.AddThemeConstantOverride("separation", 8);

        Label title = UiTheme.Body($"{instance.DisplayName} x{quantity}", UiTheme.Text);
        title.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        title.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        titleRow.AddChild(title);

        // The "small visual": worn gear gets an accent [Equipped] badge so it reads apart from loose
        // copies before the player salvages it (salvaging an equipped item takes it off first).
        if (equipped)
        {
            Label badge = UiTheme.Body($"[{Loc.T("craft.equipped")}]", UiTheme.Accent);
            badge.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            titleRow.AddChild(badge);
        }

        Button button = UiTheme.Action(Loc.T("craft.deconstruct"));
        ItemInstance captured = instance;
        button.Pressed += () => Deconstruct(captured);
        titleRow.AddChild(button);

        _list.AddChild(titleRow);

        if (recipe != null)
        {
            foreach (RecipeIngredient ingredient in recipe.IngredientList())
            {
                int recovered = Deconstruction.RecoveredQuantity(ingredient.Quantity);
                if (recovered <= 0)
                {
                    continue;
                }

                ItemResource? material = ItemDatabase.Get(ingredient.ItemId);
                string name = material?.DisplayName ?? ingredient.ItemId;
                _list.AddChild(UiTheme.Body($"   → {recovered}x {name}", UiTheme.Accent));
            }
        }
        else
        {
            // Recipe-less: generic scrap, scaled by rarity.
            int scrap = Deconstruction.ScrapYield(instance.Rarity);
            string scrapName = ItemDatabase.Get(GameIds.Items.Scrap)?.DisplayName ?? "Scrap";
            _list.AddChild(UiTheme.Body($"   → {scrap}x {scrapName}", UiTheme.Accent));
        }

        int xp = Deconstruction.Xp(instance.Template.Value, instance.Rarity);
        _list.AddChild(UiTheme.Body($"   {Loc.TF("craft.yield_xp", xp)}", UiTheme.Good));
        _list.AddChild(new HSeparator());
    }

    private bool StationShows(CraftingStationType required)
    {
        return required == CraftingStationType.Hand || required == _station;
    }

    private void AddRecipe(CraftingRecipeResource recipe)
    {
        bool canCraft = _crafting != null && _crafting.CanCraft(recipe, _station);

        var titleRow = new HBoxContainer();
        titleRow.AddThemeConstantOverride("separation", 8);

        Label title = UiTheme.Body(recipe.DisplayName, canCraft ? UiTheme.Text : UiTheme.Dim);
        title.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        title.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        titleRow.AddChild(title);

        Button craft = UiTheme.Action(Loc.T("craft.craft"));
        craft.Disabled = !canCraft;
        CraftingRecipeResource captured = recipe;
        craft.Pressed += () => Craft(captured);
        titleRow.AddChild(craft);

        _list.AddChild(titleRow);

        ItemResource? output = ItemDatabase.Get(recipe.OutputItemId);
        string outName = output?.DisplayName ?? recipe.OutputItemId;
        string rarity = recipe.OutputRarity != ItemRarity.Common ? $" [{recipe.OutputRarity}]" : string.Empty;
        _list.AddChild(UiTheme.Body($"   → {recipe.OutputQuantity}x {outName}{rarity}", UiTheme.Accent));

        foreach (RecipeIngredient ingredient in recipe.IngredientList())
        {
            int have = _inventory?.CountOf(ingredient.ItemId) ?? 0;
            ItemResource? item = ItemDatabase.Get(ingredient.ItemId);
            string itemName = item?.DisplayName ?? ingredient.ItemId;
            Color color = have >= ingredient.Quantity ? UiTheme.Good : UiTheme.Bad;
            _list.AddChild(UiTheme.Body($"      {ingredient.Quantity}x {itemName}  (have {have})", color));
        }

        _list.AddChild(new HSeparator());
    }
}
