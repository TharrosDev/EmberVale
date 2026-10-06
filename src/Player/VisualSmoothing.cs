using System;
using Godot;

namespace Embervale.Player;

/// <summary>
/// Pure arithmetic behind <see cref="PlayerVisualSmoother"/>: where to draw a body that moves at the
/// physics rate on a screen that draws faster, and how far its visible yaw may trail a camera turn.
/// Engine-free (Godot structs only) so it is unit-tested headlessly, the way
/// <see cref="CameraRigMath"/> is.
/// </summary>
public static class VisualSmoothing
{
    /// <summary>A physics step longer than this (metres) is a teleport — a respawn, a blink, a fast
    /// travel — and is shown where it landed rather than slid across.</summary>
    public const float MaxStep = 0.75f;

    /// <summary>Seconds the visible body takes to catch a turn of the camera in third person, and
    /// the furthest it may trail it (radians).</summary>
    public const float YawLagSeconds = 0.1f;
    public const float MaxYawLag = Mathf.Pi / 6f;

    /// <summary>Below this (radians) a trailing yaw is finished rather than still decaying.</summary>
    private const float YawRest = 0.0005f;

    /// <summary>
    /// The interpolation residual: what to add to the body's real position so that it is drawn
    /// <paramref name="fraction"/> of the way from where the last physics tick started to where it
    /// ended. Zero at fraction 1 (the body is drawn where it is), the whole step back at fraction 0.
    /// A step past <paramref name="maxStep"/>, or anything not finite, is not smoothed at all.
    /// </summary>
    public static Vector3 Residual(Vector3 previous, Vector3 current, float fraction, float maxStep = MaxStep)
    {
        Vector3 step = current - previous;
        if (!step.IsFinite() || !float.IsFinite(fraction) || step.LengthSquared() > maxStep * maxStep)
        {
            return Vector3.Zero;
        }

        return step * (Math.Clamp(fraction, 0f, 1f) - 1f);
    }

    /// <summary>An angle wrapped into -pi..pi.</summary>
    public static float WrapAngle(float radians)
    {
        if (!float.IsFinite(radians))
        {
            return 0f;
        }

        float wrapped = radians % Mathf.Tau;
        if (wrapped > Mathf.Pi)
        {
            wrapped -= Mathf.Tau;
        }
        else if (wrapped < -Mathf.Pi)
        {
            wrapped += Mathf.Tau;
        }

        return wrapped;
    }

    /// <summary>
    /// One frame of the third-person yaw lag. <paramref name="lag"/> is how far the visible body's
    /// yaw sits from the real one (radians); a turn of <paramref name="bodyYawDelta"/> leaves the
    /// visible body where it was, so the lag takes the opposite of it, then decays over
    /// <see cref="YawLagSeconds"/> and is held within <see cref="MaxYawLag"/>.
    ///
    /// <para>When not <paramref name="enabled"/> (first person, locked on, mid-action) no new turn is
    /// taken up and whatever lag is left bleeds off twice as fast, so the body lines up with the aim
    /// without snapping.</para>
    /// </summary>
    public static float YawLagStep(float lag, float bodyYawDelta, float delta, bool enabled)
    {
        float dt = Math.Max(delta, 0f);
        float next = enabled ? WrapAngle(lag - WrapAngle(bodyYawDelta)) : WrapAngle(lag);
        next *= Mathf.Exp(-dt / (enabled ? YawLagSeconds : YawLagSeconds * 0.5f));
        next = Math.Clamp(next, -MaxYawLag, MaxYawLag);
        return Math.Abs(next) < YawRest ? 0f : next;
    }
}
