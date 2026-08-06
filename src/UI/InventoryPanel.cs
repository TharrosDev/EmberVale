using System.Collections.Generic;
using Embervale.Combat;
using Embervale.Core;
using Embervale.Core.Events;
using Embervale.Core.Services;
using Embervale.Corruption;
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
    private SpellcastingComponent? _spellcasting;
    private ReputationComponent? _reputation;
    private CorruptionComponent? _corruption;
    private Embervale.Stats.StatsComponent? _stats;
    private UiTabs _tabs = null!;
    private VBoxContainer _list = null!;

    // --- Gear tab state (37.5C) ------------------------------------------------
    // The Gear tab is a grid + detail pane rather than a text list, so it needs a selection and a
    // sort/filter. The other three tabs are still lists and still rebuild into _list.
    private ItemInstance? _selected;
    private ItemPresentation.SortOrder _sort = ItemPresentation.SortOrder.Rarity;

    /// <summary>Category filter; null shows everything.</summary>
    private ItemType? _filter;

    /// <summary>Columns in the backpack grid. Fixed rather than derived from the panel width: the
    /// grid must be navigable by d-pad, and that means <see cref="UiFocus"/> restoring a stable
    /// index across rebuilds, which a reflowing column count would break.</summary>
    private const int GridColumns = 8;

    /// <summary>The focusable backpack cells of the current rebuild, held between building the grid
    /// and wiring its focus neighbours — see <see cref="LinkGridFocus"/> for why those cannot be
    /// the same step.</summary>
    private readonly List<Button> _gridCells = new();

    /// <summary>The character screen's tabs (Phase 29.5 spell tab + split progression/perks) —
    /// indices match the <see cref="UiTabs"/> order built in <see cref="BuildShell"/>.</summary>
    private enum CharTab { Gear, Spells, Progression, Perks }

    private CharTab _activeTab = CharTab.Gear;

    private static readonly (CharTab Tab, string Key)[] TabDefs =
    {
        (CharTab.Gear, "char.tab_gear"),
        (CharTab.Spells, "char.tab_spells"),
        (CharTab.Progression, "char.tab_progression"),
        (CharTab.Perks, "char.tab_perks"),
    };

    /// <summary>Screen-edge gutter so the panel fills the view without covering it entirely.</summary>
    private const float ScreenMargin = 70f;

    protected override string? ToggleAction => GameInput.Inventory;

    protected override void BuildShell(PanelContainer shell)
    {
        // Fills the screen with a medium gutter, anchored so it tracks any resolution.
        shell.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        shell.OffsetLeft = ScreenMargin;
        shell.OffsetTop = ScreenMargin;
        shell.OffsetRight = -ScreenMargin;
        shell.OffsetBottom = -ScreenMargin;

        MarginContainer margin = UiTheme.Padding(12);
        shell.AddChild(margin);

        var column = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        column.AddThemeConstantOverride("separation", UiTheme.SpaceSm);
        margin.AddChild(column);

        // Tab row (Gear · Spells · Progression · Perks) — built once; only _list rebuilds per tab.
        _tabs = new UiTabs();
        foreach ((CharTab _, string key) in TabDefs)
        {
            _tabs.Add(Loc.T(key));
        }

        _tabs.TabChanged += index =>
        {
            _activeTab = TabDefs[index].Tab;
            MarkDirty();
        };
        column.AddChild(_tabs);

        (ScrollContainer scroll, _list) = UiTheme.ScrollList();
        column.AddChild(scroll);
    }

    protected override void OnReady()
    {
        EventBus.Instance?.Subscribe<InventoryChangedEvent>(OnChanged);
        EventBus.Instance?.Subscribe<SpellsChangedEvent>(OnSpellsChanged);
        EventBus.Instance?.Subscribe<EquipmentChangedEvent>(OnEquipmentChanged);
        EventBus.Instance?.Subscribe<XpGainedEvent>(OnXpGained);
        EventBus.Instance?.Subscribe<LeveledUpEvent>(OnLeveledUp);
        EventBus.Instance?.Subscribe<PerkChangedEvent>(OnPerkChanged);
        EventBus.Instance?.Subscribe<ReputationChangedEvent>(OnReputationChanged);
        EventBus.Instance?.Subscribe<CorruptionChangedEvent>(OnCorruptionChanged);
    }

    public override void _ExitTree()
    {
        EventBus.Instance?.Unsubscribe<InventoryChangedEvent>(OnChanged);
        EventBus.Instance?.Unsubscribe<SpellsChangedEvent>(OnSpellsChanged);
        EventBus.Instance?.Unsubscribe<EquipmentChangedEvent>(OnEquipmentChanged);
        EventBus.Instance?.Unsubscribe<XpGainedEvent>(OnXpGained);
        EventBus.Instance?.Unsubscribe<LeveledUpEvent>(OnLeveledUp);
        EventBus.Instance?.Unsubscribe<PerkChangedEvent>(OnPerkChanged);
        EventBus.Instance?.Unsubscribe<ReputationChangedEvent>(OnReputationChanged);
        EventBus.Instance?.Unsubscribe<CorruptionChangedEvent>(OnCorruptionChanged);
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

    public void SetSpellcasting(SpellcastingComponent? spellcasting)
    {
        _spellcasting = spellcasting;
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

    private void OnSpellsChanged(SpellsChangedEvent e) => MarkDirty();

    private void OnReputationChanged(ReputationChangedEvent e) => MarkDirty();

    private void OnCorruptionChanged(CorruptionChangedEvent e) => MarkDirty();

    protected override void Rebuild()
    {
        UiTheme.ClearChildren(_list);

        switch (_activeTab)
        {
            case CharTab.Spells:
                BuildSpells();
                break;
            case CharTab.Progression:
                BuildProgression();
                BuildCorruption();
                BuildFactions();
                break;
            case CharTab.Perks:
                BuildPerks();
                break;
            default:
                BuildGear();
                break;
        }
    }

    private void BuildFactions()
    {
        if (_reputation == null || FactionDatabase.All.Count == 0)
        {
            return;
        }

        AddHeader(Loc.T("char.reputation"));

        // Corruption inflicts a global "dread" penalty (Phase 23G): the world reacts to the
        // earned standing lowered by dread, so show the world's effective tier and call out
        // why it dropped.
        int dread = _reputation.Dread;
        if (dread > 0)
        {
            AddLine(Loc.TF("char.dread", dread), UiTheme.CorruptionText);
        }

        foreach (FactionResource faction in FactionDatabase.All)
        {
            int value = _reputation.Get(faction.Id);
            ReputationTier tier = ReputationTiers.Of(_reputation.Effective(faction.Id));
            AddLine(Loc.TF("char.rep_line", faction.DisplayName, ReputationTiers.DisplayName(tier), value.ToString("+0;-0;0")),
                ReputationTiers.Color(tier));
        }
    }

    private void BuildProgression()
    {
        BuildLevelCard();
        BuildStats();
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

        MarginContainer pad = UiTheme.Padding(UiTheme.SpaceSm);
        pad.AddChild(col);
        card.AddChild(pad);
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
    private void BuildStats()
    {
        if (_stats == null)
        {
            return;
        }

        foreach ((string headerKey, Embervale.Stats.StatType[] stats) in StatsPresentation.Sections)
        {
            _list.AddChild(UiTheme.SectionRule(Loc.T(headerKey)));

            var grid = new GridContainer { Columns = 2 };
            grid.AddThemeConstantOverride("h_separation", UiTheme.SpaceLg);
            grid.AddThemeConstantOverride("v_separation", 1);

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
            }

            _list.AddChild(grid);
        }
    }

    /// <summary>Wraps a control so it centres vertically against taller siblings.</summary>
    private static Control Centred(Control control)
    {
        control.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        return control;
    }

    private void BuildCorruption()
    {
        if (_corruption == null)
        {
            return;
        }

        AddHeader(Loc.T("char.corruption"));
        AddLine(Loc.TF("char.corruption_line", CorruptionTiers.DisplayName(_corruption.Tier), _corruption.Value, CorruptionTiers.Max), UiTheme.CorruptionText);

        ProgressBar bar = UiTheme.Bar(UiTheme.Corruption);
        bar.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        bar.Value = _corruption.Value / (double)CorruptionTiers.Max;
        _list.AddChild(bar);
    }

    /// <summary>The spellbook's school display order (the six magic schools; Physical/True are not schools).</summary>
    private static readonly DamageType[] SchoolOrder =
    {
        DamageType.Fire, DamageType.Frost, DamageType.Lightning,
        DamageType.Arcane, DamageType.Nature, DamageType.Necrotic,
    };

    private static string SchoolKey(DamageType school) => school switch
    {
        DamageType.Fire => "school.fire",
        DamageType.Frost => "school.frost",
        DamageType.Lightning => "school.lightning",
        DamageType.Arcane => "school.arcane",
        DamageType.Nature => "school.nature",
        DamageType.Necrotic => "school.necrotic",
        _ => "school.fire",
    };

    /// <summary>The spellbook (29.5G): spells grouped by school, each school headed by its mastery
    /// rank + progress toward the next (the 29.5C track), tinted the school's colour.</summary>
    private void BuildSpells()
    {
        if (_spellcasting == null || SpellDatabase.All.Count == 0)
        {
            AddLine(Loc.T("char.empty"));
            return;
        }

        if (_progression != null)
        {
            AddLine(Loc.TF("char.spell_points", _progression.SpellPoints), UiTheme.Dim);
        }

        SchoolMasteryComponent? mastery = _spellcasting.Entity?.GetComponent<SchoolMasteryComponent>();
        foreach (DamageType school in SchoolOrder)
        {
            var spells = new List<SpellResource>();
            foreach (SpellResource s in SpellDatabase.All)
            {
                // Enemy-only loadouts (the Phase 34 caster roster) stay out of the player's book —
                // unless the player has actually recovered one (35F: an Ancient dragon teaches lost
                // spellcraft that can never be bought). A known spell must always be listed, or the
                // reward for the fight is a spell the character screen says you do not have.
                if (s.School == school && (s.PlayerLearnable || _spellcasting.IsKnown(s)))
                {
                    spells.Add(s);
                }
            }

            if (spells.Count == 0)
            {
                continue;
            }

            Color tint = SpellSchools.Color(school);
            int schoolRank = mastery?.RankOf(school) ?? 0;
            int bonus = (int)Mathf.Round((SchoolMasteryMath.PowerMultiplier(schoolRank) - 1f) * 100f);

            Label header = UiTheme.Header(Loc.TF("char.school_mastery",
                Loc.T(SchoolKey(school)), schoolRank, SchoolMasteryMath.MaxRank, bonus));
            header.Modulate = tint;
            _list.AddChild(header);

            // Progress toward the next mastery rank (hidden once the school is capped).
            if (mastery != null && schoolRank < SchoolMasteryMath.MaxRank)
            {
                ProgressBar bar = UiTheme.Bar(tint);
                bar.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
                bar.Value = (mastery.PointsIn(school) % SchoolMasteryMath.PointsPerRank)
                    / (double)SchoolMasteryMath.PointsPerRank;
                _list.AddChild(bar);
            }

            foreach (SpellResource spell in spells)
            {
                BuildSpellRow(spell, tint);
            }
        }
    }

    private void BuildSpellRow(SpellResource spell, Color tint)
    {
        bool known = _spellcasting!.IsKnown(spell);
        int rank = _spellcasting.RankOf(spell);
        string mode = spell.CastMode switch
        {
            CastMode.Charged => $"  {Loc.T("char.mode_charged")}",
            CastMode.Channeled => $"  {Loc.T("char.mode_channeled")}",
            _ => string.Empty,
        };
        string text = (known
            ? Loc.TF("char.spell_rank", spell.DisplayName, rank, spell.MaxRank)
            : spell.DisplayName) + mode;

        if (!known && _spellcasting.CanBuy(spell))
        {
            AddRow(text, Loc.TF("char.spell_buy", spell.LearnCost), () => _spellcasting!.Buy(spell), tint, spell.Description);
        }
        else if (known && _spellcasting.CanUpgrade(spell))
        {
            AddRow(text, Loc.TF("char.spell_upgrade", spell.UpgradeCost), () => _spellcasting!.Upgrade(spell), tint, spell.Description);
        }
        else
        {
            string suffix = known && rank >= spell.MaxRank ? $"  {Loc.T("char.spell_maxed")}"
                : !known && !_spellcasting.MeetsCorruption(spell) ? $"  {Loc.TF("char.spell_needs", CorruptionTiers.DisplayName(spell.MinCorruptionTier))}"
                : !known ? $"  {Loc.TF("char.spell_cost", spell.LearnCost)}"
                : string.Empty;
            AddLine($"• {text}{suffix}", known ? tint : UiTheme.Dim, spell.Description);
        }
    }

    private void BuildPerks()
    {
        if (_perks == null || PerkDatabase.All.Count == 0)
        {
            _list.AddChild(UiTheme.Body(Loc.T("char.perks_none"), UiTheme.Dim));
            return;
        }

        _list.AddChild(UiTheme.SectionRule(Loc.T("char.perks")));

        foreach (PerkResource perk in PerkDatabase.All)
        {
            int rank = _perks.RankOf(perk.Id);
            bool canLearn = _perks.CanLearn(perk);
            bool maxed = rank >= perk.MaxRank;

            // The spine says at a glance whether this perk is finished, available, or out of
            // reach - three states the old flat list expressed only in a trailing word.
            Color spine = maxed ? UiTheme.Accent : canLearn ? UiTheme.Good : UiTheme.Disabled;
            PanelContainer card = UiTheme.Card(spine);

            var col = new VBoxContainer();
            col.AddThemeConstantOverride("separation", 2);

            var head = new HBoxContainer();
            head.AddThemeConstantOverride("separation", UiTheme.SpaceSm);

            Label title = UiTheme.Body(perk.DisplayName, rank > 0 ? UiTheme.Text : UiTheme.Dim);
            title.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            title.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            head.AddChild(title);

            head.AddChild(Centred(RankPips(rank, perk.MaxRank)));
            head.AddChild(Centred(UiTheme.Caption(Loc.TF("char.perk_rank_short", rank, perk.MaxRank))));

            if (canLearn)
            {
                PerkResource captured = perk;
                Button learn = UiTheme.Action(Loc.TF("char.perk_learn", perk.Cost));
                learn.Pressed += () => _perks!.Learn(captured);
                head.AddChild(Centred(learn));
            }
            else if (!maxed)
            {
                // Say which refusal it is - the same rule Phase 37's property prompts follow.
                string reason = !_perks.MeetsCorruption(perk)
                    ? Loc.TF("char.perk_needs", CorruptionTiers.DisplayName(perk.MinCorruptionTier))
                    : Loc.TF("char.perk_learn", perk.Cost);
                head.AddChild(Centred(UiTheme.Chip(reason, UiTheme.Disabled)));
            }

            col.AddChild(head);

            if (!string.IsNullOrWhiteSpace(perk.Description))
            {
                col.AddChild(UiTheme.Flavour(perk.Description));
            }

            MarginContainer pad = UiTheme.Padding(UiTheme.SpaceXs);
            pad.AddChild(col);
            card.AddChild(pad);
            _list.AddChild(card);
        }
    }

    /// <summary>A perk's rank as filled pips. Paired with the "2/3" caption rather than replacing
    /// it: pips are read at a glance, the numbers are read exactly, and a rankable perk is
    /// something the player compares across a list.</summary>
    private static Control RankPips(int rank, int maxRank)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 2);

        for (int i = 0; i < Mathf.Max(1, maxRank); i++)
        {
            row.AddChild(new ColorRect
            {
                Color = i < rank ? UiTheme.Accent : UiTheme.Engrave,
                CustomMinimumSize = new Vector2(10f, 6f),
                MouseFilter = Control.MouseFilterEnum.Ignore,
            });
        }

        return row;
    }

    // --- The Gear tab (37.5C): equipment column | backpack grid | detail pane ---

    /// <summary>
    /// Lays the Gear tab out as three columns instead of one scrolling text list. The old list
    /// could not express the two things the screen most needed to say - what an item *is* at a
    /// glance, and whether picking it up is an upgrade - because both were words in a row of
    /// other words.
    /// </summary>
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
        var col = new VBoxContainer { CustomMinimumSize = new Vector2(230f, 0f) };
        col.AddThemeConstantOverride("separation", UiTheme.SpaceXs);
        col.AddChild(UiTheme.SectionRule(Loc.T("char.equipment")));

        if (_equipment == null)
        {
            return col;
        }

        foreach (EquipmentSlot slot in EquipmentSlots.DisplayOrder)
        {
            ItemInstance? item = _equipment.GetEquipped(slot);

            var line = new HBoxContainer();
            line.AddThemeConstantOverride("separation", UiTheme.SpaceSm);

            Button cell = ItemSlot.Build(item, 1, ReferenceEquals(item, _selected), 34f);
            if (item is { } worn)
            {
                cell.Pressed += () => Select(worn);
            }

            line.AddChild(cell);

            var text = new VBoxContainer { SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
            text.AddThemeConstantOverride("separation", 0);
            text.AddChild(UiTheme.Caption(EquipmentSlots.Label(slot)));
            text.AddChild(UiTheme.Body(
                item?.DisplayName ?? Loc.T("item.empty_slot"),
                item is null ? UiTheme.Disabled : UiTheme.RarityColor(item.Rarity)));
            line.AddChild(text);

            col.AddChild(line);
        }

        return col;
    }

    /// <summary>The backpack: a sort/filter row over a fixed-column grid of slots.</summary>
    private Control BuildBackpackColumn()
    {
        var col = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        col.AddThemeConstantOverride("separation", UiTheme.SpaceSm);
        col.AddChild(UiTheme.SectionRule(BackpackHeader()));

        if (_inventory == null)
        {
            return col;
        }

        col.AddChild(BuildSortRow());
        col.AddChild(BuildFilterRow());

        var grid = new GridContainer { Columns = GridColumns };
        grid.AddThemeConstantOverride("h_separation", UiTheme.SpaceXs);
        grid.AddThemeConstantOverride("v_separation", UiTheme.SpaceXs);
        col.AddChild(grid);

        var shown = new List<ItemStack>();
        foreach (ItemStack stack in _inventory.Stacks)
        {
            if (_filter is null || stack.Instance.Type == _filter)
            {
                shown.Add(stack);
            }
        }

        _gridCells.Clear();
        foreach (ItemStack stack in ItemPresentation.Sort(shown, _sort, st => ItemPresentation.KeyOf(st.Instance)))
        {
            ItemInstance instance = stack.Instance;
            Button cell = ItemSlot.Build(instance, stack.Quantity, ReferenceEquals(instance, _selected));
            cell.Pressed += () => Select(instance);
            grid.AddChild(cell);
            _gridCells.Add(cell);
        }

        // Fill the remaining capacity with empty wells so the pack reads as a container of known
        // size rather than an arbitrarily long list. This is also what makes "nearly full" legible
        // before the weight number is.
        for (int i = shown.Count; i < _inventory.Capacity; i++)
        {
            Button empty = ItemSlot.Build(null);
            empty.FocusMode = Control.FocusModeEnum.None; // nothing to inspect, so skip it in nav
            grid.AddChild(empty);
        }

        if (shown.Count == 0)
        {
            col.AddChild(UiTheme.Body(Loc.T("char.empty"), UiTheme.Dim));
        }

        return col;
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
    /// </summary>
    private void LinkGridFocus()
    {
        for (int i = 0; i < _gridCells.Count; i++)
        {
            if (!_gridCells[i].IsInsideTree())
            {
                continue;
            }

            int column = i % GridColumns;

            if (column > 0)
            {
                _gridCells[i].FocusNeighborLeft = _gridCells[i - 1].GetPath();
            }

            if (column < GridColumns - 1 && i + 1 < _gridCells.Count)
            {
                _gridCells[i].FocusNeighborRight = _gridCells[i + 1].GetPath();
            }

            if (i - GridColumns >= 0)
            {
                _gridCells[i].FocusNeighborTop = _gridCells[i - GridColumns].GetPath();
            }

            if (i + GridColumns < _gridCells.Count)
            {
                _gridCells[i].FocusNeighborBottom = _gridCells[i + GridColumns].GetPath();
            }
        }
    }

    private Control BuildSortRow()
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", UiTheme.SpaceXs);
        row.AddChild(UiTheme.Caption(Loc.T("item.sort")));

        foreach ((ItemPresentation.SortOrder order, string key) in new[]
                 {
                     (ItemPresentation.SortOrder.Name, "item.sort_name"),
                     (ItemPresentation.SortOrder.Rarity, "item.sort_rarity"),
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
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", UiTheme.SpaceXs);

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

    /// <summary>The detail pane: what the selected item is, how it compares, and what can be done
    /// with it.</summary>
    private Control BuildDetailColumn()
    {
        var col = new VBoxContainer { CustomMinimumSize = new Vector2(300f, 0f) };
        col.AddThemeConstantOverride("separation", UiTheme.SpaceSm);

        if (_selected is not { } instance || !StillHeld(instance))
        {
            // The selection can be invalidated from outside this panel entirely — a stash transfer,
            // a salvage at a station, a quest turn-in consuming the item. Re-checking on every
            // rebuild is what stops the pane offering Use on a potion that is already gone; the
            // panel does not get an event for "the thing you had selected left your pack".
            _selected = null;
            col.AddChild(UiTheme.Body(Loc.T("item.select_hint"), UiTheme.Dim));
            return col;
        }

        // Compare against whatever occupies the slot this item would go into. Gear already worn
        // compares against nothing - it *is* the baseline, and "vs itself" is always zero.
        bool worn = _equipment != null && _equipment.IsInstanceEquipped(instance);
        ItemInstance? rival = !worn && instance.Equippable is { } gear ? _equipment?.GetEquipped(gear.Slot) : null;
        col.AddChild(ItemSlot.Detail(instance, rival, compare: instance.IsEquippable && !worn));

        foreach (Control action in DetailActions(instance, worn))
        {
            col.AddChild(action);
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
                _equipment!.UnequipInstance(instance);
                Select(null);
            };
            yield return unequip;
            yield break;
        }

        if (instance.IsEquippable && _equipment != null)
        {
            Button equip = UiTheme.Action(Loc.T("char.equip"));
            equip.Pressed += () => _equipment!.Equip(instance);
            yield return equip;
        }
        else if (instance.Template is ConsumableItemResource && _inventory != null)
        {
            Button use = UiTheme.Action(Loc.T("char.use"));
            use.Pressed += () =>
            {
                _inventory!.Consume(instance);
                Select(null);
            };
            yield return use;

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

    /// <summary>The 1-5 quick-use assign strip, shown for consumables.</summary>
    private Control BuildHotbarRow(string templateId)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", UiTheme.SpaceXs);

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

    /// <summary>Whether the player still has this exact instance, in the pack or on the body.
    /// Matched by reference: two rolled items can share a template and a name while carrying
    /// different affixes, so an id comparison would happily keep the wrong one selected.</summary>
    private bool StillHeld(ItemInstance instance)
    {
        if (_equipment != null && _equipment.IsInstanceEquipped(instance))
        {
            return true;
        }

        if (_inventory == null)
        {
            return false;
        }

        foreach (ItemStack stack in _inventory.Stacks)
        {
            if (ReferenceEquals(stack.Instance, instance))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Selects an item for the detail pane. Marks dirty rather than rebuilding, because
    /// this runs inside a button signal (CLAUDE.md section 8).</summary>
    private void Select(ItemInstance? instance)
    {
        _selected = instance;
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

    private void AddHeader(string text)
    {
        var header = UiTheme.Header(text);
        header.AddThemeConstantOverride("line_spacing", 2);
        _list.AddChild(header);
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

    private void AddRow(string text, string action, System.Action onPressed, Color? color = null, string? tooltip = null, string? hotbarAssignId = null)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", UiTheme.SpaceSm);

        Label label = UiTheme.Body(text, color);
        label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        label.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        if (!string.IsNullOrEmpty(tooltip))
        {
            label.TooltipText = tooltip;
        }

        row.AddChild(label);

        // Hotbar assign: tiny 1-5 buttons that bind this item to a quick-use slot.
        if (hotbarAssignId != null && _hotbar != null)
        {
            for (int n = 0; n < HotbarComponent.SlotCount; n++)
            {
                int slot = n;
                Button assign = UiTheme.Action((n + 1).ToString());
                assign.TooltipText = Loc.TF("char.assign_hotbar", n + 1);
                // Highlight the slot this item is currently keyed to.
                if (_hotbar.Get(n) == hotbarAssignId)
                {
                    assign.Modulate = UiTheme.Accent;
                }
                assign.Pressed += () => _hotbar!.Assign(slot, hotbarAssignId);
                row.AddChild(assign);
            }
        }

        Button button = UiTheme.Action(action);
        button.Pressed += () => onPressed();
        row.AddChild(button);

        _list.AddChild(row);
    }
}
