using Embervale.Core;
using Embervale.Core.Events;
using Embervale.Items;
using Embervale.Localization;
using Godot;

namespace Embervale.UI;

/// <summary>
/// The persistent bottom-of-screen <b>consumables</b> quick-use bar — five cells mirroring the player's
/// <see cref="HotbarComponent"/>. Each shows its binding, an icon for what the consumable does, its
/// name and live count; pressing the binding uses the slot (handled by the component), clicking a cell
/// here clears it. Consumables are assigned from the inventory panel. Rebuilds from a dirty flag,
/// never during a button signal.
///
/// Three things here are not just drawing:
/// <list type="bullet">
/// <item>the <b>cooldown sweep</b>: a use is noticed by the slot's count dropping while no menu is
/// open, and the wait is drawn over the cell from <see cref="CooldownClock.Consumables"/>;</item>
/// <item>the <b>gamepad chord</b>: while the left trigger is held, the d-pad and Select are lent to
/// the five slots (<see cref="GameInput.SetHotbarChord"/>), so the component's own polling fires
/// with no second input path;</item>
/// <item>a <b>watchdog</b> that hands the keyboard back if a search field lost focus without saying
/// so (<see cref="GameInput.SetTextEntry"/>). This node lives as long as the session and processes
/// through a pause, which is what makes it the right place for both of the last two.</item>
/// </list>
/// </summary>
public partial class HotbarPanel : CanvasLayer
{
    private HotbarComponent? _hotbar;
    private InventoryComponent? _inventory;
    private PanelContainer _panel = null!;
    private HBoxContainer _row = null!;
    private Label _caption = null!;
    private bool _dirty = true;

    // Per slot: the cooldown key the cell is showing (empty for none), the overlay that draws its
    // sweep and the seconds reading on it, and the count last seen for the slot's item.
    private readonly string[] _cooldownKeys = new string[HotbarComponent.SlotCount];
    private readonly Control?[] _sweeps = new Control?[HotbarComponent.SlotCount];
    private readonly Label?[] _waits = new Label?[HotbarComponent.SlotCount];
    private readonly string[] _countedIds = new string[HotbarComponent.SlotCount];
    private readonly int[] _counts = new int[HotbarComponent.SlotCount];
    private bool _sweeping;

    /// <summary>Cell size: wide enough for a two-line item name, tall enough for the number line and both.</summary>
    private const float CellWidth = 90f;
    private const float CellHeight = 72f;

    /// <summary>When set (by the bootstrap, to <see cref="GameHud.BottomDock"/>), the bar parents
    /// into the HUD's bottom flow bar instead of anchoring itself — flow siblings can't overlap
    /// the vitals at any UI scale. Null falls back to self-anchoring (kept for tests/tools).</summary>
    public Control? Dock { get; set; }

    public void SetHotbar(HotbarComponent? hotbar)
    {
        _hotbar = hotbar;
        _dirty = true;
        Rebaseline();
    }

    public void SetInventory(InventoryComponent? inventory)
    {
        _inventory = inventory;
        _dirty = true;
        Rebaseline();
    }

    public override void _Ready()
    {
        // Through a pause: the chord has to be let go and the keyboard handed back whether or not the
        // world is running. The cooldown clock is advanced only while it is (see _Process).
        ProcessMode = ProcessModeEnum.Always;

        for (int i = 0; i < HotbarComponent.SlotCount; i++)
        {
            _cooldownKeys[i] = string.Empty;
            _countedIds[i] = string.Empty;
        }

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

        MarginContainer pad = UiTheme.Padding(UiTheme.SpaceSm);
        panel.AddChild(pad);

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", UiTheme.SpaceXs);
        pad.AddChild(column);

        _caption = UiTheme.Body(Loc.T("hud.consumables"), UiTheme.Dim);
        _caption.HorizontalAlignment = HorizontalAlignment.Center;
        column.AddChild(_caption);

        _row = new HBoxContainer();
        _row.AddThemeConstantOverride("separation", UiTheme.SpaceSm);
        column.AddChild(_row);

        EventBus.Instance?.Subscribe<HotbarChangedEvent>(OnHotbarChanged);
        EventBus.Instance?.Subscribe<InventoryChangedEvent>(OnInventoryChanged);
        EventBus.Instance?.Subscribe<InputDeviceChangedEvent>(OnDeviceChanged);
        EventBus.Instance?.Subscribe<GameLoadedEvent>(OnGameLoaded);
    }

    public override void _ExitTree()
    {
        EventBus.Instance?.Unsubscribe<HotbarChangedEvent>(OnHotbarChanged);
        EventBus.Instance?.Unsubscribe<InventoryChangedEvent>(OnInventoryChanged);
        EventBus.Instance?.Unsubscribe<InputDeviceChangedEvent>(OnDeviceChanged);
        EventBus.Instance?.Unsubscribe<GameLoadedEvent>(OnGameLoaded);

        // The session is going away: nothing may be left borrowed from the input map.
        GameInput.SetHotbarChord(false);
        GameInput.SetTextEntry(false);
    }

    private void OnHotbarChanged(HotbarChangedEvent e)
    {
        _dirty = true;
        Rebaseline(); // a reassigned slot is a different item, not a use of the old one
    }

    private void OnInventoryChanged(InventoryChangedEvent e)
    {
        _dirty = true;
        NoticeUses();
    }

    private void OnDeviceChanged(InputDeviceChangedEvent e) => _dirty = true;

    /// <summary>A load is another timeline: its potions were not drunk in this one. The restore
    /// also changes every count on the way, which <see cref="NoticeUses"/> would read as uses.</summary>
    private void OnGameLoaded(GameLoadedEvent e)
    {
        CooldownClock.Consumables.Clear();
        Rebaseline();
        _dirty = true;
    }

    /// <summary>
    /// Starts a slot's cooldown when its item's count has just dropped while the player is in the
    /// world. Out there the only thing that takes a consumable from the pack is using it; in a menu
    /// it is also sold, stored and handed in, which is why a drop seen behind a menu is recorded
    /// and not counted (the inventory screen reports its own Use button to the same clock).
    /// </summary>
    private void NoticeUses()
    {
        bool inWorld = GameManager.Instance is { IsPlaying: true } && !UiState.MenuOpen;
        for (int i = 0; i < HotbarComponent.SlotCount; i++)
        {
            string id = _hotbar?.Get(i) ?? string.Empty;
            int now = id.Length > 0 ? _inventory?.CountOf(id) ?? 0 : 0;

            if (inWorld && id.Length > 0 && id == _countedIds[i] && now < _counts[i] &&
                ItemDatabase.Get(id) is ConsumableItemResource consumable)
            {
                CooldownClock.Consumables.Start(consumable.CooldownKey, consumable.CooldownSeconds);
            }

            _countedIds[i] = id;
            _counts[i] = now;
        }
    }

    private void Rebaseline()
    {
        for (int i = 0; i < HotbarComponent.SlotCount; i++)
        {
            string id = _hotbar?.Get(i) ?? string.Empty;
            _countedIds[i] = id;
            _counts[i] = id.Length > 0 ? _inventory?.CountOf(id) ?? 0 : 0;
        }
    }

    public override void _Process(double delta)
    {
        bool playing = GameManager.Instance is { IsPlaying: true };
        // Toggle the panel, not this layer — when docked the panel lives under the GameHud layer.
        _panel.Visible = playing;

        GameInput.SetHotbarChord(
            playing && !UiState.MenuOpen && Godot.Input.IsActionPressed(GameInput.HotbarChord));

        if (GameInput.TextEntryActive && GetViewport()?.GuiGetFocusOwner() is not LineEdit)
        {
            GameInput.SetTextEntry(false);
        }

        if (!playing)
        {
            return;
        }

        if (!GetTree().Paused)
        {
            CooldownClock.Consumables.Advance(delta);
        }

        if (_dirty)
        {
            _dirty = false;
            Rebuild();
        }

        UpdateSweeps();
    }

    /// <summary>Redraws the sweeps while any cooldown runs, and once more after the last one ends
    /// so the final sliver is wiped rather than left frozen on the cell.</summary>
    private void UpdateSweeps()
    {
        bool running = CooldownClock.Consumables.AnyRunning;
        if (!running && !_sweeping)
        {
            return;
        }

        _sweeping = running;
        for (int i = 0; i < HotbarComponent.SlotCount; i++)
        {
            if (_sweeps[i] is not { } sweep || !IsInstanceValid(sweep))
            {
                continue;
            }

            double left = _cooldownKeys[i].Length > 0 ? CooldownClock.Consumables.Remaining(_cooldownKeys[i]) : 0d;
            sweep.QueueRedraw();
            if (_waits[i] is { } wait && IsInstanceValid(wait))
            {
                wait.Text = left > 0d ? ItemPresentation.Seconds((float)System.Math.Ceiling(left)) : string.Empty;
            }
        }
    }

    private void Rebuild()
    {
        foreach (Node child in _row.GetChildren())
        {
            child.QueueFree();
        }

        bool pad = InputDevice.GamepadActive;
        _caption.Text = pad
            ? Loc.TF("hud.consumables_pad", GameInput.HotbarChordLabel)
            : Loc.T("hud.consumables");

        // ⚠️ AN EMPTY SLOT SHOWS ITS NUMBER AND NOTHING ELSE (§53, §72, §73).
        //
        // It used to print the word "(EMPTY)" in every unassigned cell, so a fresh save's HUD carried
        // **four copies of the word EMPTY** across the bottom of the screen — which is both the
        // "placeholder text" §73 forbids and the debug-panel read §72 forbids, and it drew the eye to
        // the four cells with no information in them rather than the one with a potion in it. A blank
        // recessed cell already says "nothing here"; the word is the interface talking about itself.
        for (int i = 0; i < HotbarComponent.SlotCount; i++)
        {
            string id = _hotbar?.Get(i) ?? string.Empty;
            bool filled = id.Length > 0;
            var consumable = ItemDatabase.Get(id) as ConsumableItemResource;

            Button cell = UiTheme.Action(string.Empty);
            cell.CustomMinimumSize = new Vector2(CellWidth, CellHeight);
            cell.TooltipText = Tooltip(i, filled, consumable, pad);
            cell.Disabled = !filled;

            // The cell is a Button, which never sizes to its children, so the content is pinned to the cell
            // with an inset of its own: without one the name and the count ran into the cell's border.
            var inset = new MarginContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
            inset.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            inset.AddThemeConstantOverride("margin_left", UiTheme.SpaceXs);
            inset.AddThemeConstantOverride("margin_right", UiTheme.SpaceXs);
            inset.AddThemeConstantOverride("margin_top", UiTheme.Space2xs);
            inset.AddThemeConstantOverride("margin_bottom", UiTheme.Space2xs);

            var stack = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
            stack.AddThemeConstantOverride("separation", UiTheme.LineGap);
            inset.AddChild(stack);

            // The binding is always present and always in the same corner — that is what makes the row
            // scannable as "slot 3" rather than as a list of names. On a pad it is the chord's button
            // (the bar's caption names the trigger to hold). The effect icon and the count share its
            // line, which keeps a two-line name from pushing either off the cell.
            var head = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
            head.AddThemeConstantOverride("separation", UiTheme.Space2xs);
            Label number = UiTheme.Caption(GameInput.HotbarPromptLabel(i), filled ? UiTheme.Accent : UiTheme.Disabled);
            number.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            head.AddChild(number);
            stack.AddChild(head);

            _cooldownKeys[i] = string.Empty;
            _sweeps[i] = null;
            _waits[i] = null;

            if (filled)
            {
                string name = ItemDatabase.Get(id)?.DisplayName ?? id;
                int count = _inventory?.CountOf(id) ?? 0;

                // What the slot does, as a shape: a heart, a bolt, a drop, a shield, a sun. Read
                // faster than the name, and it is the part that survives when the name is two
                // truncated lines.
                if (consumable != null)
                {
                    head.AddChild(UiIcon.Create(
                        ItemPresentation.EffectIcon(consumable.Effect), 14f, ItemSlot.EffectColor(consumable.Effect)));
                }

                Label label = UiTheme.Caption(name, UiTheme.Text);
                label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
                label.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
                stack.AddChild(label);

                // A count of one is not information — every consumable you can use you have at least
                // one of, so printing "x1" adds a character to every slot and tells the player nothing.
                if (count > 1)
                {
                    head.AddChild(UiTheme.Caption($"×{count}", UiTheme.Dim));
                }
            }

            cell.AddChild(inset);

            if (consumable is { CooldownSeconds: > 0f })
            {
                _cooldownKeys[i] = consumable.CooldownKey;
                AddSweep(cell, i);
            }

            int slot = i;
            cell.Pressed += () => _hotbar?.Clear(slot);
            _row.AddChild(cell);
        }

        // Draw the new cells' sweeps on their first frame, whether or not a cooldown started since.
        _sweeping = true;
    }

    /// <summary>
    /// The cooldown overlay for one cell: a dark wedge covering the share of the wait still to run,
    /// unwinding clockwise from twelve o'clock, with the seconds left in the middle. The wedge is the
    /// glance and the number is the answer; neither depends on a colour.
    ///
    /// Drawn through the <c>Draw</c> signal of a plain <see cref="Control"/> rather than a subclass
    /// overriding <c>_Draw</c>, so the bar adds no new script type for five small overlays.
    /// </summary>
    private void AddSweep(Button cell, int slot)
    {
        var sweep = new Control { MouseFilter = Control.MouseFilterEnum.Ignore, ClipContents = true };
        sweep.SetAnchorsPreset(Control.LayoutPreset.FullRect);

        string key = _cooldownKeys[slot];
        Color shade = UiTheme.ScrimBg with { A = 0.66f };
        sweep.Draw += () =>
        {
            float fraction = (float)CooldownClock.Consumables.Fraction(key);
            if (fraction < 0.01f)
            {
                return;
            }

            Vector2 centre = sweep.Size / 2f;
            float radius = sweep.Size.Length(); // past the corners; the control clips it to the cell
            int steps = Mathf.Max(3, Mathf.CeilToInt(32f * fraction));
            float from = (-Mathf.Pi / 2f) + (Mathf.Tau * (1f - fraction));
            float span = Mathf.Tau * fraction;

            Vector2 previous = centre + (new Vector2(Mathf.Cos(from), Mathf.Sin(from)) * radius);
            for (int s = 1; s <= steps; s++)
            {
                float angle = from + (span * s / steps);
                Vector2 next = centre + (new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius);
                sweep.DrawColoredPolygon(new[] { centre, previous, next }, shade);
                previous = next;
            }
        };

        Label wait = UiTheme.Header(string.Empty);
        wait.MouseFilter = Control.MouseFilterEnum.Ignore;
        wait.HorizontalAlignment = HorizontalAlignment.Center;
        wait.VerticalAlignment = VerticalAlignment.Center;
        wait.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        sweep.AddChild(wait);

        cell.AddChild(sweep);
        _sweeps[slot] = sweep;
        _waits[slot] = wait;
    }

    /// <summary>The cell's tooltip: for a filled slot, what the consumable does and how long it
    /// makes the player wait, then how to use the slot on the device in hand.</summary>
    private static string Tooltip(int slot, bool filled, ConsumableItemResource? consumable, bool pad)
    {
        if (!filled)
        {
            return Loc.T("hud.hotbar_empty_hint");
        }

        string hint = pad
            ? Loc.TF("hud.hotbar_hint_pad", GameInput.HotbarPadLabel(slot))
            : Loc.T("hud.hotbar_hint");
        if (consumable == null)
        {
            return hint;
        }

        var lines = new System.Collections.Generic.List<string> { consumable.DisplayName };
        if (ItemSlot.EffectText(consumable) is { Length: > 0 } effect)
        {
            lines.Add(effect);
        }

        if (ItemSlot.CooldownText(consumable) is { } cooldown)
        {
            lines.Add(cooldown);
        }

        lines.Add(hint);
        return string.Join("\n", lines);
    }
}
