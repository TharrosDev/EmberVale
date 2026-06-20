using Embervale.Core;
using Embervale.Core.Events;
using Embervale.Items;
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
    private ProgressionComponent? _progression;
    private PerksComponent? _perks;
    private PanelContainer _panel = null!;
    private VBoxContainer _list = null!;
    private bool _dirty = true;

    public override void _Ready()
    {
        _panel = new PanelContainer
        {
            Visible = false,
            Position = new Vector2(900, 16),
            CustomMinimumSize = new Vector2(360, 0),
        };
        AddChild(_panel);

        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 12);
        margin.AddThemeConstantOverride("margin_right", 12);
        margin.AddThemeConstantOverride("margin_top", 10);
        margin.AddThemeConstantOverride("margin_bottom", 10);
        _panel.AddChild(margin);

        _list = new VBoxContainer();
        margin.AddChild(_list);

        EventBus.Instance?.Subscribe<InventoryChangedEvent>(OnChanged);
        EventBus.Instance?.Subscribe<EquipmentChangedEvent>(OnEquipmentChanged);
        EventBus.Instance?.Subscribe<XpGainedEvent>(OnXpGained);
        EventBus.Instance?.Subscribe<LeveledUpEvent>(OnLeveledUp);
        EventBus.Instance?.Subscribe<PerkChangedEvent>(OnPerkChanged);
    }

    public override void _ExitTree()
    {
        EventBus.Instance?.Unsubscribe<InventoryChangedEvent>(OnChanged);
        EventBus.Instance?.Unsubscribe<EquipmentChangedEvent>(OnEquipmentChanged);
        EventBus.Instance?.Unsubscribe<XpGainedEvent>(OnXpGained);
        EventBus.Instance?.Unsubscribe<LeveledUpEvent>(OnLeveledUp);
        EventBus.Instance?.Unsubscribe<PerkChangedEvent>(OnPerkChanged);
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
        UiState.MenuOpen = open;

        bool playing = GameManager.Instance is { IsPlaying: true };
        Godot.Input.MouseMode = open || !playing
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

    private void Rebuild()
    {
        _dirty = false;

        foreach (Node child in _list.GetChildren())
        {
            _list.RemoveChild(child);
            child.QueueFree();
        }

        AddHeader("CHARACTER   (I to close)");
        BuildProgression();
        BuildEquipment();
        AddHeader(BackpackHeader());
        BuildBackpack();
        BuildPerks();
    }

    private void BuildProgression()
    {
        if (_progression == null)
        {
            return;
        }

        string xp = _progression.IsMaxLevel ? "MAX" : $"{_progression.CurrentXp} / {_progression.XpToNext}";
        AddLine($"Level {_progression.Level}    XP {xp}");
        AddLine($"Skill points: {_progression.SkillPoints}");
    }

    private void BuildPerks()
    {
        if (_perks == null || PerkDatabase.All.Count == 0)
        {
            return;
        }

        AddHeader("PERKS");
        foreach (PerkResource perk in PerkDatabase.All)
        {
            int rank = _perks.RankOf(perk.Id);
            string text = $"{perk.DisplayName}  ({rank}/{perk.MaxRank})";

            if (_perks.CanLearn(perk))
            {
                PerkResource captured = perk;
                AddRow(text, $"Learn ({perk.Cost})", () => _perks.Learn(captured));
            }
            else
            {
                bool maxed = rank >= perk.MaxRank;
                AddLine(maxed ? $"• {text}  [maxed]" : $"• {text}");
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
            AddRow(text, "Unequip", () => _equipment.Unequip(captured), ItemRarities.Color(item.Rarity));
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
            AddLine("(empty)");
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
                AddRow(text, "Equip", () => _equipment.Equip(instance), color);
            }
            else
            {
                AddLine($"• {text}", color);
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
            return "BACKPACK";
        }

        return $"BACKPACK   {_inventory.UsedSlots}/{_inventory.Capacity}   wt {_inventory.TotalWeight:0.0}";
    }

    private void AddHeader(string text)
    {
        var label = new Label { Text = text };
        label.AddThemeFontSizeOverride("font_size", 16);
        _list.AddChild(label);
    }

    private void AddLine(string text, Color? color = null)
    {
        var label = new Label { Text = text };
        label.AddThemeFontSizeOverride("font_size", 14);
        if (color is { } c)
        {
            label.AddThemeColorOverride("font_color", c);
        }

        _list.AddChild(label);
    }

    private void AddRow(string text, string action, System.Action onPressed, Color? color = null)
    {
        var row = new HBoxContainer();

        var label = new Label
        {
            Text = text,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        label.AddThemeFontSizeOverride("font_size", 14);
        if (color is { } c)
        {
            label.AddThemeColorOverride("font_color", c);
        }

        row.AddChild(label);

        var button = new Button { Text = action };
        button.Pressed += () => onPressed();
        row.AddChild(button);

        _list.AddChild(row);
    }
}
