using Embervale.Core;
using Embervale.Core.Events;
using Embervale.Items;
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
    }

    public override void _ExitTree()
    {
        EventBus.Instance?.Unsubscribe<InventoryChangedEvent>(OnChanged);
        EventBus.Instance?.Unsubscribe<EquipmentChangedEvent>(OnEquipmentChanged);
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

    private void Rebuild()
    {
        _dirty = false;

        foreach (Node child in _list.GetChildren())
        {
            _list.RemoveChild(child);
            child.QueueFree();
        }

        AddHeader("CHARACTER   (I to close)");
        BuildEquipment();
        AddHeader(BackpackHeader());
        BuildBackpack();
    }

    private void BuildEquipment()
    {
        if (_equipment == null)
        {
            return;
        }

        foreach (EquipmentSlot slot in EquipmentSlots.DisplayOrder)
        {
            EquippableItemResource? item = _equipment.GetEquipped(slot);
            string text = $"{EquipmentSlots.Label(slot)}: {item?.DisplayName ?? "—"}";

            if (item == null)
            {
                AddLine(text);
                continue;
            }

            EquipmentSlot captured = slot;
            AddRow(text, "Unequip", () => _equipment.Unequip(captured));
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
            string rarity = stack.Item.Rarity != ItemRarity.Common ? $"  [{stack.Item.Rarity}]" : string.Empty;
            string text = $"{stack.Item.DisplayName}  x{stack.Quantity}{rarity}";

            if (stack.Item is EquippableItemResource equippable && _equipment != null)
            {
                AddRow(text, "Equip", () => _equipment.Equip(equippable));
            }
            else
            {
                AddLine($"• {text}");
            }
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

    private void AddLine(string text)
    {
        var label = new Label { Text = text };
        label.AddThemeFontSizeOverride("font_size", 14);
        _list.AddChild(label);
    }

    private void AddRow(string text, string action, System.Action onPressed)
    {
        var row = new HBoxContainer();

        var label = new Label
        {
            Text = text,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        label.AddThemeFontSizeOverride("font_size", 14);
        row.AddChild(label);

        var button = new Button { Text = action };
        button.Pressed += () => onPressed();
        row.AddChild(button);

        _list.AddChild(row);
    }
}
