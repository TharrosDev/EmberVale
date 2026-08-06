using Embervale.Core;
using Embervale.Core.Events;
using Embervale.Items;
using Embervale.Localization;
using Godot;

namespace Embervale.UI;

/// <summary>
/// The persistent bottom-of-screen <b>consumables</b> quick-use bar — five cells mirroring the player's
/// <see cref="HotbarComponent"/>. Each shows its number, the assigned consumable's name and live count;
/// pressing 1-5 uses the slot (handled by the component), clicking a cell here clears it. Consumables are
/// assigned from the inventory panel. Rebuilds from a dirty flag, never during a button signal.
/// </summary>
public partial class HotbarPanel : CanvasLayer
{
    private HotbarComponent? _hotbar;
    private InventoryComponent? _inventory;
    private PanelContainer _panel = null!;
    private HBoxContainer _row = null!;
    private bool _dirty = true;

    /// <summary>When set (by the bootstrap, to <see cref="GameHud.BottomDock"/>), the bar parents
    /// into the HUD's bottom flow bar instead of anchoring itself — flow siblings can't overlap
    /// the vitals at any UI scale. Null falls back to self-anchoring (kept for tests/tools).</summary>
    public Control? Dock { get; set; }

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
        // A Well, not a Panel (37.5H). The hotbar is a strip of slots docked to the bottom bar;
        // as a full framed panel it carried a 2 px brass rule and its own grain ShaderMaterial,
        // competing with the vitals panel beside it. Recessed reads correctly for a row of slots.
        PanelContainer panel = _panel = UiTheme.Well();
        if (Dock != null)
        {
            Dock.AddChild(panel);
        }
        else
        {
            panel.AnchorLeft = 0.5f;
            panel.AnchorRight = 0.5f;
            panel.AnchorTop = 1f;
            panel.AnchorBottom = 1f;
            panel.GrowHorizontal = Control.GrowDirection.Both;
            panel.GrowVertical = Control.GrowDirection.Begin;
            panel.OffsetBottom = -12f;
            AddChild(panel);
        }

        MarginContainer pad = UiTheme.Padding(8);
        panel.AddChild(pad);

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 4);
        pad.AddChild(column);

        Label caption = UiTheme.Body(Loc.T("hud.consumables"), UiTheme.Dim);
        caption.HorizontalAlignment = HorizontalAlignment.Center;
        column.AddChild(caption);

        _row = new HBoxContainer();
        _row.AddThemeConstantOverride("separation", 6);
        column.AddChild(_row);

        EventBus.Instance?.Subscribe<HotbarChangedEvent>(OnDirty);
        EventBus.Instance?.Subscribe<InventoryChangedEvent>(OnDirty);
    }

    public override void _ExitTree()
    {
        EventBus.Instance?.Unsubscribe<HotbarChangedEvent>(OnDirty);
        EventBus.Instance?.Unsubscribe<InventoryChangedEvent>(OnDirty);
    }

    private void OnDirty(HotbarChangedEvent e) => _dirty = true;

    private void OnDirty(InventoryChangedEvent e) => _dirty = true;

    public override void _Process(double delta)
    {
        bool playing = GameManager.Instance is { IsPlaying: true };
        // Toggle the panel, not this layer — when docked the panel lives under the GameHud layer.
        _panel.Visible = playing;
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
            cell.CustomMinimumSize = new Vector2(78f, 0f);
            cell.TooltipText = Loc.T("hud.hotbar_hint");
            int slot = i;
            cell.Pressed += () => _hotbar?.Clear(slot);
            _row.AddChild(cell);
        }
    }
}
