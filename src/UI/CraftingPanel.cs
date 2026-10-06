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
    private const int TabCount = 3;

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
        "craft.toast.rank_up", "craft.order.hint", "craft.order.hint_commission",
    };

    /// <summary>The confirm token for "salvage all junk" (an item is its own token).</summary>
    private static readonly object JunkConfirm = new();

    private Label _title = null!;
    private Control _wipe = null!;
    private UiTabs _modeTabs = null!;
    private Label _skill = null!;
    private ProgressBar _skillBar = null!;
    private Control _craftBar = null!;
    private LineEdit _search = null!;
    private Label _status = null!;

    // The Craft page: recipes | ingredients | result, and the order bar under them.
    private Control _craftPage = null!;
    private Label _recipeHeader = null!;
    private Label _recipeNote = null!;
    private VBoxContainer _recipeList = null!;
    private Control _ingredientColumn = null!;
    private ScrollContainer _ingredientScroll = null!;
    private Label _ingredientNote = null!;
    private VBoxContainer _ingredients = null!;
    private ScrollContainer _resultScroll = null!;
    private VBoxContainer _result = null!;
    private HFlowContainer _order = null!;

    // The Reforge and Salvage pages: one list.
    private ScrollContainer _listScroll = null!;
    private VBoxContainer _list = null!;

    private readonly List<Button> _recipeRows = new();

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

    /// <summary>The recipe the ingredient and result columns describe.</summary>
    private string _selectedRecipe = string.Empty;
    private Button? _selectedRow;
    private Button? _selectedSlot;
    private ItemInstance? _selectedSlotItem;

    /// <summary>The order bar's first verb (Craft, Commission or Study), for stepping into it.</summary>
    private Button? _primaryVerb;

    private bool _detailDirty;

    /// <summary>Focus goes to the selected recipe after the next rebuild (on opening).</summary>
    private bool _focusSelection;

    /// <summary>Focus goes to the order bar's verb once it is built (a recipe row was pressed).</summary>
    private bool _focusVerb;

    /// <summary>The piece whose reforge options are open.</summary>
    private ItemInstance? _reforgeTarget;

    /// <summary>What a destructive button is waiting on a second press for: the item, or
    /// <see cref="JunkConfirm"/>. Any other action clears it.</summary>
    private object? _confirm;

    private string _statusText = string.Empty;
    private bool _statusBad;

    protected override bool Dims => true;

    protected override IReadOnlyList<LegendEntry> Legend
    {
        get
        {
            var entries = new List<LegendEntry>();
            if (_tab == TabCraft && _recipeRows.Count > 0)
            {
                entries.Add(new LegendEntry("ui_accept", Loc.T("trade.legend.choose")));
                entries.Add(new LegendEntry(ItemSlot.CompareAction, Loc.T("item.detail.compare")));
                if (InputDevice.GamepadActive)
                {
                    entries.Add(new LegendEntry(GameInput.LookDown, Loc.T("trade.legend.details")));
                }
            }

            entries.Add(new LegendEntry(GameInput.MenuSubPrev, Loc.T("trade.legend.mode"), GameInput.MenuSubNext));
            entries.AddRange(base.Legend);
            return entries;
        }
    }

    /// <summary>Z/C and LT/RT step Craft, Reforge and Salvage, wrapping at the ends.</summary>
    protected override void OnSubTab(int delta)
    {
        UiAudio.Play(UiCue.Tab);
        _modeTabs.Select(TradeRules.StepTab(_tab, delta, TabCount));
    }

    protected override void BuildShell(PanelContainer shell)
    {
        UiTheme.ApplyScreenInset(shell);

        // Craft / Reforge / Salvage - static layout; the pages rebuild below.
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

        VBoxContainer column = UiTheme.TradePage(
            shell, UiIcon.Kind.Material, out _title, out HBoxContainer aside, out _wipe, _modeTabs);

        // The crafting skill, always in view beside the station's name: it gates recipes and sets the
        // workmanship odds, so it is the first thing a "why can I not make this" question needs.
        _skill = UiTheme.Caption(string.Empty, UiTheme.Dim);
        _skill.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        aside.AddChild(_skill);
        _skillBar = UiTheme.Bar(UiTheme.Accent, SkillBarWidth);
        _skillBar.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        aside.AddChild(_skillBar);

        column.AddChild(BuildCraftBar());

        _status = UiTheme.Caption(string.Empty);
        _status.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _status.Visible = false;
        column.AddChild(_status);

        column.AddChild(BuildCraftPage());

        // The shared list of the other two pages: row gap, and a gutter so the scrollbar stays off the
        // row borders. No fixed height (a literal overflowed the Steam Deck's 533 px viewport once
        // already); the shell sets the floor.
        (_listScroll, _list) = UiTheme.ScrollList();
        _listScroll.Visible = false;
        column.AddChild(_listScroll);
    }

    private const float SkillBarWidth = 120f;
    private const float OrderNoteMin = 96f;

    /// <summary>
    /// The Craft page: what can be made on the left, what the chosen recipe takes in the middle,
    /// what it makes on the right, and under all three the order bar with how many and the verbs.
    /// </summary>
    private Control BuildCraftPage()
    {
        var page = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        page.AddThemeConstantOverride("separation", UiTheme.SpaceSm);

        var columns = new HBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        columns.AddThemeConstantOverride("separation", UiTheme.SpaceMd);
        page.AddChild(columns);

        columns.AddChild(UiTheme.TradeColumn(0f, out _recipeHeader, out _recipeNote, out _recipeList));
        _recipeHeader.Text = Loc.T("craft.col.recipes");
        columns.AddChild(UiTheme.ColumnRule());

        _ingredientColumn = UiTheme.TradeColumn(1f, out Label needs, out _ingredientNote, out _ingredients);
        needs.Text = Loc.T("craft.col.ingredients");
        _ingredientScroll = (ScrollContainer)_ingredients.GetParent().GetParent();
        columns.AddChild(_ingredientColumn);
        columns.AddChild(UiTheme.ColumnRule());

        (_resultScroll, _result) = UiTheme.ScrollList();
        _resultScroll.SizeFlagsHorizontal = Control.SizeFlags.Fill;
        _result.AddThemeConstantOverride("separation", UiTheme.SpaceSm);
        columns.AddChild(_resultScroll);

        // Wraps rather than widen the page when a handheld cannot hold the picker and three verbs.
        _order = UiTheme.FlowRow();
        _order.CustomMinimumSize = new Vector2(0f, UiTheme.ControlHeight);
        page.AddChild(_order);

        _craftPage = page;
        return page;
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
            CustomMinimumSize = new Vector2(0f, UiTheme.ControlHeight),
        };
        UiSkin.Apply(_search);
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
        _search.TextSubmitted += _ => _focusSelection = true; // back to the recipes, not to no focus at all
        bar.AddChild(_search);

        var names = new string[CategoryKeys.Count];
        for (int i = 0; i < names.Length; i++)
        {
            names[i] = Loc.T(CategoryKeys[i]);
        }

        OptionButton filter = UiTheme.Dropdown(names, 0);
        filter.CustomMinimumSize = new Vector2(0f, UiTheme.ControlHeight);
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
        EventBus.Instance?.Subscribe<InputDeviceChangedEvent>(OnDeviceChanged);
        EventBus.Instance?.Subscribe<InventoryChangedEvent>(OnInventoryChanged);
        EventBus.Instance?.Subscribe<ItemCraftedEvent>(OnItemCrafted);
        EventBus.Instance?.Subscribe<ItemDeconstructedEvent>(OnItemDeconstructed);
        EventBus.Instance?.Subscribe<RecipeLearnedEvent>(OnRecipeLearned);
    }

    public override void _ExitTree()
    {
        EventBus.Instance?.Unsubscribe<CraftingStationOpenedEvent>(OnStationOpened);
        EventBus.Instance?.Unsubscribe<InputDeviceChangedEvent>(OnDeviceChanged);
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
        _selectedRecipe = string.Empty;
        _quantities.Clear();
        SetStatus(string.Empty);
        _modeTabs.Select(TabCraft);
        SetOpen(true);
        _focusSelection = true;
        _focusVerb = false;

        // The same interact press that opened the station is still "just pressed" this
        // frame; swallow it so the close-on-interact below doesn't fire immediately.
        _justOpened = true;
    }

    protected override void OnOpenChanged(bool open)
    {
        if (open)
        {
            UiOrnament.PlayEmberWipe(_wipe);
            ResetDetailScroll();
        }
        else
        {
            // Esc closes through the base SetOpen, not Close(), and the keyboard coming back must
            // not depend on the hidden field's focus signal arriving.
            GameInput.SetTextEntry(false);
        }
    }

    private void OnInventoryChanged(InventoryChangedEvent e) => MarkDirty();

    /// <summary>The legend carries an entry only a pad has (scroll details).</summary>
    private void OnDeviceChanged(InputDeviceChangedEvent e) => MarkDirty();

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
        if (!IsOpen || _tab != TabCraft)
        {
            return;
        }

        // The panel focuses its first control when it opens (a tab); a station starts on a recipe.
        if (_focusSelection)
        {
            _focusSelection = false;
            if (_selectedRow != null && IsInstanceValid(_selectedRow))
            {
                _selectedRow.GrabFocus();
            }
            else if (GetViewport().GuiGetFocusOwner() is LineEdit)
            {
                UiFocus.GrabFirst(Shell); // a search that matched nothing
            }
        }

        // A selection that moved within the list: the two columns beside it follow, the list stays.
        if (_detailDirty)
        {
            RebuildCraftDetail();
        }

        if (_focusVerb)
        {
            _focusVerb = false;
            if (_primaryVerb != null && IsInstanceValid(_primaryVerb))
            {
                _primaryVerb.GrabFocus();
            }
        }

        TradeRow.StickScroll(_resultScroll, delta);
        TradeRow.StickScroll(_ingredientScroll, delta);
    }

    /// <summary>Whether this window is a master's order desk rather than a free public station (38Q).
    /// Keyed off the shop, not the fee: without somewhere to price materials there is nothing to
    /// supply, and a fee alone would charge for what the forge outside does for nothing.</summary>
    private bool IsCommission => _materialsShop != null;

    /// <summary>The master's quote and the reasons for it (38U). ⚠️ The price charged is
    /// <c>Total</c> — the same number the breakdown's last line shows, because they are one
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
        // Re-read on every rebuild: the UI scale can change mid-session.
        UiTheme.ApplyScreenInset(Shell);
        float usable = UiTheme.UsableWidth(Shell);
        _ingredientColumn.CustomMinimumSize = new Vector2(TradeRules.IngredientWidth(usable), 0f);
        _resultScroll.CustomMinimumSize = new Vector2(TradeRules.DetailWidth(usable), 0f);

        UiTheme.ClearChildren(_list);
        UiTheme.ClearChildren(_recipeList);
        _recipeRows.Clear();
        _selectedRow = null;
        _selectedSlot = null;
        _selectedSlotItem = null;

        UiTheme.SetTradeTitle(_title, _stationName);
        bool craft = _tab == TabCraft;
        _craftBar.Visible = craft;
        _craftPage.Visible = craft;
        _listScroll.Visible = !craft;
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

    /// <summary>
    /// The recipe column: the pinned recipe, then what can be made now, then the rest
    /// (<see cref="TradeRules.OrderRecipes{T}"/>), and under a rule the recipes still to be learned.
    /// </summary>
    private void RebuildCraft()
    {
        CraftingComponent crafting = _crafting!;
        CraftingRecipeResource? pinned = RecipeDatabase.Get(crafting.PinnedRecipeId);
        if (pinned != null && !crafting.Knows(pinned.Id))
        {
            pinned = null;
        }

        var known = new List<CraftingRecipeResource>();
        var locked = new List<CraftingRecipeResource>();
        if (pinned != null)
        {
            // The pinned recipe leads whatever the filters say, and whatever station this is: the
            // point of a pin is to see what is still missing from wherever the player happens to be.
            known.Add(pinned);
        }

        foreach (CraftingRecipeResource recipe in RecipeDatabase.All)
        {
            if (ReferenceEquals(recipe, pinned) || !StationShows(recipe.Station) || !MatchesFilter(recipe))
            {
                continue;
            }

            if (!crafting.Knows(recipe.Id))
            {
                locked.Add(recipe);
            }
            else if (!_craftableOnly || CanOrder(recipe))
            {
                known.Add(recipe);
            }
        }

        // What is still to be learned, so a recipe the player has never seen is a goal and not a gap.
        // Hidden by the craftable-only switch: none of these can be made.
        if (_craftableOnly)
        {
            locked.Clear();
        }

        List<CraftingRecipeResource> ordered = TradeRules.OrderRecipes(
            known, r => new TradeRules.RecipeKey(ReferenceEquals(r, pinned), CanOrder(r), r.LocalizedName));
        locked.Sort((a, b) => string.Compare(a.LocalizedName, b.LocalizedName, System.StringComparison.OrdinalIgnoreCase));

        // Keep the selection on a recipe that is still listed; otherwise the first one.
        bool listed = false;
        foreach (CraftingRecipeResource recipe in ordered)
        {
            listed |= recipe.Id == _selectedRecipe;
        }

        foreach (CraftingRecipeResource recipe in locked)
        {
            listed |= recipe.Id == _selectedRecipe;
        }

        if (!listed)
        {
            _selectedRecipe = ordered.Count > 0 ? ordered[0].Id : locked.Count > 0 ? locked[0].Id : string.Empty;
        }

        _recipeNote.Text = Loc.TF("craft.col.count", ordered.Count);
        foreach (CraftingRecipeResource recipe in ordered)
        {
            AddRecipeRow(recipe, ReferenceEquals(recipe, pinned), known: true);
        }

        if (ordered.Count == 0)
        {
            _recipeList.AddChild(UiTheme.Body(Loc.T("craft.recipes_none"), UiTheme.Dim));
        }

        if (locked.Count > 0)
        {
            _recipeList.AddChild(UiTheme.SectionRule(Loc.T("craft.locked.header")));
            foreach (CraftingRecipeResource recipe in locked)
            {
                AddRecipeRow(recipe, pinned: false, known: false);
            }
        }

        RebuildCraftDetail();
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

    /// <summary>The piece a recipe turns out, for its picture and its card. A master's piece is always
    /// the plain one; a recipe whose output is not in the catalogue has none.</summary>
    private ItemInstance? Preview(CraftingRecipeResource recipe) =>
        ItemDatabase.Get(recipe.OutputItemId) is { } output
            ? new ItemInstance(output, IsCommission ? output.Rarity : recipe.OutputRarity)
            : null;

    /// <summary>
    /// One recipe in the list: the output's picture, the recipe's name, a quiet line (pinned, the
    /// station it needs, how many could be made) and at the end a mark - a tick when it can be made
    /// now, a cross when it cannot, a plus when a master will supply what is missing. The mark is
    /// the shape that says it; the dimmed name only agrees. Taking focus selects the row; pressing it
    /// steps into the order bar.
    /// </summary>
    private void AddRecipeRow(CraftingRecipeResource recipe, bool pinned, bool known)
    {
        bool can = known && CanOrder(recipe);
        ItemInstance? preview = Preview(recipe);
        Color edge = !can ? UiTheme.Disabled : UiTheme.RarityColor(preview?.Rarity ?? ItemRarity.Common);
        PanelContainer card = UiTheme.CardButton(
            edge, out Button input, out VBoxContainer content, UiTheme.TradeRowStyle(edge));

        var row = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", UiTheme.SpaceSm);

        bool selected = recipe.Id == _selectedRecipe;
        Button slot = ItemSlot.Build(preview, recipe.OutputQuantity, selected, ItemSlot.CompactSize);
        slot.FocusMode = Control.FocusModeEnum.None;
        slot.MouseFilter = Control.MouseFilterEnum.Ignore;
        slot.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        row.AddChild(slot);

        var text = new VBoxContainer
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        };
        text.AddThemeConstantOverride("separation", 0);

        Label title = UiTheme.Body(recipe.LocalizedName, can ? UiTheme.Text : UiTheme.Disabled);
        title.MouseFilter = Control.MouseFilterEnum.Ignore;
        title.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        text.AddChild(title);

        var notes = new List<string>();
        if (pinned)
        {
            notes.Add(Loc.T("craft.pinned"));
        }

        if (!known)
        {
            notes.Add(Loc.T("craft.locked.chip"));
        }
        else if (!StationShows(recipe.Station))
        {
            notes.Add(Loc.TF("craft.needs_station", CraftingStations.Label(recipe.Station)));
        }
        else if (can && !IsCommission)
        {
            notes.Add(Loc.TF("craft.can_make", Mathf.Max(1, _crafting!.MaxCraftable(recipe, _station))));
        }

        if (notes.Count > 0)
        {
            Label note = UiTheme.Caption(string.Join("   ·   ", notes));
            note.MouseFilter = Control.MouseFilterEnum.Ignore;
            note.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
            text.AddChild(note);
        }

        row.AddChild(text);
        if (known)
        {
            row.AddChild(new TradeMark(
                can && IsCommission && !_crafting!.HasIngredients(recipe) ? TradeRules.IngredientState.Supplied
                : can ? TradeRules.IngredientState.Enough
                : TradeRules.IngredientState.Short));
        }
        else
        {
            TextureRect padlock = UiIcon.Create(UiIcon.Kind.Lock, UiTheme.CaptionFontSize + 2f, UiTheme.Disabled);
            padlock.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            row.AddChild(padlock);
        }

        content.AddChild(row);
        input.TooltipText = recipe.LocalizedName;

        string id = recipe.Id;
        input.FocusEntered += () => SelectRecipe(id, slot, preview);
        input.Pressed += () =>
        {
            SelectRecipe(id, slot, preview);
            _focusVerb = true;
        };

        if (selected)
        {
            _selectedRow = input;
            _selectedSlot = slot;
            _selectedSlotItem = preview;
        }

        _recipeList.AddChild(card);
        _recipeRows.Add(input);
    }

    private void SelectRecipe(string id, Button slot, ItemInstance? preview)
    {
        if (_focusSelection || id == _selectedRecipe)
        {
            return; // a rebuild restoring focus must not undo the selection it opened on
        }

        if (_selectedSlot != null && IsInstanceValid(_selectedSlot))
        {
            ItemSlot.SetSelected(_selectedSlot, _selectedSlotItem, false);
        }

        _selectedRecipe = id;
        _selectedSlot = slot;
        _selectedSlotItem = preview;
        ItemSlot.SetSelected(slot, preview, true);
        _confirm = null;
        ResetDetailScroll();
        _detailDirty = true;
    }

    /// <summary>Another recipe's ingredients and card open at their head, not where the last were left.</summary>
    private void ResetDetailScroll()
    {
        _ingredientScroll.ScrollVertical = 0;
        _resultScroll.ScrollVertical = 0;
    }

    /// <summary>The ingredient column, the result column and the order bar for the selected recipe.</summary>
    private void RebuildCraftDetail()
    {
        _detailDirty = false;
        _primaryVerb = null;
        UiTheme.ClearChildren(_ingredients);
        UiTheme.ClearChildren(_result);
        UiTheme.ClearChildren(_order);
        _ingredientNote.Text = string.Empty;

        if (_crafting is not { } crafting || RecipeDatabase.Get(_selectedRecipe) is not { } recipe)
        {
            OrderNote(Loc.T("craft.order.none"), bad: false);
            WireFocus();
            return;
        }

        bool known = crafting.Knows(recipe.Id);
        bool pinned = recipe.Id == crafting.PinnedRecipeId;
        PriceQuote quote = known && IsCommission ? CommissionQuote(recipe) : default;
        bool canCraft = known && CanOrder(recipe);
        int most = !known || IsCommission ? 1 : Mathf.Max(1, crafting.MaxCraftable(recipe, _station));
        int quantity = Mathf.Clamp(_quantities.GetValueOrDefault(recipe.Id, 1), 1, most);

        BuildIngredients(recipe, quantity, known, pinned);
        BuildResult(recipe, quote, known);

        if (!known)
        {
            BuildStudyOrder(recipe);
        }
        else
        {
            BuildCraftOrder(recipe, quote, canCraft, quantity, most, pinned);
        }

        WireFocus();
    }

    /// <summary>
    /// What the recipe takes, one line per ingredient: a mark, the ingredient's picture and name, and
    /// held over needed. The mark is a tick, a cross or a plus (<see cref="TradeMark"/>), so the line
    /// reads without its colour. ⚠️ A shortfall at a master's desk is not a refusal, it is a line on
    /// the bill - so it carries the plus rather than the cross. Same numbers, opposite meaning.
    /// </summary>
    private void BuildIngredients(CraftingRecipeResource recipe, int quantity, bool known, bool pinned)
    {
        _ingredientNote.Text = quantity > 1 ? Loc.TF("craft.qty", quantity) : string.Empty;

        var missing = new List<string>();
        foreach (RecipeIngredient ingredient in recipe.IngredientList())
        {
            int need = ingredient.Quantity * quantity;
            int have = _inventory?.CountOf(ingredient.ItemId) ?? 0;
            ItemResource? item = ItemDatabase.Get(ingredient.ItemId);
            string itemName = item?.DisplayName ?? ingredient.ItemId;
            TradeRules.IngredientState state = TradeRules.IngredientOf(have, need, IsCommission);
            if (state != TradeRules.IngredientState.Enough)
            {
                missing.Add(Loc.TF("craft.amount", need - have, itemName));
            }

            _ingredients.AddChild(IngredientLine(item, itemName, have, need, state));
        }

        if (known && pinned && missing.Count > 0 && !IsCommission)
        {
            Label shortfall = UiTheme.Caption(Loc.TF("craft.pinned_missing", string.Join(", ", missing)), UiTheme.Bad);
            shortfall.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            _ingredients.AddChild(shortfall);
        }

        if (known && !StationShows(recipe.Station))
        {
            _ingredients.AddChild(Wrapped(UiTheme.Caption(
                Loc.TF("craft.needs_station", CraftingStations.Label(recipe.Station)), UiTheme.Bad)));
        }

        if (known && !IsCommission && !_crafting!.HasSkillFor(recipe))
        {
            _ingredients.AddChild(Wrapped(UiTheme.Caption(
                Loc.TF("craft.needs_rank", Loc.T(CraftingSkill.RankNameKey(CraftingSkill.RequiredRank(recipe.Tier)))),
                UiTheme.Bad)));
        }
    }

    private static Control IngredientLine(ItemResource? item, string itemName, int have, int need, TradeRules.IngredientState state)
    {
        var line = new HBoxContainer();
        line.AddThemeConstantOverride("separation", UiTheme.SpaceXs);
        line.AddChild(new TradeMark(state));

        if (item != null)
        {
            line.AddChild(StaticSlot(ItemInstance.Plain(item), 1, ItemSlot.CompactSize));
        }

        Label name = UiTheme.Body(itemName);
        name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        name.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        name.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        name.TooltipText = itemName;
        line.AddChild(name);

        Label count = UiTheme.Body(Loc.TF("craft.have_of_need", have, need), TradeMark.ColorOf(state));
        count.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        line.AddChild(count);
        return line;
    }

    private static Label Wrapped(Label label)
    {
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        return label;
    }

    /// <summary>
    /// What the piece will be: the shared item card for the output, compared against what is worn,
    /// then how many a craft makes and the odds of fine workmanship at the player's rank (gear only —
    /// a potion or an ingot is the same whoever makes it). A recipe not yet learned shows its card
    /// too, with where to learn it: a goal should be something the player can look at.
    /// </summary>
    private void BuildResult(CraftingRecipeResource recipe, PriceQuote quote, bool known)
    {
        ItemInstance? preview = Preview(recipe);
        if (preview != null)
        {
            _result.AddChild(ItemSlot.Detail(preview, new ItemSlot.DetailContext(
                _player?.GetComponent<EquipmentComponent>(),
                _player?.GetComponent<Progression.ProgressionComponent>()?.Level ?? 0,
                Compare: true)));
        }

        if (!known)
        {
            _result.AddChild(Wrapped(UiTheme.Caption(SourceHint(recipe))));
            return;
        }

        if (recipe.OutputQuantity > 1)
        {
            _result.AddChild(UiTheme.Caption(Loc.TF("craft.makes", recipe.OutputQuantity)));
        }

        // 38U: the fee splits into the work and each material the player failed to bring. Without it a
        // player who walked in carrying half the recipe could not tell they had saved anything. Printed
        // rather than hidden behind a hover, because a pad has no pointer to hover with.
        if (IsCommission)
        {
            var bill = new VBoxContainer();
            bill.AddThemeConstantOverride("separation", UiTheme.LineGap);
            bill.AddChild(UiTheme.Caption(Loc.T("trade.price_reasons"), UiTheme.Accent));
            foreach (string line in PriceTooltip.Lines(quote))
            {
                bill.AddChild(Wrapped(UiTheme.Caption(line, UiTheme.Text)));
            }

            _result.AddChild(bill);
        }

        if (preview is not { IsEquippable: true } || preview.Template.IsStackable)
        {
            return;
        }

        if (IsCommission)
        {
            _result.AddChild(Wrapped(UiTheme.Caption(Loc.T("craft.commission_plain"))));
            return;
        }

        (int fine, int superior, int masterwork) = CraftingSkill.Odds(_crafting!.SkillRank, recipe.Tier);
        _result.AddChild(Wrapped(UiTheme.Caption(Loc.TF("craft.odds", fine, superior, masterwork))));
    }

    private Label OrderNote(string text, bool bad)
    {
        Label note = Wrapped(UiTheme.Caption(text, bad ? UiTheme.Bad : UiTheme.Dim));
        note.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        note.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        note.CustomMinimumSize = new Vector2(OrderNoteMin, 0f);
        _order.AddChild(note);
        return note;
    }

    private Button OrderVerb(string text)
    {
        Button button = UiTheme.Action(text);
        button.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        _order.AddChild(button);
        return button;
    }

    /// <summary>
    /// The order bar for a known recipe: how many (only where more than one could be made, and never
    /// at a master's desk - he quotes one piece at a time, and each quote depends on what the last one
    /// used up), Craft, Craft max in one press, and the pin. A recipe that cannot be made keeps its
    /// Craft, greyed, with the reason beside it, and pressing it plays the refusal.
    /// </summary>
    private void BuildCraftOrder(
        CraftingRecipeResource recipe, PriceQuote quote, bool canCraft, int quantity, int most, bool pinned)
    {
        CraftingRecipeResource captured = recipe;
        string reason = canCraft ? string.Empty : RefusalOf(recipe, quote);
        OrderNote(reason.Length > 0 ? reason : Loc.T(IsCommission ? "craft.order.hint_commission" : "craft.order.hint"), bad: reason.Length > 0);

        Button craft = UiTheme.Action(string.Empty);
        craft.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        if (!canCraft)
        {
            craft.AddThemeColorOverride("font_color", UiTheme.Disabled);
        }

        // The picker reports and the bar keeps the number. The ingredient column is redrawn for the
        // new amount; the bar is not, because a rebuild of it would free the slider mid-drag.
        int shown = quantity;
        void Requote(int count)
        {
            _quantities[captured.Id] = count;
            craft.Text = IsCommission ? Loc.TF("craft.commission", quote.Total)
                : count > 1 ? Loc.TF("craft.craft_many", count)
                : Loc.T("craft.craft");
            if (count != shown)
            {
                shown = count;
                UiTheme.ClearChildren(_ingredients);
                BuildIngredients(captured, count, known: true, pinned);
            }
        }

        if (canCraft && most > 1)
        {
            _order.AddChild(QuantityPicker.Build(1, most, quantity, Requote));
        }

        craft.Pressed += () => Craft(captured, Mathf.Clamp(_quantities.GetValueOrDefault(captured.Id, 1), 1, most));
        _order.AddChild(craft);
        _primaryVerb = craft;

        if (canCraft && most > 1)
        {
            OrderVerb(Loc.TF("craft.qty_max", most)).Pressed += () => Craft(captured, most);
        }

        OrderVerb(Loc.T(pinned ? "craft.unpin" : "craft.pin")).Pressed += () =>
        {
            _crafting?.SetPinned(pinned ? string.Empty : captured.Id);
            MarkDirty();
        };

        Requote(quantity);
    }

    /// <summary>The order bar for a recipe not yet learned: Study, when the scroll for it is in the pack.</summary>
    private void BuildStudyOrder(CraftingRecipeResource recipe)
    {
        OrderNote(SourceHint(recipe), bad: false);

        ItemInstance? scroll = recipe.ScrollItemId.Length > 0 ? _inventory?.FirstInstanceOf(recipe.ScrollItemId) : null;
        if (scroll == null || !_crafting!.CanStudy(scroll))
        {
            return;
        }

        Button study = OrderVerb(Loc.T("craft.locked.study"));
        study.Pressed += () =>
        {
            _confirm = null;
            bool learned = _crafting?.StudyScroll(scroll) ?? false;
            UiAudio.Play(learned ? UiCue.Confirm : UiCue.Denied);
            MarkDirty();
        };
        _primaryVerb = study;
    }

    /// <summary>Why a known recipe cannot be ordered right now, in the order the player can fix it.</summary>
    private string RefusalOf(CraftingRecipeResource recipe, PriceQuote quote)
    {
        if (!StationShows(recipe.Station))
        {
            return Loc.TF("craft.needs_station", CraftingStations.Label(recipe.Station));
        }

        if (IsCommission)
        {
            int gold = _inventory?.CountOf(GameIds.Currency.Gold) ?? 0;
            return Loc.TF("trade.need_more", TradeRules.Shortfall(quote.Total, gold));
        }

        return !_crafting!.HasSkillFor(recipe)
            ? Loc.TF("craft.needs_rank", Loc.T(CraftingSkill.RankNameKey(CraftingSkill.RequiredRank(recipe.Tier))))
            : Loc.T("craft.order.missing");
    }

    /// <summary>Up and down the recipe list, and right from it into the order bar. Run once the rows
    /// and the bar are in the tree.</summary>
    private void WireFocus()
    {
        Control? verb = _primaryVerb != null && IsInstanceValid(_primaryVerb) ? _primaryVerb : null;
        TradeRow.WireColumn(_recipeRows, null, verb);
    }

    private void Craft(CraftingRecipeResource recipe, int quantity)
    {
        _confirm = null;
        if (_crafting == null)
        {
            return;
        }

        if (!CanOrder(recipe))
        {
            // Nothing is attempted: the order bar already says why, and the press answers with the refusal.
            UiAudio.Play(UiCue.Denied);
            return;
        }

        int made = IsCommission
            ? (_crafting.Commission(recipe, _station, CommissionQuote(recipe).Total) ? 1 : 0)
            : _crafting.Craft(recipe, _station, quantity);

        string name = ItemDatabase.Get(recipe.OutputItemId)?.DisplayName ?? recipe.OutputItemId;
        if (made <= 0)
        {
            SetStatus(Loc.T("craft.status.no_room"), bad: true);
            UiAudio.Play(UiCue.Denied);
        }
        else
        {
            // The one flourish a finished piece gets: the rule under the station's name is drawn
            // again by its line of heat, with the confirm cue. Under reduced motion it is the cue alone.
            UiOrnament.PlayEmberWipe(_wipe);
            UiAudio.Play(UiCue.Confirm);

            if (!IsCommission && _crafting.LastCrafted is { IsEquippable: true, Quality: not CraftQuality.Standard } piece)
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
        }

        // The bar is rebuilt around a new amount (the picker may be gone, the verb greyed); focus
        // goes back to its first verb rather than to whichever button now sits where Craft was.
        _quantities.Remove(recipe.Id);
        _focusVerb = true;
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

    // --- Capture hooks (src/Debugging/TradeShots.cs) ------------------------

    /// <summary>Recipe rows drawn in the Craft page's list on the last rebuild.</summary>
    public int ShownRecipeCount => _recipeRows.Count;

    /// <summary>Whether the Craft page is showing a recipe's ingredients and its result card.</summary>
    public bool ShowingThreeColumns =>
        _tab == TabCraft && _craftPage.Visible && _ingredients.GetChildCount() > 0 && _result.GetChildCount() > 0;

    /// <summary>The open page: 0 Craft, 1 Reforge, 2 Salvage.</summary>
    public int Mode => _tab;

    /// <summary>Opens a page by index, as the tab strip or Z/C would.</summary>
    public void ShowModeForCapture(int mode) => _modeTabs.Select(Mathf.Clamp(mode, 0, TabCount - 1));

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
        UiAudio.Play(reforged == null ? UiCue.Denied : UiCue.Confirm);
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
                UiAudio.Play(done > 0 ? UiCue.Confirm : UiCue.Denied);
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

    private static Button StaticSlot(ItemInstance instance, int quantity, float size = ItemSlot.RowSize)
    {
        Button slot = ItemSlot.Build(instance, quantity, selected: false, size: size);
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
            UiAudio.Play(done ? UiCue.Confirm : UiCue.Denied);
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
