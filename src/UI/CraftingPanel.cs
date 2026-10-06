using System.Collections.Generic;
using Embervale.Core;
using Embervale.Core.Events;
using Embervale.Crafting;
using Embervale.Economy;
using Embervale.Entities;
using Embervale.Items;
using Embervale.Localization;
using Embervale.Stats;
using Godot;

namespace Embervale.UI;

/// <summary>
/// The crafting window (on the 30.5F <see cref="UiPanel"/> framework). It is event-driven: a
/// <see cref="CraftingStationComponent"/> publishes a <see cref="CraftingStationOpenedEvent"/>
/// on interact, this panel resolves the player's <see cref="CraftingComponent"/> +
/// <see cref="InventoryComponent"/> and offers three pages on the shared <see cref="UiTabs"/> strip:
///
/// <list type="bullet">
/// <item><b>Craft</b> — the recipes for this station, searchable and filterable, each with its
/// ingredients, an output preview, a quantity picker and a pin; recipes the player has not learned
/// are listed locked with where to learn them.</item>
/// <item><b>Reforge</b> — at a forge only: reroll one affix, raise the upgrade level, or promote the
/// rarity of a piece of gear, each with its bill.</item>
/// <item><b>Salvage</b> — break gear down for the materials <see cref="CraftingComponent.PlanSalvage"/>
/// promises.</item>
/// </list>
///
/// Every number shown comes from the same call that is then charged or paid
/// (<c>CommissionQuote</c>, <c>RerollQuote</c>, <c>PlanSalvage</c>); the window computes nothing of
/// its own. The base owns the modal contract, the dirty-flag rebuild and focus restore.
/// </summary>
public partial class CraftingPanel : UiPanel
{
    private const int TabCraft = 0;
    private const int TabReforge = 1;
    private const int TabSalvage = 2;

    /// <summary>The filter row's categories, in dropdown order.</summary>
    public static readonly IReadOnlyList<string> CategoryKeys = new[]
    {
        "craft.filter.all", "craft.filter.weapons", "craft.filter.armor", "craft.filter.accessories",
        "craft.filter.consumables", "craft.filter.materials", "craft.filter.other",
    };

    /// <summary>
    /// The locale keys this window picks between at runtime (a button that reads "Pin" or "Unpin").
    /// A key chosen by a condition is invisible to the source scan that pins literal
    /// <c>Loc.T("...")</c> calls to the catalogue, so they are declared here and <c>--validate</c>
    /// requires every one. Add to this list when adding such a choice.
    /// </summary>
    public static readonly IReadOnlyList<string> ComputedKeys = new[]
    {
        "craft.pin", "craft.unpin", "craft.reforge.open", "craft.reforge.close",
        "craft.salvage.junk", "craft.salvage.junk_confirm", "craft.salvage.confirm", "craft.deconstruct",
        "craft.status.crafted", "craft.status.crafted_some", "craft.status.salvaged", "craft.status.salvaged_some",
        "craft.locked.trainer", "craft.locked.trainer_or_scroll", "craft.locked.scroll", "craft.locked.unknown",
        "craft.toast.recipe_learned", "craft.toast.recipes_learned", "craft.toast.recipes_learned_detail",
        "craft.toast.rank_up",
    };

    /// <summary>The confirm token for "salvage all junk" (an item is its own token).</summary>
    private static readonly object JunkConfirm = new();

    private Label _title = null!;
    private UiTabs _modeTabs = null!;
    private Label _skill = null!;
    private ProgressBar _skillBar = null!;
    private Control _craftBar = null!;
    private LineEdit _search = null!;
    private Label _status = null!;
    private VBoxContainer _list = null!;

    private IEntity? _player;
    private CraftingComponent? _crafting;
    private InventoryComponent? _inventory;
    private CraftingStationType _station;
    private string _stationName = string.Empty;

    // 38Q: zero is a free public station, which is every station the game had before it. Non-zero
    // turns this window into a master's order desk — he supplies what the player is short of, and
    // every Craft button becomes a priced Commission.
    private int _labourGold;
    private ShopResource? _materialsShop;
    private bool _justOpened;

    private int _tab;
    private string _query = string.Empty;
    private int _category;
    private bool _craftableOnly;
    private readonly Dictionary<string, int> _quantities = new();

    /// <summary>The piece whose reforge options are open.</summary>
    private ItemInstance? _reforgeTarget;

    /// <summary>What a destructive button is waiting on a second press for: the item, or
    /// <see cref="JunkConfirm"/>. Any other action clears it.</summary>
    private object? _confirm;

    private string _statusText = string.Empty;
    private bool _statusBad;

    protected override void BuildShell(PanelContainer shell)
    {
        UiTheme.ApplyWorkspace(shell, 0.66f);

        MarginContainer margin = UiTheme.Padding(UiTheme.PanelPad);
        shell.AddChild(margin);

        var column = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        column.AddThemeConstantOverride("separation", UiTheme.SpaceSm);
        margin.AddChild(column);

        _title = UiTheme.Header(string.Empty);
        column.AddChild(_title);

        // Craft / Reforge / Salvage - static layout; the pages rebuild inside the list below.
        _modeTabs = new UiTabs();
        _modeTabs.Add(Loc.T("craft.mode_craft"));
        _modeTabs.Add(Loc.T("craft.mode_reforge"));
        _modeTabs.Add(Loc.T("craft.mode_salvage"));
        _modeTabs.TabChanged += index =>
        {
            _tab = index;
            _confirm = null;
            SetStatus(string.Empty);
            MarkDirty();
        };
        column.AddChild(_modeTabs);

        // The crafting skill, always in view: it gates recipes and sets the workmanship odds, so it
        // is the first thing a "why can I not make this" question needs.
        var skillRow = new HBoxContainer();
        skillRow.AddThemeConstantOverride("separation", UiTheme.SpaceSm);
        _skill = UiTheme.Caption(string.Empty, UiTheme.Dim);
        _skill.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        skillRow.AddChild(_skill);
        _skillBar = UiTheme.Bar(UiTheme.Accent, 140f);
        _skillBar.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        skillRow.AddChild(_skillBar);
        column.AddChild(skillRow);

        column.AddChild(BuildCraftBar());

        _status = UiTheme.Caption(string.Empty);
        _status.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _status.Visible = false;
        column.AddChild(_status);
        column.AddChild(UiTheme.Divider());

        // The shared list: row gap, and a gutter so the scrollbar stays off the row borders. No fixed
        // height (a literal overflowed the Steam Deck's 533 px viewport once already); the workspace
        // shell sets the floor.
        (ScrollContainer scroll, _list) = UiTheme.ScrollList();
        column.AddChild(scroll);
    }

    /// <summary>Search, category and the craftable-only switch. Static controls, so a rebuild of the
    /// list never steals the caret from a half-typed search.</summary>
    private Control BuildCraftBar()
    {
        var bar = new HBoxContainer();
        bar.AddThemeConstantOverride("separation", UiTheme.SpaceSm);

        _search = new LineEdit
        {
            PlaceholderText = Loc.T("craft.search_placeholder"),
            ClearButtonEnabled = true,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        UiTheme.ApplyType(_search, UiTheme.FontRole.Interface, UiTheme.BodyFontSize);
        _search.TextChanged += text =>
        {
            _query = text.Trim();
            MarkDirty();
        };

        // While the field has focus the gameplay keys are out of the map (GameInput.SetTextEntry):
        // panels toggle on a polled action, so "iron" would otherwise open the inventory over this.
        _search.FocusEntered += () => GameInput.SetTextEntry(true);
        _search.FocusExited += () => GameInput.SetTextEntry(false);
        bar.AddChild(_search);

        var names = new string[CategoryKeys.Count];
        for (int i = 0; i < names.Length; i++)
        {
            names[i] = Loc.T(CategoryKeys[i]);
        }

        OptionButton filter = UiTheme.Dropdown(names, 0);
        filter.ItemSelected += index =>
        {
            _category = (int)index;
            MarkDirty();
        };
        bar.AddChild(filter);

        CheckButton craftable = UiTheme.Toggle(false);
        craftable.Text = Loc.T("craft.craftable_only");
        craftable.Toggled += on =>
        {
            _craftableOnly = on;
            MarkDirty();
        };
        bar.AddChild(craftable);

        _craftBar = bar;
        return bar;
    }

    protected override void OnReady()
    {
        EventBus.Instance?.Subscribe<CraftingStationOpenedEvent>(OnStationOpened);
        EventBus.Instance?.Subscribe<InventoryChangedEvent>(OnInventoryChanged);
        EventBus.Instance?.Subscribe<ItemCraftedEvent>(OnItemCrafted);
        EventBus.Instance?.Subscribe<ItemDeconstructedEvent>(OnItemDeconstructed);
        EventBus.Instance?.Subscribe<RecipeLearnedEvent>(OnRecipeLearned);
    }

    public override void _ExitTree()
    {
        EventBus.Instance?.Unsubscribe<CraftingStationOpenedEvent>(OnStationOpened);
        EventBus.Instance?.Unsubscribe<InventoryChangedEvent>(OnInventoryChanged);
        EventBus.Instance?.Unsubscribe<ItemCraftedEvent>(OnItemCrafted);
        EventBus.Instance?.Unsubscribe<ItemDeconstructedEvent>(OnItemDeconstructed);
        EventBus.Instance?.Unsubscribe<RecipeLearnedEvent>(OnRecipeLearned);
    }

    private void OnStationOpened(CraftingStationOpenedEvent e)
    {
        // Ignore a second station while one is open.
        if (IsOpen)
        {
            return;
        }

        _player = e.Player;
        _crafting = e.Player.GetComponent<CraftingComponent>();
        _inventory = e.Player.GetComponent<InventoryComponent>();
        _station = e.Station;
        _stationName = e.StationName;
        _labourGold = e.LabourGold;
        _materialsShop = string.IsNullOrEmpty(e.MaterialsShopId) ? null : ShopDatabase.Get(e.MaterialsShopId);

        if (_crafting == null)
        {
            return;
        }

        _tab = TabCraft;
        _confirm = null;
        _reforgeTarget = null;
        _quantities.Clear();
        SetStatus(string.Empty);
        _modeTabs.Select(TabCraft);
        SetOpen(true);

        // The same interact press that opened the station is still "just pressed" this
        // frame; swallow it so the close-on-interact below doesn't fire immediately.
        _justOpened = true;
    }

    protected override void OnOpenChanged(bool open)
    {
        if (!open)
        {
            // Esc closes through the base SetOpen, not Close(), and the keyboard coming back must
            // not depend on the hidden field's focus signal arriving.
            GameInput.SetTextEntry(false);
        }
    }

    private void OnInventoryChanged(InventoryChangedEvent e) => MarkDirty();

    private void OnItemCrafted(ItemCraftedEvent e) => MarkDirty();

    private void OnItemDeconstructed(ItemDeconstructedEvent e) => MarkDirty();

    private void OnRecipeLearned(RecipeLearnedEvent e) => MarkDirty();

    public override void _Process(double delta)
    {
        if (IsOpen)
        {
            // Swallow the interact press that opened the station this frame.
            if (_justOpened)
            {
                _justOpened = false;
            }
            else if (Godot.Input.IsActionJustPressed(UiLive.Interact) &&
                     GetViewport().GuiGetFocusOwner() is not LineEdit)
            {
                // A modal needs an easy out; the interact key both opens and closes it. Not while the
                // search box has the caret: the interact key is a letter, and typing it would shut
                // the window on the word being searched for.
                Close();
                return;
            }
        }

        base._Process(delta);
    }

    /// <summary>Whether this window is a master's order desk rather than a free public station (38Q).
    /// Keyed off the shop, not the fee: without somewhere to price materials there is nothing to
    /// supply, and a fee alone would charge for what the forge outside does for nothing.</summary>
    private bool IsCommission => _materialsShop != null;

    /// <summary>The master's quote and the reasons for it (38U). ⚠️ The price charged is
    /// <c>Total</c> — the same number the tooltip's last line shows, because they are one
    /// value rather than two computations of one.</summary>
    private PriceQuote CommissionQuote(CraftingRecipeResource recipe) =>
        EconomyReport.CommissionQuote(recipe, _materialsShop!, _inventory, _labourGold);

    private void SetStatus(string text, bool bad = false)
    {
        _statusText = text;
        _statusBad = bad;
    }

    private void Close()
    {
        IEntity? player = _player;
        SetOpen(false);
        _player = null;
        _crafting = null;
        _inventory = null;
        _reforgeTarget = null;
        _confirm = null;

        if (player != null)
        {
            EventBus.Instance?.Publish(new CraftingStationClosedEvent(player));
        }
    }

    protected override void Rebuild()
    {
        UiTheme.ClearChildren(_list);

        _title.Text = Loc.TF("craft.title", _stationName);
        _craftBar.Visible = _tab == TabCraft;
        _status.Visible = _statusText.Length > 0;
        _status.Text = _statusText;
        _status.AddThemeColorOverride("font_color", _statusBad ? UiTheme.Bad : UiTheme.Good);

        if (_crafting == null)
        {
            return;
        }

        int rank = _crafting.SkillRank;
        _skill.Text = rank >= CraftingSkill.MaxRank
            ? Loc.TF("craft.skill_max", Loc.T(CraftingSkill.RankNameKey(rank)))
            : Loc.TF(
                "craft.skill", Loc.T(CraftingSkill.RankNameKey(rank)), _crafting.SkillXp,
                CraftingSkill.XpForRank(rank + 1));
        _skillBar.Value = CraftingSkill.RankProgress(_crafting.SkillXp);

        switch (_tab)
        {
            case TabReforge:
                RebuildReforge();
                break;
            case TabSalvage:
                RebuildSalvage();
                break;
            default:
                RebuildCraft();
                break;
        }
    }

    // --- Craft --------------------------------------------------------------

    private void RebuildCraft()
    {
        CraftingRecipeResource? pinned = RecipeDatabase.Get(_crafting!.PinnedRecipeId);
        if (pinned != null && _crafting.Knows(pinned.Id))
        {
            // The pinned recipe leads whatever the filters say, and whatever station this is: the
            // point of a pin is to see what is still missing from wherever the player happens to be.
            AddRecipe(pinned, pinned: true);
        }
        else
        {
            pinned = null;
        }

        var locked = new List<CraftingRecipeResource>();
        bool any = pinned != null;
        foreach (CraftingRecipeResource recipe in RecipeDatabase.All)
        {
            if (ReferenceEquals(recipe, pinned) || !StationShows(recipe.Station) || !MatchesFilter(recipe))
            {
                continue;
            }

            if (!_crafting.Knows(recipe.Id))
            {
                locked.Add(recipe);
                continue;
            }

            if (_craftableOnly && !CanOrder(recipe))
            {
                continue;
            }

            any = true;
            AddRecipe(recipe, pinned: false);
        }

        if (!any)
        {
            _list.AddChild(UiTheme.Body(Loc.T("craft.recipes_none"), UiTheme.Dim));
        }

        // What is still to be learned, so a recipe the player has never seen is a goal and not a gap.
        // Hidden by the craftable-only switch: none of these can be made.
        if (locked.Count > 0 && !_craftableOnly)
        {
            _list.AddChild(UiTheme.SectionRule(Loc.T("craft.locked.header")));
            foreach (CraftingRecipeResource recipe in locked)
            {
                AddLocked(recipe);
            }
        }
    }

    private bool StationShows(CraftingStationType required) => CraftingComponent.StationAccepts(required, _station);

    private bool MatchesFilter(CraftingRecipeResource recipe)
    {
        ItemResource? output = ItemDatabase.Get(recipe.OutputItemId);
        if (_category != 0 && CategoryOf(output) != _category)
        {
            return false;
        }

        if (_query.Length == 0)
        {
            return true;
        }

        return recipe.LocalizedName.Contains(_query, System.StringComparison.OrdinalIgnoreCase) ||
            (output != null && output.DisplayName.Contains(_query, System.StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The <see cref="CategoryKeys"/> index an output falls under.</summary>
    private static int CategoryOf(ItemResource? output)
    {
        if (output is EquippableItemResource equippable)
        {
            return EquipmentSlots.FamilyOf(equippable.Slot) switch
            {
                GearFamily.Weapon => 1,
                GearFamily.Armor => 2,
                GearFamily.Accessory => 3,
                _ => 6,
            };
        }

        return output?.Type switch
        {
            ItemType.Consumable => 4,
            ItemType.Material => 5,
            _ => 6,
        };
    }

    /// <summary>Whether the Craft (or Commission) button for this recipe would be live.</summary>
    private bool CanOrder(CraftingRecipeResource recipe)
    {
        if (_crafting == null)
        {
            return false;
        }

        // At a master's desk the gate is the money, not the materials or the skill — supplying
        // those is what he is being paid for.
        return IsCommission
            ? _crafting.CanMake(recipe, _station) &&
                ShopPricing.CanAfford(CommissionQuote(recipe).Total, _inventory?.CountOf(GameIds.Currency.Gold) ?? 0)
            : _crafting.CanCraft(recipe, _station);
    }

    private void AddRecipe(CraftingRecipeResource recipe, bool pinned)
    {
        PriceQuote quote = IsCommission ? CommissionQuote(recipe) : default;
        bool canCraft = CanOrder(recipe);
        int most = IsCommission ? 1 : Mathf.Max(1, _crafting!.MaxCraftable(recipe, _station));
        int quantity = Mathf.Clamp(_quantities.GetValueOrDefault(recipe.Id, 1), 1, most);

        // The card's spine carries the *output's* rarity, so a recipe that produces something good
        // announces it before the player reads a word. A master's piece is always the plain one. A
        // recipe that cannot be made is dimmed as a whole rather than only in its title.
        ItemRarity rarity = IsCommission ? ItemRarity.Common : recipe.OutputRarity;
        PanelContainer card = UiTheme.Card(canCraft ? UiTheme.RarityColor(rarity) : UiTheme.Disabled);
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", UiTheme.SpaceMd);

        // Text stack on the left, the verbs on the right and centred against the whole stack: with the
        // button in the title line, its 44 px set the row's top band and left dead air beside the title.
        var col = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        };
        col.AddThemeConstantOverride("separation", UiTheme.LineGap);

        var titleRow = new HBoxContainer();
        titleRow.AddThemeConstantOverride("separation", UiTheme.SpaceSm);
        Label title = UiTheme.Body(recipe.LocalizedName, canCraft ? UiTheme.Text : UiTheme.Disabled);
        title.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        title.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        titleRow.AddChild(title);
        if (pinned)
        {
            titleRow.AddChild(UiTheme.Chip(Loc.T("craft.pinned"), UiTheme.Accent));
        }

        if (!StationShows(recipe.Station))
        {
            titleRow.AddChild(UiTheme.Chip(
                Loc.TF("craft.needs_station", CraftingStations.Label(recipe.Station)), UiTheme.Bad));
        }

        col.AddChild(titleRow);

        ItemResource? output = ItemDatabase.Get(recipe.OutputItemId);
        string outName = output?.DisplayName ?? recipe.OutputItemId;
        col.AddChild(UiTheme.Caption(
            Loc.TF("craft.output", recipe.OutputQuantity * quantity, outName), UiTheme.RarityColor(rarity)));

        // Ingredients as chips: green when you have enough, red when you do not. The old indented
        // "(have 2)" lines made the player do the subtraction that decides whether they can craft.
        HFlowContainer costs = UiTheme.FlowRow();
        var missing = new List<string>();
        foreach (RecipeIngredient ingredient in recipe.IngredientList())
        {
            int need = ingredient.Quantity * quantity;
            int have = _inventory?.CountOf(ingredient.ItemId) ?? 0;
            string itemName = ItemDatabase.Get(ingredient.ItemId)?.DisplayName ?? ingredient.ItemId;
            bool enough = have >= need;
            if (!enough)
            {
                missing.Add(Loc.TF("craft.amount", need - have, itemName));
            }

            // A shortfall at a master's desk is not a refusal, it is a line on the bill - so it reads
            // as him supplying it rather than as red missing materials. Same numbers, opposite
            // meaning, and showing it red would tell the player the button is broken.
            Color colour = enough ? UiTheme.Good : IsCommission ? UiTheme.Accent : UiTheme.Bad;
            costs.AddChild(UiTheme.Chip(Loc.TF("craft.have_need", itemName, have, need), colour));
        }

        col.AddChild(costs);

        if (pinned && missing.Count > 0)
        {
            Label shortfall = UiTheme.Caption(Loc.TF("craft.pinned_missing", string.Join(", ", missing)), UiTheme.Bad);
            shortfall.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            col.AddChild(shortfall);
        }

        AddOutputPreview(col, recipe, output);
        row.AddChild(col);
        row.AddChild(BuildRecipeVerbs(recipe, quote, canCraft, quantity, most, pinned));

        card.AddChild(row);
        _list.AddChild(card);
    }

    /// <summary>What the piece will be: its base stats, and the odds of fine workmanship at the
    /// player's rank. Gear only — a potion or an ingot is the same whoever makes it.</summary>
    private void AddOutputPreview(VBoxContainer col, CraftingRecipeResource recipe, ItemResource? output)
    {
        if (!IsCommission && !_crafting!.HasSkillFor(recipe))
        {
            col.AddChild(UiTheme.Caption(
                Loc.TF("craft.needs_rank", Loc.T(CraftingSkill.RankNameKey(CraftingSkill.RequiredRank(recipe.Tier)))),
                UiTheme.Bad));
        }

        if (output is not EquippableItemResource equippable || output.IsStackable)
        {
            return;
        }

        HFlowContainer stats = UiTheme.FlowRow();
        foreach ((StatType stat, float value) in equippable.StatBonuses())
        {
            if (value != 0f)
            {
                stats.AddChild(UiTheme.Chip(
                    Loc.TF("craft.stat", StatNames.Label(stat), StatsPresentation.Format(stat, value)), UiTheme.Dim));
            }
        }

        if (stats.GetChildCount() > 0)
        {
            col.AddChild(stats);
        }
        else
        {
            stats.QueueFree();
        }

        if (IsCommission)
        {
            col.AddChild(UiTheme.Caption(Loc.T("craft.commission_plain"), UiTheme.Dim));
            return;
        }

        (int fine, int superior, int masterwork) = CraftingSkill.Odds(_crafting!.SkillRank, recipe.Tier);
        col.AddChild(UiTheme.Caption(Loc.TF("craft.odds", fine, superior, masterwork), UiTheme.Dim));
    }

    private Control BuildRecipeVerbs(
        CraftingRecipeResource recipe, PriceQuote quote, bool canCraft, int quantity, int most, bool pinned)
    {
        var verbs = new VBoxContainer { SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        verbs.AddThemeConstantOverride("separation", UiTheme.SpaceXs);
        CraftingRecipeResource captured = recipe;

        // The quantity picker: only where more than one could be made, and never at a master's desk
        // (he quotes one piece at a time, and each quote depends on what the last one used up).
        if (!IsCommission && most > 1)
        {
            var picker = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
            picker.AddThemeConstantOverride("separation", UiTheme.SpaceXs);

            Button fewer = UiTheme.Action(Loc.T("craft.qty_less"));
            fewer.Disabled = quantity <= 1;
            fewer.Pressed += () => SetQuantity(captured, quantity - 1);
            picker.AddChild(fewer);

            Label count = UiTheme.Body(Loc.TF("craft.qty", quantity));
            count.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            picker.AddChild(count);

            Button more = UiTheme.Action(Loc.T("craft.qty_more"));
            more.Disabled = quantity >= most;
            more.Pressed += () => SetQuantity(captured, quantity + 1);
            picker.AddChild(more);

            Button max = UiTheme.Action(Loc.TF("craft.qty_max", most));
            max.Disabled = quantity >= most;
            max.Pressed += () => SetQuantity(captured, most);
            picker.AddChild(max);

            verbs.AddChild(picker);
        }

        Button craft = UiTheme.Action(
            IsCommission ? Loc.TF("craft.commission", quote.Total)
            : quantity > 1 ? Loc.TF("craft.craft_many", quantity)
            : Loc.T("craft.craft"));
        craft.Disabled = !canCraft;

        // 38U: the fee splits into the work and each material the player failed to bring. Without it a
        // player who walked in carrying half the recipe could not tell they had saved anything - the
        // window quoted one figure either way. A Button is hoverable even when disabled, so the
        // breakdown is readable exactly when the player is deciding whether to go and fetch the rest.
        if (IsCommission)
        {
            craft.TooltipText = PriceTooltip.Render(quote);
        }

        craft.Pressed += () => Craft(captured, quantity);
        verbs.AddChild(craft);

        Button pin = UiTheme.Action(Loc.T(pinned ? "craft.unpin" : "craft.pin"));
        pin.Pressed += () =>
        {
            _crafting?.SetPinned(pinned ? string.Empty : captured.Id);
            MarkDirty();
        };
        verbs.AddChild(pin);
        return verbs;
    }

    private void SetQuantity(CraftingRecipeResource recipe, int quantity)
    {
        _quantities[recipe.Id] = Mathf.Max(1, quantity);
        _confirm = null;
        MarkDirty();
    }

    private void Craft(CraftingRecipeResource recipe, int quantity)
    {
        _confirm = null;
        if (_crafting == null)
        {
            return;
        }

        int made = IsCommission
            ? (_crafting.Commission(recipe, _station, CommissionQuote(recipe).Total) ? 1 : 0)
            : _crafting.Craft(recipe, _station, quantity);

        string name = ItemDatabase.Get(recipe.OutputItemId)?.DisplayName ?? recipe.OutputItemId;
        if (made <= 0)
        {
            SetStatus(Loc.T("craft.status.no_room"), bad: true);
        }
        else if (!IsCommission && _crafting.LastCrafted is { IsEquippable: true, Quality: not CraftQuality.Standard } piece)
        {
            SetStatus(Loc.TF(
                "craft.status.crafted_quality", made * recipe.OutputQuantity, name, Loc.T(QualityKey(piece.Quality))));
        }
        else
        {
            SetStatus(
                Loc.TF(made < quantity ? "craft.status.crafted_some" : "craft.status.crafted", made * recipe.OutputQuantity, name),
                bad: made < quantity);
        }

        _quantities.Remove(recipe.Id);
        MarkDirty(); // rebuild next frame (events also flag it)
    }

    /// <summary>The locale key of a workmanship tier's name.</summary>
    public static string QualityKey(CraftQuality quality) => quality switch
    {
        CraftQuality.Fine => "craft.quality.fine",
        CraftQuality.Superior => "craft.quality.superior",
        CraftQuality.Masterwork => "craft.quality.masterwork",
        _ => "craft.quality.standard",
    };

    /// <summary>A recipe the player has not learned: what it makes and where to learn it, with a
    /// Study button when the scroll for it is in the pack.</summary>
    private void AddLocked(CraftingRecipeResource recipe)
    {
        PanelContainer card = UiTheme.Card(UiTheme.Disabled);
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", UiTheme.SpaceMd);

        var col = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        };
        col.AddThemeConstantOverride("separation", UiTheme.LineGap);

        var titleRow = new HBoxContainer();
        titleRow.AddThemeConstantOverride("separation", UiTheme.SpaceSm);
        Label title = UiTheme.Body(recipe.LocalizedName, UiTheme.Disabled);
        title.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        title.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        titleRow.AddChild(title);
        titleRow.AddChild(UiTheme.Chip(Loc.T("craft.locked.chip"), UiTheme.Dim));
        col.AddChild(titleRow);

        Label source = UiTheme.Caption(SourceHint(recipe), UiTheme.Dim);
        source.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        col.AddChild(source);
        row.AddChild(col);

        ItemInstance? scroll = recipe.ScrollItemId.Length > 0 ? _inventory?.FirstInstanceOf(recipe.ScrollItemId) : null;
        if (scroll != null && _crafting!.CanStudy(scroll))
        {
            Button study = UiTheme.Action(Loc.T("craft.locked.study"));
            study.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            study.Pressed += () =>
            {
                _confirm = null;
                _crafting?.StudyScroll(scroll);
                MarkDirty();
            };
            row.AddChild(study);
        }

        card.AddChild(row);
        _list.AddChild(card);
    }

    /// <summary>Where a recipe is learned: the trainer whose lesson lists it, a recipe scroll, or
    /// both.</summary>
    private static string SourceHint(CraftingRecipeResource recipe)
    {
        string trainer = string.Empty;
        foreach (ServiceResource service in ServiceDatabase.All)
        {
            if (service.Kind == ServiceKind.Trainer && service.TaughtRecipeIds.Contains(recipe.Id))
            {
                trainer = Loc.T(service.NameKey);
                break;
            }
        }

        bool scroll = recipe.ScrollItemId.Length > 0;
        if (trainer.Length > 0)
        {
            return Loc.TF(scroll ? "craft.locked.trainer_or_scroll" : "craft.locked.trainer", trainer);
        }

        return Loc.T(scroll ? "craft.locked.scroll" : "craft.locked.unknown");
    }

    // --- Reforge ------------------------------------------------------------

    private void RebuildReforge()
    {
        if (_station != CraftingStationType.Forge)
        {
            _list.AddChild(UiTheme.Body(Loc.T("craft.reforge.needs_forge"), UiTheme.Dim));
            return;
        }

        bool any = false;
        EquipmentComponent? equipment = _player?.GetComponent<EquipmentComponent>();
        if (equipment != null)
        {
            foreach (ItemInstance instance in new List<ItemInstance>(equipment.EquippedInstances))
            {
                if (IsGear(instance))
                {
                    any = true;
                    AddReforge(instance, worn: true);
                }
            }
        }

        if (_inventory != null)
        {
            foreach (ItemStack stack in _inventory.Stacks)
            {
                if (IsGear(stack.Instance))
                {
                    any = true;
                    AddReforge(stack.Instance, worn: false);
                }
            }
        }

        if (!any)
        {
            _list.AddChild(UiTheme.Body(Loc.T("craft.reforge.none"), UiTheme.Dim));
        }
    }

    private void AddReforge(ItemInstance instance, bool worn)
    {
        bool open = ReferenceEquals(_reforgeTarget, instance);
        PanelContainer card = UiTheme.Card(UiTheme.RarityColor(instance.Rarity));
        var body = new VBoxContainer();
        body.AddThemeConstantOverride("separation", UiTheme.SpaceSm);

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", UiTheme.SpaceMd);
        row.AddChild(StaticSlot(instance, 1));

        var col = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        };
        col.AddThemeConstantOverride("separation", UiTheme.LineGap);

        Label title = UiTheme.Body(instance.DisplayName, UiTheme.RarityColor(instance.Rarity));
        title.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        col.AddChild(title);

        HFlowContainer chips = UiTheme.FlowRow();
        if (worn)
        {
            chips.AddChild(UiTheme.Chip(Loc.T("craft.equipped"), UiTheme.Accent));
        }

        chips.AddChild(UiTheme.Chip(
            Loc.TF("craft.reforge.level", instance.UpgradeLevel, ItemUpgrades.MaxLevel), UiTheme.Dim));
        if (instance.Quality != CraftQuality.Standard)
        {
            chips.AddChild(UiTheme.Chip(Loc.T(QualityKey(instance.Quality)), UiTheme.Good));
        }

        if (instance.Locked)
        {
            chips.AddChild(UiTheme.Chip(Loc.T("craft.locked_item"), UiTheme.Dim));
        }

        col.AddChild(chips);
        row.AddChild(col);

        Button toggle = UiTheme.Action(Loc.T(open ? "craft.reforge.close" : "craft.reforge.open"));
        toggle.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        ItemInstance captured = instance;
        toggle.Pressed += () =>
        {
            _reforgeTarget = open ? null : captured;
            _confirm = null;
            MarkDirty();
        };
        row.AddChild(toggle);
        body.AddChild(row);

        if (open)
        {
            AddReforgeActions(body, instance);
        }

        card.AddChild(body);
        _list.AddChild(card);
    }

    private void AddReforgeActions(VBoxContainer body, ItemInstance instance)
    {
        CraftingComponent crafting = _crafting!;
        ItemInstance captured = instance;
        body.AddChild(UiTheme.Divider());

        int percent = Mathf.RoundToInt(ItemUpgrades.StatPerLevel * 100f);
        AddReforgeAction(
            body, Loc.TF("craft.reforge.upgrade_desc", percent), Loc.T("craft.reforge.upgrade"),
            crafting.UpgradeQuote(instance, _station),
            () => Report(crafting.Upgrade(captured, _station) ? captured : null));

        if (ReforgeRules.CanPromote(instance.Rarity))
        {
            AddReforgeAction(
                body, Loc.T("craft.reforge.promote_desc"), Loc.T("craft.reforge.promote"),
                crafting.PromoteQuote(instance, _station),
                () => Report(crafting.Promote(captured, _station)));
        }

        // One reroll button per affix: the player chooses which line goes, and the price is the same
        // whichever they pick (it rises with each reroll of this item, not with the affix).
        ReforgeQuote reroll = crafting.RerollQuote(instance, _station);
        for (int i = 0; i < instance.Affixes.Count; i++)
        {
            int index = i;
            ReforgeQuote quote = reroll.Allowed && !crafting.CanRerollAffix(instance, i)
                ? reroll with { BlockKey = CraftingComponent.BlockNoPool }
                : reroll;
            AddReforgeAction(
                body, Loc.TF("craft.reforge.reroll_desc", instance.Affixes[i].DisplayValue), Loc.T("craft.reforge.reroll"),
                quote, () => Report(crafting.RerollAffix(captured, index, _station)));
        }
    }

    /// <summary>One priced reforge line: what it does, what it costs (gold and metal, each coloured by
    /// whether the player has it), the button, and when refused, why. The bill's reasons are the
    /// button's tooltip, rendered by the same <see cref="PriceTooltip"/> a shop price uses.</summary>
    private void AddReforgeAction(VBoxContainer body, string description, string verb, ReforgeQuote quote, System.Action act)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", UiTheme.SpaceSm);

        var col = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        };
        col.AddThemeConstantOverride("separation", UiTheme.LineGap);

        Label text = UiTheme.Body(description);
        text.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        col.AddChild(text);

        HFlowContainer costs = UiTheme.FlowRow();
        int gold = _inventory?.CountOf(GameIds.Currency.Gold) ?? 0;
        costs.AddChild(UiTheme.Chip(Loc.TF("shop.price", quote.Gold), gold >= quote.Gold ? UiTheme.Good : UiTheme.Bad));
        if (quote.Material != null)
        {
            int have = _inventory?.CountOf(quote.Material.Id) ?? 0;
            costs.AddChild(UiTheme.Chip(
                Loc.TF("craft.have_need", quote.Material.DisplayName, have, quote.Materials),
                have >= quote.Materials ? UiTheme.Good : UiTheme.Bad));
        }

        col.AddChild(costs);

        bool blockedByMeans = quote.BlockKey is CraftingComponent.BlockGold or CraftingComponent.BlockMaterials;
        if (!quote.Allowed && !blockedByMeans)
        {
            // Short of gold or metal is already said by the red chips; anything else needs words.
            col.AddChild(UiTheme.Caption(Loc.T(quote.BlockKey), UiTheme.Bad));
        }

        row.AddChild(col);

        Button button = UiTheme.Action(verb);
        button.Disabled = !quote.Allowed;
        button.TooltipText = PriceTooltip.Render(quote.Bill);
        button.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        button.Pressed += act;
        row.AddChild(button);
        body.AddChild(row);
    }

    /// <summary>After a reforge: keep the reforged piece open (a reroll or promotion returns a new
    /// instance) and say what happened.</summary>
    private void Report(ItemInstance? reforged)
    {
        _confirm = null;
        if (reforged == null)
        {
            SetStatus(Loc.T("craft.status.reforge_failed"), bad: true);
        }
        else
        {
            _reforgeTarget = reforged;
            SetStatus(Loc.TF("craft.status.reforged", reforged.DisplayName));
        }

        MarkDirty();
    }

    // --- Salvage ------------------------------------------------------------

    /// <summary>Lists every piece of gear in the pack and every worn piece with what salvaging it
    /// here returns, and one button to break down everything marked as junk.</summary>
    private void RebuildSalvage()
    {
        bool any = false;

        List<ItemStack> junk = _crafting!.SalvageableJunk(_station);
        if (junk.Count > 0)
        {
            int pieces = 0;
            foreach (ItemStack stack in junk)
            {
                pieces += stack.Quantity;
            }

            bool armed = ReferenceEquals(_confirm, JunkConfirm);
            Button all = UiTheme.Action(Loc.TF(armed ? "craft.salvage.junk_confirm" : "craft.salvage.junk", pieces));
            all.Pressed += () =>
            {
                if (!armed)
                {
                    _confirm = JunkConfirm;
                    MarkDirty();
                    return;
                }

                _confirm = null;
                int done = _crafting?.SalvageAllJunk(_station) ?? 0;
                SetStatus(
                    Loc.TF(done < pieces ? "craft.status.salvaged_some" : "craft.status.salvaged", done),
                    bad: done < pieces);
                MarkDirty();
            };
            _list.AddChild(all);
        }

        // Loose inventory items first, then the gear the player is wearing (badged) — both salvageable.
        if (_inventory != null)
        {
            foreach (ItemStack stack in _inventory.Stacks)
            {
                if (IsGear(stack.Instance))
                {
                    any = true;
                    AddSalvage(stack.Instance, stack.Quantity, equipped: false);
                }
            }
        }

        EquipmentComponent? equipment = _player?.GetComponent<EquipmentComponent>();
        if (equipment != null)
        {
            foreach (ItemInstance instance in new List<ItemInstance>(equipment.EquippedInstances))
            {
                if (IsGear(instance))
                {
                    any = true;
                    AddSalvage(instance, 1, equipped: true);
                }
            }
        }

        if (!any)
        {
            _list.AddChild(UiTheme.Body(Loc.T("craft.salvage_none"), UiTheme.Dim));
        }
    }

    /// <summary>A single piece of gear: equippable and not a stack. Ammunition is equippable too, but
    /// a sheaf is neither salvaged nor reforged, so it is left out of both lists.</summary>
    private static bool IsGear(ItemInstance instance) => instance.IsEquippable && !instance.Template.IsStackable;

    private static Button StaticSlot(ItemInstance instance, int quantity)
    {
        Button slot = ItemSlot.Build(instance, quantity, selected: false, size: ItemSlot.RowSize);
        slot.FocusMode = Control.FocusModeEnum.None;
        slot.MouseFilter = Control.MouseFilterEnum.Ignore;
        slot.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        return slot;
    }

    /// <summary>Whether salvaging this piece asks for a second press: it is being worn, or it is
    /// something the player would be sorry to lose to a stray click.</summary>
    private static bool NeedsConfirm(ItemInstance instance, bool equipped) =>
        equipped || instance.Rarity >= ItemRarity.Epic || instance.UpgradeLevel > 0 ||
        instance.Quality != CraftQuality.Standard;

    private void AddSalvage(ItemInstance instance, int quantity, bool equipped)
    {
        // 37.5C: a card with the shared slot, matching the character screen and the stash. The
        // yields hang inside the card rather than as loose indented lines under it, so a long
        // salvage list stops reading as one undifferentiated paragraph.
        bool can = _crafting!.CanDeconstruct(instance, _station);
        PanelContainer card = UiTheme.Card(can ? UiTheme.RarityColor(instance.Rarity) : UiTheme.Disabled);
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", UiTheme.SpaceMd);
        row.AddChild(StaticSlot(instance, quantity));

        // Text stack on the left, the verb on the right and centred against the whole stack: with the
        // button in the title line, its 44 px set the row's top band and left dead air beside the title.
        var col = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        };
        col.AddThemeConstantOverride("separation", UiTheme.LineGap);

        var titleRow = new HBoxContainer();
        titleRow.AddThemeConstantOverride("separation", UiTheme.SpaceSm);

        Label title = UiTheme.Body(instance.DisplayName, UiTheme.RarityColor(instance.Rarity));
        title.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        title.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        title.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        titleRow.AddChild(title);

        // Worn gear gets an accent chip so it reads apart from loose copies before the player
        // salvages it (salvaging an equipped item takes it off first).
        if (equipped)
        {
            PanelContainer badge = UiTheme.Chip(Loc.T("craft.equipped"), UiTheme.Accent);
            badge.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            titleRow.AddChild(badge);
        }

        if (instance.Locked)
        {
            // A locked piece is listed, not hidden: a row that says why it cannot be salvaged is
            // better than a piece of gear that has silently gone missing from the list.
            titleRow.AddChild(UiTheme.Chip(Loc.T("craft.locked_item"), UiTheme.Dim));
        }

        col.AddChild(titleRow);

        // ⚠️ The preview is the plan the component then pays out: one computation, not two.
        SalvagePlan plan = _crafting.PlanSalvage(instance, _station);
        HFlowContainer yields = UiTheme.FlowRow();
        foreach ((ItemResource material, int amount) in plan.Materials)
        {
            yields.AddChild(UiTheme.Chip(Loc.TF("craft.amount", amount, material.DisplayName), UiTheme.Accent));
        }

        yields.AddChild(UiTheme.Chip(Loc.TF("craft.yield_xp", plan.Xp), UiTheme.Good));
        if (!plan.FromRecipe && plan.BetterStation != CraftingStationType.Hand)
        {
            yields.AddChild(UiTheme.Chip(
                Loc.TF("craft.salvage.better_station", CraftingStations.Label(plan.BetterStation)), UiTheme.Dim));
        }

        col.AddChild(yields);
        row.AddChild(col);

        bool armed = ReferenceEquals(_confirm, instance);
        Button button = UiTheme.Action(Loc.T(armed ? "craft.salvage.confirm" : "craft.deconstruct"));
        button.Disabled = !can;
        ItemInstance captured = instance;
        button.Pressed += () =>
        {
            if (!armed && NeedsConfirm(captured, equipped))
            {
                _confirm = captured;
                MarkDirty();
                return;
            }

            _confirm = null;
            bool done = _crafting?.Deconstruct(captured, _station) ?? false;
            SetStatus(
                done ? Loc.TF("craft.status.salvaged", 1) : Loc.T("craft.status.no_room"), bad: !done);
            MarkDirty(); // rebuild next frame (events also flag it)
        };
        button.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        row.AddChild(button);

        card.AddChild(row);
        _list.AddChild(card);
    }
}
