using Embervale.Core;
using Embervale.Core.Events;
using Embervale.Entities;
using Embervale.Items;
using Embervale.Localization;
using Godot;

namespace Embervale.UI;

/// <summary>
/// The persistent bottom-of-screen <b>consumables</b> quick-use bar — five cells mirroring the player's
/// <see cref="HotbarComponent"/>. Each cell is a square keylined well showing the item's icon, the
/// input that uses it and how many are left (an empty one is the faintest hint of a well); pressing the binding uses the slot (handled by the component),
/// clicking a cell here clears it. Consumables are assigned from the inventory panel. Rebuilds from a
/// dirty flag, never during a button signal.
///
/// A cell is in exactly one <see cref="HotbarSlotState"/> and each state has a shape of its own, so
/// "wait" and "cannot" are never the same picture: a cooldown is a wipe (and a number for the last
/// seconds), a level lock is a padlock over a dimmed icon, an item with nothing to do is dimmed with
/// no mark, and an empty pack shows its zero.
///
/// Three things here are not just drawing:
/// <list type="bullet">
/// <item>the <b>cooldown wipe</b>: the wait is drawn over the cell from the real cooldown the
/// player's <see cref="HotbarComponent.CooldownFraction"/> reports, the same one that refuses the
/// use, so the bar cannot disagree with the rule;</item>
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
    private HBoxContainer _chord = null!;
    private GameHud? _hud;
    private bool _dirty = true;

    // Per slot, what the cell is made of and what it last showed. A slot whose item has no cooldown
    // has no sweep; an empty slot has no icon.
    private readonly Control?[] _sweeps = new Control?[HotbarComponent.SlotCount];
    private readonly Label?[] _waits = new Label?[HotbarComponent.SlotCount];
    private readonly TextureRect?[] _icons = new TextureRect?[HotbarComponent.SlotCount];
    private readonly TextureRect?[] _locks = new TextureRect?[HotbarComponent.SlotCount];
    private readonly Label?[] _countLabels = new Label?[HotbarComponent.SlotCount];
    private readonly ConsumableItemResource?[] _items = new ConsumableItemResource?[HotbarComponent.SlotCount];
    private readonly Color[] _iconTints = new Color[HotbarComponent.SlotCount];
    private readonly bool[] _filled = new bool[HotbarComponent.SlotCount];
    private readonly int[] _counts = new int[HotbarComponent.SlotCount];
    private readonly int[] _statesShown = new int[HotbarComponent.SlotCount];
    private readonly int[] _numeralsShown = new int[HotbarComponent.SlotCount];
    private readonly int[] _wipeStepsShown = new int[HotbarComponent.SlotCount];
    private readonly Vector2[] _wedge = new Vector2[3];
    private bool _sweeping;
    private float _cellWidthBuilt = HudCoreMetrics.HotbarCell;
    private int _panelShown = -1;
    private float _stateTimer;

    /// <summary>How often each filled cell re-asks whether its item can be used. The answer turns on
    /// a pool filling, a status ending or a level, none of which is worth an event subscription that
    /// fires every regeneration tick; four times a second is as fast as the eye wants it.</summary>
    private const float StateInterval = 0.25f;

    // The seconds a cooling cell prints, made once: the number changes once a second, and then it
    // is a lookup and not a format.
    private static readonly string[] Numerals = { string.Empty, "1", "2", "3", "4", "5", "6", "7", "8", "9" };

    // Converted once: a string handed to Input converts (and allocates) on every call.
    private static readonly StringName ChordAction = GameInput.HotbarChord;

    /// <summary>When set (by the bootstrap, to <see cref="GameHud.BottomDock"/>), the bar parents
    /// into the HUD's bottom flow bar instead of anchoring itself — flow siblings can't overlap
    /// the vitals at any UI scale. Null falls back to self-anchoring (kept for tests/tools).</summary>
    public Control? Dock { get; set; }

    /// <summary>What a cell is showing. Read by the screenshot harness.</summary>
    public HotbarSlotState SlotStateForCapture(int slot) =>
        slot >= 0 && slot < HotbarComponent.SlotCount && _statesShown[slot] >= 0
            ? (HotbarSlotState)_statesShown[slot]
            : HotbarSlotState.Empty;

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
        // Through a pause: the chord has to be let go and the keyboard handed back whether or not the
        // world is running. The cooldowns themselves tick on the player and stop with the tree.
        ProcessMode = ProcessModeEnum.Always;

        // No ground of its own. It was a Panel, then a Well: a box round five boxes. The cells are
        // the only thing here that needs an edge, and each has its own keyline.
        PanelContainer panel = _panel = UiTheme.HudBare();
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
            panel.OffsetBottom = -UiTheme.SpaceLg;
            AddChild(panel);
        }

        var column = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        column.AddThemeConstantOverride("separation", UiTheme.SpaceXs);
        panel.AddChild(column);

        // On a pad the cells show the chord's pressed half; this line above them names the held half.
        // Absent on a keyboard, where the cells say everything.
        _chord = new HBoxContainer
        {
            Visible = false,
            Alignment = BoxContainer.AlignmentMode.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _chord.AddThemeConstantOverride("separation", UiTheme.SpaceXs);
        column.AddChild(_chord);

        _row = new HBoxContainer();
        _row.AddThemeConstantOverride("separation", UiTheme.SpaceXs);
        column.AddChild(_row);

        System.Array.Fill(_statesShown, -1);

        // The cells narrow with the layout so the bottom bar still fits a small screen.
        _hud = GameHud.Of(_panel);
        if (_hud != null)
        {
            _hud.LayoutFitted += OnLayoutFitted;
        }

        EventBus.Instance?.Subscribe<Settings.SettingsAppliedEvent>(OnSettingsApplied);
        EventBus.Instance?.Subscribe<HotbarChangedEvent>(OnHotbarChanged);
        EventBus.Instance?.Subscribe<InventoryChangedEvent>(OnInventoryChanged);
        EventBus.Instance?.Subscribe<InputDeviceChangedEvent>(OnDeviceChanged);
        EventBus.Instance?.Subscribe<InputBindingsChangedEvent>(OnBindingsChanged);
        EventBus.Instance?.Subscribe<GameLoadedEvent>(OnGameLoaded);
    }

    public override void _ExitTree()
    {
        if (_hud != null && IsInstanceValid(_hud))
        {
            _hud.LayoutFitted -= OnLayoutFitted;
        }

        EventBus.Instance?.Unsubscribe<Settings.SettingsAppliedEvent>(OnSettingsApplied);
        EventBus.Instance?.Unsubscribe<HotbarChangedEvent>(OnHotbarChanged);
        EventBus.Instance?.Unsubscribe<InventoryChangedEvent>(OnInventoryChanged);
        EventBus.Instance?.Unsubscribe<InputDeviceChangedEvent>(OnDeviceChanged);
        EventBus.Instance?.Unsubscribe<InputBindingsChangedEvent>(OnBindingsChanged);
        EventBus.Instance?.Unsubscribe<GameLoadedEvent>(OnGameLoaded);

        // The session is going away: nothing may be left borrowed from the input map.
        GameInput.SetHotbarChord(false);
        GameInput.SetTextEntry(false);
    }

    private void OnHotbarChanged(HotbarChangedEvent e)
    {
        _dirty = true;
        NoteChanged();
    }

    private void OnInventoryChanged(InventoryChangedEvent e) => _dirty = true;

    private void OnDeviceChanged(InputDeviceChangedEvent e) => _dirty = true;

    private void OnBindingsChanged(InputBindingsChangedEvent e) => _dirty = true;

    private void OnGameLoaded(GameLoadedEvent e) => _dirty = true;

    // The cells are made of the text scale and the contrast setting, and neither says when it moves.
    private void OnSettingsApplied(Settings.SettingsAppliedEvent e) => _dirty = true;

    private void OnLayoutFitted()
    {
        if (CellWidth() != _cellWidthBuilt)
        {
            _dirty = true;
        }
    }

    /// <summary>Brings a Dynamic hotbar up: an assignment, a count, a cooldown starting or ending.</summary>
    private void NoteChanged()
    {
        _hud ??= GameHud.Of(_panel);
        _hud?.MarkChanged(HudElement.Hotbar);
    }

    public override void _Process(double delta)
    {
        bool playing = GameManager.Instance is { IsPlaying: true };
        // Toggle the panel, not this layer — when docked the panel lives under the GameHud layer.
        // Written when the state changes, not restated to the engine every frame.
        int shown = playing ? 1 : 0;
        if (shown != _panelShown)
        {
            _panelShown = shown;
            _panel.Visible = playing;
        }

        GameInput.SetHotbarChord(
            playing && !UiState.MenuOpen && Godot.Input.IsActionPressed(ChordAction));

        if (GameInput.TextEntryActive && GetViewport()?.GuiGetFocusOwner() is not LineEdit)
        {
            GameInput.SetTextEntry(false);
        }

        if (!playing)
        {
            return;
        }

        if (_dirty)
        {
            _dirty = false;
            Rebuild();
            _stateTimer = 0f;
        }

        _stateTimer -= (float)delta;
        if (_stateTimer <= 0f)
        {
            _stateTimer = StateInterval;
            UpdateStates();
        }

        UpdateSweeps();
    }

    /// <summary>Re-resolves each cell's state and redraws the ones whose answer changed.</summary>
    private void UpdateStates()
    {
        IEntity? owner = _hotbar?.Entity;
        for (int i = 0; i < HotbarComponent.SlotCount; i++)
        {
            ConsumableItemResource? item = _items[i];
            ConsumeRefusal refusal = item != null && owner != null
                ? ConsumableEffectsComponent.Check(owner, item)
                : ConsumeRefusal.None;
            var state = HotbarRules.State(_filled[i], _counts[i], _hotbar?.CooldownRemaining(i) ?? 0f, refusal);
            if ((int)state == _statesShown[i])
            {
                continue;
            }

            // Known before, and the wait just began or ended: that is the change a hidden bar is for.
            bool wasCooling = _statesShown[i] == (int)HotbarSlotState.Cooling;
            if (_statesShown[i] >= 0 && (wasCooling || state == HotbarSlotState.Cooling))
            {
                NoteChanged();
            }

            _statesShown[i] = (int)state;
            ApplyState(i, state);
        }
    }

    /// <summary>
    /// Draws a state onto a cell. The icon stays at full strength under a cooldown, because the wipe
    /// over it is already the message; it dims for the two "cannot" states, and only the level lock
    /// adds the padlock, so the three are told apart without a colour.
    /// </summary>
    private void ApplyState(int slot, HotbarSlotState state)
    {
        if (_icons[slot] is { } icon && IsInstanceValid(icon))
        {
            float strength = state switch
            {
                HotbarSlotState.Unusable => 0.45f,
                HotbarSlotState.Locked or HotbarSlotState.Depleted => 0.28f,
                _ => 1f,
            };
            icon.Modulate = _iconTints[slot] with { A = strength };
        }

        if (_locks[slot] is { } padlock && IsInstanceValid(padlock))
        {
            padlock.Visible = state == HotbarSlotState.Locked;
        }

        if (_countLabels[slot] is { } count && IsInstanceValid(count))
        {
            UiLive.FontColor(count, state == HotbarSlotState.Depleted ? UiTheme.Bad : UiTheme.Text);
        }
    }

    /// <summary>Repaints a wipe when it has moved a step and a numeral when its second has turned,
    /// while any cooldown runs, and once more after the last one ends so the final sliver is wiped
    /// rather than left frozen on the cell.</summary>
    private void UpdateSweeps()
    {
        bool running = false;
        for (int i = 0; i < HotbarComponent.SlotCount && !running; i++)
        {
            running = _sweeps[i] != null && _hotbar != null && _hotbar.CooldownRemaining(i) > 0f;
        }

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

            int step = HotbarRules.WipeStep(_hotbar?.CooldownFraction(i) ?? 0f);
            if (step != _wipeStepsShown[i])
            {
                _wipeStepsShown[i] = step;
                sweep.QueueRedraw();
            }

            int numeral = HotbarRules.Numeral(_hotbar?.CooldownRemaining(i) ?? 0f);
            if (numeral != _numeralsShown[i] && _waits[i] is { } wait && IsInstanceValid(wait))
            {
                _numeralsShown[i] = numeral;
                wait.Text = Numerals[numeral];
            }
        }
    }

    /// <summary>The cell's width for the layout the HUD has (<see cref="HudCoreMetrics.HotbarCellWidth"/>).</summary>
    private float CellWidth() => HudCoreMetrics.HotbarCellWidth(_hud?.LayoutWidth ?? 0f);

    /// <summary>The cell's size: square where the layout has room, and grown by the text scale,
    /// because a Button does not grow to fit what is laid inside it and the key and the count are a
    /// line of text.</summary>
    private static Vector2 CellSize(float width)
    {
        float grown = UiTheme.FontSize(UiTheme.CaptionFontSize) - UiTheme.CaptionFontSize;
        return new Vector2(width + (2f * grown), HudCoreMetrics.HotbarCellHeight(width) + (2f * grown));
    }

    private void Rebuild()
    {
        foreach (Node child in _row.GetChildren())
        {
            _row.RemoveChild(child);
            child.QueueFree();
        }

        bool pad = InputDevice.GamepadActive;
        RebuildChord(pad);

        _cellWidthBuilt = CellWidth();
        Vector2 cellSize = CellSize(_cellWidthBuilt);
        float cellHeight = HudCoreMetrics.HotbarCellHeight(_cellWidthBuilt);
        float iconSide = HudCoreMetrics.HotbarIconSide(cellHeight);
        float glyphSide = HudCoreMetrics.HotbarGlyphSide(cellHeight);
        for (int i = 0; i < HotbarComponent.SlotCount; i++)
        {
            string id = _hotbar?.Get(i) ?? string.Empty;
            bool filled = id.Length > 0;
            ItemResource? template = filled ? ItemDatabase.Get(id) : null;
            var consumable = template as ConsumableItemResource;
            int count = filled ? _inventory?.CountOf(id) ?? 0 : 0;

            // A count that moved is what the player wants to see after drinking something.
            if (filled && _filled[i] && ReferenceEquals(consumable, _items[i]) && count != _counts[i])
            {
                NoteChanged();
            }

            _filled[i] = filled;
            _items[i] = consumable;
            _counts[i] = count;
            _statesShown[i] = -1;
            _numeralsShown[i] = 0;
            _wipeStepsShown[i] = -1;
            _sweeps[i] = null;
            _waits[i] = null;
            _icons[i] = null;
            _locks[i] = null;
            _countLabels[i] = null;

            Button cell = UiTheme.Action(string.Empty);
            cell.CustomMinimumSize = cellSize;
            cell.TooltipText = Tooltip(i, filled, consumable, pad);
            cell.Disabled = !filled;
            StyleCell(cell);

            // The cell is a Button, which never sizes to its children, so the content is pinned to the
            // cell with an inset of its own.
            var inset = new MarginContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
            inset.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            inset.AddThemeConstantOverride("margin_left", UiTheme.Space2xs);
            inset.AddThemeConstantOverride("margin_right", UiTheme.Space2xs);
            inset.AddThemeConstantOverride("margin_top", UiTheme.Space2xs);
            inset.AddThemeConstantOverride("margin_bottom", UiTheme.Space2xs);

            var stack = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
            stack.AddThemeConstantOverride("separation", 0);
            inset.AddChild(stack);

            // ⚠️ AN EMPTY SLOT SHOWS ITS INPUT AND NOTHING ELSE (§53, §72, §73). It used to print the
            // word "(EMPTY)" in every unassigned cell. A blank recessed cell already says "nothing
            // here"; the word is the interface talking about itself.
            //
            // The input is always present and always in the same corner — that is what makes the row
            // scannable as "slot 3". On a pad it is the chord's button (the line above names the
            // trigger to hold). The count shares its line, in the opposite corner.
            var head = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
            head.AddChild(SlotGlyph(i, pad));
            head.AddChild(new Control
            {
                MouseFilter = Control.MouseFilterEnum.Ignore,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            });
            stack.AddChild(head);

            if (filled)
            {
                // A count of one is not information — every consumable you can use you have at least
                // one of. Zero is: the slot is assigned and the pack is empty.
                if (count != 1)
                {
                    Label badge = UiTheme.HudInk(UiTheme.Caption(Loc.TF("hud.hotbar.count", count), UiTheme.Text));
                    badge.MouseFilter = Control.MouseFilterEnum.Ignore;
                    badge.SizeFlagsVertical = Control.SizeFlags.ShrinkBegin;
                    head.AddChild(badge);
                    _countLabels[i] = badge;
                }

                // The picture takes whatever the head line leaves, so it sits in the middle of that
                // room at any text scale.
                Texture2D? picture = ItemIcons.For(template);
                var body = new CenterContainer
                {
                    MouseFilter = Control.MouseFilterEnum.Ignore,
                    SizeFlagsVertical = picture != null ? Control.SizeFlags.ExpandFill : Control.SizeFlags.Fill,
                };
                stack.AddChild(body);

                // The item's own picture when it has one, and then the picture is the whole label: a
                // name under it was cut to "Health..." in every cell, and the tooltip has it in full.
                // Until an item has one, what the slot does as a shape (a heart, a bolt, a drop, a
                // shield, a sun) with the name under it, because two potions that share an effect
                // share that shape.
                TextureRect icon;
                if (picture != null)
                {
                    icon = new TextureRect
                    {
                        Texture = picture,
                        CustomMinimumSize = new Vector2(iconSide, iconSide),
                        ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                        StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                        MouseFilter = Control.MouseFilterEnum.Ignore,
                    };
                    _iconTints[i] = Colors.White;
                }
                else
                {
                    UiIcon.Kind kind = consumable != null
                        ? ItemPresentation.EffectIcon(consumable.Effect)
                        : UiIcon.Kind.Consumable;
                    _iconTints[i] = consumable != null ? ItemSlot.EffectColor(consumable.Effect) : UiTheme.Text;
                    icon = UiIcon.Create(kind, glyphSide, _iconTints[i]);
                }

                body.AddChild(icon);
                _icons[i] = icon;

                TextureRect padlock = UiIcon.Create(UiIcon.Kind.Lock, glyphSide, UiTheme.Text);
                padlock.Visible = false;
                body.AddChild(padlock);
                _locks[i] = padlock;

                // One line under a shape, in the room the shape leaves. A clipped label asks for no
                // width of its own, so it takes the cell's.
                if (picture == null)
                {
                    Label name = UiTheme.HudInk(UiTheme.Caption(template?.DisplayName ?? id, UiTheme.Text));
                    name.MouseFilter = Control.MouseFilterEnum.Ignore;
                    name.HorizontalAlignment = HorizontalAlignment.Center;
                    name.VerticalAlignment = VerticalAlignment.Center;
                    name.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
                    name.ClipText = true;
                    name.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
                    stack.AddChild(name);
                }
            }

            cell.AddChild(inset);

            if (consumable is { CooldownSeconds: > 0f })
            {
                AddSweep(cell, i);
            }

            int slot = i;
            cell.Pressed += () => _hotbar?.Clear(slot);
            _row.AddChild(cell);
        }

        // Draw the new cells' sweeps on their first frame, whether or not a cooldown started since.
        _sweeping = true;
    }

    /// <summary>The line above the cells on a pad: the trigger's glyph and what holding it does.</summary>
    private void RebuildChord(bool pad)
    {
        foreach (Node child in _chord.GetChildren())
        {
            _chord.RemoveChild(child);
            child.QueueFree();
        }

        _chord.Visible = pad;
        if (!pad)
        {
            return;
        }

        _chord.AddChild(UiGlyph.For(GameInput.HotbarChord));
        Label hint = UiTheme.HudInk(UiTheme.Caption(Loc.T("hud.hotbar.chord"), UiTheme.Text));
        hint.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        _chord.AddChild(hint);
    }

    /// <summary>The input that uses a slot: its key on a keyboard, the chord's pressed button on a pad
    /// (the buttons are only lent to the hotbar actions while the trigger is held, so they are named
    /// from <see cref="GameInput.HotbarChordButtons"/> and not read back from the input map).</summary>
    private static Control SlotGlyph(int slot, bool pad)
    {
        Control glyph = pad && slot < GameInput.HotbarChordButtons.Length
            ? UiGlyph.Pad(UiGlyphRules.ForButton(GameInput.HotbarChordButtons[slot], UiGlyph.Family))
            : UiGlyph.For(GameInput.Hotbar[slot]);
        glyph.MouseFilter = Control.MouseFilterEnum.Ignore;
        glyph.SizeFlagsVertical = Control.SizeFlags.ShrinkBegin;
        return glyph;
    }

    /// <summary>A cell's faces: a keylined well, fainter when empty, edged in ember under the pointer
    /// and ringed when it has focus (the bar is reachable from the inventory, where slots are assigned).</summary>
    private static void StyleCell(Button cell)
    {
        cell.AddThemeStyleboxOverride("normal", UiTheme.HudSlotStyle());
        cell.AddThemeStyleboxOverride("disabled", UiTheme.HudSlotStyle(null, UiTheme.HudSlotQuiet));
        cell.AddThemeStyleboxOverride("hover", UiTheme.HudSlotStyle(UiTheme.Accent));
        cell.AddThemeStyleboxOverride("pressed", UiTheme.HudSlotStyle(UiTheme.AccentHot));

        StyleBoxFlat focus = UiTheme.HudSlotStyle(UiTheme.FocusRing);
        focus.DrawCenter = false;
        cell.AddThemeStyleboxOverride("focus", focus);
    }

    /// <summary>
    /// The cooldown overlay for one cell: a dark wedge covering the share of the wait still to run,
    /// unwinding clockwise from twelve o'clock, with the seconds left in the middle once there are
    /// nine or fewer. The wedge is the glance and the number is the answer; neither depends on a
    /// colour.
    ///
    /// Drawn through the <c>Draw</c> signal of a plain <see cref="Control"/> rather than a subclass
    /// overriding <c>_Draw</c>, so the bar adds no new script type for five small overlays.
    /// </summary>
    private void AddSweep(Button cell, int slot)
    {
        var sweep = new Control { MouseFilter = Control.MouseFilterEnum.Ignore, ClipContents = true };
        sweep.SetAnchorsPreset(Control.LayoutPreset.FullRect);

        Color shade = UiTheme.HudWipe;
        sweep.Draw += () =>
        {
            float fraction = _hotbar?.CooldownFraction(slot) ?? 0f;
            if (fraction < 0.01f)
            {
                return;
            }

            Vector2 centre = sweep.Size / 2f;
            float radius = sweep.Size.Length(); // past the corners; the control clips it to the cell
            int steps = Mathf.Max(3, Mathf.CeilToInt(32f * fraction));
            float from = (-Mathf.Pi / 2f) + (Mathf.Tau * (1f - fraction));
            float span = Mathf.Tau * fraction;

            // One reused triangle: the engine copies the points on each call.
            _wedge[0] = centre;
            _wedge[1] = centre + (new Vector2(Mathf.Cos(from), Mathf.Sin(from)) * radius);
            for (int s = 1; s <= steps; s++)
            {
                float angle = from + (span * s / steps);
                _wedge[2] = centre + (new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius);
                sweep.DrawColoredPolygon(_wedge, shade);
                _wedge[1] = _wedge[2];
            }
        };

        // Inter, at header size: a numeral in the display face reads as an inscription, not a count.
        Label wait = UiTheme.HudInk(UiTheme.Body(string.Empty, UiTheme.Text));
        UiTheme.ApplyType(wait, UiTheme.FontRole.Interface, UiTheme.HeaderFontSize);
        wait.MouseFilter = Control.MouseFilterEnum.Ignore;
        wait.HorizontalAlignment = HorizontalAlignment.Center;
        wait.VerticalAlignment = VerticalAlignment.Center;
        wait.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        sweep.AddChild(wait);

        cell.AddChild(sweep);
        _sweeps[slot] = sweep;
        _waits[slot] = wait;
    }

    /// <summary>The cell's tooltip: for a filled slot, what the consumable does, the level it needs
    /// and how long it makes the player wait, then how to use the slot on the device in hand.</summary>
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

        if (consumable.RequiredLevel > 0)
        {
            lines.Add(Loc.TF("item.requires_level", consumable.RequiredLevel));
        }

        if (ItemSlot.CooldownText(consumable) is { } cooldown)
        {
            lines.Add(cooldown);
        }

        lines.Add(hint);
        return string.Join("\n", lines);
    }
}
