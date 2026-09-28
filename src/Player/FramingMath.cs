using System;
using Godot;

namespace Embervale.Player;

/// <summary>What a lock-on asks of the camera at full weight: a lateral and upward shift in camera
/// rest space, a small pitch and yaw in radians, and a pull-back multiplier.</summary>
public readonly record struct LockFraming(Vector3 Offset, float Pitch, float Yaw, float DistanceScale)
{
    public static readonly LockFraming None = new(Vector3.Zero, 0f, 0f, 1f);
}

/// <summary>What aiming asks of the camera at full weight, on top of the Aim profile.</summary>
public readonly record struct AimFraming(Vector3 Offset, float FovOffset, float DistanceScale)
{
    public static readonly AimFraming None = new(Vector3.Zero, 0f, 1f);
}

/// <summary>
/// Pure rules behind <see cref="CameraFramingLayer"/> and the lock-on's camera-facing behaviour.
/// Engine-free (Godot structs only) so every rule is unit-testable headlessly, like
/// <see cref="CameraRigMath"/>.
///
/// <para>⚠️ <b>Everything here is restrained on purpose.</b> The camera's forward is the aim ray, so
/// the angles are clamped to a few degrees and the offsets to a few decimetres; framing is a lean,
/// not a drag. The rig owns the big shape (<see cref="CameraProfile"/>); this is the last
/// centimetre of composition.</para>
/// </summary>
public static class FramingMath
{
    /// <summary>The most pitch a lock-on may add, radians (2.5 degrees).</summary>
    public const float MaxLockPitch = 0.0436f;

    /// <summary>The most yaw a lock-on may add, radians (3 degrees).</summary>
    public const float MaxLockYaw = 0.0524f;

    /// <summary>A target nearer than this is "close": the framing is at full strength.</summary>
    public const float CloseTarget = 3f;

    /// <summary>A target further than this is "far": the close-range framing has faded to nothing.</summary>
    public const float FarTarget = 10f;

    /// <summary>How far past the shoulder the camera slides to see a close target, metres.</summary>
    public const float LockLateral = 0.15f;

    /// <summary>How much extra rise a close target earns, metres.</summary>
    public const float LockRise = 0.14f;

    /// <summary>How much further back the camera sits for a close target (fraction).</summary>
    public const float LockPullback = 0.06f;

    /// <summary>The FOV tighten aiming adds on top of the Aim profile, degrees (negative = narrower).</summary>
    public const float AimFov = -2.5f;

    /// <summary>The pull-in aiming adds on top of the Aim profile (multiplier).</summary>
    public const float AimDistanceScale = 0.95f;

    /// <summary>Lateral lead toward the aim side over a chosen shoulder, metres.</summary>
    public const float AimLead = 0.10f;

    /// <summary>Lateral lead with the centred shoulder, where the body would otherwise sit on the
    /// reticle, metres.</summary>
    public const float AimLeadCentred = 0.30f;

    /// <summary>Smoothstep of <paramref name="x"/> between two edges, 0 below and 1 above.</summary>
    public static float Smooth01(float edge0, float edge1, float x)
    {
        if (edge1 <= edge0)
        {
            return x >= edge1 ? 1f : 0f;
        }

        return CameraRigMath.Ease((x - edge0) / (edge1 - edge0));
    }

    /// <summary>+1 for a right-shoulder camera, -1 for left. A centred camera (offset of zero) reads
    /// as +1, so a nudge that needs a side always has one.</summary>
    public static float ShoulderSign(float shoulderOffset) => shoulderOffset < 0f ? -1f : 1f;

    /// <summary>
    /// The framing a lock-on asks for. A target that is <b>close</b> is looked at past the shoulder
    /// and from a touch higher and further back, so it is not hidden behind the player's back; a far
    /// target needs none of that. The yaw exactly cancels the lateral slide so the target stays on
    /// the crosshair, and the pitch follows ground the target stands above or below the player so a
    /// taller or higher foe keeps its head in frame.
    /// </summary>
    /// <param name="distance">Horizontal metres from the player to the target.</param>
    /// <param name="heightDelta">Target's ground height minus the player's, metres.</param>
    /// <param name="shoulderSign">+1 right shoulder, -1 left; see <see cref="ShoulderSign"/>.</param>
    public static LockFraming Lock(float distance, float heightDelta, float shoulderSign)
    {
        float d = Math.Max(distance, 1f);
        float closeness = 1f - Smooth01(CloseTarget, FarTarget, d);
        float lateral = shoulderSign * LockLateral * closeness;
        float rise = LockRise * closeness;

        float yaw = Math.Clamp(MathF.Atan2(lateral, d), -MaxLockYaw, MaxLockYaw);
        float pitch = Math.Clamp(MathF.Atan2(heightDelta, d) * 0.3f, -MaxLockPitch, MaxLockPitch);
        return new LockFraming(new Vector3(lateral, rise, 0f), pitch, yaw, 1f + (LockPullback * closeness));
    }

    /// <summary>
    /// The framing aiming asks for. The lead and pull-in only exist in third person (scaled by
    /// <paramref name="thirdBlend"/>, 0 = first person), because in first person the camera IS the
    /// eye and there is no body to clear. The FOV tighten exists in both and is scaled by the
    /// player's FOV-kick comfort, so Reduced Motion removes it.
    /// </summary>
    public static AimFraming Aim(float shoulderOffset, float thirdBlend, float fovKick)
    {
        float blend = Math.Clamp(thirdBlend, 0f, 1f);
        float lead = shoulderOffset == 0f ? AimLeadCentred : AimLead;
        return new AimFraming(
            new Vector3(ShoulderSign(shoulderOffset) * lead * blend, 0f, 0f),
            AimFov * Math.Clamp(fovKick, 0f, 1f),
            1f + ((AimDistanceScale - 1f) * blend));
    }

    /// <summary>A lock's framing as a nudge, at <paramref name="weight"/> (0..1, already eased). The
    /// lateral slide, rise and its cancelling yaw fade out in first person
    /// (<paramref name="thirdBlend"/> 0) where they would just move the eye; the pitch does not.</summary>
    public static CameraNudge ToNudge(in LockFraming f, float weight, float thirdBlend)
    {
        float w = Math.Clamp(weight, 0f, 1f);
        float third = Math.Clamp(thirdBlend, 0f, 1f);
        return new CameraNudge(
            f.Offset * (w * third),
            new Vector3(f.Pitch * w, f.Yaw * w * third, 0f),
            0f,
            1f + ((f.DistanceScale - 1f) * w));
    }

    /// <summary>An aim framing as a nudge at <paramref name="weight"/> (0..1, already eased).</summary>
    public static CameraNudge ToNudge(in AimFraming f, float weight)
    {
        float w = Math.Clamp(weight, 0f, 1f);
        return new CameraNudge(f.Offset * w, Vector3.Zero, f.FovOffset * w, 1f + ((f.DistanceScale - 1f) * w));
    }

    /// <summary>Exponentially approaches <paramref name="target"/>, frame-rate independent (see
    /// <see cref="CameraRigMath.Damp"/>).</summary>
    public static float Approach(float current, float target, float dt, float seconds) =>
        current + ((target - current) * CameraRigMath.Damp(dt, seconds));

    /// <summary>
    /// Eases a 0..1 weight toward on/off, faster in than out: framing arrives promptly when a lock or
    /// an aim begins and lets go slowly when it ends, so nothing snaps back the moment it breaks.
    /// Linear, so it reaches exactly 0 and 1; the layer smooths it with <see cref="CameraRigMath.Ease"/>.
    /// </summary>
    public static float StepWeight(float weight, bool active, float dt, float inSeconds, float outSeconds) =>
        dt <= 0f ? weight : CameraRigMath.StepBlend(weight, active ? 1f : 0f, dt, active ? inSeconds : outSeconds);

    /// <summary>Signed angle in radians from <paramref name="forward"/> to <paramref name="to"/>
    /// about the up axis, both flattened. Positive means the target is to the right. 0 when either
    /// has no horizontal extent.</summary>
    public static float SignedBearing(Vector3 forward, Vector3 to)
    {
        if (((forward.X * forward.X) + (forward.Z * forward.Z)) < 1e-6f ||
            ((to.X * to.X) + (to.Z * to.Z)) < 1e-6f)
        {
            return 0f;
        }

        // Godot is right-handed with -Z forward, so this cross product is positive for a turn to the
        // LEFT; the sign is flipped so right reads positive, the way a compass does.
        float cross = (forward.Z * to.X) - (forward.X * to.Z);
        float dot = (forward.X * to.X) + (forward.Z * to.Z);
        return MathF.Atan2(-cross, dot);
    }

    /// <summary>Yaw (radians about Y) that makes -Z face <paramref name="to"/>. 0 with no horizontal
    /// extent.</summary>
    public static float YawTo(Vector3 to) =>
        ((to.X * to.X) + (to.Z * to.Z)) < 1e-6f ? 0f : MathF.Atan2(-to.X, -to.Z);

    /// <summary>The shortest signed angle from <paramref name="from"/> to <paramref name="to"/>.</summary>
    public static float AngleDelta(float from, float to)
    {
        float d = (to - from) % MathF.Tau;
        if (d > MathF.PI)
        {
            d -= MathF.Tau;
        }
        else if (d < -MathF.PI)
        {
            d += MathF.Tau;
        }

        return d;
    }

    /// <summary>
    /// One step of the body's turn toward a newly-locked target: a fast ease, capped at
    /// <paramref name="maxRate"/> radians a second, so a lock or a cycle swings the view round over a
    /// beat instead of cutting. Lands exactly on the target inside a hair, so it always finishes.
    /// </summary>
    public static float SlewYaw(float current, float target, float dt, float seconds, float maxRate)
    {
        float error = AngleDelta(current, target);
        if (Math.Abs(error) < 0.002f)
        {
            return target;
        }

        float step = error * CameraRigMath.Damp(dt, seconds);
        float cap = Math.Max(maxRate, 0f) * Math.Max(dt, 0f);
        return current + Math.Clamp(step, -cap, cap);
    }

    /// <summary>Where the crosshair ray starts. It is cast from the camera, but in third person the
    /// camera is behind the player, so the first <paramref name="pullback"/> metres are the space
    /// between camera and player, and a prop caught there must not become the aim point. In first
    /// person the pullback is 0 and this is the camera position, exactly as before.</summary>
    public static Vector3 AimTraceStart(Vector3 cameraPosition, Vector3 forward, float pullback) =>
        cameraPosition + (forward * Math.Max(pullback, 0f));

    /// <summary>The point the aim converges on: the ray's hit, unless it is closer than
    /// <paramref name="minDistance"/> to the start (a wall at the player's nose), in which case the
    /// far point along the ray, so the aim does not swing wildly against a close surface.</summary>
    public static Vector3 AimConvergence(
        Vector3 start, Vector3 forward, Vector3? hit, float minDistance, float farDistance)
    {
        if (hit is { } point && (point - start).LengthSquared() >= minDistance * minDistance)
        {
            return point;
        }

        return start + (forward * farDistance);
    }
}
