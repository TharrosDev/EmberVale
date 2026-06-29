using Embervale.Core;
using Embervale.Core.Events;
using Embervale.Corruption;
using Embervale.Factions;
using Embervale.Items;
using Embervale.Localization;
using Embervale.Progression;
using Godot;

namespace Embervale.UI;

/// <summary>
/// The character screen: toggled with the <c>inventory</c> action, it shows the
/// equipment slots (with Unequip buttons) and the backpack contents (with Equip
/// buttons on equippable stacks). While open it frees the mouse and sets
/// <see cref="UiState.MenuOpen"/> so the player controller stops driving the
/// character. Rebuilt from a dirty flag in <c>_Process</c> (never during a button
/// signal) to avoid freeing a control mid-callback.
/// </summary>
public partial class InventoryPanel : CanvasLayer
{
    private InventoryComponent? _inventory;
    private EquipmentComponent? _equipment;
    private HotbarComponent? _hotbar;
    private ProgressionComponent? _progression;
    private PerksComponent? _perks;
    private ReputationComponent? _reputation;
    private CorruptionComponent? _corruption;
    private PanelContainer _panel = null!;
    private VBoxContainer _list = null!;
    private bool _dirty = true;

    public override void _Ready()
    {
        _panel = UiTheme.Panel();
        _panel.Visible = false;
        _panel.Position = new Vector2(900, 16);
        _panel.CustomMinimumSize = new Vector2(360, 0);
        AddChild(_panel);

        MarginContainer margin = UiTheme.Padding(12);
        _panel.AddChild(margin);

        // A bounded scroll area so a full backpack + perk list never runs off-screen.
        var scroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(336, 520),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        margin.AddChild(scroll);

        _list = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _list.AddThemeConstantOverride("separation", 3);
        scroll.AddChild(_list);

        EventBus.Instance?.Subscribe<InventoryChangedEvent>(OnChanged);
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
        _dirty = true;
    }

    public void SetEquipment(EquipmentComponent? equipment)
    {
        _equipment = equipment;
        _dirty = true;
    }

    public void SetHotbar(HotbarComponent? hotbar)
    {
        _hotbar = hotbar;
        _dirty = true;
    }

    public void SetProgression(ProgressionComponent? progression)
    {
        _progression = progression;
        _dirty = true;
    }

    public void SetPerks(PerksComponent? perks)
    {
        _perks = perks;
        _dirty = true;
    }

    public void SetReputation(ReputationComponent? reputation)
    {
        _reputation = reputation;
        _dirty = true;
    }

    public void SetCorruption(CorruptionComponent? corruption)
    {
        _corruption = corruption;
        _dirty = true;
    }

    public override void _Process(double delta)
    {
        if (Godot.Input.IsActionJustPressed(GameInput.Inventory))
        {
            Toggle();
        }

        if (_panel.Visible && _dirty)
        {
            Rebuild();
        }
    }

    private void Toggle()
    {
        bool open = !_panel.Visible;
        _panel.Visible = open;
        if (open) UiState.Open(this); else UiState.Close(this);

        bool playing = GameManager.Instance is { IsPlaying: true };
        Godot.Input.MouseMode = UiState.MenuOpen || !playing
            ? Godot.Input.MouseModeEnum.Visible
            : Godot.Input.MouseModeEnum.Captured;

        if (open)
        {
            _dirty = true;
        }
    }

    private void OnChanged(InventoryChangedEvent e) => _dirty = true;

    private void OnEquipmentChanged(EquipmentChangedEvent e) => _dirty = true;

    private void OnXpGained(XpGainedEvent e) => _dirty = true;

    private void OnLeveledUp(LeveledUpEvent e) => _dirty = true;

    private void OnPerkChanged(PerkChangedEvent e) => _dirty = true;

    private void OnReputationChanged(ReputationChangedEvent e) => _dirty = true;

    private void OnCorruptionChanged(CorruptionChangedEvent e) => _dirty = true;

    private void Rebuild()
    {
        _dirty = false;

        foreach (Node child in _list.GetChildren())
        {
            _list.RemoveChild(child);
            child.QueueFree();
        }

        AddHeader(Loc.T("char.title"));
        BuildProgression();
        BuildCorruption();
        BuildEquipment();
        AddHeader(BackpackHeader());
        BuildBackpack();
        BuildPerks();
        BuildFactions();
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
            AddLine(Loc.TF("char.dread", dread), new Color(0.74f, 0.45f, 0.62f));
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

        string xp = _progression.IsMaxLevel ? Loc.T("char.xp_max") : $"{_progression.CurrentXp} / {_progression.XpToNext}";
        AddLine(Loc.TF("char.level_line", _progression.Level, xp));
        AddLine(Loc.TF("char.skill_points", _progression.SkillPoints));
    }

    private void BuildCorruption()
    {
        if (_corruption == null)
        {
            return;
        }

        AddHeader(Loc.T("char.corruption"));
        AddLine(Loc.TF("char.corruption_line", CorruptionTiers.Label(_corruption.Tier), _corruption.Value, CorruptionTiers.Max), UiTheme.Corruption);

        ProgressBar bar = UiTheme.Bar(UiTheme.Corruption);
        bar.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        bar.Value = _corruption.Value / (double)CorruptionTiers.Max;
        _list.AddChild(bar);
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
                AddRow(text, Loc.T("char.equip"), () => _equipment.Equip(instance), color, instance.Template.Description, instance.TemplateId);
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
            AddLine($"      {affix.DisplayValue}", new Color(0.65f, 0.75f, 0.65f));
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
        row.AddThemeConstantOverride("separation", 8);

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
