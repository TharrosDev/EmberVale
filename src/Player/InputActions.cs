using Embervale.Core;
using Godot;

namespace Embervale.Player;

/// <summary>
/// The input actions gameplay polls every frame, as <see cref="StringName"/>s built once.
///
/// <para><see cref="GameInput"/> names its actions as string constants, and every
/// <c>Input.IsActionPressed("...")</c> made from one converts it to a new <see cref="StringName"/>
/// on the way in: a trip across the engine boundary and a finalizable managed object per call. The
/// player's input router alone asks about thirty of them per physics frame, so that was a couple
/// of thousand short-lived objects a second for the collector to chase — in the one loop where a
/// collection pause is felt as a dropped input.</para>
///
/// <para>The names stay owned by <see cref="GameInput"/>; this only holds the converted form, so
/// the two cannot drift.</para>
/// </summary>
internal static class InputActions
{
    public static readonly StringName MoveForward = GameInput.MoveForward;
    public static readonly StringName MoveBack = GameInput.MoveBack;
    public static readonly StringName MoveLeft = GameInput.MoveLeft;
    public static readonly StringName MoveRight = GameInput.MoveRight;
    public static readonly StringName Jump = GameInput.Jump;
    public static readonly StringName Sprint = GameInput.Sprint;
    public static readonly StringName WalkToggle = GameInput.WalkToggle;
    public static readonly StringName Dodge = GameInput.Dodge;
    public static readonly StringName Mount = GameInput.Mount;
    public static readonly StringName ToggleCamera = GameInput.ToggleCamera;

    public static readonly StringName Attack = GameInput.Attack;
    public static readonly StringName Block = GameInput.Block;
    public static readonly StringName Cast = GameInput.Cast;
    public static readonly StringName CycleSpell = GameInput.CycleSpell;
    public static readonly StringName Interact = GameInput.Interact;

    public static readonly StringName LockOn = GameInput.LockOn;
    public static readonly StringName LockCycleNext = GameInput.LockCycleNext;
    public static readonly StringName LockCyclePrev = GameInput.LockCyclePrev;

    public static readonly StringName LookLeft = GameInput.LookLeft;
    public static readonly StringName LookRight = GameInput.LookRight;
    public static readonly StringName LookUp = GameInput.LookUp;
    public static readonly StringName LookDown = GameInput.LookDown;

    public static readonly StringName CompanionCommand = GameInput.CompanionCommand;
    public static readonly StringName Place = GameInput.Place;

    /// <summary>Hotbar slots, index-aligned with <see cref="GameInput.Hotbar"/>.</summary>
    public static readonly StringName[] Hotbar = BuildHotbar();

    private static StringName[] BuildHotbar()
    {
        var names = new StringName[GameInput.Hotbar.Length];
        for (int i = 0; i < names.Length; i++)
        {
            names[i] = GameInput.Hotbar[i];
        }

        return names;
    }
}
