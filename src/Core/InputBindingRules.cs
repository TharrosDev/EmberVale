using System;
using System.Collections.Generic;
using Embervale.Settings;
using Godot;

namespace Embervale.Core;

/// <summary>Which half of the input map a binding belongs to. Keyboard includes the mouse.</summary>
public enum BindingDevice
{
    Keyboard = 0,
    Gamepad = 1,
}

/// <summary>What kind of input a binding is.</summary>
public enum BindingKind
{
    /// <summary>Deliberately unbound: the player cleared it.</summary>
    None = 0,
    Key = 1,
    Mouse = 2,
    JoyButton = 3,
    JoyAxis = 4,
}

/// <summary>How the binding screen groups its rows.</summary>
public enum BindingGroup
{
    Movement = 0,
    Combat = 1,
    World = 2,
    Screens = 3,
    Hotbar = 4,
}

/// <summary>
/// One input an action can be bound to: a key, a mouse button, a pad button or one direction of a
/// pad axis. <paramref name="Code"/> is the engine enum's value (<see cref="Key"/>,
/// <see cref="MouseButton"/>, <see cref="JoyButton"/>, <see cref="JoyAxis"/>);
/// <paramref name="Sign"/> is the axis direction and 0 for everything else.
/// </summary>
public readonly record struct InputBinding(BindingKind Kind, long Code = 0, int Sign = 0)
{
    public static readonly InputBinding Unbound = new(BindingKind.None);

    public static InputBinding OfKey(Key key) => new(BindingKind.Key, (long)key);

    public static InputBinding OfMouse(MouseButton button) => new(BindingKind.Mouse, (long)button);

    public static InputBinding OfJoy(JoyButton button) => new(BindingKind.JoyButton, (long)button);

    public static InputBinding OfAxis(JoyAxis axis, int sign) =>
        new(BindingKind.JoyAxis, (long)axis, sign < 0 ? -1 : 1);

    public bool IsUnbound => Kind == BindingKind.None;
}

/// <summary>One row of the binding screen: an action and which of its halves the player may change.</summary>
public readonly record struct RemapAction(string Action, BindingGroup Group, bool Keyboard, bool Gamepad);

/// <summary>
/// The rules of remapping, with no engine in them: which actions can be rebound, which inputs can
/// never be bound, how a binding is written to and read from the saved
/// <c>Settings.KeyBindings</c> / <c>Settings.PadBindings</c> lists, and when two actions collide.
/// <see cref="GameInput.ApplyBindings"/> turns the answers into the engine's input map.
///
/// The saved form is one <c>action=binding</c> line per remapped action
/// (<see cref="SettingsMath.BindingEntry"/>), where the binding is <c>key:E</c>,
/// <c>mouse:Left</c>, <c>joy:A</c>, <c>axis:TriggerLeft:+</c> or <c>none</c>, spelled with the
/// engine's enum names so the file can be read and edited by hand. An action with no line keeps
/// its default; a line that cannot be read, or that names an input its action may not have, is
/// ignored the same way.
/// </summary>
public static class InputBindingRules
{
    private const string NoneToken = "none";

    /// <summary>
    /// Every action the player may rebind, in the order the screen lists them. Left out on
    /// purpose: pause (Esc and Start are how every menu is left), the menu tab actions, the look
    /// stick and the hotbar chord's trigger. The pad half of movement is the left stick and the
    /// pad half of the hotbar is the chord, so those halves are fixed.
    /// </summary>
    public static readonly IReadOnlyList<RemapAction> Actions = new[]
    {
        new RemapAction(GameInput.MoveForward, BindingGroup.Movement, true, false),
        new RemapAction(GameInput.MoveBack, BindingGroup.Movement, true, false),
        new RemapAction(GameInput.MoveLeft, BindingGroup.Movement, true, false),
        new RemapAction(GameInput.MoveRight, BindingGroup.Movement, true, false),
        new RemapAction(GameInput.Jump, BindingGroup.Movement, true, true),
        new RemapAction(GameInput.Sprint, BindingGroup.Movement, true, true),
        new RemapAction(GameInput.WalkToggle, BindingGroup.Movement, true, true),
        new RemapAction(GameInput.Dodge, BindingGroup.Movement, true, true),

        new RemapAction(GameInput.Attack, BindingGroup.Combat, true, true),
        new RemapAction(GameInput.Block, BindingGroup.Combat, true, true),
        new RemapAction(GameInput.Cast, BindingGroup.Combat, true, true),
        new RemapAction(GameInput.CycleSpell, BindingGroup.Combat, true, true),
        new RemapAction(GameInput.LockOn, BindingGroup.Combat, true, true),
        new RemapAction(GameInput.LockCycleNext, BindingGroup.Combat, true, true),
        new RemapAction(GameInput.LockCyclePrev, BindingGroup.Combat, true, true),

        new RemapAction(GameInput.Interact, BindingGroup.World, true, true),
        new RemapAction(GameInput.Mount, BindingGroup.World, true, true),
        new RemapAction(GameInput.CompanionCommand, BindingGroup.World, true, true),
        new RemapAction(GameInput.ToggleCamera, BindingGroup.World, true, true),
        new RemapAction(GameInput.Place, BindingGroup.World, true, true),

        new RemapAction(GameInput.Inventory, BindingGroup.Screens, true, true),
        new RemapAction(GameInput.Spellbook, BindingGroup.Screens, true, true),
        new RemapAction(GameInput.Journal, BindingGroup.Screens, true, true),
        new RemapAction(GameInput.Map, BindingGroup.Screens, true, true),
        new RemapAction(GameInput.Bestiary, BindingGroup.Screens, true, true),
        new RemapAction(GameInput.HudRecall, BindingGroup.Screens, true, true),

        new RemapAction(GameInput.Hotbar[0], BindingGroup.Hotbar, true, false),
        new RemapAction(GameInput.Hotbar[1], BindingGroup.Hotbar, true, false),
        new RemapAction(GameInput.Hotbar[2], BindingGroup.Hotbar, true, false),
        new RemapAction(GameInput.Hotbar[3], BindingGroup.Hotbar, true, false),
        new RemapAction(GameInput.Hotbar[4], BindingGroup.Hotbar, true, false),
    };

    /// <summary>Whether the player may change <paramref name="action"/>'s binding on <paramref name="device"/>.</summary>
    public static bool IsRemappable(string action, BindingDevice device)
    {
        foreach (RemapAction entry in Actions)
        {
            if (entry.Action == action)
            {
                return device == BindingDevice.Keyboard ? entry.Keyboard : entry.Gamepad;
            }
        }

        return false;
    }

    /// <summary>The device a binding is made on. An unbound binding belongs to neither and reads
    /// as the keyboard; callers that care check <see cref="InputBinding.IsUnbound"/> first.</summary>
    public static BindingDevice DeviceOf(InputBinding binding) =>
        binding.Kind is BindingKind.JoyButton or BindingKind.JoyAxis ? BindingDevice.Gamepad : BindingDevice.Keyboard;

    /// <summary>
    /// Inputs no action may take, because a menu cannot be worked or left without them. Esc and
    /// Start pause and back out; Enter, Tab and the arrows drive focus, and a panel that toggles on
    /// its own key would close under them; the function keys are quick save, quick load and the
    /// developer tools. On a pad the sticks are movement, look and focus, so the only axes that can
    /// be bound are the two triggers.
    /// </summary>
    public static bool IsReserved(InputBinding binding)
    {
        switch (binding.Kind)
        {
            case BindingKind.Key:
                var key = (Key)binding.Code;
                return key is Key.None or Key.Escape or Key.Enter or Key.KpEnter or Key.Tab
                           or Key.Up or Key.Down or Key.Left or Key.Right or Key.Meta or Key.Print
                       || (key >= Key.F1 && key <= Key.F12);
            case BindingKind.Mouse:
                return (MouseButton)binding.Code == MouseButton.None;
            case BindingKind.JoyButton:
                var button = (JoyButton)binding.Code;
                return button is JoyButton.Invalid or JoyButton.Start or JoyButton.Guide;
            case BindingKind.JoyAxis:
                var axis = (JoyAxis)binding.Code;
                return axis is not (JoyAxis.TriggerLeft or JoyAxis.TriggerRight) || binding.Sign <= 0;
            default:
                return false;
        }
    }

    /// <summary>Whether <paramref name="action"/> may be given <paramref name="binding"/> on
    /// <paramref name="device"/>. Clearing a binding is always allowed on a half that can change.</summary>
    public static bool CanBind(string action, BindingDevice device, InputBinding binding)
    {
        if (!IsRemappable(action, device))
        {
            return false;
        }

        return binding.IsUnbound || (DeviceOf(binding) == device && !IsReserved(binding));
    }

    // --- The saved form -------------------------------------------------------------------------

    /// <summary>The text a binding is saved as.</summary>
    public static string Serialise(InputBinding binding) => binding.Kind switch
    {
        BindingKind.Key => $"key:{(Key)binding.Code}",
        BindingKind.Mouse => $"mouse:{(MouseButton)binding.Code}",
        BindingKind.JoyButton => $"joy:{(JoyButton)binding.Code}",
        BindingKind.JoyAxis => $"axis:{(JoyAxis)binding.Code}:{(binding.Sign < 0 ? "-" : "+")}",
        _ => NoneToken,
    };

    /// <summary>Reads a saved binding. False for anything that is not one: an unknown kind, a name
    /// the engine does not have, a number that is not a member of its enum.</summary>
    public static bool TryParse(string? text, out InputBinding binding)
    {
        binding = InputBinding.Unbound;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        string trimmed = text.Trim();
        if (trimmed.Equals(NoneToken, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        string[] parts = trimmed.Split(':');
        if (parts.Length < 2)
        {
            return false;
        }

        switch (parts[0].ToLowerInvariant())
        {
            case "key" when parts.Length == 2 && TryEnum(parts[1], out Key key) && key != Key.None:
                binding = InputBinding.OfKey(key);
                return true;
            case "mouse" when parts.Length == 2 && TryEnum(parts[1], out MouseButton mouse) && mouse != MouseButton.None:
                binding = InputBinding.OfMouse(mouse);
                return true;
            case "joy" when parts.Length == 2 && TryEnum(parts[1], out JoyButton button) && button != JoyButton.Invalid:
                binding = InputBinding.OfJoy(button);
                return true;
            case "axis" when parts.Length == 3 && TryEnum(parts[1], out JoyAxis axis) && axis != JoyAxis.Invalid
                             && parts[2] is "+" or "-":
                binding = InputBinding.OfAxis(axis, parts[2] == "-" ? -1 : 1);
                return true;
            default:
                return false;
        }
    }

    private static bool TryEnum<T>(string name, out T value)
        where T : struct, Enum =>
        Enum.TryParse(name, ignoreCase: true, out value) && Enum.IsDefined(value);

    /// <summary>Whether the saved list holds a usable line for <paramref name="action"/> on
    /// <paramref name="device"/>, which is what "changed from the default" means on the screen.</summary>
    public static bool IsRemapped(string[]? entries, string action, BindingDevice device) =>
        TryParse(SettingsMath.BindingFor(entries, action), out InputBinding saved) && CanBind(action, device, saved);

    /// <summary>The binding in force for <paramref name="action"/>: the saved one when it is
    /// readable and allowed, otherwise <paramref name="fallback"/> (the default).</summary>
    public static InputBinding Resolve(string[]? entries, string action, BindingDevice device, InputBinding fallback) =>
        TryParse(SettingsMath.BindingFor(entries, action), out InputBinding saved) && CanBind(action, device, saved)
            ? saved
            : fallback;

    /// <summary>The binding in force for every action that can change on <paramref name="device"/>.</summary>
    public static Dictionary<string, InputBinding> Effective(
        string[]? entries, BindingDevice device, Func<string, InputBinding> defaultOf)
    {
        var map = new Dictionary<string, InputBinding>();
        foreach (RemapAction entry in Actions)
        {
            if (device == BindingDevice.Keyboard ? entry.Keyboard : entry.Gamepad)
            {
                map[entry.Action] = Resolve(entries, entry.Action, device, defaultOf(entry.Action));
            }
        }

        return map;
    }

    /// <summary>
    /// The saved list with <paramref name="action"/> set to <paramref name="binding"/>. Every older
    /// line for the action is dropped, and no new one is written when the binding is the default,
    /// so a layout put back by hand saves as the default layout. Other actions' lines are kept as
    /// they are, readable or not. Always a new array.
    /// </summary>
    public static string[] With(string[]? entries, string action, InputBinding binding, InputBinding defaultBinding)
    {
        var kept = new List<string>();
        if (entries != null)
        {
            foreach (string? entry in entries)
            {
                if (entry != null && !IsEntryFor(entry, action))
                {
                    kept.Add(entry);
                }
            }
        }

        if (binding != defaultBinding)
        {
            kept.Add(SettingsMath.BindingEntry(action, Serialise(binding)));
        }

        return kept.ToArray();
    }

    /// <summary>The saved list with <paramref name="action"/> back on its default.</summary>
    public static string[] Without(string[]? entries, string action) => With(entries, action, default, default);

    private static bool IsEntryFor(string entry, string action) =>
        entry.Length > action.Length && entry[action.Length] == '=' && entry.StartsWith(action, StringComparison.Ordinal);

    // --- Conflicts ------------------------------------------------------------------------------

    /// <summary>
    /// The other action already holding <paramref name="binding"/> in <paramref name="effective"/>
    /// (the bindings in force on one device), or null. An unbound binding collides with nothing.
    /// The first in screen order is returned; the default layout has no shared inputs, so there is
    /// at most one unless the settings file was edited by hand.
    /// </summary>
    public static string? FindConflict(
        string action, InputBinding binding, IReadOnlyDictionary<string, InputBinding> effective)
    {
        if (binding.IsUnbound)
        {
            return null;
        }

        foreach (RemapAction entry in Actions)
        {
            if (entry.Action != action && effective.TryGetValue(entry.Action, out InputBinding other) && other == binding)
            {
                return entry.Action;
            }
        }

        return null;
    }
}
