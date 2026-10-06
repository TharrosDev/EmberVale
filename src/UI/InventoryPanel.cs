using System.Collections.Generic;
using Embervale.Combat;
using Embervale.Core;
using Embervale.Core.Events;
using Embervale.Core.Services;
using Embervale.Corruption;
using Embervale.Crafting;
using Embervale.Factions;
using Embervale.Items;
using Embervale.Localization;
using Embervale.Magic;
using Embervale.Progression;
using Godot;

namespace Embervale.UI;

/// <summary>
/// The character screen: toggled with the <c>inventory</c> action, it shows the
/// equipment slots (with Unequip buttons) and the backpack contents (with Equip
/// buttons on equippable stacks). Built on the 30.5F <see cref="UiPanel"/> framework
/// (the proving port): the base owns the modal contract, the toggle input, and the
/// dirty-flag rebuild loop; tabs ride the shared <see cref="UiTabs"/> strip.
/// </summary>
public partial class InventoryPanel : UiPanel
{
    private InventoryComponent? _inventory;
    private EquipmentComponent? _equipment;
    private HotbarComponent? _hotbar;
    private ProgressionComponent? _progression;
    private PerksComponent? _perks;
    private ReputationComponent? _reputation;
    private Dialogue.StoryFlagsComponent? _flags;
    private CorruptionComponent? _corruption;
    private Embervale.Stats.StatsComponent? _stats;
    private UiTabs _tabs = null!;
    private VBoxContainer _list = null!;
    private ScrollContainer _scroll = null!;

    // --- Gear tab shell (2026-10) ----------------------------------------------
    // Built once. The strip and the three columns keep their place between rebuilds, and each
    // column scrolls on its own, so the detail pane stays put while the pack scrolls under it.
    private Control _gear = null!;
    private HFlowContainer _strip = null!;
    private VBoxContainer _equipColumn = null!;
    private VBoxContainer _equipList = null!;
    private VBoxContainer _packHead = null!;
    private VBoxContainer _packList = null!;
    private VBoxContainer _detailColumn = null!;
    private VBoxContainer _detailList = null!;

    // --- Gear tab state (37.5C) ------------------------------------------------
    // The Gear tab is a grid + detail pane rather than a text list, so it needs a selection and a
    // sort/filter. The other three tabs are still lists and still rebuild into _list.
    private ItemInstance? _selected;

    /// <summary>The sort order. Static so it outlives the panel: the UI root is rebuilt with each
    /// session, and a player who sorts by value expects it to still be by value after a load. It is
    /// a preference of this run of the game and is deliberately not saved.</summary>
    private static ItemPresentation.SortOrder _sort = ItemPresentation.SortOrder.Rarity;

    /// <summary>Category filter; null shows everything.</summary>
    private ItemType? _filter;

    /// <summary>The equipment slot the player is choosing for; null shows the whole pack. Focusing a
    /// slot in the equipment column sets it, and the pack then lists only what fits that slot.</summary>
    private EquipmentSlot? _equipFocus;

    // Focus moves selection in place, without a rebuild: these say which parts of the Gear tab are
    // out of date and are served on the next frame (never inside the focus signal that set them).
    private bool _packStale;
    private bool _detailStale;

    // The frame the selection last moved on, and whether the press in flight is the click that
    // moved it. A mouse click focuses a cell and then presses it; that press is the selection and
    // must not also count as "press the selected item again to equip it".
    private ulong _selectedFrame = ulong.MaxValue;
    private bool _pressSelected;

    // Whether the press in flight came from the mouse. A second click on a piece of gear puts it on;
    // accept on a pad or the keys steps into the detail pane, which nothing else leads to.
    private bool _pressByMouse;

    // Set by a full rebuild and cleared once the base has put focus back. Focus arriving on a control
    // because the rebuild restored it there is not the player walking onto it, and must not narrow
    // the pack, leave the material bag or end the choosing for a slot.
    private bool _restoring;

    // Whether focus was in the detail pane when the rebuild began. An action there can empty the pane
    // (unequip, use, drop), and focus then goes back to the cell it came from, not to the screen's first tab.
    private bool _paneHadFocus;

    /// <summary>The pack's cells by the instance each shows, and the equipment column's rows in
    /// order, for restyling a selection and wiring focus without rebuilding either. A row's cell is
    /// the pressable laid over the whole row; its well is the framed slot drawn inside it.</summary>
    private readonly Dictionary<ItemInstance, Button> _cellOf = new(ReferenceEqualityComparer.Instance);
    private readonly List<(EquipmentSlot Slot, ItemInstance? Item, Button Cell, Button Well)> _equipCells = new();

    /// <summary>The strip's focusable controls of the current fill, for the same focus wiring.</summary>
    private readonly List<Control> _stripControls = new();

    /// <summary>What was held when the pack was last closed, and what has arrived since. By reference:
    /// two rolled swords share a template and are different things. Session-local and never saved;
    /// a load makes everything known again.</summary>
    private readonly HashSet<ItemInstance> _seen = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<ItemInstance> _fresh = new(ReferenceEqualityComparer.Instance);

    /// <summary>False shows the pack's slots, true the material bag.</summary>
    private bool _bagView;

    /// <summary>The typed search. Lives here rather than in the field, so a rebuild reads it without
    /// touching the control.</summary>
    private string _query = string.Empty;

    /// <summary>The search row. Built once in the shell, not per rebuild: a text field that is freed
    /// and recreated on every keystroke loses its caret and its focus each time.</summary>
    private HBoxContainer _toolRow = null!;
    private LineEdit _search = null!;

    /// <summary>What the detail pane is asking about the selected stack, if anything. A question,
    /// not a mode: selecting something else or closing the screen drops it.</summary>
    private enum Pending { None, Split, Drop }

    private Pending _pending;
    private int _splitQuantity = 1;

    /// <summary>
    /// Columns in the backpack grid, derived from the viewport each rebuild (37.5G).
    ///
    /// 37.5C fixed this at 8 and claimed a reflowing count would break `UiFocus` restore. That was
    /// wrong: restore walks *child indices*, and the grid's child order does not change when it
    /// wraps differently — only the visual rows do. What the fixed count actually did was overflow
    /// the panel by 321 px on a Steam Deck at UI scale 1.5, where the logical viewport is 853 px
    /// wide rather than the ~1900 the number was quietly assuming.
    /// </summary>
    private int _gridColumns = 8;

    /// <summary>The focusable backpack cells of the current rebuild, held between building the grid
    /// and wiring its focus neighbours — see <see cref="LinkFocus"/> for why those cannot be
    /// the same step.</summary>
    private readonly List<Button> _gridCells = new();

    /// <summary>Slot edge length. Below the 44 px touch/legibility floor a glyph stops reading.</summary>
    private const float SlotSize = 48f;

    /// <summary>Width of one Progression section column (stats, corruption, standing).</summary>
    private const float SectionColumn = 320f;

    private float _sideWidth = 230f;
    private float _detailWidth = 300f;

    private static readonly (ItemPresentation.SortOrder Order, string Key)[] SortDefs =
    {
        (ItemPresentation.SortOrder.Name, "item.sort_name"),
        (ItemPresentation.SortOrder.Rarity, "item.sort_rarity"),
        (ItemPresentation.SortOrder.Type, "item.sort_type"),
        (ItemPresentation.SortOrder.Level, "item.sort_level"),
        (ItemPresentation.SortOrder.Weight, "item.sort_weight"),
        (ItemPresentation.SortOrder.Value, "item.sort_value"),
    };

    /// <summary>The character screen's tabs (Phase 29.5 spell tab + split progression/perks) —
    /// indices match the <see cref="UiTabs"/> order built in <see cref="BuildShell"/>.</summary>
    /// <summary>The character screen's tabs. Spells left in 37.5D for <see cref="SpellbookPanel"/> —
    /// magic had been a fourth tab wearing the gear screen's chrome, which is the opposite of the
    /// distinct arcane identity it needed.</summary>
    /// <summary>Guilds joined the strip in 42A: membership, rank and the five orders' standing are
    /// the one thing the character screen could not answer, and they do not belong under
    /// Progression — that tab is about the player's own numbers, this one about who claims them.</summary>
    private enum CharTab { Gear, Progression, Perks, Guilds }

    private CharTab _activeTab = CharTab.Gear;

    /// <summary>The perk tree's open branch, focused perk and pending respec, which the per-change rebuild would
    /// otherwise forget.</summary>
    private readonly PerkTreePanel.ViewState _perkView = new();

    private static readonly (CharTab Tab, string Key)[] TabDefs =
    {
        (CharTab.Gear, "char.tab_gear"),
        (CharTab.Progression, "char.tab_progression"),
        (CharTab.Perks, "char.tab_perks"),
        (CharTab.Guilds, "char.tab_guilds"),
    };

    protected override string? ToggleAction => GameInput.Inventory;

    protected override HubTab? Hub => HubTab.Character;

    private Label _title = null!;

    /// <summary>The page width under which the title leaves the tab row: the four tabs and the
    /// search field need the whole of a handheld's row.</summary>
    private const float TitleMinWidth = 1000f;

    protected override void BuildShell(PanelContainer shell)
    {
        UiTheme.ApplyScreenInset(shell);

        // The same cut plate as the journal, the map and the bestiary: one lit edge along the top
        // and no box. This screen kept the full iron frame, and beside its four neighbours in the
        // hub it read as a window from another game.
        UiTheme.ApplyHubPlate(shell);

        MarginContainer margin = UiTheme.Padding(UiTheme.PanelPad);
        shell.AddChild(margin);

        var column = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        column.AddThemeConstantOverride("separation", UiTheme.SpaceSm);
        margin.AddChild(column);

        // No title ROW: the screen has two rows of its own controls already and no height to spare.
        // The title the other hub screens carry sits at the head of the tab row instead, and gives
        // way on a narrow viewport (Rebuild), where the hub strip above the frame is name enough.
        _title = UiTheme.Title(Loc.T("ui.hub.character"));
        _title.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;

        // Tab row (Gear · Spells · Progression · Perks) — built once; only _list rebuilds per tab.
        _tabs = new UiTabs();
        foreach ((CharTab _, string key) in TabDefs)
        {
            _tabs.Add(Loc.T(key));
        }

        _tabs.TabChanged += index =>
        {
            _activeTab = TabDefs[index].Tab;
            _perkView.ConfirmingRespec = false;
            _perkView.DeniedId = null;
            MarkDirty();
        };
        // The tabs and the search share one row. ⚠️ The field comes after the tabs, and beside them
        // rather than under them, for two reasons. The panel focuses its first focusable control
        // when it opens, and a text field holding focus takes the gameplay keys out of the map -
        // including the one that closes this screen. And a d-pad going down from the tabs to the
        // grid must not pass through a text field on the way: on a handheld that raises the
        // on-screen keyboard every time the player walks past it.
        var tabRow = new HBoxContainer();
        tabRow.AddThemeConstantOverride("separation", UiTheme.SpaceMd);
        _tabs.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        tabRow.AddChild(_title);
        tabRow.AddChild(_tabs);
        column.AddChild(tabRow);

        _toolRow = new HBoxContainer();
        _toolRow.AddThemeConstantOverride("separation", UiTheme.SpaceSm);
        _toolRow.AddChild(Centred(UiTheme.Caption(Loc.T("item.search"))));
        _search = new LineEdit
        {
            PlaceholderText = Loc.T("item.search_placeholder"),
            ClearButtonEnabled = true,
            CustomMinimumSize = new Vector2(180f, UiTheme.ControlHeight),
        };
        UiTheme.ApplyType(_search, UiTheme.FontRole.Interface, UiTheme.BodyFontSize);
        _search.TextChanged += text =>
        {
            _query = text;
            MarkDirty();
        };

        // While the field has focus the gameplay keys are out of the map (GameInput.SetTextEntry),
        // or typing "bow" would open the bestiary. Enter hands focus back to the grid.
        _search.FocusEntered += () => GameInput.SetTextEntry(true);
        _search.FocusExited += () => GameInput.SetTextEntry(false);
        _search.TextSubmitted += _ =>
        {
            if (_gridCells.Count > 0 && IsInstanceValid(_gridCells[0]) && _gridCells[0].IsInsideTree())
            {
                _gridCells[0].GrabFocus();
            }
            else
            {
                _search.ReleaseFocus();
            }
        };
        _toolRow.AddChild(_search);
        tabRow.AddChild(_toolRow);

        UiSkin.Apply(_search);

        _gear = BuildGearShell();
        column.AddChild(_gear);

        (_scroll, _list) = UiTheme.ScrollList();
        column.AddChild(_scroll);
    }

    /// <summary>The Gear tab's fixed frame: one strip of view, sort and filter controls over three
    /// columns that each scroll on their own. Only what is inside them is rebuilt.</summary>
    private Control BuildGearShell()
    {
        var gear = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        gear.AddThemeConstantOverride("separation", UiTheme.SpaceSm);

        _strip = UiTheme.FlowRow();
        gear.AddChild(_strip);

        var columns = new HBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        columns.AddThemeConstantOverride("separation", UiTheme.SpaceLg);
        gear.AddChild(columns);

        (_equipColumn, VBoxContainer equipHead, _equipList) = GearColumn(expand: false);
        equipHead.AddChild(UiTheme.SectionRule(Loc.T("char.equipment"), first: true));
        columns.AddChild(_equipColumn);

        (VBoxContainer pack, _packHead, _packList) = GearColumn(expand: true);
        columns.AddChild(pack);

        (_detailColumn, VBoxContainer detailHead, _detailList) = GearColumn(expand: false);
        detailHead.AddChild(UiTheme.SectionRule(Loc.T("char.details"), first: true));
        columns.AddChild(_detailColumn);
        return gear;
    }

    /// <summary>One column of the Gear tab: a heading that stays, over a list that scrolls.</summary>
    private static (VBoxContainer Column, VBoxContainer Head, VBoxContainer List) GearColumn(bool expand)
    {
        var column = new VBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        if (expand)
        {
            column.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        }

        column.AddThemeConstantOverride("separation", UiTheme.SpaceSm);
        var head = new VBoxContainer();
        column.AddChild(head);

        (ScrollContainer scroll, VBoxContainer list) = UiTheme.ScrollList();
        column.AddChild(scroll);
        return (column, head, list);
    }

    /// <summary>Opens the Gear tab on the material bag, through the same switch a click on its tab
    /// throws (for `--panelshots`).</summary>
    public void ShowMaterials()
    {
        _bagView = true;
        _equipFocus = null;
        _pending = Pending.None;
        _selected = _inventory?.Materials.Count > 0 ? _inventory.Materials[0].Instance : null;
        _tabs.Select(0);
        MarkDirty();
    }

    /// <summary>Whether the Gear tab is showing the material bag (read by `--panelshots`).</summary>
    public bool ShowingMaterials => _activeTab == CharTab.Gear && _bagView;

    /// <summary>Selects the Guilds tab through the real tab strip (42A), so `--panelshots` drives
    /// the same path a click does rather than reaching past it into <c>_activeTab</c>.</summary>
    public void ShowGuilds()
    {
        for (int i = 0; i < TabDefs.Length; i++)
        {
            if (TabDefs[i].Tab == CharTab.Guilds)
            {
                _tabs.Select(i);
                return;
            }
        }
    }

    /// <summary>Selects the Progression tab (level, XP and the stat block) through the real tab strip.</summary>
    public void ShowProgression()
    {
        for (int i = 0; i < TabDefs.Length; i++)
        {
            if (TabDefs[i].Tab == CharTab.Progression)
            {
                _tabs.Select(i);
                return;
            }
        }
    }

    /// <summary>Selects the Perks tab through the real tab strip, optionally on one branch and with the respec confirmation
    /// showing: the state a click on a branch tab and on Respec leaves, so `--panelshots` photographs what a player reaches.</summary>
    public void ShowPerks(PerkBranch? branch = null, bool confirmRespec = false)
    {
        _perkView.Branch = branch ?? _perkView.Branch;
        _perkView.FocusedId = null;
        _perkView.ConfirmingRespec = confirmRespec;
        MarkDirty();
        for (int i = 0; i < TabDefs.Length; i++)
        {
            if (TabDefs[i].Tab == CharTab.Perks)
            {
                _tabs.Select(i);
                _perkView.ConfirmingRespec = confirmRespec; // the tab handler clears a pending respec
                return;
            }
        }
    }

    /// <summary>The perk tree's view state: open branch, focused perk, pending respec (read by `--panelshots`).</summary>
    public PerkTreePanel.ViewState PerkTreeState => _perkView;

    /// <summary>Opens the authored equipment / backpack / inspection composition through the real tab strip.</summary>
    public void ShowGear()
    {
        _bagView = false;
        _equipFocus = null;
        if (_selected == null && _inventory?.Stacks.Count > 0)
        {
            _selected = _inventory.Stacks[0].Instance;
        }

        _tabs.Select(0);
        MarkDirty();
    }

    protected override void OnOpenChanged(bool open)
    {
        // A respec confirmation is a moment, not a mode: reopening the screen must not land on a pending one.
        _perkView.ConfirmingRespec = false;
        _perkView.DeniedId = null;
        _pending = Pending.None;

        if (open)
        {
            // What arrived while the pack was shut wears the new pip for as long as this visit lasts,
            // or until it is looked at.
            _fresh.Clear();
            foreach (ItemInstance arrived in ItemPresentation.NewSince(HeldInstances(), _seen))
            {
                _fresh.Add(arrived);
            }
        }
        else
        {
            MarkAllSeen();
            _equipFocus = null;

            // A hidden field gives up focus on its own, but the keyboard coming back must not depend
            // on that signal arriving.
            GameInput.SetTextEntry(false);
        }
    }

    /// <summary>Everything in the pack, the material bag and on the body, as instances.</summary>
    private IEnumerable<ItemInstance> HeldInstances()
    {
        if (_inventory != null)
        {
            foreach (ItemStack stack in _inventory.AllStacks)
            {
                yield return stack.Instance;
            }
        }

        if (_equipment != null)
        {
            foreach (ItemInstance worn in _equipment.EquippedInstances)
            {
                yield return worn;
            }
        }
    }

    /// <summary>Makes everything held known, so nothing wears the new pip: on closing the pack, and
    /// after a load, which rebuilds every instance and would otherwise mark the whole pack new.</summary>
    private void MarkAllSeen()
    {
        _seen.Clear();
        _fresh.Clear();
        foreach (ItemInstance held in HeldInstances())
        {
            _seen.Add(held);
        }
    }

    /// <summary>The footer legend: what accept does on this tab, the comparison toggle on the Gear tab,
    /// the sub-tab step, then the hub's own entries.</summary>
    protected override IReadOnlyList<LegendEntry> Legend
    {
        get
        {
            var entries = new List<LegendEntry>();
            if (_activeTab == CharTab.Gear)
            {
                entries.Add(new LegendEntry("ui_accept", Loc.T("char.legend.actions")));
                entries.Add(new LegendEntry(ItemSlot.CompareAction, Loc.T("item.detail.compare")));
            }
            else if (_activeTab == CharTab.Perks)
            {
                entries.Add(new LegendEntry("ui_accept", Loc.T("char.legend.learn")));
            }

            entries.Add(new LegendEntry(GameInput.MenuSubPrev, Loc.T("char.legend.tabs"), GameInput.MenuSubNext));
            entries.AddRange(base.Legend);
            return entries;
        }
    }

    /// <summary>Z/C or LT/RT walk this screen's tabs as one run: Pack, Materials, Progression, Perks,
    /// Guilds, wrapping at the ends (<see cref="InventoryTabRules"/>).</summary>
    protected override void OnSubTab(int delta)
    {
        int stop = InventoryTabRules.StopOf(_tabs.Current, _bagView);
        (int tab, bool bag) = InventoryTabRules.FromStop(InventoryTabRules.Step(stop, delta, TabDefs.Length));

        UiAudio.Play(UiCue.Tab);

        // Focus left in the equipment column would be put back there by the rebuild, on a column that
        // belongs to the pack view. It moves to the view's own tab, and down from there is the grid.
        int view = bag ? 1 : 0;
        if (tab == 0 && _activeTab == CharTab.Gear && FocusIsIn(_equipList) && view < _stripControls.Count
            && IsInstanceValid(_stripControls[view]) && _stripControls[view].IsInsideTree())
        {
            _stripControls[view].GrabFocus();
        }

        _bagView = bag;
        _equipFocus = null;
        _pending = Pending.None;
        if (tab != _tabs.Current)
        {
            _tabs.Select(tab); // its handler marks the panel dirty
        }
        else
        {
            MarkDirty();
        }
    }

    /// <summary>Serves the in-place refreshes focus asked for (<see cref="_packStale"/>,
    /// <see cref="_detailStale"/>) after the base has run any full rebuild.</summary>
    public override void _Process(double delta)
    {
        base._Process(delta);
        bool paneHadFocus = _restoring && _paneHadFocus;
        _restoring = false;
        _paneHadFocus = false;
        if (!IsOpen || _activeTab != CharTab.Gear)
        {
            return;
        }

        if (paneHadFocus && !FocusIsIn(_detailList))
        {
            Control? back = SelectionCell() ?? (_gridCells.Count > 0 && _gridCells[0].IsInsideTree() ? _gridCells[0] : null);
            back?.GrabFocus();
        }

        if (!_packStale && !_detailStale)
        {
            return;
        }

        if (_packStale)
        {
            FillStrip();
            FillPack();
        }

        FillDetail();
        LinkFocus();
        _packStale = false;
        _detailStale = false;
    }

    protected override void OnReady()
    {
        EventBus.Instance?.Subscribe<InventoryChangedEvent>(OnChanged);
        EventBus.Instance?.Subscribe<EquipmentChangedEvent>(OnEquipmentChanged);
        EventBus.Instance?.Subscribe<XpGainedEvent>(OnXpGained);
        EventBus.Instance?.Subscribe<LeveledUpEvent>(OnLeveledUp);
        EventBus.Instance?.Subscribe<PerkChangedEvent>(OnPerkChanged);
        EventBus.Instance?.Subscribe<ReputationChangedEvent>(OnReputationChanged);
        EventBus.Instance?.Subscribe<CorruptionChangedEvent>(OnCorruptionChanged);
        EventBus.Instance?.Subscribe<Dialogue.StoryFlagChangedEvent>(OnStoryFlagChanged);

        // ⚠️ Invariant 10 — a wholesale load does NOT replay the individual flag events, so a panel
        // that only listens to StoryFlagChangedEvent would keep drawing the abandoned timeline's
        // membership after a quickload. This screen had no load subscription at all before 42A.
        EventBus.Instance?.Subscribe<GameLoadedEvent>(OnGameLoaded);
    }

    public override void _ExitTree()
    {
        EventBus.Instance?.Unsubscribe<InventoryChangedEvent>(OnChanged);
        EventBus.Instance?.Unsubscribe<EquipmentChangedEvent>(OnEquipmentChanged);
        EventBus.Instance?.Unsubscribe<XpGainedEvent>(OnXpGained);
        EventBus.Instance?.Unsubscribe<LeveledUpEvent>(OnLeveledUp);
        EventBus.Instance?.Unsubscribe<PerkChangedEvent>(OnPerkChanged);
        EventBus.Instance?.Unsubscribe<ReputationChangedEvent>(OnReputationChanged);
        EventBus.Instance?.Unsubscribe<CorruptionChangedEvent>(OnCorruptionChanged);
        EventBus.Instance?.Unsubscribe<Dialogue.StoryFlagChangedEvent>(OnStoryFlagChanged);
        EventBus.Instance?.Unsubscribe<GameLoadedEvent>(OnGameLoaded);
        GameInput.SetTextEntry(false);
    }

    public void SetInventory(InventoryComponent? inventory)
    {
        _inventory = inventory;
        MarkAllSeen();
        MarkDirty();
    }

    public void SetEquipment(EquipmentComponent? equipment)
    {
        _equipment = equipment;
        MarkAllSeen();
        MarkDirty();
    }

    public void SetHotbar(HotbarComponent? hotbar)
    {
        _hotbar = hotbar;
        MarkDirty();
    }

    public void SetProgression(ProgressionComponent? progression)
    {
        _progression = progression;
        MarkDirty();
    }

    public void SetPerks(PerksComponent? perks)
    {
        _perks = perks;
        MarkDirty();
    }

    public void SetReputation(ReputationComponent? reputation)
    {
        _reputation = reputation;
        MarkDirty();
    }

    /// <summary>The player's story flags — the only membership authority the Guilds tab reads
    /// (Phase 42A). The panel derives every guild line from these and never writes one.</summary>
    public void SetStoryFlags(Dialogue.StoryFlagsComponent? flags)
    {
        _flags = flags;
        MarkDirty();
    }

    public void SetCorruption(CorruptionComponent? corruption)
    {
        _corruption = corruption;
        MarkDirty();
    }

    /// <summary>The player's stats. Wired in 37.5C2 - before that this panel had no reference to
    /// StatsComponent at all, which is why the game had never shown the player their own Armor,
    /// Crit Chance or any of the six Phase 34E resistances.</summary>
    public void SetStats(Embervale.Stats.StatsComponent? stats)
    {
        _stats = stats;
        MarkDirty();
    }

    private void OnChanged(InventoryChangedEvent e) => MarkDirty();

    private void OnEquipmentChanged(EquipmentChangedEvent e) => MarkDirty();

    private void OnXpGained(XpGainedEvent e) => MarkDirty();

    private void OnLeveledUp(LeveledUpEvent e) => MarkDirty();

    private void OnPerkChanged(PerkChangedEvent e) => MarkDirty();

    private void OnReputationChanged(ReputationChangedEvent e) => MarkDirty();

    private void OnCorruptionChanged(CorruptionChangedEvent e) => MarkDirty();

    private void OnStoryFlagChanged(Dialogue.StoryFlagChangedEvent e) => MarkDirty();

    private void OnGameLoaded(GameLoadedEvent e)
    {
        MarkAllSeen();
        MarkDirty();
    }

    protected override void Rebuild()
    {
        _restoring = true;
        _paneHadFocus = FocusIsIn(_detailList);
        UiTheme.ClearChildren(_list);

        // Re-derived per rebuild so a mid-session UI-scale change lands without a restart.
        UiTheme.ApplyScreenInset(Shell);
        bool roomForTitle = UiTheme.UsableWidth(Shell) >= TitleMinWidth;
        if (_title.Visible != roomForTitle)
        {
            _title.Visible = roomForTitle;
        }

        MeasureColumns();
        bool gear = _activeTab == CharTab.Gear;
        _toolRow.Visible = gear;
        _gear.Visible = gear;
        _scroll.Visible = !gear;
        _packStale = false;
        _detailStale = false;

        switch (_activeTab)
        {
            case CharTab.Progression:
                BuildProgression();
                break;
            case CharTab.Perks:
                BuildPerks();
                break;
            case CharTab.Guilds:
                BuildGuilds();
                break;
            default:
                BuildGear();
                break;
        }
    }

    private void BuildFactions(Container sections)
    {
        if (_reputation == null || FactionDatabase.All.Count == 0)
        {
            return;
        }

        VBoxContainer section = Section(Loc.T("char.reputation"));
        sections.AddChild(section);

        // Corruption inflicts a global "dread" penalty (Phase 23G): the world reacts to the
        // earned standing lowered by dread, so show the world's effective tier and call out
        // why it dropped.
        int dread = _reputation.Dread;
        if (dread > 0)
        {
            section.AddChild(UiTheme.Body(Loc.TF("char.dread", dread), UiTheme.CorruptionText));
        }

        foreach (FactionResource faction in FactionDatabase.All)
        {
            int value = _reputation.Get(faction.Id);
            ReputationTier tier = ReputationTiers.Of(_reputation.Effective(faction.Id));
            section.AddChild(UiTheme.Body(
                Loc.TF("char.rep_line", faction.DisplayName, ReputationTiers.DisplayName(tier), value.ToString("+0;-0;0")),
                UiTheme.ReputationColor(tier)));
        }
    }

    /// <summary>
    /// The Guilds tab (42A): one card per guild, every line DERIVED from the player's story flags
    /// plus the authored <see cref="FactionResource"/>. The panel is not an authority — it holds no
    /// membership state, and closing it loses nothing.
    /// </summary>
    private void BuildGuilds()
    {
        _list.AddChild(UiTheme.SectionRule(Loc.T("guild.header"), first: true));

        System.Predicate<string> has = _flags != null ? _flags.Has : _ => false;
        bool any = false;

        foreach (FactionResource guild in FactionDatabase.All)
        {
            if (!guild.IsGuild)
            {
                continue;
            }

            any = true;
            GuildStanding standing = GuildRules.Resolve(has, guild);
            _list.AddChild(GuildCard(guild, standing));
        }

        if (!any)
        {
            AddLine(Loc.T("guild.none"), UiTheme.Dim);
        }
    }

    private PanelContainer GuildCard(FactionResource guild, GuildStanding standing)
    {
        PanelContainer card = UiTheme.Card(standing.IsMember ? UiTheme.Accent : UiTheme.Dim);
        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", UiTheme.SpaceXs);

        var head = new HBoxContainer();
        head.AddThemeConstantOverride("separation", UiTheme.SpaceMd);
        head.AddChild(UiTheme.Header(guild.DisplayName));
        head.AddChild(Centred(UiTheme.Chip(GuildStateName(standing.State),
            standing.IsMember ? UiTheme.Accent : UiTheme.GlyphLight)));
        col.AddChild(head);

        // The rank line names the rank the player HOLDS, from the guild's own authored key list —
        // never a number on its own, which reads as a score rather than a place in an order.
        col.AddChild(UiTheme.Body(standing.Rank > 0
            ? Loc.TF("guild.rank_line", Loc.T(guild.RankNameKeys[standing.Rank - 1]), standing.Rank, guild.RankNameKeys.Count)
            : Loc.T("guild.rank_none"), standing.Rank > 0 ? null : UiTheme.Dim));

        if (_reputation != null)
        {
            ReputationTier tier = ReputationTiers.Of(_reputation.Effective(guild.Id));
            col.AddChild(UiTheme.Caption(Loc.TF("guild.standing_line",
                ReputationTiers.DisplayName(tier), _reputation.Get(guild.Id).ToString("+0;-0;0")),
                UiTheme.ReputationColor(tier)));
        }

        // A contradiction is a bad authored/saved record, not a player state. It is surfaced rather
        // than hidden: the alternative is a screen that quietly renders a rank nothing awarded.
        if (standing.Contradiction != GuildContradiction.None)
        {
            col.AddChild(UiTheme.Caption(Loc.TF("guild.contradiction", standing.Contradiction), UiTheme.CorruptionText));
        }

        card.AddChild(col); // the card's own margins are the padding; a second pad doubled the left edge
        return card;
    }

    private static string GuildStateName(GuildState state) => Loc.T(state switch
    {
        GuildState.Offered => "guild.state.offered",
        GuildState.Refused => "guild.state.refused",
        GuildState.Member => "guild.state.member",
        GuildState.Left => "guild.state.left",
        GuildState.Finale => "guild.state.finale",
        _ => "guild.state.unknown",
    });

    /// <summary>Level card on top, then the stat, corruption and standing sections as a wrapping row of
    /// equal columns: three sections side by side fit the 1280x720 viewport without a scroll, and a
    /// narrow handheld viewport folds them under each other.</summary>
    private void BuildProgression()
    {
        BuildLevelCard();

        HFlowContainer sections = UiTheme.FlowRow();
        sections.AddThemeConstantOverride("h_separation", UiTheme.SpaceLg);
        sections.AddThemeConstantOverride("v_separation", UiTheme.SpaceMd);
        _list.AddChild(sections);

        BuildStats(sections);
        BuildCorruption(sections);
        BuildFactions(sections);
    }

    /// <summary>One column of the Progression tab: a titled rule over its rows.</summary>
    private static VBoxContainer Section(string title)
    {
        var section = new VBoxContainer { CustomMinimumSize = new Vector2(SectionColumn, 0f) };
        section.AddThemeConstantOverride("separation", UiTheme.SpaceSm);
        section.AddChild(UiTheme.SectionRule(title, first: true));
        return section;
    }

    /// <summary>The level card: a badge, the XP meter, and the unspent points as chips. Replaces
    /// three loose text lines - the points in particular were a sentence the player had to read to
    /// discover they had something to spend.</summary>
    private void BuildLevelCard()
    {
        if (_progression == null)
        {
            return;
        }

        PanelContainer card = UiTheme.Card(UiTheme.Accent);
        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", UiTheme.SpaceXs);

        var head = new HBoxContainer();
        head.AddThemeConstantOverride("separation", UiTheme.SpaceMd);

        Label badge = UiTheme.Display(Loc.TF("char.level_badge", _progression.Level));
        badge.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        head.AddChild(badge);

        // Unspent points are the only actionable thing on this tab, so they read as chips rather
        // than as prose. Nothing is shown when there is nothing to spend.
        if (_progression.SkillPoints > 0)
        {
            head.AddChild(Centred(UiTheme.Chip(Loc.TF("char.points_skill", _progression.SkillPoints), UiTheme.Accent)));
        }

        if (_progression.SpellPoints > 0)
        {
            head.AddChild(Centred(UiTheme.Chip(Loc.TF("char.points_spell", _progression.SpellPoints), UiTheme.GlyphLight)));
        }

        col.AddChild(head);

        if (_progression.IsMaxLevel || _progression.XpToNext <= 0)
        {
            col.AddChild(UiTheme.Caption(Loc.T("char.xp_max")));
        }
        else
        {
            col.AddChild(UiTheme.Caption(Loc.TF("char.xp_progress", _progression.CurrentXp, _progression.XpToNext)));
            ProgressBar bar = UiTheme.Bar(UiTheme.Accent);
            bar.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            bar.Value = _progression.CurrentXp / (double)_progression.XpToNext;
            col.AddChild(bar);
        }

        card.AddChild(col); // the card's own margins are the padding
        _list.AddChild(card);
    }

    /// <summary>
    /// The stat block - new in 37.5C2, because the game had never displayed one.
    ///
    /// Defence stats carry their derived mitigation percentage beside the raw number. "Armor 8" is
    /// opaque and the curve is hyperbolic, so a player cannot infer that it removes about 7% of a
    /// hit, nor that doubling it is not double the benefit. The percentage comes from
    /// CombatMath.ArmorMultiplier itself, so the screen cannot disagree with combat.
    /// </summary>
    private void BuildStats(Container sections)
    {
        if (_stats == null)
        {
            return;
        }

        for (int index = 0; index < StatsPresentation.Sections.Length; index++)
        {
            (string headerKey, Embervale.Stats.StatType[] stats) = StatsPresentation.Sections[index];
            VBoxContainer section = Section(Loc.T(headerKey));
            sections.AddChild(section);

            // One stat leads each group at display size; the rest follow as rows. Which one leads is
            // the character's own doing (the higher of physical and spell power, the highest primary).
            Embervale.Stats.StatType? hero = StatsPresentation.SectionHero(index, _stats.GetValue);
            if (hero is { } lead)
            {
                section.AddChild(BuildHeroStat(lead));
            }

            var grid = new GridContainer { Columns = 2 };
            grid.AddThemeConstantOverride("h_separation", UiTheme.SpaceLg);
            grid.AddThemeConstantOverride("v_separation", UiTheme.GridGap);

            foreach (Embervale.Stats.StatType stat in stats)
            {
                if (stat == hero)
                {
                    continue;
                }

                float value = _stats.GetValue(stat);
                grid.AddChild(UiTheme.Body(Embervale.Stats.StatNames.Label(stat), UiTheme.Dim));

                var right = new HBoxContainer();
                right.AddThemeConstantOverride("separation", UiTheme.SpaceSm);

                Label reading = UiTheme.Body(StatsPresentation.Format(stat, value));
                reading.HorizontalAlignment = HorizontalAlignment.Right;
                reading.CustomMinimumSize = new Vector2(58f, 0f);
                right.AddChild(reading);

                if (StatsPresentation.IsMitigation(stat))
                {
                    float pct = StatsPresentation.MitigationFraction(value) * 100f;
                    right.AddChild(UiTheme.Caption(
                        Loc.TF("char.stat_reduced", pct.ToString("0.#")),
                        value > 0f ? UiTheme.Good : UiTheme.Disabled));
                }

                grid.AddChild(right);

                if (Embervale.Stats.StatDerivation.IsPrimary(stat))
                {
                    grid.AddChild(new Control());
                    Label perPoint = UiTheme.Caption(PerPointText(stat));
                    perPoint.AutowrapMode = TextServer.AutowrapMode.WordSmart;
                    perPoint.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
                    grid.AddChild(perPoint);
                }
            }

            section.AddChild(grid);
        }
    }

    /// <summary>A group's leading stat: the number at display size, its name beside it, and under the
    /// name what the number means (the damage it removes, or what a point of it buys).</summary>
    private Control BuildHeroStat(Embervale.Stats.StatType stat)
    {
        float value = _stats!.GetValue(stat);

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", UiTheme.SpaceMd);
        row.AddChild(UiTheme.Display(StatsPresentation.Format(stat, value), UiTheme.Text));

        var side = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        };
        side.AddThemeConstantOverride("separation", 0);
        side.AddChild(UiTheme.Body(Embervale.Stats.StatNames.Label(stat), UiTheme.Accent));

        if (StatsPresentation.IsMitigation(stat))
        {
            float pct = StatsPresentation.MitigationFraction(value) * 100f;
            side.AddChild(UiTheme.Caption(
                Loc.TF("char.stat_reduced", pct.ToString("0.#")),
                value > 0f ? UiTheme.Good : UiTheme.Disabled));
        }
        else if (Embervale.Stats.StatDerivation.IsPrimary(stat))
        {
            Label perPoint = UiTheme.Caption(PerPointText(stat));
            perPoint.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            side.AddChild(perPoint);
        }

        row.AddChild(side);
        return row;
    }

    /// <summary>"Per point: +0.8 Physical Power" for a primary, built from <see cref="StatsPresentation.PerPoint"/>.</summary>
    private static string PerPointText(Embervale.Stats.StatType primary)
    {
        var parts = new List<string>();
        foreach (StatsPresentation.PerPointPart part in StatsPresentation.PerPoint(primary))
        {
            string name = part.NameKey != null ? Loc.T(part.NameKey) : Embervale.Stats.StatNames.Label(part.Stat!.Value);
            parts.Add($"{part.Amount} {name}");
        }

        return Loc.TF("char.stat_per_point", string.Join(", ", parts));
    }

    /// <summary>Wraps a control so it centres vertically against taller siblings.</summary>
    private static Control Centred(Control control)
    {
        control.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        return control;
    }

    private void BuildCorruption(Container sections)
    {
        if (_corruption == null)
        {
            return;
        }

        VBoxContainer section = Section(Loc.T("char.corruption"));
        sections.AddChild(section);
        section.AddChild(UiTheme.Body(
            Loc.TF("char.corruption_line", CorruptionTiers.DisplayName(_corruption.Tier), _corruption.Value, CorruptionTiers.Max),
            UiTheme.CorruptionText));

        ProgressBar bar = UiTheme.Bar(UiTheme.Corruption);
        bar.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        bar.Value = _corruption.Value / (double)CorruptionTiers.Max;
        section.AddChild(bar);
    }

    /// <summary>The Perks tab: <see cref="PerkTreePanel"/> draws the tree and calls <see cref="PerksComponent"/>
    /// itself; this tab only hosts it and keeps the view state across rebuilds.</summary>
    private void BuildPerks()
    {
        if (_perks == null || PerkDatabase.All.Count == 0)
        {
            _list.AddChild(UiTheme.Body(Loc.T("char.perks_none"), UiTheme.Dim));
            return;
        }

        _list.AddChild(new PerkTreePanel(_perks, _progression, _inventory, _perkView, MarkDirty, UiTheme.UsableWidth(Shell)));
    }

    // --- The Gear tab: one strip over equipment | pack | detail -----------------

    /// <summary>
    /// Sizes the Gear tab's three columns against the viewport actually available.
    ///
    /// The side columns shrink first and the grid takes what is left, because the grid is the only
    /// one of the three whose content genuinely reflows — narrowing the detail pane costs a line
    /// wrap, narrowing the grid costs a column. Each column gives its scrollbar a gutter, which is
    /// taken off the grid's share here.
    /// </summary>
    private void MeasureColumns()
    {
        float usable = UiTheme.UsableWidth(Shell);

        _sideWidth = Mathf.Clamp(usable * 0.22f, 150f, 230f);
        _detailWidth = Mathf.Clamp(usable * 0.30f, 220f, 320f);

        float forGrid = usable - _sideWidth - _detailWidth - (UiTheme.SpaceLg * 2f) - UiTheme.ScrollGutter;
        float cell = SlotSize + UiTheme.GridGap;
        _gridColumns = Mathf.Clamp(Mathf.FloorToInt(forGrid / cell), 4, 10);
    }

    /// <summary>
    /// Fills the Gear tab's fixed frame. The old screen was one scrolling row of three columns under
    /// four wrapped rows of sort and filter buttons; this is one strip, and three lists that scroll
    /// separately, so what is selected stays in view while the pack moves.
    /// </summary>
    private void BuildGear()
    {
        _equipColumn.CustomMinimumSize = new Vector2(_sideWidth, 0f);
        _detailColumn.CustomMinimumSize = new Vector2(_detailWidth, 0f);

        FillStrip();
        FillEquipment();
        FillPack();
        FillDetail();

        // Only now is every cell actually in the tree. NodePaths do not exist before that.
        LinkFocus();
    }

    /// <summary>The one strip: pack or materials, the sort, the kind filter, and, while the player is
    /// choosing for an equipment slot, what the pack is narrowed to and the way back out. The sort and
    /// the kind are single buttons that step to the next choice, so neither opens a list that the
    /// cancel button would have to close before it could close the screen.</summary>
    private void FillStrip()
    {
        UiTheme.ClearChildren(_strip);
        _stripControls.Clear();
        if (_inventory == null)
        {
            return;
        }

        UiTabs views = BuildViewRow();
        _strip.AddChild(views);
        foreach (Node tab in views.GetChildren())
        {
            if (tab is Control control)
            {
                TrackStrip(control);
            }
        }

        _strip.AddChild(TrackStrip(BuildSortStep()));
        if (_bagView)
        {
            return;
        }

        if (_equipFocus != null)
        {
            Button all = UiTheme.Action(Loc.T("item.strip.show_all"));
            all.TooltipText = Loc.T("item.strip.show_all_tip");
            all.AddThemeColorOverride("font_color", UiTheme.Accent);
            all.Pressed += () =>
            {
                _equipFocus = null;
                MarkDirty();
            };
            _strip.AddChild(TrackStrip(all));
        }
        else
        {
            _strip.AddChild(TrackStrip(BuildKindStep()));
        }
    }

    private Control TrackStrip(Control control)
    {
        _stripControls.Add(control);
        control.FocusEntered += OnStripFocus;
        return control;
    }

    /// <summary>
    /// Walking back up to the strip with a pad or the keys ends the choosing for an equipment slot, and
    /// the pack is whole again: the way into the narrowed view is the equipment column, so the way out
    /// is leaving the columns. A mouse click on a strip button is not a walk. It keeps the view it
    /// clicked in (a rebuild here would free the button under the cursor before its press landed) and
    /// uses the "show whole pack" button instead. Nor is focus put back on a strip button by the
    /// rebuild its own press caused.
    /// </summary>
    private void OnStripFocus()
    {
        if (_equipFocus != null && !_restoring && !Godot.Input.IsMouseButtonPressed(MouseButton.Left))
        {
            _equipFocus = null;
            MarkDirty();
        }
    }

    /// <summary>The worn-gear column: one row per slot, in the canonical display order, so an empty
    /// slot is as visible as a filled one. The whole row is the pressable, at the control height: the
    /// 34 px well inside it is too small a target to be the only way into choosing for a slot.
    /// Focusing a row narrows the pack to what fits that slot; pressing a worn one steps into the
    /// detail pane, pressing an empty one into the candidates. An empty slot shows the ghost of what
    /// belongs in it and says how many pieces in the pack would fit.</summary>
    private void FillEquipment()
    {
        UiTheme.ClearChildren(_equipList);
        _equipCells.Clear();
        if (_equipment == null)
        {
            return;
        }

        foreach (EquipmentSlot slot in EquipmentSlots.DisplayOrder)
        {
            ItemInstance? item = _equipment.GetEquipped(slot);
            EquipmentSlot captured = slot;

            var line = new HBoxContainer();
            line.AddThemeConstantOverride("separation", UiTheme.SpaceSm);

            // The quiver holds a whole stack, so its cell counts the arrows left.
            int held = slot == EquipmentSlot.Ammo ? Mathf.Max(1, _equipment.AmmoCount) : 1;
            Button well = item is null
                ? ItemSlot.BuildEmpty(slot, ItemSlot.CompactSize)
                : ItemSlot.Build(item, held, ReferenceEquals(item, _selected), ItemSlot.CompactSize, ItemSlot.Marks.Equipped);
            well.FocusMode = Control.FocusModeEnum.None;
            well.MouseFilter = Control.MouseFilterEnum.Ignore;
            line.AddChild(well);

            // A frame that draws nothing and pads nothing: the row is a target, not a card.
            var bare = new StyleBoxFlat { DrawCenter = false };
            bare.SetContentMarginAll(0f);
            PanelContainer row = UiTheme.CardButton(null, out Button cell, out VBoxContainer content, bare);
            row.CustomMinimumSize = new Vector2(0f, UiTheme.ControlHeight);
            content.Alignment = BoxContainer.AlignmentMode.Center;
            cell.TooltipText = well.TooltipText;
            cell.FocusEntered += () => BrowseSlot(captured);
            cell.ButtonDown += NotePress;
            cell.Pressed += () => ChooseFor(captured);

            var text = new VBoxContainer
            {
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
            };
            text.AddThemeConstantOverride("separation", 0);
            text.AddChild(UiTheme.Caption(EquipmentSlots.Label(slot)));

            // A long affixed name takes a second line and is then trimmed, rather than allowed to
            // widen the column; the detail pane and the tooltip carry the full text.
            int fits = item is null ? CountFitting(slot) : 0;
            Label name = item is not null
                ? UiTheme.Body(item.DisplayName, UiTheme.RarityColor(item.Rarity))
                : fits > 0
                    ? UiTheme.Body(Loc.TF("item.slot_empty_fits", fits), UiTheme.Dim)
                    : UiTheme.Body(Loc.T("item.empty_slot"), UiTheme.Disabled);
            name.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            name.MaxLinesVisible = 2;
            name.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
            name.TooltipText = item?.DisplayName ?? name.Text;
            text.AddChild(name);
            line.AddChild(text);

            content.AddChild(line);
            _equipList.AddChild(row);
            _equipCells.Add((slot, item, cell, well));
        }
    }

    /// <summary>How many stacks in the pack would go in <paramref name="slot"/>.</summary>
    private int CountFitting(EquipmentSlot slot)
    {
        int count = 0;
        if (_inventory != null)
        {
            foreach (ItemStack stack in _inventory.Stacks)
            {
                if (stack.Instance.Equippable?.Slot == slot)
                {
                    count++;
                }
            }
        }

        return count;
    }

    /// <summary>The pack column: its heading (slots used, weight carried) and a grid of slots. The
    /// pack is a container of known size, so its free slots are drawn; the material bag has no size,
    /// so its grid is exactly as long as what is in it.</summary>
    private void FillPack()
    {
        UiTheme.ClearChildren(_packHead);
        UiTheme.ClearChildren(_packList);
        _gridCells.Clear();
        _cellOf.Clear();

        // Narrowed to an equipment slot, the heading says so: the pack has not shrunk, the view has.
        _packHead.AddChild(UiTheme.SectionRule(
            _equipFocus is { } fitting && !_bagView ? Loc.TF("item.pack_fits", EquipmentSlots.Label(fitting)) : BackpackHeader(),
            first: true));
        if (_inventory == null)
        {
            return;
        }

        var grid = new GridContainer { Columns = _gridColumns };
        grid.AddThemeConstantOverride("h_separation", UiTheme.GridGap);
        grid.AddThemeConstantOverride("v_separation", UiTheme.GridGap);
        _packList.AddChild(grid);

        IReadOnlyList<ItemStack> source = _bagView ? _inventory.Materials : _inventory.Stacks;
        var shown = new List<ItemStack>();
        foreach (ItemStack stack in source)
        {
            if (Shows(stack))
            {
                shown.Add(stack);
            }
        }

        foreach (ItemStack stack in ItemPresentation.Sort(shown, _sort, st => ItemPresentation.KeyOf(st.Instance)))
        {
            ItemInstance instance = stack.Instance;
            Button cell = ItemSlot.Build(
                instance, stack.Quantity, ReferenceEquals(instance, _selected), SlotSize,
                _fresh.Contains(instance) ? ItemSlot.Marks.New : ItemSlot.Marks.None);
            cell.FocusEntered += () => Browse(instance);
            cell.ButtonDown += NotePress;
            cell.Pressed += () => Activate(instance);
            grid.AddChild(cell);
            _gridCells.Add(cell);
            _cellOf[instance] = cell;
        }

        // The pack's free slots, as empty wells, so it reads as a container of known size rather
        // than an arbitrarily long list. They count what is really free, not what the filter hid,
        // so narrowing the view never makes the pack look emptier than it is. While the pack is
        // narrowed to one equipment slot they are left out: that view is a short list of candidates.
        if (!_bagView && _equipFocus == null)
        {
            for (int i = source.Count; i < _inventory.Capacity; i++)
            {
                Button empty = ItemSlot.Build(null, 1, false, SlotSize);
                empty.FocusMode = Control.FocusModeEnum.None; // nothing to inspect, so skip it in nav
                grid.AddChild(empty);
            }
        }

        if (shown.Count == 0)
        {
            string key = _equipFocus != null && !_bagView ? "item.slot_empty_none"
                : source.Count > 0 ? "item.no_match"
                : _bagView ? "item.materials_empty" : "char.empty";
            Label none = UiTheme.Body(Loc.T(key), UiTheme.Dim);
            none.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            _packList.AddChild(none);
        }
    }

    /// <summary>Whether a stack survives the filters and the typed search. The search reads the
    /// name, the category and the affix lines, so "armor" finds a ring that grants it. While the
    /// player is choosing for an equipment slot, that slot is the filter and the kind is not asked.</summary>
    private bool Shows(ItemStack stack)
    {
        ItemInstance instance = stack.Instance;
        EquipmentSlot slot = instance.Equippable?.Slot ?? EquipmentSlot.None;
        if (!_bagView && !ItemPresentation.PassesFilter(
                instance.Type, slot, _equipFocus is null ? _filter : null, _equipFocus))
        {
            return false;
        }

        if (_query.Length == 0)
        {
            return true;
        }

        var affixes = new List<string>();
        foreach (ItemAffix affix in instance.Affixes)
        {
            affixes.Add(affix.DisplayValue);
        }

        return ItemPresentation.Matches(
            _query, instance.DisplayName, Loc.T(ItemSlot.TypeKey(instance.Type)), string.Join(" ", affixes));
    }

    /// <summary>The pack / materials switch, on the shared tab strip so the active one carries the
    /// ember underline as well as the colour.</summary>
    private UiTabs BuildViewRow()
    {
        var views = new UiTabs();
        views.Add(Loc.TF("item.view_pack", _inventory!.UsedSlots, _inventory.Capacity));
        views.Add(Loc.TF("item.view_materials", _inventory.Materials.Count));
        if (_bagView)
        {
            views.Select(1); // before the handler is attached: this is restoring, not switching
        }

        views.TabChanged += index =>
        {
            _bagView = index == 1;
            _equipFocus = null;
            _pending = Pending.None;
            MarkDirty();
        };
        return views;
    }

    /// <summary>The sort, as one button that names the order in force and steps to the next.</summary>
    private Control BuildSortStep()
    {
        int current = 0;
        for (int i = 0; i < SortDefs.Length; i++)
        {
            if (SortDefs[i].Order == _sort)
            {
                current = i;
            }
        }

        Button step = UiTheme.Action(Loc.TF("item.strip.sort", Loc.T(SortDefs[current].Key)));
        step.TooltipText = Loc.T("item.strip.step_tip");

        // Never rebuild inside a button signal (CLAUDE.md section 8) - flip the flag, mark
        // dirty, and let _Process rebuild on the next frame.
        step.Pressed += () =>
        {
            _sort = SortDefs[(current + 1) % SortDefs.Length].Order;
            MarkDirty();
        };
        return step;
    }

    /// <summary>The kind filter, as one button that steps through All and then each category the pack
    /// actually holds: a filter for a category you are carrying none of is a control that does
    /// nothing. A filter left on a kind the pack no longer has is dropped.</summary>
    private Control BuildKindStep()
    {
        var present = new List<ItemType>();
        foreach (ItemStack stack in _inventory!.Stacks)
        {
            if (!present.Contains(stack.Instance.Type))
            {
                present.Add(stack.Instance.Type);
            }
        }

        present.Sort();
        if (_filter is { } active && !present.Contains(active))
        {
            _filter = null;
        }

        string name = _filter is { } kind ? Loc.T(ItemSlot.TypeKey(kind)) : Loc.T("item.filter_all");
        Button step = UiTheme.Action(Loc.TF("item.strip.kind", name));
        step.TooltipText = Loc.T("item.strip.step_tip");
        if (_filter != null)
        {
            step.AddThemeColorOverride("font_color", UiTheme.Accent);
        }

        step.Pressed += () =>
        {
            int next = _filter is { } now ? present.IndexOf(now) + 1 : 0;
            _filter = next < present.Count ? present[next] : null;
            MarkDirty();
        };
        return step;
    }

    /// <summary>
    /// Wires explicit focus neighbours across the Gear tab.
    ///
    /// Without this a d-pad walks the **tab order**, which in a GridContainer is left to right
    /// through every cell - so "down" moves one square right. Godot cannot infer the grid shape.
    /// UI_STYLE section 6 calls this out because it is invisible with a mouse and immediately
    /// broken on a controller, which is exactly the combination that ships.
    ///
    /// ⚠️ **Must run after the cells are parented, not while they are being built.**
    /// `FocusNeighbor*` takes a NodePath, and `GetPath()` throws on a node that is not yet in the
    /// scene tree. It also has to run again whenever the pack is refilled on its own: a neighbour
    /// path that points at a freed cell is an error the moment the d-pad goes that way.
    ///
    /// The grid's cells link to each other; its left edge leads to the equipment column, and every
    /// equipment cell leads right into the first cell of the grid. Down from anywhere on the strip
    /// lands in the pack, not on the equipment column the engine's own search would pick from the
    /// left-hand buttons: stepping onto an equipment cell narrows the pack, and that should be
    /// something the player went left to do. The pack and the material bag share this pass: both
    /// fill <see cref="_gridCells"/> in display order.
    /// </summary>
    private void LinkFocus()
    {
        Button? home = null;
        foreach ((EquipmentSlot slot, ItemInstance? _, Button cell, Button _) in _equipCells)
        {
            if (cell.IsInsideTree() && (home == null || slot == _equipFocus))
            {
                home = cell;
            }
        }

        for (int i = 0; i < _gridCells.Count; i++)
        {
            if (!_gridCells[i].IsInsideTree())
            {
                continue;
            }

            int column = i % _gridColumns;

            if (column > 0)
            {
                _gridCells[i].FocusNeighborLeft = _gridCells[i - 1].GetPath();
            }
            else if (home != null)
            {
                _gridCells[i].FocusNeighborLeft = home.GetPath();
            }

            if (column < _gridColumns - 1 && i + 1 < _gridCells.Count)
            {
                _gridCells[i].FocusNeighborRight = _gridCells[i + 1].GetPath();
            }

            if (i - _gridColumns >= 0)
            {
                _gridCells[i].FocusNeighborTop = _gridCells[i - _gridColumns].GetPath();
            }

            if (i + _gridColumns < _gridCells.Count)
            {
                _gridCells[i].FocusNeighborBottom = _gridCells[i + _gridColumns].GetPath();
            }
        }

        bool anyCell = _gridCells.Count > 0 && _gridCells[0].IsInsideTree();
        for (int i = 0; i < _equipCells.Count; i++)
        {
            Button cell = _equipCells[i].Cell;
            if (!cell.IsInsideTree())
            {
                continue;
            }

            if (i > 0)
            {
                cell.FocusNeighborTop = _equipCells[i - 1].Cell.GetPath();
            }

            if (i + 1 < _equipCells.Count)
            {
                cell.FocusNeighborBottom = _equipCells[i + 1].Cell.GetPath();
            }

            // With no cell to go to, the neighbour is cleared and the engine's own search takes over.
            cell.FocusNeighborRight = anyCell ? _gridCells[0].GetPath() : new NodePath();
        }

        foreach (Control control in _stripControls)
        {
            if (control.IsInsideTree())
            {
                control.FocusNeighborBottom = anyCell ? _gridCells[0].GetPath() : new NodePath();
            }
        }

        // Left out of the detail pane goes back to the cell the pane is describing. The engine's own
        // search would pick whichever pack cell is nearest, and arriving there reselects.
        if (SelectionCell() is { } back)
        {
            LinkPane(_detailList, back.GetPath(), leads: true);
        }
    }

    /// <summary>Points the pane's controls left at <paramref name="back"/>: every one that has no
    /// other control before it in its own row. Returns whether it found any control at all.</summary>
    private static bool LinkPane(Node node, NodePath back, bool leads)
    {
        bool row = node is HBoxContainer or HFlowContainer;
        bool any = false;
        foreach (Node child in node.GetChildren())
        {
            bool found;
            if (child is Control { FocusMode: Control.FocusModeEnum.All } control)
            {
                if (leads)
                {
                    control.FocusNeighborLeft = back;
                }

                found = true;
            }
            else
            {
                found = LinkPane(child, back, leads);
            }

            any |= found;
            if (row && found)
            {
                leads = false;
            }
        }

        return any;
    }

    /// <summary>The cell showing what the detail pane describes: the selected item's pack cell or
    /// equipment row, or, with nothing selected, the row of the slot being chosen for.</summary>
    private Control? SelectionCell()
    {
        if (_selected != null && _cellOf.TryGetValue(_selected, out Button? packed) && IsInstanceValid(packed) && packed.IsInsideTree())
        {
            return packed;
        }

        foreach ((EquipmentSlot slot, ItemInstance? item, Button cell, Button _) in _equipCells)
        {
            if (cell.IsInsideTree() && (_selected != null ? ReferenceEquals(item, _selected) : slot == _equipFocus))
            {
                return cell;
            }
        }

        return null;
    }

    private bool FocusIsIn(Control root) =>
        GetViewport()?.GuiGetFocusOwner() is { } focus && root.IsAncestorOf(focus);

    /// <summary>Steps from a cell into the detail pane's first live button. This is the pad's and the
    /// keys' only way to the pane that does not cross another cell, and crossing a cell reselects.</summary>
    private void EnterPane()
    {
        if (_packStale || _detailStale)
        {
            return; // the pane is about to be refilled
        }

        if (!UiFocus.GrabFirst(_detailList))
        {
            UiAudio.Play(UiCue.Denied);
        }
    }

    // --- Selection follows focus ---------------------------------------------------------------

    /// <summary>Focus arrived on a pack cell: the detail pane follows it.</summary>
    private void Browse(ItemInstance instance)
    {
        if (!ReferenceEquals(_selected, instance))
        {
            SetSelection(instance);
        }
    }

    /// <summary>Focus arrived on an equipment cell: the pane shows what is worn there (or that the
    /// slot is empty) and the pack narrows to what would fit it.</summary>
    private void BrowseSlot(EquipmentSlot slot)
    {
        if (_restoring)
        {
            return;
        }

        ItemInstance? worn = _equipment?.GetEquipped(slot);
        if (_equipFocus == slot && !_bagView && ReferenceEquals(_selected, worn))
        {
            return;
        }

        if (_equipFocus != slot || _bagView)
        {
            _equipFocus = slot;
            _bagView = false;
            _packStale = true;
        }

        SetSelection(worn);
    }

    /// <summary>Moves the selection without a rebuild: the ember rule leaves one cell and lands on
    /// another, a new pip is taken off once its item has been looked at, and the detail pane is
    /// refilled on the next frame. Rebuilding the grid here would free the very cell whose focus
    /// signal is running.</summary>
    private void SetSelection(ItemInstance? instance)
    {
        ItemInstance? previous = _selected;
        _selected = instance;
        _pending = Pending.None;
        _selectedFrame = Engine.GetProcessFrames();
        _detailStale = true;

        if (!ReferenceEquals(previous, instance))
        {
            Restyle(previous, false);
            Restyle(instance, true);
        }

        if (instance != null && _fresh.Remove(instance) && _cellOf.TryGetValue(instance, out Button? seen) && IsInstanceValid(seen))
        {
            ItemSlot.ClearNew(seen);
        }
    }

    private void Restyle(ItemInstance? instance, bool selected)
    {
        if (instance == null)
        {
            return;
        }

        if (_cellOf.TryGetValue(instance, out Button? cell) && IsInstanceValid(cell))
        {
            ItemSlot.SetSelected(cell, instance, selected);
        }

        foreach ((EquipmentSlot _, ItemInstance? item, Button _, Button worn) in _equipCells)
        {
            if (ReferenceEquals(item, instance) && IsInstanceValid(worn))
            {
                ItemSlot.SetSelected(worn, instance, selected);
            }
        }
    }

    /// <summary>A press began. If the selection moved on this same frame, the press is the click that
    /// moved it (a click focuses a cell before it presses it) and is spent on selecting.</summary>
    private void NotePress()
    {
        _pressSelected = _selectedFrame == Engine.GetProcessFrames();
        _pressByMouse = Godot.Input.IsMouseButtonPressed(MouseButton.Left);
    }

    /// <summary>A pack cell was pressed. The first press selects. Accept on the selected cell steps
    /// into the detail pane, onto the first thing that can be done with the item (Equip, for gear).
    /// A second mouse click on a piece of gear puts it on, since the mouse reaches the pane unaided.
    /// Only gear: putting on the wrong helmet is undone in one press, drinking the wrong potion is not.</summary>
    private void Activate(ItemInstance instance)
    {
        if (_pressSelected || !ReferenceEquals(_selected, instance))
        {
            Browse(instance);
            return;
        }

        if (!_pressByMouse || !instance.IsEquippable || _equipment == null)
        {
            EnterPane();
            return;
        }

        if (_equipment.CanEquip(instance) == EquipRefusal.None)
        {
            UiAudio.Play(UiCue.Confirm);
            _equipment.Equip(instance);
        }
        else
        {
            // The reason is already written under the Equip button in the pane.
            UiAudio.Play(UiCue.Denied);
        }
    }

    /// <summary>An equipment row was pressed. Worn gear steps into the detail pane, where Unequip is;
    /// an empty slot steps into the pack's candidates for it (right from any row does the same).</summary>
    private void ChooseFor(EquipmentSlot slot)
    {
        if (_pressSelected || _packStale)
        {
            return;
        }

        if (_equipFocus != slot || _bagView)
        {
            BrowseSlot(slot); // focus was put back here by a rebuild: the press is the choosing
            return;
        }

        if (_equipment?.GetEquipped(slot) != null)
        {
            EnterPane();
            return;
        }

        if (_gridCells.Count > 0 && IsInstanceValid(_gridCells[0]) && _gridCells[0].IsInsideTree())
        {
            _gridCells[0].GrabFocus();
        }
        else
        {
            UiAudio.Play(UiCue.Denied); // nothing in the pack fits; the pack column says so
        }
    }

    /// <summary>The detail pane: what the selected item is, how it compares, what wearing it would do
    /// to the sheet, and what can be done with it. It is always there; with nothing selected it says
    /// what to do, and on an empty equipment slot it says what the slot is waiting for.</summary>
    private void FillDetail()
    {
        VBoxContainer col = _detailList;
        UiTheme.ClearChildren(col);

        if (_selected is not { } instance || !StillHeld(instance))
        {
            // The selection can be invalidated from outside this panel entirely — a stash transfer,
            // a salvage at a station, a quest turn-in consuming the item. Re-checking on every
            // rebuild is what stops the pane offering Use on a potion that is already gone; the
            // panel does not get an event for "the thing you had selected left your pack".
            _selected = null;
            _pending = Pending.None;
            BuildEmptyDetail(col);
            return;
        }

        // The card works the comparison out itself from what is worn: one slot for most gear, every
        // ring slot for a ring, and nothing for gear already on the body - it *is* the baseline.
        bool worn = _equipment != null && _equipment.IsInstanceEquipped(instance);
        var context = new ItemSlot.DetailContext(_equipment, _progression?.Level ?? 0, Compare: true)
        {
            Actions = new[] { new LegendEntry("ui_accept", Loc.T("char.legend.actions")) },
        };
        col.AddChild(ItemSlot.Detail(instance, context));

        if (!worn && BuildSheetPreview(instance) is { } preview)
        {
            col.AddChild(preview);
        }

        ItemStack? held = worn ? null : StackOf(instance);
        if (held != null && _pending == Pending.Split && CanSplit(held))
        {
            BuildSplit(col, held);
            return;
        }

        if (held != null && _pending == Pending.Drop && ItemTransfer.CanDrop(instance))
        {
            BuildDropConfirm(col, held);
            return;
        }

        _pending = Pending.None;
        foreach (Control action in DetailActions(instance, worn))
        {
            col.AddChild(action);
        }

        if (held != null)
        {
            col.AddChild(BuildStackActions(held));
        }
    }

    /// <summary>The pane with no item in it: the standing hint, or, on an empty equipment slot, the
    /// slot's name, whether anything in the pack fits it and how to get to those pieces.</summary>
    private void BuildEmptyDetail(VBoxContainer col)
    {
        if (_equipFocus is not { } slot || _equipment?.GetEquipped(slot) != null)
        {
            Label hint = UiTheme.Body(Loc.T("item.select_hint"), UiTheme.Dim);
            hint.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            col.AddChild(hint);
            return;
        }

        col.AddChild(UiTheme.Body(Loc.TF("item.slot_empty_title", EquipmentSlots.Label(slot))));

        int fits = CountFitting(slot);
        Label say = UiTheme.Body(
            fits > 0 ? Loc.TF("item.slot_empty_pick", fits) : Loc.T("item.slot_empty_none"), UiTheme.Dim);
        say.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        col.AddChild(say);
    }

    /// <summary>
    /// What putting this piece on would do to the whole sheet, or null when it would change nothing.
    /// The card above compares the two items; this follows the change through: the primaries a piece
    /// grants also move the stats they buy (<see cref="StatsPresentation.DerivedDelta"/>), so +2
    /// Strength reads here as Physical Power as well. Each row is the value after the swap, an arrow
    /// and the signed change. Measured against what is worn in the piece's own slot.
    /// </summary>
    private Control? BuildSheetPreview(ItemInstance instance)
    {
        if (instance.Equippable is not { } gear || _equipment == null)
        {
            return null;
        }

        IReadOnlyList<(Embervale.Stats.StatType Stat, float Delta)> changes = StatsPresentation.DerivedDelta(
            ItemPresentation.Compare(instance, _equipment.GetEquipped(gear.Slot)));
        if (changes.Count == 0)
        {
            return null;
        }

        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", UiTheme.SpaceXs);
        box.AddChild(UiTheme.SectionRule(Loc.T("item.preview.title"), first: true));

        var grid = new GridContainer { Columns = 3 };
        grid.AddThemeConstantOverride("h_separation", UiTheme.SpaceSm);
        grid.AddThemeConstantOverride("v_separation", UiTheme.LineGap);
        foreach ((Embervale.Stats.StatType stat, float delta) in changes)
        {
            // Wraps, like the card's own stat names: "Arcane Re..." beside a number says nothing.
            Label name = UiTheme.Body(Embervale.Stats.StatNames.Label(stat), UiTheme.Dim);
            name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            name.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            grid.AddChild(name);

            // The value the sheet would read after the swap; left blank when there is no sheet to read.
            Label after = UiTheme.Body(
                _stats != null ? StatsPresentation.Format(stat, _stats.GetValue(stat) + delta) : string.Empty);
            after.HorizontalAlignment = HorizontalAlignment.Right;
            grid.AddChild(after);

            var change = new HBoxContainer();
            change.AddThemeConstantOverride("separation", UiTheme.Space2xs);
            change.AddChild(UiTheme.DeltaArrow(delta));
            change.AddChild(UiTheme.Caption(
                StatsPresentation.FormatDelta(stat, delta), delta > 0f ? UiTheme.Good : UiTheme.Bad));
            grid.AddChild(change);
        }

        box.AddChild(grid);
        return box;
    }

    // --- Capture hooks (the screenshot harnesses) ------------------------------------------------

    /// <summary>Puts the Gear tab in the state focusing an equipment cell leaves: the pack narrowed to
    /// what fits <paramref name="slot"/> and the pane on what is worn there.</summary>
    public void FocusEquipmentSlotForCapture(EquipmentSlot slot)
    {
        _bagView = false;
        _equipFocus = slot;
        _selected = _equipment?.GetEquipped(slot);
        _pending = Pending.None;
        _tabs.Select(0);
        MarkDirty();
    }

    /// <summary>Selects the first piece of gear in the pack that has something worn to be compared
    /// with, and opens or closes the side-by-side view. False when the pack holds no such piece.</summary>
    public bool CompareForCapture(bool sideBySide)
    {
        ItemSlot.CompareOpen = sideBySide;
        if (_inventory == null || _equipment == null)
        {
            return false;
        }

        foreach (ItemStack stack in _inventory.Stacks)
        {
            if (stack.Instance.Equippable is { } gear && _equipment.GetEquipped(gear.Slot) != null)
            {
                _bagView = false;
                _equipFocus = null;
                _selected = stack.Instance;
                _pending = Pending.None;
                _tabs.Select(0);
                MarkDirty();
                return true;
            }
        }

        return false;
    }

    /// <summary>The equipment slot the pack is narrowed to, if any (read by `--uishots`).</summary>
    public EquipmentSlot? EquipmentFocus => _activeTab == CharTab.Gear ? _equipFocus : null;

    /// <summary>How many pack cells the last fill drew (read by `--uishots`).</summary>
    public int ShownCellCount => _gridCells.Count;

    /// <summary>How many held items wear the new pip (read by `--uishots`).</summary>
    public int NewItemCount => _fresh.Count;

    /// <summary>Whether the detail pane's card is showing its side-by-side view (read by `--uishots`).</summary>
    public bool ShowingSideBySide
    {
        get
        {
            foreach (Node child in _detailList.GetChildren())
            {
                if (child is ItemDetailCard card)
                {
                    return card.ShowingSideBySide;
                }
            }

            return false;
        }
    }

    /// <summary>The verbs available for the selected item. Built from the item rather than from
    /// where it was clicked, so an equippable behaves the same whether it was selected in the pack
    /// or on the body.</summary>
    private IEnumerable<Control> DetailActions(ItemInstance instance, bool worn)
    {
        if (worn && _equipment != null)
        {
            Button unequip = UiTheme.Action(Loc.T("char.unequip"));
            unequip.Pressed += () =>
            {
                // Unequip refuses when the pack has no slot for the item. That used to be a button
                // that did nothing; now the feed says why.
                if (!_equipment!.UnequipInstance(instance))
                {
                    ItemTransfer.AnnouncePackFull(instance, 1);
                }

                Select(null);
            };
            yield return unequip;
            yield break;
        }

        if (instance.IsEquippable && _equipment != null)
        {
            // Every refusal the equipment itself would make (level, a two-handed weapon in the way,
            // no room for what comes off) is asked for here and written under the button: a toast
            // raised inside a menu is held until it closes, and a button that looks live and does
            // nothing explains itself to nobody.
            EquipRefusal refusal = _equipment.CanEquip(instance);
            string reason = refusal == EquipRefusal.LevelTooLow
                ? Loc.TF("item.requires_level", instance.Template.RequiredLevel)
                : InventoryRules.ReasonKey(refusal) is { Length: > 0 } key ? Loc.T(key) : string.Empty;

            Button equip = UiTheme.Action(Loc.T("char.equip"));
            equip.Disabled = refusal != EquipRefusal.None;
            equip.TooltipText = reason;
            equip.Pressed += () => _equipment!.Equip(instance);
            yield return equip;

            if (reason.Length > 0)
            {
                Label why = UiTheme.Caption(reason, UiTheme.Bad);
                why.AutowrapMode = TextServer.AutowrapMode.WordSmart;
                yield return why;
            }
        }
        else if (CraftingComponent.ScrollRecipe(instance.TemplateId) is { } taught &&
                 _inventory?.Entity?.GetComponent<CraftingComponent>() is { } crafting)
        {
            // A recipe scroll is read where it is carried. One the player already knows is kept
            // (it still sells), and says so instead of offering a press that would do nothing.
            bool fresh = crafting.CanStudy(instance);
            Button study = UiTheme.Action(Loc.T("craft.locked.study"));
            study.Disabled = !fresh;
            study.Pressed += () =>
            {
                crafting.StudyScroll(instance);
                Select(null);
            };
            yield return study;

            Label teaches = UiTheme.Caption(
                Loc.TF(fresh ? "item.scroll.teaches" : "item.scroll.known", taught.LocalizedName),
                fresh ? UiTheme.Dim : UiTheme.Disabled);
            teaches.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            yield return teaches;
        }
        else if (instance.Template is ConsumableItemResource consumable && _inventory != null)
        {
            // Asked of the rule itself (cooldown, a full resource, nothing to cure): a refusal's
            // toast is held until the menu closes, so the reason has to be readable right here.
            ConsumeRefusal refusal = _inventory.CanConsume(instance);
            Button use = UiTheme.Action(Loc.T("char.use"));
            use.Disabled = refusal != ConsumeRefusal.None;
            use.Pressed += () =>
            {
                _inventory!.Consume(instance);
                Select(null);
            };
            yield return use;

            float wait = _inventory.Entity?.GetComponent<ConsumableEffectsComponent>()?.CooldownRemaining(consumable) ?? 0f;
            if (wait > 0f)
            {
                yield return UiTheme.Caption(Loc.TF("item.cooling_down", ItemPresentation.Seconds(wait)));
            }
            else if (refusal != ConsumeRefusal.None)
            {
                yield return UiTheme.Caption(Loc.T(ConsumableRules.ReasonKey(refusal)), UiTheme.Bad);
            }

            if (_hotbar != null)
            {
                yield return BuildHotbarRow(instance.TemplateId);
            }
        }
        else if (instance.Template is PlaceableItemResource placeable)
        {
            // 37C: placement mode is entered from the item, not from a keybind - every letter key
            // and every gamepad button in this game is already bound.
            Button place = UiTheme.Action(Loc.T("char.place"));
            place.Pressed += () => BeginPlacement(placeable);
            yield return place;
        }
    }

    /// <summary>
    /// What can be done to a held stack as a stack: split it, lock it, mark it junk, drop it. Every
    /// refusal keeps its button, greyed, with the reason in the tooltip - the same rule the shop's
    /// rows follow, because a verb that vanishes reads as a verb that never existed.
    /// </summary>
    private Control BuildStackActions(ItemStack held)
    {
        ItemInstance instance = held.Instance;
        HFlowContainer row = UiTheme.FlowRow();

        if (held.Quantity > 1 && InPack(held))
        {
            bool room = _inventory!.UsedSlots < _inventory.Capacity;
            Button split = UiTheme.Action(Loc.T("item.split"));
            split.Disabled = !room;
            split.TooltipText = room ? string.Empty : Loc.T("item.split_no_room");
            split.Pressed += () =>
            {
                _splitQuantity = ItemPresentation.ClampQuantity(held.Quantity / 2, held.Quantity, keepOne: true);
                _pending = Pending.Split;
                MarkDirty();
            };
            row.AddChild(split);
        }

        Button lockButton = UiTheme.Action(Loc.T(instance.Locked ? "item.unlock" : "item.lock"));
        lockButton.TooltipText = Loc.T("item.lock_hint");
        lockButton.Pressed += () => _inventory!.SetLocked(instance, !instance.Locked);
        row.AddChild(lockButton);

        Button junk = UiTheme.Action(Loc.T(instance.Junk ? "item.unjunk" : "item.mark_junk"));
        junk.Disabled = instance.Locked;
        junk.TooltipText = Loc.T(instance.Locked ? "item.junk_locked" : "item.junk_hint");
        junk.Pressed += () => _inventory!.SetJunk(instance, !instance.Junk);
        row.AddChild(junk);

        bool droppable = ItemTransfer.CanDrop(instance);
        Button drop = UiTheme.Action(Loc.T("item.drop"));
        drop.Disabled = !droppable;
        drop.TooltipText = droppable ? string.Empty
            : Loc.T(instance.Locked ? "item.drop_locked" : "item.drop_quest");
        drop.Pressed += () =>
        {
            _pending = Pending.Drop;
            MarkDirty();
        };
        row.AddChild(drop);

        return row;
    }

    /// <summary>The split question: how many to move into a new stack, then yes or no. The picker
    /// keeps its own reading, so moving the slider does not rebuild the pane out from under it.</summary>
    private void BuildSplit(VBoxContainer col, ItemStack held)
    {
        _splitQuantity = ItemPresentation.ClampQuantity(_splitQuantity, held.Quantity, keepOne: true);
        col.AddChild(UiTheme.Caption(Loc.T("item.split_prompt")));
        col.AddChild(QuantityPicker.Build(1, held.Quantity - 1, _splitQuantity, value => _splitQuantity = value));

        HFlowContainer row = UiTheme.FlowRow();
        Button confirm = UiTheme.Action(Loc.T("item.split_confirm"));
        confirm.Pressed += () =>
        {
            _inventory?.SplitStack(held, _splitQuantity);
            _pending = Pending.None;
            MarkDirty();
        };
        row.AddChild(confirm);
        row.AddChild(CancelButton());
        col.AddChild(row);
    }

    /// <summary>The drop question. It names the item and the count, because the thing being
    /// confirmed is the whole stack and "Drop?" alone does not say so.</summary>
    private void BuildDropConfirm(VBoxContainer col, ItemStack held)
    {
        Label ask = UiTheme.Body(Loc.TF("item.drop_confirm", held.Instance.DisplayName, held.Quantity), UiTheme.Bad);
        ask.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        col.AddChild(ask);

        HFlowContainer row = UiTheme.FlowRow();
        Button confirm = UiTheme.Action(Loc.T("item.drop"));
        confirm.Pressed += () =>
        {
            if (_inventory != null)
            {
                ItemTransfer.Drop(_inventory, held);
            }

            Select(null);
        };

        // Cancel comes first: a focus restored into this row lands on its first button, and the
        // safe answer to "discard this?" should be the one under the thumb.
        row.AddChild(CancelButton());
        row.AddChild(confirm);
        col.AddChild(row);
    }

    private Button CancelButton()
    {
        Button cancel = UiTheme.Action(Loc.T("item.cancel"));
        cancel.Pressed += () =>
        {
            _pending = Pending.None;
            MarkDirty();
        };
        return cancel;
    }

    /// <summary>The 1-5 quick-use assign strip, shown for consumables.</summary>
    private Control BuildHotbarRow(string templateId)
    {
        HFlowContainer row = UiTheme.FlowRow();

        for (int n = 0; n < HotbarComponent.SlotCount; n++)
        {
            int slot = n;
            Button assign = UiTheme.Action((n + 1).ToString());
            assign.TooltipText = Loc.TF("char.assign_hotbar", n + 1);
            if (_hotbar!.Get(n) == templateId)
            {
                assign.AddThemeColorOverride("font_color", UiTheme.Accent);
            }

            assign.Pressed += () =>
            {
                _hotbar!.Assign(slot, templateId);
                MarkDirty();
            };
            row.AddChild(assign);
        }

        return row;
    }

    /// <summary>Whether the player still has this exact instance, in the pack, in the material bag
    /// or on the body. Matched by reference: two rolled items can share a template and a name while
    /// carrying different affixes, so an id comparison would happily keep the wrong one selected.</summary>
    private bool StillHeld(ItemInstance instance)
    {
        if (_equipment != null && _equipment.IsInstanceEquipped(instance))
        {
            return true;
        }

        return StackOf(instance) != null;
    }

    /// <summary>The held stack carrying this exact instance (pack, then material bag), or null.</summary>
    private ItemStack? StackOf(ItemInstance instance)
    {
        if (_inventory == null)
        {
            return null;
        }

        foreach (ItemStack stack in _inventory.AllStacks)
        {
            if (ReferenceEquals(stack.Instance, instance))
            {
                return stack;
            }
        }

        return null;
    }

    private bool InPack(ItemStack stack)
    {
        foreach (ItemStack held in _inventory!.Stacks)
        {
            if (ReferenceEquals(held, stack))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>A split needs a pack stack of two or more and a free slot to put the half in. The
    /// material bag holds one stack per material by definition and never splits.</summary>
    private bool CanSplit(ItemStack stack) =>
        stack.Quantity > 1 && InPack(stack) && _inventory!.UsedSlots < _inventory.Capacity;

    /// <summary>Selects an item for the detail pane. Marks dirty rather than rebuilding, because
    /// this runs inside a button signal (CLAUDE.md section 8).</summary>
    private void Select(ItemInstance? instance)
    {
        _selected = instance;
        _pending = Pending.None;
        MarkDirty();
    }

    /// <summary>Closes the character screen and hands the kit to the placement director — the ghost
    /// has to be aimed at the world, which cannot happen behind a modal that pauses it.</summary>
    private void BeginPlacement(PlaceableItemResource kit)
    {
        if (ServiceLocator.Instance is { } locator && locator.TryGet(out Housing.PlacementDirector placement))
        {
            SetOpen(false);
            placement.Begin(kit);
        }
    }

    private string BackpackHeader()
    {
        if (_inventory == null)
        {
            return Loc.T("char.backpack");
        }

        return Loc.TF("char.backpack_full", _inventory.UsedSlots, _inventory.Capacity,
            _inventory.TotalWeight.ToString("0.0"));
    }

    private void AddLine(string text, Color? color = null, string? tooltip = null)
    {
        Label label = UiTheme.Body(text, color);
        if (!string.IsNullOrEmpty(tooltip))
        {
            label.TooltipText = tooltip;
        }

        _list.AddChild(label);
    }
}
