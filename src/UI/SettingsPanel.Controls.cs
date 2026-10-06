using System.Collections.Generic;
using Embervale.Core;
using Embervale.Core.Diagnostics;
using Embervale.Core.Services;
using Embervale.Localization;
using Embervale.Settings;
using Godot;
using S = Embervale.Settings.Settings;

namespace Embervale.UI;

/// <summary>
/// The Controls tab of <see cref="SettingsPanel"/>: look sensitivity, then one row per remappable
/// action with its keyboard and gamepad binding. Selecting a binding listens for the next key or
/// button; an input another action already holds asks whether to swap, unbind the other or cancel.
/// What may be bound and what collides is <see cref="InputBindingRules"/>; this file is the screen.
/// </summary>
public partial class SettingsPanel
{
    /// <summary>A binding being listened for: the next input on <paramref name="Device"/> goes to
    /// <paramref name="Action"/>.</summary>
    private sealed record Listening(string Action, BindingDevice Device);

    private Listening? _listening;
    private Label? _listenNote;

    /// <summary>The binding cell holding focus, which is what the clear action clears.</summary>
    private Listening? _focusedCell;

    private readonly List<(Button? Key, Button? Pad)> _bindingRows = new();

    private void BuildControls(VBoxContainer body)
    {
        var s = _settings.Current;

        Section(body, Loc.T("settings.section.look"), first: true);
        body.AddChild(SliderRow(
            Info(Loc.T("settings.mouse_sensitivity"), Loc.T("settings.mouse_sensitivity.desc"), nameof(S.MouseSensitivity)),
            0.05, 2.0, 0.05, s.MouseSensitivity, v => s.MouseSensitivity = v, Decimal));
        body.AddChild(ToggleRow(
            Info(Loc.T("settings.invert_y"), Loc.T("settings.invert_y.desc"), nameof(S.InvertY)),
            s.InvertY, v => { s.InvertY = v; Persist(); }));
        body.AddChild(SliderRow(
            Info(Loc.T("settings.pad_sensitivity_x"), Loc.T("settings.pad_sensitivity_x.desc"), nameof(S.PadSensitivityX)),
            SettingsMath.PadSensitivityMin, SettingsMath.PadSensitivityMax, 0.05,
            SettingsMath.ClampPadSensitivity(s.PadSensitivityX), v => s.PadSensitivityX = v, Decimal));
        body.AddChild(SliderRow(
            Info(Loc.T("settings.pad_sensitivity_y"), Loc.T("settings.pad_sensitivity_y.desc"), nameof(S.PadSensitivityY)),
            SettingsMath.PadSensitivityMin, SettingsMath.PadSensitivityMax, 0.05,
            SettingsMath.ClampPadSensitivity(s.PadSensitivityY), v => s.PadSensitivityY = v, Decimal));

        Section(body, Loc.T("settings.section.bindings"));
        body.AddChild(BindingHeader());

        BindingGroup? group = null;
        foreach (RemapAction entry in InputBindingRules.Actions)
        {
            if (entry.Group != group)
            {
                group = entry.Group;
                Label heading = UiTheme.Caption(Loc.T(GroupKey(entry.Group)), UiTheme.Accent);
                var indent = new MarginContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
                indent.AddThemeConstantOverride("margin_left", UiTheme.SpaceMd);
                indent.AddThemeConstantOverride("margin_top", UiTheme.SpaceXs);
                indent.AddChild(heading);
                body.AddChild(indent);
            }

            body.AddChild(BindingRow(entry));
        }

        var reset = new RowInfo
        {
            Title = Loc.T("settings.bind.reset"),
            Description = Loc.T("settings.bind.reset.desc"),
        };
        body.AddChild(Row(reset, HoldButton(reset, () =>
        {
            _settings.ResetFields(SettingsTabRules.BindingFields);
            Persist();
            MarkDirty();
        })));
    }

    private static string GroupKey(BindingGroup group) => group switch
    {
        BindingGroup.Movement => "settings.bind.group.movement",
        BindingGroup.Combat => "settings.bind.group.combat",
        BindingGroup.World => "settings.bind.group.world",
        BindingGroup.Screens => "settings.bind.group.screens",
        _ => "settings.bind.group.hotbar",
    };

    /// <summary>The two column names over the binding cells, laid out exactly as a row is.</summary>
    private static Control BindingHeader()
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", UiTheme.SpaceMd);
        row.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore });
        foreach (string key in new[] { "settings.bind.column.keyboard", "settings.bind.column.gamepad" })
        {
            Label caption = UiTheme.Caption(Loc.T(key));
            caption.CustomMinimumSize = new Vector2(UiTheme.SettingsBindingCell, 0f);
            caption.HorizontalAlignment = HorizontalAlignment.Center;
            caption.ClipText = true;
            row.AddChild(caption);
        }

        // The restore slot's width, plus the row frame's right margin.
        row.AddChild(new Control
        {
            CustomMinimumSize = new Vector2(UiTheme.ControlHeight + UiTheme.SpaceXs, 0f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        });
        return row;
    }

    private static string ActionName(string action) => Loc.T("settings.bind.action." + action);

    private Control BindingRow(RemapAction entry)
    {
        var s = _settings.Current;
        string action = entry.Action;
        string specific = "settings.bind.desc." + action;
        var info = new RowInfo
        {
            Title = ActionName(action),
            Description = Loc.Has(specific) ? Loc.T(specific) : Loc.T("settings.bind.desc"),
            Changed = () => InputBindingRules.IsRemapped(s.KeyBindings, action, BindingDevice.Keyboard)
                            || InputBindingRules.IsRemapped(s.PadBindings, action, BindingDevice.Gamepad),
            Revert = () =>
            {
                // With whatever its default now collides with: the other half of a swap goes back too.
                s.KeyBindings = InputBindingRules.Restore(s.KeyBindings, action, BindingDevice.Keyboard,
                    a => GameInput.DefaultBinding(a, BindingDevice.Keyboard));
                s.PadBindings = InputBindingRules.Restore(s.PadBindings, action, BindingDevice.Gamepad,
                    a => GameInput.DefaultBinding(a, BindingDevice.Gamepad));
            },
        };

        var frame = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Pass };
        frame.AddThemeStyleboxOverride("panel", UiTheme.SettingsRowStyle(false));
        info.Frame = frame;

        var row = new HBoxContainer { CustomMinimumSize = new Vector2(0f, UiTheme.ControlHeight) };
        row.AddThemeConstantOverride("separation", UiTheme.SpaceMd);
        frame.AddChild(row);

        Label name = UiTheme.Body(info.Title);
        name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        name.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        name.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        row.AddChild(name);

        Button key = BindingCell(info, entry, BindingDevice.Keyboard);
        Button pad = BindingCell(info, entry, BindingDevice.Gamepad);
        row.AddChild(key);
        row.AddChild(pad);
        row.AddChild(RevertSlot(info));

        frame.MouseEntered += () => ShowDescription(info);
        _rows.Add(info);
        _bindingRows.Add((key.Disabled ? null : key, pad.Disabled ? null : pad));
        return frame;
    }

    /// <summary>One binding as a button: the key's cap or the pad button's glyph, or the word for
    /// "unbound". A half the player cannot change (the stick behind movement, the chord behind the
    /// hotbar) shows what it is and is disabled, so the row still answers "what do I press".</summary>
    private Button BindingCell(RowInfo info, RemapAction entry, BindingDevice device)
    {
        bool remappable = device == BindingDevice.Keyboard ? entry.Keyboard : entry.Gamepad;
        string[] saved = device == BindingDevice.Keyboard ? _settings.Current.KeyBindings : _settings.Current.PadBindings;

        Button cell = UiTheme.Action(string.Empty);
        cell.CustomMinimumSize = new Vector2(UiTheme.SettingsBindingCell, UiTheme.ControlHeight);
        cell.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        cell.Disabled = !remappable;

        Control glyph = remappable
            ? BindingGlyph(InputBindingRules.Resolve(saved, entry.Action, device, GameInput.DefaultBinding(entry.Action, device)))
            : FixedGlyph(entry.Action);
        var centre = new CenterContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        centre.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        centre.AddChild(glyph);
        cell.AddChild(centre);

        if (!remappable)
        {
            // Bare: no face, so it reads as a fact about the action and not as a dead button.
            cell.AddThemeStyleboxOverride("disabled", new StyleBoxEmpty());
            cell.FocusMode = Control.FocusModeEnum.None;
            cell.TooltipText = Loc.T("settings.bind.fixed");
            return cell;
        }

        var target = new Listening(entry.Action, device);
        cell.TooltipText = Loc.T(device == BindingDevice.Keyboard ? "settings.bind.column.keyboard" : "settings.bind.column.gamepad");
        cell.Pressed += () => BeginListening(target);
        cell.FocusEntered += () =>
        {
            _focusedCell = target;
            OnRowFocus(info);
        };
        cell.FocusExited += () =>
        {
            if (_focusedCell == target)
            {
                _focusedCell = null;
            }

            OnRowBlur(info);
        };
        info.Main ??= cell;
        return cell;
    }

    private static Control BindingGlyph(InputBinding binding)
    {
        Control glyph = binding.Kind switch
        {
            BindingKind.Key or BindingKind.Mouse => UiTheme.KeyCap(GameInput.BindingLabel(binding)),
            BindingKind.JoyButton => UiGlyph.Pad(UiGlyphRules.ForButton((JoyButton)binding.Code, UiGlyph.Family)),
            BindingKind.JoyAxis => UiGlyph.Pad(UiGlyphRules.ForAxis((JoyAxis)binding.Code, UiGlyph.Family)),
            _ => UiTheme.Caption(Loc.T("settings.bind.unbound")),
        };
        glyph.MouseFilter = Control.MouseFilterEnum.Ignore;
        return glyph;
    }

    /// <summary>The pad half of an action that is not the player's to move: the left stick for
    /// movement, the trigger chord for a hotbar slot.</summary>
    private static Control FixedGlyph(string action)
    {
        int slot = System.Array.IndexOf(GameInput.Hotbar, action);
        Control glyph = slot >= 0
            ? UiTheme.KeyCap(GameInput.HotbarPadLabel(slot))
            : UiGlyph.Pad(UiGlyphRules.ForAxis(JoyAxis.LeftX, UiGlyph.Family));
        glyph.MouseFilter = Control.MouseFilterEnum.Ignore;
        return glyph;
    }

    /// <summary>
    /// The binding cells are a grid, so their neighbours are set by hand: left and right stay in
    /// the row, up and down stay in the column (a d-pad would otherwise walk the tab order). A row
    /// with no cell in a column hands the step to its other cell, and the way back up from a cell
    /// two cells stepped down into is the first of them. The first row's way up and the last row's
    /// way down are left to the engine, which finds the control above and below the grid.
    /// </summary>
    private void WireBindingGrid()
    {
        for (int i = 0; i < _bindingRows.Count; i++)
        {
            (Button? key, Button? pad) = _bindingRows[i];
            if (key != null && pad != null)
            {
                key.FocusNeighborRight = key.GetPathTo(pad);
                pad.FocusNeighborLeft = pad.GetPathTo(key);
            }

            if (i + 1 >= _bindingRows.Count)
            {
                continue;
            }

            (Button? nextKey, Button? nextPad) = _bindingRows[i + 1];
            Link(key, nextKey ?? nextPad);
            Link(pad, nextPad ?? nextKey);
        }
    }

    private static void Link(Button? above, Button? below)
    {
        if (above == null || below == null)
        {
            return;
        }

        above.FocusNeighborBottom = above.GetPathTo(below);
        if (below.FocusNeighborTop.IsEmpty)
        {
            below.FocusNeighborTop = below.GetPathTo(above);
        }
    }

    // --- Listening ----------------------------------------------------------

    private void BeginListening(Listening target)
    {
        _listening = target;
        ShowListeningPrompt(target);
    }

    /// <summary>The "press a key" prompt. It has no buttons: every input is the answer, and the
    /// one that is not (the pause button) is named in it.</summary>
    private void ShowListeningPrompt(Listening target)
    {
        var exit = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        exit.AddThemeConstantOverride("separation", UiTheme.SpaceXs);
        exit.AddChild(UiGlyph.For(GameInput.Pause));
        exit.AddChild(UiTheme.Caption(Loc.T("settings.bind.listen_cancel"), UiTheme.Text));

        string text = Loc.T(target.Device == BindingDevice.Keyboard ? "settings.bind.listen_key" : "settings.bind.listen_pad");
        _listenNote = OpenPrompt(ActionName(target.Action), text, exit);

        // OpenPrompt closed the prompt before it, which ends a listen; this one is still on.
        _listening = target;
        UpdateLegend();
    }

    public override void _Input(InputEvent @event)
    {
        if (_listening is not { } target || @event is InputEventMouseMotion)
        {
            return;
        }

        // Nothing reaches the sheet while a binding is being captured: the key is the answer, not
        // a focus step or a button press.
        GetViewport().SetInputAsHandled();

        if (@event.IsActionPressed(GameInput.Pause))
        {
            UiAudio.Play(UiCue.Back);
            ClosePrompt();
            return;
        }

        switch (@event)
        {
            case InputEventKey { Pressed: true, Echo: false } key when target.Device == BindingDevice.Keyboard:
                Offer(target, InputBinding.OfKey(key.PhysicalKeycode != Key.None ? key.PhysicalKeycode : key.Keycode));
                break;
            case InputEventMouseButton { Pressed: true } mouse when target.Device == BindingDevice.Keyboard:
                Offer(target, InputBinding.OfMouse(mouse.ButtonIndex));
                break;
            case InputEventJoypadButton { Pressed: true } pad when target.Device == BindingDevice.Gamepad:
                Offer(target, InputBinding.OfJoy(pad.ButtonIndex));
                break;
            case InputEventJoypadMotion motion when target.Device == BindingDevice.Gamepad
                                                    && Mathf.Abs(motion.AxisValue) >= AxisCaptureThreshold:
                Offer(target, InputBinding.OfAxis(motion.Axis, motion.AxisValue < 0f ? -1 : 1));
                break;
        }
    }

    /// <summary>How far an axis must travel to count as the answer; past the stick drift and the
    /// resting weight of a finger on a trigger.</summary>
    private const float AxisCaptureThreshold = 0.6f;

    private void Offer(Listening target, InputBinding binding)
    {
        if (!InputBindingRules.CanBind(target.Action, target.Device, binding))
        {
            // Stay listening and say why nothing happened. A stick is not said to be reserved:
            // it moves all the time, and the note would flicker under a resting thumb.
            if (binding.Kind != BindingKind.JoyAxis && _listenNote != null && IsInstanceValid(_listenNote))
            {
                UiAudio.Play(UiCue.Denied);
                _listenNote.Text = Loc.T("settings.bind.reserved");
            }

            return;
        }

        ClosePrompt();
        string? other = InputBindingRules.FindConflict(target.Action, binding, Effective(target.Device));
        if (other == null)
        {
            UiAudio.Play(UiCue.Confirm);
            SetBinding(target.Action, target.Device, binding);
            CommitBindings();
            return;
        }

        string taken = Loc.TF("settings.bind.conflict", BindingName(binding), ActionName(other));
        InputBinding mine = Effective(target.Device)[target.Action];
        OpenPrompt(ActionName(target.Action), taken, null,
            new PromptChoice(Loc.T("settings.bind.swap"), UiCue.Confirm, () =>
            {
                SetBinding(target.Action, target.Device, binding);
                SetBinding(other, target.Device, mine);
                CommitBindings();
            }, Focused: true),
            new PromptChoice(Loc.T("settings.bind.unbind_other"), UiCue.Confirm, () =>
            {
                SetBinding(target.Action, target.Device, binding);
                SetBinding(other, target.Device, InputBinding.Unbound);
                CommitBindings();
            }),
            new PromptChoice(Loc.T("common.cancel"), UiCue.Back, () => { }));
    }

    /// <summary>A binding in words, for a sentence: the key's name, or the pad button's.</summary>
    private static string BindingName(InputBinding binding) => binding.Kind switch
    {
        BindingKind.JoyButton => UiGlyphRules.ForButton((JoyButton)binding.Code, UiGlyph.Family).Name,
        BindingKind.JoyAxis => UiGlyphRules.ForAxis((JoyAxis)binding.Code, UiGlyph.Family).Name,
        _ => GameInput.BindingLabel(binding),
    };

    private Dictionary<string, InputBinding> Effective(BindingDevice device) => InputBindingRules.Effective(
        device == BindingDevice.Keyboard ? _settings.Current.KeyBindings : _settings.Current.PadBindings,
        device, action => GameInput.DefaultBinding(action, device));

    private void SetBinding(string action, BindingDevice device, InputBinding binding)
    {
        var s = _settings.Current;
        InputBinding fallback = GameInput.DefaultBinding(action, device);
        if (device == BindingDevice.Keyboard)
        {
            s.KeyBindings = InputBindingRules.With(s.KeyBindings, action, binding, fallback);
        }
        else
        {
            s.PadBindings = InputBindingRules.With(s.PadBindings, action, binding, fallback);
        }
    }

    /// <summary>Applies and saves the binding lists (the apply pushes them into the input map,
    /// which is what redraws every prompt in the game) and redraws the rows.</summary>
    private void CommitBindings()
    {
        Persist();
        MarkDirty();
    }

    private void ClearFocusedBinding()
    {
        if (_focusedCell is not { } cell ||
            Effective(cell.Device).TryGetValue(cell.Action, out InputBinding current) && current.IsUnbound)
        {
            return;
        }

        UiAudio.Play(UiCue.Confirm);
        SetBinding(cell.Action, cell.Device, InputBinding.Unbound);
        CommitBindings();
    }

    // --- Capture hooks ------------------------------------------------------

    /// <summary>Screenshot entry point: the listening state for <paramref name="action"/>, as
    /// selecting its keyboard or gamepad binding opens it.</summary>
    public void ListenForCapture(string action, bool gamepad)
    {
        BeginListening(new Listening(action, gamepad ? BindingDevice.Gamepad : BindingDevice.Keyboard));
    }

    /// <summary>Screenshot entry point: the conflict prompt, reached the way a player reaches it,
    /// by offering <paramref name="action"/> the keyboard binding <paramref name="other"/> holds.</summary>
    public void ShowConflictForCapture(string action, string other)
    {
        var target = new Listening(action, BindingDevice.Keyboard);
        BeginListening(target);
        Offer(target, Effective(BindingDevice.Keyboard)[other]);
    }
}
