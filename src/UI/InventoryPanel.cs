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

    /// <summary>Gear-slot filter (head, chest, ring...); null shows every slot.</summary>
    private EquipmentSlot? _slotFilter;

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
    /// and wiring its focus neighbours — see <see cref="LinkGridFocus"/> for why those cannot be
    /// the same step.</summary>
    private readonly List<Button> _gridCells = new();

    /// <summary>Slot edge length. Below the 44 px touch/legibility floor a glyph stops reading.</summary>
    private const float SlotSize = 48f;

    /// <summary>Width of one Progression section column (stats, corruption, standing).</summary>
    private const float SectionColumn = 320f;

    private float _sideColumn = 230f;
    private float _detailColumn = 300f;

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

    protected override void BuildShell(PanelContainer shell)
    {
        UiTheme.ApplyScreenInset(shell);

        MarginContainer margin = UiTheme.Padding(UiTheme.PanelPad);
        shell.AddChild(margin);

        var column = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        column.AddThemeConstantOverride("separation", UiTheme.SpaceSm);
        margin.AddChild(column);

        var identity = new HBoxContainer();
        identity.AddThemeConstantOverride("separation", UiTheme.SpaceSm);
        identity.AddChild(UiIcon.Create(UiIcon.Kind.Inventory, 28f, UiTheme.Accent));
        Label screenTitle = UiTheme.Title(Loc.T("char.title"));
        screenTitle.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        identity.AddChild(screenTitle);
        column.AddChild(identity);
        column.AddChild(UiTheme.Divider());

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

        (ScrollContainer scroll, _list) = UiTheme.ScrollList();
        column.AddChild(scroll);
    }

    /// <summary>Opens the Gear tab on the material bag, through the same switch a click on its tab
    /// throws (for `--panelshots`).</summary>
    public void ShowMaterials()
    {
        _bagView = true;
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
        _pending = Pending.None;

        if (!open)
        {
            // A hidden field gives up focus on its own, but the keyboard coming back must not depend
            // on that signal arriving.
            GameInput.SetTextEntry(false);
        }
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
        MarkDirty();
    }

    public void SetEquipment(EquipmentComponent? equipment)
    {
        _equipment = equipment;
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

    private void OnGameLoaded(GameLoadedEvent e) => MarkDirty();

    protected override void Rebuild()
    {
        UiTheme.ClearChildren(_list);

        // Re-derived per rebuild so a mid-session UI-scale change lands without a restart.
        UiTheme.ApplyScreenInset(Shell);
        MeasureColumns();
        _toolRow.Visible = _activeTab == CharTab.Gear;
        _gridCells.Clear(); // the cells of the last rebuild were freed with it

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

        foreach ((string headerKey, Embervale.Stats.StatType[] stats) in StatsPresentation.Sections)
        {
            VBoxContainer section = Section(Loc.T(headerKey));
            sections.AddChild(section);

            var grid = new GridContainer { Columns = 2 };
            grid.AddThemeConstantOverride("h_separation", UiTheme.SpaceLg);
            grid.AddThemeConstantOverride("v_separation", UiTheme.GridGap);

            foreach (Embervale.Stats.StatType stat in stats)
            {
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

    // --- The Gear tab (37.5C): equipment column | backpack grid | detail pane ---

    /// <summary>
    /// Lays the Gear tab out as three columns instead of one scrolling text list. The old list
    /// could not express the two things the screen most needed to say - what an item *is* at a
    /// glance, and whether picking it up is an upgrade - because both were words in a row of
    /// other words.
    /// </summary>
    /// <summary>
    /// Sizes the Gear tab's three columns against the viewport actually available.
    ///
    /// The side columns shrink first and the grid takes what is left, because the grid is the only
    /// one of the three whose content genuinely reflows — narrowing the detail pane costs a line
    /// wrap, narrowing the grid costs a column.
    /// </summary>
    private void MeasureColumns()
    {
        float usable = UiTheme.UsableWidth(Shell);

        _sideColumn = Mathf.Clamp(usable * 0.22f, 150f, 230f);
        _detailColumn = Mathf.Clamp(usable * 0.28f, 200f, 300f);

        float forGrid = usable - _sideColumn - _detailColumn - (UiTheme.SpaceLg * 2f);
        float cell = SlotSize + UiTheme.GridGap;
        _gridColumns = Mathf.Clamp(Mathf.FloorToInt(forGrid / cell), 4, 10);
    }

    private void BuildGear()
    {
        var row = new HBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        row.AddThemeConstantOverride("separation", UiTheme.SpaceLg);
        _list.AddChild(row);

        row.AddChild(BuildEquipmentColumn());
        row.AddChild(BuildBackpackColumn());
        row.AddChild(BuildDetailColumn());

        // Only now is every cell actually in the tree. NodePaths do not exist before that, so this
        // cannot be folded back into BuildBackpackColumn — see LinkGridFocus.
        LinkGridFocus();
    }

    /// <summary>The worn-gear column: one well per slot, in the canonical display order, so an
    /// empty slot is as visible as a filled one. Selecting a filled slot describes it in the
    /// detail pane, where the Unequip verb lives.</summary>
    private Control BuildEquipmentColumn()
    {
        var col = new VBoxContainer { CustomMinimumSize = new Vector2(_sideColumn, 0f) };
        col.AddThemeConstantOverride("separation", UiTheme.SpaceXs);
        col.AddChild(UiTheme.SectionRule(Loc.T("char.equipment"), first: true));

        if (_equipment == null)
        {
            return col;
        }

        foreach (EquipmentSlot slot in EquipmentSlots.DisplayOrder)
        {
            ItemInstance? item = _equipment.GetEquipped(slot);

            var line = new HBoxContainer();
            line.AddThemeConstantOverride("separation", UiTheme.SpaceSm);

            // The quiver holds a whole stack, so its cell counts the arrows left.
            int held = slot == EquipmentSlot.Ammo ? Mathf.Max(1, _equipment.AmmoCount) : 1;
            Button cell = ItemSlot.Build(item, held, ReferenceEquals(item, _selected), ItemSlot.CompactSize);
            if (item is { } worn)
            {
                cell.Pressed += () => Select(worn);
            }

            line.AddChild(cell);

            var text = new VBoxContainer
            {
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
            };
            text.AddThemeConstantOverride("separation", 0);
            text.AddChild(UiTheme.Caption(EquipmentSlots.Label(slot)));

            // A long affixed name is trimmed rather than allowed to widen the column; the detail pane
            // and the tooltip carry the full text.
            Label name = UiTheme.Body(
                item?.DisplayName ?? Loc.T("item.empty_slot"),
                item is null ? UiTheme.Disabled : UiTheme.RarityColor(item.Rarity));
            name.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
            name.TooltipText = item?.DisplayName ?? string.Empty;
            text.AddChild(name);
            line.AddChild(text);

            col.AddChild(line);
        }

        return col;
    }

    /// <summary>The backpack: a pack / materials switch and a sort and filter block over a grid of
    /// slots. The pack is a container of known size, so its free slots are drawn; the material bag
    /// has no size, so its grid is exactly as long as what is in it.</summary>
    private Control BuildBackpackColumn()
    {
        var col = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        col.AddThemeConstantOverride("separation", UiTheme.SpaceSm);
        col.AddChild(UiTheme.SectionRule(BackpackHeader(), first: true));

        if (_inventory == null)
        {
            return col;
        }

        col.AddChild(BuildViewRow());
        col.AddChild(BuildSortRow());
        if (!_bagView)
        {
            col.AddChild(BuildFilterRow());
            if (BuildSlotFilterRow() is { } slots)
            {
                col.AddChild(slots);
            }
        }

        var grid = new GridContainer { Columns = _gridColumns };
        grid.AddThemeConstantOverride("h_separation", UiTheme.GridGap);
        grid.AddThemeConstantOverride("v_separation", UiTheme.GridGap);
        col.AddChild(grid);

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
            Button cell = ItemSlot.Build(instance, stack.Quantity, ReferenceEquals(instance, _selected), SlotSize);
            cell.Pressed += () => Select(instance);
            grid.AddChild(cell);
            _gridCells.Add(cell);
        }

        // The pack's free slots, as empty wells, so it reads as a container of known size rather
        // than an arbitrarily long list. They count what is really free, not what the filter hid,
        // so narrowing the view never makes the pack look emptier than it is.
        if (!_bagView)
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
            string key = source.Count > 0 ? "item.no_match" : _bagView ? "item.materials_empty" : "char.empty";
            col.AddChild(UiTheme.Body(Loc.T(key), UiTheme.Dim));
        }

        return col;
    }

    /// <summary>Whether a stack survives the filters and the typed search. The search reads the
    /// name, the category and the affix lines, so "armor" finds a ring that grants it.</summary>
    private bool Shows(ItemStack stack)
    {
        ItemInstance instance = stack.Instance;
        EquipmentSlot slot = instance.Equippable?.Slot ?? EquipmentSlot.None;
        if (!_bagView && !ItemPresentation.PassesFilter(instance.Type, slot, _filter, _slotFilter))
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
    private Control BuildViewRow()
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
            _pending = Pending.None;
            MarkDirty();
        };
        return views;
    }

    /// <summary>
    /// Wires explicit focus neighbours across the grid.
    ///
    /// Without this a d-pad walks the **tab order**, which in a GridContainer is left to right
    /// through every cell - so "down" moves one square right. Godot cannot infer the grid shape.
    /// UI_STYLE section 6 calls this out because it is invisible with a mouse and immediately
    /// broken on a controller, which is exactly the combination that ships.
    ///
    /// ⚠️ **Must run after the whole tab is parented, not while the grid is being built.**
    /// `FocusNeighbor*` takes a NodePath, and `GetPath()` throws on a node that is not yet in the
    /// scene tree. The first pass wired neighbours inside the grid builder, whose result is only
    /// added to its parent *after* it returns — so every cell errored, every frame the screen was
    /// open, and the grid still worked under a mouse.
    ///
    /// The pack and the material bag share this pass: both fill <see cref="_gridCells"/> in display
    /// order, and only one of them is on screen at a time.
    /// </summary>
    private void LinkGridFocus()
    {
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
    }

    private Control BuildSortRow()
    {
        HFlowContainer row = UiTheme.FlowRow();
        row.AddChild(Centred(UiTheme.Caption(Loc.T("item.sort"))));

        foreach ((ItemPresentation.SortOrder order, string key) in new[]
                 {
                     (ItemPresentation.SortOrder.Name, "item.sort_name"),
                     (ItemPresentation.SortOrder.Rarity, "item.sort_rarity"),
                     (ItemPresentation.SortOrder.Type, "item.sort_type"),
                     (ItemPresentation.SortOrder.Level, "item.sort_level"),
                     (ItemPresentation.SortOrder.Weight, "item.sort_weight"),
                     (ItemPresentation.SortOrder.Value, "item.sort_value"),
                 })
        {
            ItemPresentation.SortOrder captured = order;
            Button button = UiTheme.Action(Loc.T(key));
            if (_sort == order)
            {
                button.AddThemeColorOverride("font_color", UiTheme.Accent);
            }

            // Never rebuild inside a button signal (CLAUDE.md section 8) - flip the flag, mark
            // dirty, and let _Process rebuild on the next frame.
            button.Pressed += () =>
            {
                _sort = captured;
                MarkDirty();
            };
            row.AddChild(button);
        }

        return row;
    }

    private Control BuildFilterRow()
    {
        HFlowContainer row = UiTheme.FlowRow();

        Button all = UiTheme.Action(Loc.T("item.filter_all"));
        if (_filter is null)
        {
            all.AddThemeColorOverride("font_color", UiTheme.Accent);
        }

        all.Pressed += () =>
        {
            _filter = null;
            MarkDirty();
        };
        row.AddChild(all);

        // Only categories actually present in the pack get a button - a filter for a category you
        // are carrying none of is a control that does nothing.
        var present = new List<ItemType>();
        if (_inventory != null)
        {
            foreach (ItemStack stack in _inventory.Stacks)
            {
                if (!present.Contains(stack.Instance.Type))
                {
                    present.Add(stack.Instance.Type);
                }
            }
        }

        present.Sort();
        foreach (ItemType type in present)
        {
            ItemType captured = type;
            Button button = UiTheme.Action(Loc.T(ItemSlot.TypeKey(type)));
            if (_filter == type)
            {
                button.AddThemeColorOverride("font_color", UiTheme.Accent);
            }

            button.Pressed += () =>
            {
                _filter = captured;
                MarkDirty();
            };
            row.AddChild(button);
        }

        return row;
    }

    /// <summary>The gear-slot filter: one button per slot the pack actually holds gear for. Absent
    /// until there are two slots to choose between, and a filter left pointing at a slot the pack no
    /// longer has gear for is dropped rather than left hiding everything behind a button that is gone.</summary>
    private Control? BuildSlotFilterRow()
    {
        var present = new List<EquipmentSlot>();
        foreach (ItemStack stack in _inventory!.Stacks)
        {
            if (stack.Instance.Equippable is { } gear && gear.Slot != EquipmentSlot.None && !present.Contains(gear.Slot))
            {
                present.Add(gear.Slot);
            }
        }

        if (_slotFilter is { } active && !present.Contains(active))
        {
            _slotFilter = null;
        }

        if (present.Count < 2)
        {
            return null;
        }

        HFlowContainer row = UiTheme.FlowRow();
        Button any = UiTheme.Action(Loc.T("item.filter_any_slot"));
        if (_slotFilter is null)
        {
            any.AddThemeColorOverride("font_color", UiTheme.Accent);
        }

        any.Pressed += () =>
        {
            _slotFilter = null;
            MarkDirty();
        };
        row.AddChild(any);

        present.Sort();
        foreach (EquipmentSlot slot in present)
        {
            EquipmentSlot captured = slot;
            Button button = UiTheme.Action(EquipmentSlots.Label(slot));
            if (_slotFilter == slot)
            {
                button.AddThemeColorOverride("font_color", UiTheme.Accent);
            }

            button.Pressed += () =>
            {
                _slotFilter = captured;
                MarkDirty();
            };
            row.AddChild(button);
        }

        return row;
    }

    /// <summary>The detail pane: what the selected item is, how it compares, and what can be done
    /// with it.</summary>
    private Control BuildDetailColumn()
    {
        var col = new VBoxContainer { CustomMinimumSize = new Vector2(_detailColumn, 0f) };
        col.AddThemeConstantOverride("separation", UiTheme.SpaceSm);

        if (_selected is not { } instance || !StillHeld(instance))
        {
            // The selection can be invalidated from outside this panel entirely — a stash transfer,
            // a salvage at a station, a quest turn-in consuming the item. Re-checking on every
            // rebuild is what stops the pane offering Use on a potion that is already gone; the
            // panel does not get an event for "the thing you had selected left your pack".
            _selected = null;
            _pending = Pending.None;
            col.AddChild(UiTheme.Body(Loc.T("item.select_hint"), UiTheme.Dim));
            return col;
        }

        // The card works the comparison out itself from what is worn: one slot for most gear, every
        // ring slot for a ring, and nothing for gear already on the body - it *is* the baseline.
        bool worn = _equipment != null && _equipment.IsInstanceEquipped(instance);
        col.AddChild(ItemSlot.Detail(
            instance, new ItemSlot.DetailContext(_equipment, _progression?.Level ?? 0, Compare: true)));

        ItemStack? held = worn ? null : StackOf(instance);
        if (held != null && _pending == Pending.Split && CanSplit(held))
        {
            BuildSplit(col, held);
            return col;
        }

        if (held != null && _pending == Pending.Drop && ItemTransfer.CanDrop(instance))
        {
            BuildDropConfirm(col, held);
            return col;
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

        return col;
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
