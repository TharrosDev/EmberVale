using Godot;

namespace Embervale.Movement;

/// <summary>
/// Whether a motion vector is safe to hand to the physics server (Phase 37F). Pure and testable
/// headlessly for the reason <see cref="Player.CameraRigMath"/> is: a <c>Vector3</c> is a struct, so
/// the test project can reach it where it cannot construct a <c>Node</c>.
///
/// ⚠️ <b>IT EXISTS BECAUSE A `CharacterBody3D` KEEPS ITS VELOCITY BETWEEN FRAMES.</b> One NaN written
/// into <c>Velocity</c> is not one bad frame — it is every frame afterwards, for the rest of the run,
/// because the next frame reads the poisoned value back out and multiplies it forward. That is why a
/// single bad value surfaced as two unrelated-looking crashes in two unrelated systems: a dead enemy
/// reached <c>Mathf.MoveToward</c>, which throws on NaN inside <c>Math.Sign</c>, while a companion
/// reached <c>MoveAndSlide</c>, which warns that a Vector3 "cannot be normalized". Neither is where
/// the value came from.
/// </summary>
public static class MotionSafety
{
    /// <summary>Whether every component is a real number — not NaN, not ±∞.</summary>
    public static bool IsFinite(Vector3 v) =>
        float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);

    /// <summary>
    /// The vector if it is usable, otherwise <see cref="Vector3.Zero"/>.
    ///
    /// ⚠️ <b>Zero rather than a clamp.</b> A non-finite velocity carries no recoverable information —
    /// there is no "very large" to clamp toward, because NaN is not big, it is meaningless. Stopping
    /// the body is the only answer that cannot make things worse, and a stationary actor is a visible
    /// symptom the player can report, where a body flung to infinity simply disappears.
    /// </summary>
    public static Vector3 Sanitize(Vector3 v) => IsFinite(v) ? v : Vector3.Zero;

    /// <summary>
    /// Whether a body's <em>position</em> is somewhere it can still be: finite, and not below
    /// <paramref name="worldFloorY"/>.
    ///
    /// ⚠️ <b>THE SAME PERSISTENCE ARGUMENT, ONE LEVEL UP.</b> A velocity guard stops a NaN before it
    /// moves the body; this catches the body that is already somewhere impossible — a NaN written
    /// straight into a transform by a teleport, or a body that slipped through a terrain seam and is
    /// falling forever. Neither recovers on its own: the first poisons every query the body is part
    /// of, the second accelerates until float precision turns its collisions into noise.
    /// <c>WorldRecovery</c> answers deep water and pits for the player; nothing answered "below the
    /// world" for anyone.
    /// </summary>
    public static bool IsInWorld(Vector3 position, float worldFloorY) =>
        IsFinite(position) && position.Y >= worldFloorY;
}
