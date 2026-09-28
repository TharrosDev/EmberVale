using System;
using Godot;

namespace Embervale.Player;

/// <summary>
/// Pure arithmetic behind the player's hybrid first/third-person camera rig. Kept engine-free
/// (Godot structs only — no <c>GodotObject</c>, no <c>GD.*</c>) so the load-bearing parts, the
/// mode blend, the wall spring, the shoulder swap and the way layers are combined, are
/// unit-testable headlessly, the same way <see cref="Settings.SettingsMath"/> and
/// <see cref="UI.CompassMath"/> are. The physics queries and node writes stay in
/// <see cref="PlayerCameraRig"/>.
/// </summary>
public static class CameraRigMath
{
    /// <summary>Widest and narrowest vertical field of view any combination of setting, profile, punch
    /// and layers may produce (degrees).</summary>
    public const float MinFov = 55f;
    public const float MaxFov = 115f;

    /// <summary>The nearest and furthest the third-person camera may sit behind the pivot (metres),
    /// and the most rise and shoulder offset it may be given. The settings sliders sit inside these;
    /// they exist so a stack of profile and layer scales cannot compound into a camera in the head or
    /// in orbit.</summary>
    public const float MinDistance = 1f;
    public const float MaxDistance = 8f;
    public const float MaxRise = 1.2f;
    public const float MaxShoulder = 1.2f;

    /// <summary>What one summed layer nudge is clamped to. Pitch and yaw are tight because they move
    /// the crosshair (the aim ray reads the camera's forward); roll is free by comparison.</summary>
    public const float MaxNudgeOffset = 1f;
    public const float MaxNudgeAim = 0.35f;
    public const float MaxNudgeRoll = 0.5f;
    public const float MaxNudgeFov = 25f;
    public const float MinNudgeDistanceScale = 0.5f;
    public const float MaxNudgeDistanceScale = 1.6f;

    /// <summary>A third-person seat with less than this much clear distance behind the pivot is "fully
    /// blocked": a swing out into it would put the camera against the head.</summary>
    public const float MinSeatDistance = 1f;

    /// <summary>Shoulder-swap thresholds, as fractions of the shoulder offset that is clear. The
    /// chosen shoulder is abandoned below <see cref="SwapBelow"/> when the other one is clearly
    /// better (<see cref="SwapMargin"/>), and returned to only when it is nearly free again
    /// (<see cref="SwapReturnAbove"/>) — the gap between the two is what stops it flickering.</summary>
    public const float SwapBelow = 0.55f;
    public const float SwapMargin = 0.25f;
    public const float SwapReturnAbove = 0.95f;

    /// <summary>Minimum seconds between shoulder swaps, so two walls a body-width apart cannot make the
    /// camera see-saw.</summary>
    public const float SwapHoldSeconds = 0.8f;

    /// <summary>Third person cannot look as far up as first: the orbit swings under the pivot and the
    /// camera ends up looking at its own feet through the floor.</summary>
    public const float ThirdPersonPitchLimit = 1.3f;

    /// <summary>How the wall spring's push-out is shaped: a pause before anything moves, a ramp up to
    /// full speed, and a slowing approach so it does not stop dead at full extension.</summary>
    public const float PushRampSeconds = 0.3f;
    public const float PushSettleRate = 6f;

    /// <summary>Narrowing the view slows the look by at most this much of its normal rate.</summary>
    public const float MinLookScale = 0.35f;

    /// <summary>The number of sweeps the wall spring makes to the seat, and how far the outer ones fan
    /// out (metres). See <see cref="ProbeMotion"/>.</summary>
    public const int ProbeCount = 5;
    public const float ProbeSpreadX = 0.3f;
    public const float ProbeSpreadY = 0.2f;

    /// <summary>Critical damping reaches 95% of the way in this many time constants.</summary>
    private const float SettleOmega = 4.75f;

    /// <summary>
    /// The camera's rest position in camera-pivot space for a given mode. First person puts the
    /// camera on the pivot itself (the pivot already sits at eye height). Third person swings it
    /// back (+Z is behind in Godot, which looks down -Z), up, and to the side — the shoulder
    /// offset is what keeps the body from sitting on top of the crosshair.
    /// </summary>
    public static Vector3 RestOffset(bool firstPerson, float back, float rise, float shoulder) =>
        firstPerson ? Vector3.Zero : new Vector3(shoulder, rise, back);

    /// <summary>Which shoulder the camera looks over, as a signed lateral offset. Side ids are
    /// <see cref="Settings.Settings.ShoulderRight"/> / <c>ShoulderLeft</c> / <c>ShoulderCentre</c>;
    /// anything unrecognised falls back to the right shoulder rather than throwing, so a settings
    /// file hand-edited to nonsense still yields a playable camera.</summary>
    public static float ShoulderOffset(int side, float magnitude) => side switch
    {
        Settings.Settings.ShoulderLeft => -magnitude,
        Settings.Settings.ShoulderCentre => 0f,
        _ => magnitude,
    };

    /// <summary>Smoothstep, clamped — the ease used wherever a 0..1 value needs to start and stop
    /// gently rather than linearly.</summary>
    public static float Ease(float t)
    {
        float x = Math.Clamp(t, 0f, 1f);
        return x * x * (3f - (2f * x));
    }

    /// <summary>
    /// Steps the 0..1 mode blend toward <paramref name="target"/> as a critically damped spring that
    /// reaches ~95% of the way in <paramref name="settleSeconds"/>. A spring rather than a timer
    /// because it carries <paramref name="velocity"/>: a toggle pressed halfway through a swap turns
    /// the camera around by decelerating and coming back, where a linear ramp would reverse in one
    /// frame at full speed — the lurch that makes a swap feel like a cut. A non-positive duration
    /// snaps, which is what a mode set before the first frame (a save loaded in third person) wants.
    /// </summary>
    public static void SpringBlend(
        ref float value, ref float velocity, float target, float delta, float settleSeconds)
    {
        float goal = Math.Clamp(target, 0f, 1f);
        if (settleSeconds <= 0f)
        {
            value = goal;
            velocity = 0f;
            return;
        }

        if (delta <= 0f)
        {
            return;
        }

        float omega = SettleOmega / settleSeconds;
        float offset = value - goal;
        float carry = velocity + (omega * offset);
        float decay = Mathf.Exp(-omega * delta);

        value = goal + ((offset + (carry * delta)) * decay);
        velocity = (velocity - (omega * carry * delta)) * decay;

        // Never leaves the range (a hard reversal near an end could nudge past it), and settles
        // exactly, so "fully first person" and "fully third" are values the rig can test for.
        if (Math.Abs(value - goal) < 0.002f && Math.Abs(velocity) < 0.02f)
        {
            value = goal;
            velocity = 0f;
        }
        else if (value < 0f || value > 1f)
        {
            value = Math.Clamp(value, 0f, 1f);
            velocity = 0f;
        }
    }

    /// <summary>Linear interpolation between two rest offsets at an already-eased <paramref name="t"/>.
    /// Also the eye-anchor crossfade: the head-bone seat at 0, the fixed-pivot seat at 1.</summary>
    public static Vector3 Blend(Vector3 from, Vector3 to, float t) =>
        from.Lerp(to, Math.Clamp(t, 0f, 1f));

    /// <summary>The third-person seat at full extension, in pivot space, with every input clamped to a
    /// sane range: the player's distance setting scaled by the profile and by the summed layers, the
    /// profile's rise, and the shoulder offset.</summary>
    public static Vector3 ComposeSeat(
        float baseDistance, float profileScale, float layerScale, float rise, float shoulder) => new(
        Math.Clamp(shoulder, -MaxShoulder, MaxShoulder),
        Math.Clamp(rise, 0f, MaxRise),
        Math.Clamp(baseDistance * profileScale * layerScale, MinDistance, MaxDistance));

    /// <summary>The final vertical field of view: the player's setting plus every offset (profile,
    /// punch, layers), clamped to the range the settings panel itself allows and then some.</summary>
    public static float ComposeFov(float baseFov, float offset) =>
        Math.Clamp(baseFov + offset, MinFov, MaxFov);

    /// <summary>
    /// A profile's FOV offset with the player's FOV Kick setting applied. Only WIDENING is a kick —
    /// it is what a sprint or a roll does to the view, and the part that upsets people. Narrowing
    /// (aiming, locking on) is framing the player asked for by pressing the button, so it is
    /// untouched.
    /// </summary>
    public static float ScaleFovKick(float fovOffset, float kick) =>
        fovOffset > 0f ? fovOffset * Math.Clamp(kick, 0f, 1f) : fovOffset;

    /// <summary>
    /// Sums every layer's nudge for the frame and clamps the total. The clamp is the safety net for
    /// the whole layer system: any one layer can be well behaved and three of them together still not
    /// be, and a layer that returns a non-finite number must not be able to write it to the camera.
    /// </summary>
    public static CameraNudge CombineLayers(ReadOnlySpan<CameraNudge> samples)
    {
        CameraNudge sum = CameraNudge.Identity;
        foreach (CameraNudge sample in samples)
        {
            sum = sum.Combine(sample);
        }

        return ClampNudge(sum);
    }

    /// <summary>One nudge held to <see cref="MaxNudgeOffset"/> and friends, non-finite parts dropped.</summary>
    public static CameraNudge ClampNudge(in CameraNudge nudge) => new(
        new Vector3(
            Limit(nudge.Offset.X, MaxNudgeOffset),
            Limit(nudge.Offset.Y, MaxNudgeOffset),
            Limit(nudge.Offset.Z, MaxNudgeOffset)),
        new Vector3(
            Limit(nudge.Euler.X, MaxNudgeAim),
            Limit(nudge.Euler.Y, MaxNudgeAim),
            Limit(nudge.Euler.Z, MaxNudgeRoll)),
        Limit(nudge.FovOffset, MaxNudgeFov),
        float.IsFinite(nudge.DistanceScale)
            ? Math.Clamp(nudge.DistanceScale, MinNudgeDistanceScale, MaxNudgeDistanceScale)
            : 1f);

    private static float Limit(float value, float magnitude) =>
        float.IsFinite(value) ? Math.Clamp(value, -magnitude, magnitude) : 0f;

    /// <summary>Horizontal speed as a 0..1 fraction of sprint speed — what the snapshot hands the
    /// layers and what the sprint lean is measured on. Zero when there is no sprint speed to
    /// measure against.</summary>
    public static float Speed01(float horizontalSpeed, float sprintSpeed) =>
        sprintSpeed <= 0f ? 0f : Math.Clamp(horizontalSpeed / sprintSpeed, 0f, 1f);

    /// <summary>Whether a third-person seat with <paramref name="allowedDistance"/> of clear room is
    /// worth swinging out to. A swap into a closet or a tunnel mouth is held in first person until
    /// there is room, rather than putting the camera against the back of the head.</summary>
    public static bool SeatUsable(float allowedDistance) => allowedDistance >= MinSeatDistance;

    /// <summary>
    /// Whether the camera should be over the OTHER shoulder, given how much of each shoulder's
    /// offset is clear (0..1, from a sweep out to it). Prefers swapping to pulling in: a wall against
    /// the chosen shoulder is answered by swinging to the free one, with hysteresis both ways (see
    /// <see cref="SwapBelow"/>) and a minimum hold so it cannot see-saw between two close walls.
    /// </summary>
    public static bool ShoulderSwapped(bool swapped, float homeClear, float awayClear, float secondsSinceSwap)
    {
        if (secondsSinceSwap < SwapHoldSeconds)
        {
            return swapped;
        }

        return swapped
            ? homeClear < SwapReturnAbove && awayClear > homeClear - SwapMargin
            : homeClear < SwapBelow && awayClear >= homeClear + SwapMargin;
    }

    /// <summary>
    /// Where probe <paramref name="index"/> of the wall sweep aims, in pivot space. Index 0 is the
    /// seat itself; 1..4 fan out left, right, up and down of it. All of them start at the pivot — a
    /// probe that STARTED offset would begin inside a wall the player is standing against and report
    /// "no room at all" — and only the far end moves, so a corner that the centre line slips past is
    /// still caught by the probe that grazes it.
    /// </summary>
    public static Vector3 ProbeMotion(Vector3 seat, int index) => index switch
    {
        1 => seat + new Vector3(ProbeSpreadX, 0f, 0f),
        2 => seat - new Vector3(ProbeSpreadX, 0f, 0f),
        3 => seat + new Vector3(0f, ProbeSpreadY, 0f),
        4 => seat - new Vector3(0f, ProbeSpreadY, 0f),
        _ => seat,
    };

    /// <summary>
    /// One step of a wall spring on a 0..1 fraction of full extension (the seat, the shoulder offset
    /// or the rise). <paramref name="allowed"/> is how much the sweep permits this frame.
    ///
    /// <para>Deliberately asymmetric: pull in <b>instantly</b> so geometry never gets between the eye
    /// and the character, then push back out only after <paramref name="holdSeconds"/> of clear
    /// space, easing up to <paramref name="pushOutPerSec"/> and slowing into the end. A symmetric
    /// spring visibly lags into the wall on the way in, and one that pushes out at once pumps in and
    /// out past every fence post. <paramref name="clearTime"/> is the caller's own running timer.</para>
    /// </summary>
    public static float SpringStep(
        float current, float desired, float allowed, float delta, float pushOutPerSec,
        ref float clearTime, float holdSeconds)
    {
        float target = Math.Min(desired, allowed);
        if (target <= current)
        {
            clearTime = 0f;
            return Math.Max(target, 0f);
        }

        clearTime += Math.Max(delta, 0f);
        if (clearTime < holdSeconds)
        {
            return current;
        }

        float remaining = target - current;
        float ramp = Ease((clearTime - holdSeconds) / PushRampSeconds);
        float speed = Math.Min(Math.Max(pushOutPerSec, 0f) * ramp, remaining * PushSettleRate);
        float next = current + (speed * Math.Max(delta, 0f));

        // The slowing approach never quite arrives; close enough is arrived.
        return remaining - (next - current) <= 0.004f ? target : Math.Min(next, target);
    }

    /// <summary>The pitch limit (radians, either direction) in force: the context's own limit, capped
    /// toward <see cref="ThirdPersonPitchLimit"/> as the camera goes out to third person.</summary>
    public static float PitchLimit(float profileLimit, float thirdBlend) =>
        Mathf.Lerp(profileLimit, Math.Min(profileLimit, ThirdPersonPitchLimit), Math.Clamp(thirdBlend, 0f, 1f));

    /// <summary>Eases a pitch that a tightening limit has left out of range back inside it, rather
    /// than yanking the view when the player locks on or mounts. Inside the limit it is untouched, so
    /// this never fights the player's own look input.</summary>
    public static float EasePitchInto(float pitch, float limit, float delta)
    {
        float clamped = Math.Clamp(pitch, -limit, limit);
        if (clamped == pitch)
        {
            return pitch;
        }

        float eased = Mathf.Lerp(pitch, clamped, Damp(delta, 0.15f));
        return Math.Abs(eased - clamped) < 0.001f ? clamped : eased;
    }

    /// <summary>
    /// How much of its normal rate the look should turn at for the current field of view: the ratio
    /// of the view's half-width now to at the player's setting, so a narrowed view (aiming) turns
    /// through the same fraction of the SCREEN per movement of the mouse rather than the same angle.
    /// Never faster than normal — a sprint widening the view does not speed the look up — and never
    /// below <see cref="MinLookScale"/>.
    /// </summary>
    public static float LookScale(float fov, float baseFov)
    {
        if (fov <= 0f || baseFov <= 0f)
        {
            return 1f;
        }

        float ratio = Mathf.Tan(Mathf.DegToRad(fov) * 0.5f) / Mathf.Tan(Mathf.DegToRad(baseFov) * 0.5f);
        return Math.Clamp(ratio, MinLookScale, 1f);
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

    /// <summary>
    /// A frame-rate-independent smoothing factor: the fraction of the way to a target to move this
    /// frame so that the remaining error decays by ~63% every <paramref name="seconds"/>.
    ///
    /// ⚠️ <b>A raw <c>Lerp(a, b, 0.1f)</c> is not frame-rate independent</b> — it converges twice as
    /// fast at 120 fps as at 60, so a camera tuned on one machine is wrong on another. This is the
    /// exponential form, which converges at the same rate in seconds whatever the frame rate.
    /// </summary>
    public static float Damp(float delta, float seconds)
    {
        if (seconds <= 0f)
        {
            return 1f;
        }

        float t = 1f - Mathf.Exp(-delta / seconds);
        return t < 0f ? 0f : t > 1f ? 1f : t;
    }

    /// <summary>Damps toward a target with one time constant going up and another coming down — a
    /// punch that lands in a few frames and bleeds off slowly, which is the shape of every kick.</summary>
    public static float AsymmetricDamp(
        float current, float target, float delta, float riseSeconds, float fallSeconds) =>
        Mathf.Lerp(current, target, Damp(delta, target > current ? riseSeconds : fallSeconds));
}
