using System;
using Godot;

namespace Embervale.Player;

/// <summary>
/// Where the casting hand goes in first person while a spell is being wound up, charged or
/// channelled, as arithmetic with no engine in it (Godot structs only), so it can be unit-tested.
///
/// <para>The cast and channel clips hold the casting arm straight out at shoulder height. Seen from
/// the eye that is either on the crosshair or, with a wide view and a low hand, out of frame; and a
/// plain cast's wind-up has no pose of its own at all, so the hand hangs at the hip until the spell
/// has already left. The first-person presentation therefore swings the whole arm at the shoulder so
/// the wrist sits on one chosen line of sight: low and to the hand's own side, the same place on
/// screen at every field of view. One rotation at the shoulder, eased in and out; the elbow, the
/// wrist and the fingers keep whatever the clip gave them.</para>
/// </summary>
public static class FirstPersonArmRules
{
    /// <summary>Seconds the arm takes to come up when a cast begins, and to go back when it ends.</summary>
    public const float RaiseSeconds = 0.18f;
    public const float LowerSeconds = 0.3f;

    /// <summary>Where on screen the wrist is put, as fractions of the half-width (toward the hand's
    /// own side) and of the half-height (below centre). Inside the frame at any field of view and
    /// aspect, and well clear of the crosshair.</summary>
    public const float FrameSide = 0.42f;
    public const float FrameBelow = 0.5f;

    /// <summary>The nearest the wrist is brought to the eye along that line (metres): beyond the
    /// near plane and far enough that the hand does not fill the corner of the view.</summary>
    public const float MinAhead = 0.3f;

    /// <summary>The most the arm is swung from where the clip has it (radians). The correction is
    /// meant to be a small one; an arm that would need more is left partway rather than wrenched.</summary>
    public const float MaxSwing = 1.6f;

    /// <summary>Steps the 0..1 raise toward up or down at the two rates above.</summary>
    public static float StepWeight(float weight, float target, float delta)
    {
        float goal = Math.Clamp(float.IsFinite(target) ? target : 0f, 0f, 1f);
        float current = Math.Clamp(float.IsFinite(weight) ? weight : 0f, 0f, 1f);
        float seconds = goal > current ? RaiseSeconds : LowerSeconds;
        float step = Math.Max(delta, 0f) / seconds;
        return goal > current ? Math.Min(current + step, goal) : Math.Max(current - step, goal);
    }

    /// <summary>
    /// The line of sight the wrist is put on, as a unit vector in the camera's own axes (+X right,
    /// +Y up, -Z forward), for a vertical field of view in degrees and an aspect (width over height).
    /// <paramref name="side"/> is -1 for a hand on the camera's left, +1 for one on its right.
    /// </summary>
    public static Vector3 ViewDirection(float fovDegrees, float aspect, float side)
    {
        float fov = Math.Clamp(float.IsFinite(fovDegrees) ? fovDegrees : 75f, 1f, 170f);
        float wide = float.IsFinite(aspect) && aspect > 0f ? aspect : 16f / 9f;
        float tan = Mathf.Tan(Mathf.DegToRad(fov) * 0.5f);
        float sign = side < 0f ? -1f : 1f;
        return new Vector3(sign * FrameSide * tan * wide, -FrameBelow * tan, -1f).Normalized();
    }

    /// <summary>
    /// The line of sight to a point given in the camera's own axes (+X right, +Y up, -Z forward),
    /// mirrored onto the hand's own side (<paramref name="side"/>: -1 left, +1 right): the line the
    /// wrist is put on so the hand sits under whatever is drawn at that point. A point that is not
    /// in front of the camera gives the default line instead.
    /// </summary>
    public static Vector3 LineToward(Vector3 viewPoint, float side)
    {
        if (!viewPoint.IsFinite() || viewPoint.Z > -0.01f)
        {
            return ViewDirection(75f, 16f / 9f, side);
        }

        float sign = side < 0f ? -1f : 1f;
        return new Vector3(sign * Math.Abs(viewPoint.X), viewPoint.Y, viewPoint.Z).Normalized();
    }

    /// <summary>
    /// The point on the line of sight from <paramref name="eye"/> along <paramref name="direction"/>
    /// (a unit vector) that the wrist can be swung onto: <paramref name="reach"/> from the
    /// <paramref name="shoulder"/>, on the far side of the line's pass through that sphere. Where the
    /// line misses the sphere the arm cannot get there, and the nearest point of the line is
    /// returned so the arm at least points at it. Never nearer the eye than <see cref="MinAhead"/>.
    /// </summary>
    public static Vector3 HandTarget(Vector3 eye, Vector3 direction, Vector3 shoulder, float reach)
    {
        Vector3 fromShoulder = eye - shoulder;
        float along = fromShoulder.Dot(direction);
        float discriminant = (along * along) - (fromShoulder.LengthSquared() - (reach * reach));
        float distance = discriminant >= 0f ? -along + Mathf.Sqrt(discriminant) : -along;
        return eye + (direction * Math.Max(distance, MinAhead));
    }

    /// <summary>
    /// The rotation that swings the arm about the <paramref name="shoulder"/> so the
    /// <paramref name="hand"/> points at <paramref name="target"/>, scaled by the eased
    /// <paramref name="weight"/> and held to <see cref="MaxSwing"/>. Identity when there is nothing
    /// to swing (a zero-length arm, a target on the shoulder, a hand already there).
    /// </summary>
    public static Quaternion Swing(Vector3 shoulder, Vector3 hand, Vector3 target, float weight)
    {
        Vector3 from = hand - shoulder;
        Vector3 to = target - shoulder;
        if (from.LengthSquared() < 1e-6f || to.LengthSquared() < 1e-6f || !from.IsFinite() || !to.IsFinite())
        {
            return Quaternion.Identity;
        }

        from = from.Normalized();
        to = to.Normalized();
        Vector3 axis = from.Cross(to);
        float angle = Mathf.Atan2(axis.Length(), from.Dot(to));
        float eased = CameraRigMath.Ease(weight);
        if (axis.LengthSquared() < 1e-10f || angle < 1e-4f || eased <= 0f)
        {
            return Quaternion.Identity;
        }

        return new Quaternion(axis.Normalized(), Math.Min(angle, MaxSwing) * eased);
    }
}
