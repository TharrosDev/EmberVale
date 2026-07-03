using Embervale.Core.Events;
using Godot;

namespace Embervale.Core;

/// <summary>
/// Tracks whether the player is currently driving with a gamepad or keyboard/mouse (30.5J),
/// fed raw events by <see cref="GameManager"/>. Prompt glyphs read
/// <see cref="GamepadActive"/> through <c>GameInput.PromptLabel</c>; surfaces that display a
/// glyph subscribe to <see cref="InputDeviceChangedEvent"/> to refresh on a device flip.
/// Mouse motion is deliberately ignored — a nudged mouse must not flash prompts back to
/// keyboard glyphs mid-controller-session.
/// </summary>
public static class InputDevice
{
    /// <summary>Stick drift below this never flips the device (matches the ui_* deadzone).</summary>
    private const float MotionThreshold = 0.5f;

    /// <summary>True while the most recent meaningful input came from a gamepad.</summary>
    public static bool GamepadActive { get; private set; }

    public static void Observe(InputEvent input)
    {
        bool? gamepad = input switch
        {
            InputEventJoypadButton { Pressed: true } => true,
            InputEventJoypadMotion motion when Mathf.Abs(motion.AxisValue) >= MotionThreshold => true,
            InputEventKey { Pressed: true } => false,
            InputEventMouseButton { Pressed: true } => false,
            _ => null,
        };

        if (gamepad is not { } active || active == GamepadActive)
        {
            return;
        }

        GamepadActive = active;
        EventBus.Instance?.Publish(new InputDeviceChangedEvent(active));
    }
}
