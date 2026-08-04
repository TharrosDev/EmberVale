using Godot;

namespace Embervale.Core;

/// <summary>
/// Central definition of the game's input actions. Registering them in code
/// (rather than the fragile <c>[input]</c> block of <c>project.godot</c>) keeps
/// the bindings type-checked against the <see cref="Key"/>/<see cref="MouseButton"/>
/// enums and version-control friendly. Call <see cref="EnsureActions"/> once at
/// startup; it is idempotent, so editor-defined bindings (if any) are preserved.
/// </summary>
public static class GameInput
{
    public const string MoveForward = "move_forward";
    public const string MoveBack = "move_back";
    public const string MoveLeft = "move_left";
    public const string MoveRight = "move_right";
    public const string Jump = "jump";
    public const string Sprint = "sprint";
    public const string Dodge = "dodge";
    public const string Interact = "interact";
    public const string Attack = "attack";
    public const string Block = "block";
    public const string Cast = "cast";
    public const string CycleSpell = "cycle_spell";
    public const string LockOn = "lock_on";
    public const string LockCycleNext = "lock_cycle_next";
    public const string LockCyclePrev = "lock_cycle_prev";
    public const string Inventory = "inventory";
    public const string Journal = "journal";
    public const string Map = "map";

    /// <summary>Opens the bestiary — the Ash Hunters' field journal (Phase 34G).</summary>
    public const string Bestiary = "bestiary";

    /// <summary>Cycles the party's standing order (follow → hold → engage), Phase 32B.</summary>
    public const string CompanionCommand = "companion_command";

    /// <summary>Swaps first ↔ third person. Flips the persisted ThirdPersonCamera setting, so this
    /// key and the settings panel's toggle are the same switch.</summary>
    public const string ToggleCamera = "toggle_camera";
    public const string Pause = "pause";

    /// <summary>Right-stick look (Phase 54). Mouse-look stays event-driven in
    /// <c>PlayerController._Input</c>; a stick is a held axis, so it is polled per frame instead.</summary>
    public const string LookLeft = "look_left";
    public const string LookRight = "look_right";
    public const string LookUp = "look_up";
    public const string LookDown = "look_down";

    /// <summary>Deadzone for the look axes. Godot defaults an action to 0.5, which on a look stick
    /// reads as a dead controller until it is half-deflected.</summary>
    private const float LookDeadzone = 0.15f;

    /// <summary>Hotbar slots 1-5 (number-row keys) — quick-use/equip an assigned item.</summary>
    public static readonly string[] Hotbar = { "hotbar_1", "hotbar_2", "hotbar_3", "hotbar_4", "hotbar_5" };

    /// <summary>The display label for <paramref name="action"/>'s first bound key (e.g. "E"),
    /// resolved live from the InputMap so HUD prompts stay correct if bindings change
    /// (the Phase 54 remap seam). Falls back to "?" for unbound/non-key actions.</summary>
    public static string KeyLabel(string action)
    {
        foreach (InputEvent bound in InputMap.ActionGetEvents(action))
        {
            if (bound is InputEventKey key)
            {
                Key code = key.PhysicalKeycode != Key.None ? key.PhysicalKeycode : key.Keycode;
                return OS.GetKeycodeString(code);
            }
        }

        return "?";
    }

    /// <summary>The display label for <paramref name="action"/>'s first bound gamepad button
    /// (e.g. "X", "Start"), resolved live from the InputMap. Falls back to <see cref="KeyLabel"/>
    /// when the action has no gamepad binding.</summary>
    public static string PadLabel(string action)
    {
        foreach (InputEvent bound in InputMap.ActionGetEvents(action))
        {
            if (bound is InputEventJoypadButton pad)
            {
                return ButtonLabel(pad.ButtonIndex);
            }
        }

        return KeyLabel(action);
    }

    /// <summary>The device-aware prompt label (30.5J): the gamepad glyph while the player is
    /// driving with a controller (<see cref="InputDevice.GamepadActive"/>), the key otherwise.</summary>
    public static string PromptLabel(string action) =>
        InputDevice.GamepadActive ? PadLabel(action) : KeyLabel(action);

    /// <summary>Xbox-style display labels for gamepad buttons (pure; pinned by tests).</summary>
    public static string ButtonLabel(JoyButton button) => button switch
    {
        JoyButton.A => "A",
        JoyButton.B => "B",
        JoyButton.X => "X",
        JoyButton.Y => "Y",
        JoyButton.Back => "Select",
        JoyButton.Start => "Start",
        JoyButton.LeftShoulder => "LB",
        JoyButton.RightShoulder => "RB",
        JoyButton.LeftStick => "LS",
        JoyButton.RightStick => "RS",
        JoyButton.DpadUp => "D-Up",
        JoyButton.DpadDown => "D-Down",
        JoyButton.DpadLeft => "D-Left",
        JoyButton.DpadRight => "D-Right",
        _ => "?",
    };

    public static void EnsureActions()
    {
        Bind(MoveForward, new InputEventKey { PhysicalKeycode = Key.W });
        Bind(MoveBack, new InputEventKey { PhysicalKeycode = Key.S });
        Bind(MoveLeft, new InputEventKey { PhysicalKeycode = Key.A });
        Bind(MoveRight, new InputEventKey { PhysicalKeycode = Key.D });
        Bind(Jump, new InputEventKey { PhysicalKeycode = Key.Space });
        Bind(Sprint, new InputEventKey { PhysicalKeycode = Key.Shift });
        Bind(Dodge, new InputEventKey { PhysicalKeycode = Key.Ctrl });
        Bind(Interact, new InputEventKey { PhysicalKeycode = Key.E });
        Bind(Inventory, new InputEventKey { PhysicalKeycode = Key.I });
        Bind(Journal, new InputEventKey { PhysicalKeycode = Key.J });
        Bind(Map, new InputEventKey { PhysicalKeycode = Key.M });
        Bind(Bestiary, new InputEventKey { PhysicalKeycode = Key.B });
        Bind(CompanionCommand, new InputEventKey { PhysicalKeycode = Key.C });
        Bind(ToggleCamera, new InputEventKey { PhysicalKeycode = Key.V });
        Bind(Pause, new InputEventKey { PhysicalKeycode = Key.Escape });
        Bind(Attack, new InputEventMouseButton { ButtonIndex = MouseButton.Left });
        Bind(Block, new InputEventMouseButton { ButtonIndex = MouseButton.Right });
        Bind(Cast, new InputEventKey { PhysicalKeycode = Key.Q });
        Bind(CycleSpell, new InputEventKey { PhysicalKeycode = Key.F });
        Bind(LockOn, new InputEventMouseButton { ButtonIndex = MouseButton.Middle });
        Bind(LockCycleNext, new InputEventMouseButton { ButtonIndex = MouseButton.WheelDown });
        Bind(LockCyclePrev, new InputEventMouseButton { ButtonIndex = MouseButton.WheelUp });

        Key[] digits = { Key.Key1, Key.Key2, Key.Key3, Key.Key4, Key.Key5 };
        for (int i = 0; i < Hotbar.Length; i++)
        {
            Bind(Hotbar[i], new InputEventKey { PhysicalKeycode = digits[i] });
        }

        BindGamepad();
    }

    /// <summary>The gamepad layer: the menu-facing actions so no menu is mouse-only, the left stick
    /// mapped onto Godot's built-in <c>ui_*</c> focus-navigation actions (the D-pad and A/B are
    /// already bound to them by the engine defaults), <b>and the full gameplay set</b> — sticks for
    /// move/look, triggers for attack/guard, shoulders for magic. Before this the pad could open
    /// every menu in the game and not walk out of the first room. Remapping is still Phase 54.</summary>
    private static void BindGamepad()
    {
        Bind(Pause, new InputEventJoypadButton { ButtonIndex = JoyButton.Start });
        Bind(Inventory, new InputEventJoypadButton { ButtonIndex = JoyButton.Y });
        Bind(Journal, new InputEventJoypadButton { ButtonIndex = JoyButton.Back });
        Bind(Map, new InputEventJoypadButton { ButtonIndex = JoyButton.DpadUp });
        Bind(Bestiary, new InputEventJoypadButton { ButtonIndex = JoyButton.DpadDown });
        Bind(Interact, new InputEventJoypadButton { ButtonIndex = JoyButton.X });
        Bind(CompanionCommand, new InputEventJoypadButton { ButtonIndex = JoyButton.DpadRight });

        Bind(ToggleCamera, new InputEventJoypadButton { ButtonIndex = JoyButton.DpadLeft });

        // Movement: left stick. Bound to the same four actions WASD uses, so Input.GetVector in the
        // controller picks the pad up with no branch on device.
        Bind(MoveForward, new InputEventJoypadMotion { Axis = JoyAxis.LeftY, AxisValue = -1f });
        Bind(MoveBack, new InputEventJoypadMotion { Axis = JoyAxis.LeftY, AxisValue = 1f });
        Bind(MoveLeft, new InputEventJoypadMotion { Axis = JoyAxis.LeftX, AxisValue = -1f });
        Bind(MoveRight, new InputEventJoypadMotion { Axis = JoyAxis.LeftX, AxisValue = 1f });

        // Look: right stick, polled per frame by the controller.
        Bind(LookLeft, new InputEventJoypadMotion { Axis = JoyAxis.RightX, AxisValue = -1f });
        Bind(LookRight, new InputEventJoypadMotion { Axis = JoyAxis.RightX, AxisValue = 1f });
        Bind(LookUp, new InputEventJoypadMotion { Axis = JoyAxis.RightY, AxisValue = -1f });
        Bind(LookDown, new InputEventJoypadMotion { Axis = JoyAxis.RightY, AxisValue = 1f });
        foreach (string look in new[] { LookLeft, LookRight, LookUp, LookDown })
        {
            InputMap.ActionSetDeadzone(look, LookDeadzone);
        }

        // Combat: triggers for the two things held, face/shoulder buttons for the rest.
        Bind(Attack, new InputEventJoypadMotion { Axis = JoyAxis.TriggerRight, AxisValue = 1f });
        Bind(Block, new InputEventJoypadMotion { Axis = JoyAxis.TriggerLeft, AxisValue = 1f });
        Bind(Jump, new InputEventJoypadButton { ButtonIndex = JoyButton.A });
        Bind(Dodge, new InputEventJoypadButton { ButtonIndex = JoyButton.B });
        Bind(Sprint, new InputEventJoypadButton { ButtonIndex = JoyButton.LeftStick });
        Bind(LockOn, new InputEventJoypadButton { ButtonIndex = JoyButton.RightStick });
        Bind(Cast, new InputEventJoypadButton { ButtonIndex = JoyButton.RightShoulder });
        Bind(CycleSpell, new InputEventJoypadButton { ButtonIndex = JoyButton.LeftShoulder });

        Bind("ui_up", new InputEventJoypadMotion { Axis = JoyAxis.LeftY, AxisValue = -1f });
        Bind("ui_down", new InputEventJoypadMotion { Axis = JoyAxis.LeftY, AxisValue = 1f });
        Bind("ui_left", new InputEventJoypadMotion { Axis = JoyAxis.LeftX, AxisValue = -1f });
        Bind("ui_right", new InputEventJoypadMotion { Axis = JoyAxis.LeftX, AxisValue = 1f });
    }

    private static void Bind(string action, InputEvent trigger)
    {
        if (!InputMap.HasAction(action))
        {
            InputMap.AddAction(action);
        }

        if (!InputMap.ActionHasEvent(action, trigger))
        {
            InputMap.ActionAddEvent(action, trigger);
        }
    }
}
