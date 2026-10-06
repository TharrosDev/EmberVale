using Embervale.Core;
using Godot;

namespace Embervale.UI;

/// <summary>
/// The two extra verbs a knowledge screen has beyond accept and cancel (track a quest, mark the
/// map), on inputs that are free while a menu is up.
///
/// They borrow play actions, one per device, because every key and button already has a job:
/// F and X (cycle spell, interact) for the first, V and the right stick's click (camera, lock on)
/// for the second. The pairs are split by device on purpose. Each of those actions also has a
/// binding on the other device that a menu uses for something else (interact is E, which walks the
/// hub; cycle spell is LB), so a press only counts from the device its half belongs to.
/// </summary>
internal static class KnowledgeInput
{
    /// <summary>The action whose glyph the legend shows for the first verb, on the device in use.</summary>
    public static string Primary => InputDevice.GamepadActive ? GameInput.Interact : GameInput.CycleSpell;

    /// <summary>The action whose glyph the legend shows for the second verb, on the device in use.</summary>
    public static string Secondary => InputDevice.GamepadActive ? GameInput.LockOn : GameInput.ToggleCamera;

    public static bool IsPrimary(InputEvent input) => Pressed(input, GameInput.CycleSpell, GameInput.Interact);

    public static bool IsSecondary(InputEvent input) => Pressed(input, GameInput.ToggleCamera, GameInput.LockOn);

    private static bool Pressed(InputEvent input, string keyAction, string padAction) => input switch
    {
        InputEventKey { Pressed: true, Echo: false } => input.IsAction(keyAction),
        InputEventJoypadButton { Pressed: true } => input.IsAction(padAction),
        _ => false,
    };
}
