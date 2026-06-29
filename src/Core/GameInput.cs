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
    public const string Inventory = "inventory";
    public const string Journal = "journal";
    public const string Map = "map";
    public const string Pause = "pause";

    /// <summary>Hotbar slots 1-5 (number-row keys) — quick-use/equip an assigned item.</summary>
    public static readonly string[] Hotbar = { "hotbar_1", "hotbar_2", "hotbar_3", "hotbar_4", "hotbar_5" };

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
        Bind(Pause, new InputEventKey { PhysicalKeycode = Key.Escape });
        Bind(Attack, new InputEventMouseButton { ButtonIndex = MouseButton.Left });
        Bind(Block, new InputEventMouseButton { ButtonIndex = MouseButton.Right });
        Bind(Cast, new InputEventKey { PhysicalKeycode = Key.Q });
        Bind(CycleSpell, new InputEventKey { PhysicalKeycode = Key.F });

        Key[] digits = { Key.Key1, Key.Key2, Key.Key3, Key.Key4, Key.Key5 };
        for (int i = 0; i < Hotbar.Length; i++)
        {
            Bind(Hotbar[i], new InputEventKey { PhysicalKeycode = digits[i] });
        }
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
