using System;
using Godot;

namespace Embervale.Player;

/// <summary>
/// Pure arithmetic behind the player's hybrid first/third-person camera rig. Kept engine-free
/// (Godot structs only — no <c>GodotObject</c>, no <c>GD.*</c>) so the load-bearing parts, the
/// mode blend and the wall-collision spring, are unit-testable headlessly, the same way
/// <see cref="Settings.SettingsMath"/> and <see cref="UI.CompassMath"/> are. The physics queries
/// and node writes stay in <see cref="PlayerController"/>.
/// </summary>
public static class CameraRigMath
{
    /// <summary>
    /// The camera's rest position in camera-pivot space for a given mode. First person puts the
    /// camera on the pivot itself (the pivot already sits at eye height). Third person swings it
    /// back (+Z is behind in Godot, which looks down -Z), up, and to the side — the shoulder
    /// offset is what keeps the body from sitting on top of the crosshair.
    /// </summary>
    public static Vector3 RestOffset(bool firstPerson, float back, float rise, float shoulder) =>
        firstPerson ? Vector3.Zero : new Vector3(shoulder, rise, back);

    /// <summary>Smoothstep, clamped — the ease applied to the mode blend so the swap eases in and
    /// out rather than sliding linearly.</summary>
    public static float Ease(float t)
    {
        float x = Math.Clamp(t, 0f, 1f);
        return x * x * (3f - (2f * x));
    }

    /// <summary>Advances the 0..1 mode blend toward <paramref name="target"/> at a rate that
    /// crosses the whole range in <paramref name="seconds"/>. A non-positive duration snaps,
    /// which is what a mode set before the first frame (a save loaded in third person) wants.</summary>
    public static float StepBlend(float t, float target, float delta, float seconds)
    {
        if (seconds <= 0f || delta <= 0f)
        {
            return Math.Clamp(target, 0f, 1f);
        }

        float step = delta / seconds;
        return target > t
            ? Math.Min(target, t + step)
            : Math.Max(target, t - step);
    }

    /// <summary>Linear interpolation between two rest offsets at an already-eased <paramref name="t"/>.</summary>
    public static Vector3 Blend(Vector3 from, Vector3 to, float t) =>
        from.Lerp(to, Math.Clamp(t, 0f, 1f));

    /// <summary>
    /// The camera's distance from the pivot this frame given the mode's <paramref name="desired"/>
    /// distance and the <paramref name="allowed"/> distance the collision cast permits.
    /// Deliberately asymmetric: pull in <b>instantly</b> so geometry never gets between the eye and
    /// the character, then ease back out at <paramref name="pushOutPerSec"/>. A symmetric spring
    /// visibly lags into the wall on the way in, which is the whole failure this exists to avoid.
    /// </summary>
    public static float SpringDistance(float current, float desired, float allowed, float delta, float pushOutPerSec)
    {
        float target = Math.Min(desired, allowed);
        if (target <= current)
        {
            return Math.Max(target, 0f);
        }

        float eased = current + (Math.Max(pushOutPerSec, 0f) * Math.Max(delta, 0f));
        return Math.Max(Math.Min(target, eased), 0f);
    }

    /// <summary>
    /// Direction from an aim origin to the point the crosshair converges on. In first person the
    /// camera sits on the pivot, so this returns the pivot's own forward and every aim path
    /// (interact, spells) behaves exactly as it did before the rig existed — the invariant that
    /// makes this change safe for the shipping mode.
    /// </summary>
    public static Vector3 AimDirection(Vector3 origin, Vector3 focusPoint)
    {
        Vector3 to = focusPoint - origin;
        return to.LengthSquared() < 0.000001f ? Vector3.Forward : to.Normalized();
    }
}
