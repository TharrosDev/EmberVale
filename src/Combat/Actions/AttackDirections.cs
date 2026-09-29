using Godot;

namespace Embervale.Combat.Actions;

/// <summary>Which way the attacker was moving when the swing began. Append-only.</summary>
// APPEND ONLY: ordinals may reach .tres and saves — never reorder/insert/remove (EnumStabilityTests).
public enum AttackDirection
{
    /// <summary>Standing still: the weapon's ordinary swing.</summary>
    Neutral = 0,

    /// <summary>Pushing forward: a lunging strike that closes the gap.</summary>
    Forward = 1,

    /// <summary>Pulling back: a quick cut that keeps the attacker's distance.</summary>
    Back = 2,

    /// <summary>Stepping left: a wide sweeping cut.</summary>
    Left = 3,

    /// <summary>Stepping right: a wide sweeping cut.</summary>
    Right = 4,
}

/// <summary>The numbers a direction changes about a swing. All neutral at 1 / zero.</summary>
public readonly record struct DirectionModifier(
    float DamageScale, float PoiseScale, float DurationScale, Vector3 Advance);

/// <summary>
/// Pure resolution of a humanoid's attack direction from its movement input, and what each direction
/// does. Godot math only (no nodes), so it is unit-tested.
///
/// <para><b>Direction is chosen once, at the commit, and then it is fixed.</b> The swing plays the
/// same clip whichever way it was pushed; what changes is a bounded step in that direction during the
/// wind-up and a small trade in damage, poise and speed. A lunge hits a little harder and leaves you
/// in the target's reach; a back-cut hits lighter and keeps you out of it. Readable, and never a
/// second move-set to author.</para>
/// </summary>
public static class AttackDirections
{
    /// <summary>Stick length below which the attack is a neutral one.</summary>
    public const float Deadzone = 0.5f;

    /// <summary>Resolves the direction from the raw move vector, where <c>X</c> is right and <c>Y</c>
    /// is back (Godot's <c>Input.GetVector</c> convention, so forward is negative Y).</summary>
    public static AttackDirection Resolve(Vector2 input)
    {
        if (input.LengthSquared() < Deadzone * Deadzone)
        {
            return AttackDirection.Neutral;
        }

        if (Mathf.Abs(input.Y) >= Mathf.Abs(input.X))
        {
            return input.Y < 0f ? AttackDirection.Forward : AttackDirection.Back;
        }

        return input.X < 0f ? AttackDirection.Left : AttackDirection.Right;
    }

    /// <summary>What <paramref name="direction"/> changes. <c>Advance</c> is in the actor's local
    /// frame: forward is negative Z.</summary>
    public static DirectionModifier Modifier(AttackDirection direction) => direction switch
    {
        AttackDirection.Forward => new(1.1f, 1f, 1.05f, new Vector3(0f, 0f, -0.9f)),
        AttackDirection.Back => new(0.85f, 0.75f, 0.9f, new Vector3(0f, 0f, 0.8f)),
        AttackDirection.Left => new(1f, 1.2f, 1.1f, new Vector3(-0.6f, 0f, 0f)),
        AttackDirection.Right => new(1f, 1.2f, 1.1f, new Vector3(0.6f, 0f, 0f)),
        _ => new(1f, 1f, 1f, Vector3.Zero),
    };
}
