using Embervale.Core;
using Embervale.Core.Events;
using Embervale.Items;
using Embervale.Localization;
using Godot;

namespace Embervale.UI;

/// <summary>
/// The persistent bottom-of-screen quick-use bar — five cells mirroring the player's
/// <see cref="HotbarComponent"/>. Each shows its number, the assigned item's name and live count;
/// pressing 1-5 uses the slot (handled by the component), clicking a cell here clears it. Items are
/// assigned from the inventory panel. Rebuilds from a dirty flag, never during a button signal.
/// </summary>
public partial class HotbarPanel : CanvasLayer
{
    private HotbarComponent? _hotbar;
    private InventoryComponent? _inventory;
    private HBoxContainer _row = null!;
    private bool _dirty = true;

    public void SetHotbar(HotbarComponent? hotbar)
    {
        _hotbar = hotbar;
        _dirty = true;
    }

    public void SetInventory(InventoryComponent? inventory)
    {
        _inventory = inventory;
        _dirty = true;
    }

    public override void _Ready()
    {
        PanelContainer panel = UiTheme.Panel();
        panel.AnchorLeft = 0.5f;
        panel.AnchorRight = 0.5f;
        panel.AnchorTop = 1f;
        panel.AnchorBottom = 1f;
        panel.GrowHorizontal = Control.GrowDirection.Both;
        panel.GrowVertical = Control.GrowDirection.Begin;
        panel.OffsetBottom = -12f;
        AddChild(panel);

        MarginContainer pad = UiTheme.Padding(8);
        panel.AddChild(pad);
        _row = new HBoxContainer();
        _row.AddThemeConstantOverride("separation", 6);
        pad.AddChild(_row);

        EventBus.Instance?.Subscribe<HotbarChangedEvent>(OnDirty);
        EventBus.Instance?.Subscribe<InventoryChangedEvent>(OnDirty);
        EventBus.Instance?.Subscribe<EquipmentChangedEvent>(OnDirty);
    }

    public override void _ExitTree()
    {
        EventBus.Instance?.Unsubscribe<HotbarChangedEvent>(OnDirty);
        EventBus.Instance?.Unsubscribe<InventoryChangedEvent>(OnDirty);
        EventBus.Instance?.Unsubscribe<EquipmentChangedEvent>(OnDirty);
    }

    private void OnDirty(HotbarChangedEvent e) => _dirty = true;

    private void OnDirty(InventoryChangedEvent e) => _dirty = true;

    private void OnDirty(EquipmentChangedEvent e) => _dirty = true;

    public override void _Process(double delta)
    {
        bool playing = GameManager.Instance is { IsPlaying: true };
        Visible = playing;
        if (!_dirty || !playing)
        {
            return;
        }

        _dirty = false;
        Rebuild();
    }

    private void Rebuild()
    {
        foreach (Node child in _row.GetChildren())
        {
            child.QueueFree();
        }

        for (int i = 0; i < HotbarComponent.SlotCount; i++)
        {
            string id = _hotbar?.Get(i) ?? string.Empty;
            string text;
            if (id.Length == 0)
            {
                text = $"{i + 1}\n{Loc.T("char.empty")}";
            }
            else
            {
                string name = ItemDatabase.Get(id)?.DisplayName ?? id;
                int count = _inventory?.CountOf(id) ?? 0;
                string qty = count > 1 ? $" x{count}" : string.Empty;
                text = $"{i + 1}. {name}{qty}";
            }

            Button cell = UiTheme.Action(text);
            cell.CustomMinimumSize = new Vector2(112f, 0f);
            cell.TooltipText = Loc.T("hud.hotbar_hint");
            int slot = i;
            cell.Pressed += () => _hotbar?.Clear(slot);
            _row.AddChild(cell);
        }
    }
}
