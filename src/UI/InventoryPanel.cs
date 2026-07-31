using System.Collections.Generic;
using Embervale.Combat;
using Embervale.Core;
using Embervale.Core.Events;
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
    private UiTabs _tabs = null!;
    private VBoxContainer _list = null!;

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
                BuildEquipment();
                AddHeader(BackpackHeader());
                BuildBackpack();
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
            AddLine(Loc.TF("char.rep_line", faction.DisplayName, ReputationTiers.Label(tier), value.ToString("+0;-0;0")),
                ReputationTiers.Color(tier));
        }
    }

    private void BuildProgression()
    {
        if (_progression == null)
        {
            return;
        }

        AddHeader(Loc.T("char.tab_progression"));
        string xp = _progression.IsMaxLevel ? Loc.T("char.xp_max") : $"{_progression.CurrentXp} / {_progression.XpToNext}";
        AddLine(Loc.TF("char.level_line", _progression.Level, xp));

        // XP toward the next level as a bar (30.5G) — same glanceable shape as corruption below.
        if (!_progression.IsMaxLevel && _progression.XpToNext > 0)
        {
            ProgressBar bar = UiTheme.Bar(UiTheme.Accent);
            bar.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            bar.Value = _progression.CurrentXp / (double)_progression.XpToNext;
            _list.AddChild(bar);
        }

        AddLine(Loc.TF("char.skill_points", _progression.SkillPoints));
    }

    private void BuildCorruption()
    {
        if (_corruption == null)
        {
            return;
        }

        AddHeader(Loc.T("char.corruption"));
        AddLine(Loc.TF("char.corruption_line", CorruptionTiers.Label(_corruption.Tier), _corruption.Value, CorruptionTiers.Max), UiTheme.CorruptionText);

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
                : !known && !_spellcasting.MeetsCorruption(spell) ? $"  {Loc.TF("char.spell_needs", CorruptionTiers.Label(spell.MinCorruptionTier))}"
                : !known ? $"  {Loc.TF("char.spell_cost", spell.LearnCost)}"
                : string.Empty;
            AddLine($"• {text}{suffix}", known ? tint : UiTheme.Dim, spell.Description);
        }
    }

    private void BuildPerks()
    {
        if (_perks == null || PerkDatabase.All.Count == 0)
        {
            return;
        }

        AddHeader(Loc.T("char.perks"));
        foreach (PerkResource perk in PerkDatabase.All)
        {
            int rank = _perks.RankOf(perk.Id);
            string text = Loc.TF("char.perk_rank", perk.DisplayName, rank, perk.MaxRank);

            if (_perks.CanLearn(perk))
            {
                PerkResource captured = perk;
                AddRow(text, Loc.TF("char.perk_learn", perk.Cost), () => _perks.Learn(captured));
            }
            else
            {
                bool maxed = rank >= perk.MaxRank;
                string suffix = maxed ? $"  {Loc.T("char.perk_maxed")}"
                    : !_perks.MeetsCorruption(perk) ? $"  {Loc.TF("char.perk_needs", CorruptionTiers.Label(perk.MinCorruptionTier))}"
                    : string.Empty;
                AddLine($"• {text}{suffix}");
            }
        }
    }

    private void BuildEquipment()
    {
        if (_equipment == null)
        {
            return;
        }

        AddHeader(Loc.T("char.equipment"));
        foreach (EquipmentSlot slot in EquipmentSlots.DisplayOrder)
        {
            ItemInstance? item = _equipment.GetEquipped(slot);
            string text = $"{EquipmentSlots.Label(slot)}: {item?.DisplayName ?? "—"}";

            if (item == null)
            {
                AddLine(text);
                continue;
            }

            EquipmentSlot captured = slot;
            AddRow(text, Loc.T("char.unequip"), () => _equipment.Unequip(captured), ItemRarities.Color(item.Rarity),
                item.Template.Description);
            AddAffixLines(item);
        }
    }

    private void BuildBackpack()
    {
        if (_inventory == null)
        {
            return;
        }

        if (_inventory.Stacks.Count == 0)
        {
            AddLine(Loc.T("char.empty"));
            return;
        }

        foreach (ItemStack stack in _inventory.Stacks)
        {
            ItemInstance instance = stack.Instance;
            string rarity = instance.Rarity != ItemRarity.Common ? $"  [{instance.Rarity}]" : string.Empty;
            string count = stack.Quantity > 1 ? $"  x{stack.Quantity}" : string.Empty;
            string text = $"{instance.DisplayName}{count}{rarity}";
            Color color = ItemRarities.Color(instance.Rarity);

            if (instance.IsEquippable && _equipment != null)
            {
                AddRow(text, Loc.T("char.equip"), () => _equipment.Equip(instance), color, instance.Template.Description);
            }
            else if (instance.Template is ConsumableItemResource && _inventory != null)
            {
                AddRow(text, Loc.T("char.use"), () => _inventory.Consume(instance), color, instance.Template.Description, instance.TemplateId);
            }
            else
            {
                AddLine($"• {text}", color, instance.Template.Description);
            }

            AddAffixLines(instance);
        }
    }

    private void AddAffixLines(ItemInstance instance)
    {
        foreach (ItemAffix affix in instance.Affixes)
        {
            _list.AddChild(UiTheme.Caption($"      {affix.DisplayValue}", UiTheme.Good));
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
