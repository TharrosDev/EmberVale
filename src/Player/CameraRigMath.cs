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

    /// <summary>The furthest the first-person eye may sit from the fixed pivot (metres). A clip that
    /// throws the head — a knockdown, a death — must not throw the camera with it.</summary>
    public const float MaxEyeOffset = 0.45f;

    /// <summary>How much of the head's animated travel the first-person eye takes on foot: a little
    /// of the rise and fall, almost none of the sway and lean. The eye is anchored to where the head
    /// RESTS, so a sprint that leans the head forward leaves the camera behind it instead of dragging
    /// the view through every footfall.</summary>
    public const float EyeFollowVertical = 0.3f;
    public const float EyeFollowHorizontal = 0.1f;

    /// <summary>Where the neck pivots, measured from the head bone's rest position (pivot space:
    /// +Y up, -Z forward), and the arm from that pivot out to the eye. Pitch swings the arm, so
    /// looking down carries the eye forward and down over the chest the way a tilted head does.</summary>
    public static readonly Vector3 NeckOffset = new(0f, -0.03f, 0f);
    public static readonly Vector3 EyeArm = new(0f, 0.10f, -0.09f);

    /// <summary>How much further the eye is carried as the look goes down: forward, out over the
    /// chest, and back up by about what the swung arm drops it, reaching the full amount at
    /// <see cref="LookDownFullPitch"/> (<see cref="LookDownReach"/>). The arm alone left the eye
    /// 11 cm in front of the neck and 10 cm below the head bone at a straight-down look, which on
    /// the player's body is three centimetres from the collar with the chest standing 14 cm proud
    /// just beneath it: the frame was the player's own torso. The body does not bend at the waist
    /// when the player looks down, so the eye leans out instead, the way the head would.</summary>
    public const float LookDownForward = 0.24f;
    public const float LookDownRise = 0.10f;
    public const float LookDownFullPitch = 1.4835f;

    /// <summary>The front of the player's torso in the sagittal plane, measured on
    /// <c>chr_player_base</c> at rest from its head bone (up, forward, metres): the collar and neck
    /// base come out to <see cref="CollarFront"/> and start <see cref="CollarTop"/> below the bone,
    /// the chest to <see cref="ChestFront"/> from <see cref="ChestTop"/> down. Everything above the
    /// collar is the head, which is cut out. <see cref="TorsoClearance"/> measures the eye against
    /// these, and the eye's path must keep <see cref="SafeEyeClearance"/> (more than the 0.08 m near
    /// plane) from them at every pitch.</summary>
    public const float CollarTop = -0.04f;
    public const float CollarFront = 0.09f;
    public const float ChestTop = -0.17f;
    public const float ChestFront = 0.16f;
    public const float SafeEyeClearance = 0.09f;

    /// <summary>The sphere the body shader cuts out of the player's own mesh in first person:
    /// its centre measured from the animated head bone (up, forward) and its radius. Sized to take
    /// the skull and hair and leave the shoulders, so the body is still there when looking down.</summary>
    public const float HeadSphereRise = 0.05f;
    public const float HeadSphereForward = 0.03f;
    public const float HeadSphereRadius = 0.22f;

    /// <summary>The head is cut out while the camera is nearer its centre than
    /// <see cref="HeadHideWithin"/> and drawn again once it is past <see cref="HeadShowBeyond"/>.
    /// Distance rather than the view mode, so the swap out to third person shows the head as the
    /// camera leaves it and a third-person camera a wall has pushed into the skull hides it.</summary>
    public const float HeadHideWithin = 0.4f;
    public const float HeadShowBeyond = 0.5f;

    /// <summary>Fully in first person the head stays cut out to this distance instead. Looking down
    /// leans the eye out ahead of the body, and a clip can have the head a quarter of a metre behind
    /// where it rests, so the eye is routinely further from the head than
    /// <see cref="HeadShowBeyond"/>; a head that came back then would come back in the player's
    /// face. Past this the head has been thrown clear of the eye (a knockdown, a roll) and is drawn,
    /// so the player does not look at their own headless body.</summary>
    public const float FirstPersonHeadShowBeyond = 0.8f;

    /// <summary>The second cut-out, a sphere round the eye itself: nothing of the player's own body
    /// is drawn nearer the camera than the far corners of its near plane, plus
    /// <see cref="EyeSphereMargin"/>, so a collar or a shoulder a clip swings past the eye is opened
    /// cleanly instead of being sliced by the near plane and filling the frame. Held between the two
    /// limits: never so small it does nothing, and never so wide it reaches the chest the player is
    /// looking down at (about 0.18 m from the eye at a stand), which at the widest fields of view
    /// means the far corners of the near plane are left to the near plane.
    ///
    /// <para>The upper limit itself comes down to the lower one as the look goes down
    /// (<see cref="EyeSphereLimit"/>). Looking down, what is nearest the eye is the chest the player
    /// is looking at, standing a hand's width behind the view's axis: a wide sphere takes a round
    /// bite out of it at the bottom of the frame, where the near plane by itself reaches nothing
    /// that is in view.</para></summary>
    public const float EyeSphereMargin = 0.02f;
    public const float MinEyeSphereRadius = 0.12f;
    public const float MaxEyeSphereRadius = 0.2f;

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

    /// <summary>
    /// How much of the head's animated travel the eye follows, as (vertical, horizontal) fractions.
    /// On foot it is <see cref="EyeFollowVertical"/> and <see cref="EyeFollowHorizontal"/>, and
    /// nothing at all under Reduced Motion. In the saddle the seated pose is where the head IS, not a
    /// motion to damp, so the eye goes with it fully as <paramref name="mountedBlend"/> reaches 1.
    /// </summary>
    public static Vector2 EyeFollow(bool reducedMotion, float mountedBlend)
    {
        float onFootVertical = reducedMotion ? 0f : EyeFollowVertical;
        float onFootHorizontal = reducedMotion ? 0f : EyeFollowHorizontal;
        float t = Math.Clamp(mountedBlend, 0f, 1f);
        return new Vector2(Mathf.Lerp(onFootVertical, 1f, t), Mathf.Lerp(onFootHorizontal, 1f, t));
    }

    /// <summary>The part of the head's animated offset from its rest position the eye takes:
    /// <paramref name="follow"/>.X of the rise and fall, <paramref name="follow"/>.Y of the rest.</summary>
    public static Vector3 FollowDelta(Vector3 animatedDelta, Vector2 follow) => new(
        Finite(animatedDelta.X) * follow.Y,
        Finite(animatedDelta.Y) * follow.X,
        Finite(animatedDelta.Z) * follow.Y);

    /// <summary>
    /// Where the first-person eye is, in the pivot's own frame before pitch (the body's axes, origin
    /// on the pivot): the head's rest position, down to the neck, out along the pitched
    /// <see cref="EyeArm"/>, plus whatever of the animated travel is being followed.
    /// </summary>
    public static Vector3 EyeUnpitched(Vector3 restHead, float pitch, Vector3 followedDelta) =>
        restHead + NeckOffset + EyeArm.Rotated(Vector3.Right, pitch) + LookDownReach(pitch) + followedDelta;

    /// <summary>
    /// The lean the eye takes on top of the swung <see cref="EyeArm"/> as the look goes below level,
    /// in the pivot's unpitched frame: nothing at or above level, easing to
    /// <see cref="LookDownForward"/> ahead and <see cref="LookDownRise"/> up at
    /// <see cref="LookDownFullPitch"/>. Eased at both ends, so the eye does not change speed as the
    /// look crosses level or arrives at the limit.
    /// </summary>
    public static Vector3 LookDownReach(float pitch)
    {
        float t = Ease(-Finite(pitch) / LookDownFullPitch);
        return new Vector3(0f, LookDownRise * t, -LookDownForward * t);
    }

    /// <summary>
    /// How far a point is from the front of the torso (<see cref="CollarTop"/> and the constants
    /// beside it), given its offset from the head bone in the body's axes (+Y up, -Z forward).
    /// Positive is clear air in front of or above the body; negative is inside it.
    /// </summary>
    public static float TorsoClearance(Vector3 fromHead) => Math.Min(
        SlabClearance(fromHead.Y - CollarTop, -fromHead.Z - CollarFront),
        SlabClearance(fromHead.Y - ChestTop, -fromHead.Z - ChestFront));

    /// <summary>Distance to a block that fills everything below a top and behind a front, given how
    /// far the point is above the one and ahead of the other.</summary>
    private static float SlabClearance(float above, float ahead) =>
        above >= 0f && ahead >= 0f
            ? Mathf.Sqrt((above * above) + (ahead * ahead))
            : Math.Max(above, ahead);

    /// <summary>
    /// The camera's position under the pitched pivot that puts it on <see cref="EyeUnpitched"/>,
    /// held within <see cref="MaxEyeOffset"/> of the pivot. The camera is the pivot's child and the
    /// pivot carries the pitch, so the eye is turned back by the pitch to find its local seat.
    /// </summary>
    public static Vector3 EyeLocal(Vector3 restHead, float pitch, Vector3 followedDelta)
    {
        Vector3 local = EyeUnpitched(restHead, pitch, followedDelta).Rotated(Vector3.Right, -pitch);
        float length = local.Length();
        return length > MaxEyeOffset ? local * (MaxEyeOffset / length) : local;
    }

    /// <summary>Centre of the first-person head cut-out, from the animated head bone's position and
    /// the body's up and forward.</summary>
    public static Vector3 HeadSphereCentre(Vector3 head, Vector3 up, Vector3 forward) =>
        head + (up * HeadSphereRise) + (forward * HeadSphereForward);

    /// <summary>Whether the head is cut out this frame, given whether it was and how far the camera
    /// is from the cut-out's centre. The gap between the two distances stops it flickering.</summary>
    public static bool HeadHidden(bool hidden, float cameraDistance) =>
        hidden ? cameraDistance < HeadShowBeyond : cameraDistance < HeadHideWithin;

    /// <summary>The same, for a rig that knows how far out to third person it is
    /// (<paramref name="thirdBlend"/>, 0 in first person). Fully in first person the head is cut out
    /// all the way to <see cref="FirstPersonHeadShowBeyond"/>.</summary>
    public static bool HeadHidden(bool hidden, float cameraDistance, float thirdBlend) =>
        thirdBlend <= 0f ? cameraDistance < FirstPersonHeadShowBeyond : HeadHidden(hidden, cameraDistance);

    /// <summary>
    /// Radius of the cut-out round the eye for a camera with this near plane, vertical field of view
    /// (degrees) and aspect (width over height): the distance from the eye to a corner of the near
    /// plane, plus <see cref="EyeSphereMargin"/>. Derived from the camera rather than fixed, because
    /// a wide view pushes those corners out and a fixed sphere would let the near plane slice
    /// whatever sat between the two. <paramref name="pitch"/> (radians, negative down) narrows it
    /// as the look goes down, so it never reaches the chest.
    /// </summary>
    public static float EyeSphereRadius(float near, float fovDegrees, float aspect, float pitch = 0f)
    {
        float tan = Mathf.Tan(Mathf.DegToRad(Math.Clamp(Finite(fovDegrees), 1f, 170f)) * 0.5f);
        float wide = float.IsFinite(aspect) && aspect > 0f ? aspect : 16f / 9f;
        float corner = Math.Max(Finite(near), 0f) * Mathf.Sqrt(1f + (tan * tan * (1f + (wide * wide))));
        return Math.Clamp(corner + EyeSphereMargin, MinEyeSphereRadius, EyeSphereLimit(pitch));
    }

    /// <summary>The widest the cut-out round the eye may be at a pitch:
    /// <see cref="MaxEyeSphereRadius"/> at or above level, easing down to
    /// <see cref="MinEyeSphereRadius"/> at <see cref="LookDownFullPitch"/>, in step with the lean
    /// that carries the eye out over the chest (<see cref="LookDownReach"/>).</summary>
    public static float EyeSphereLimit(float pitch) => Mathf.Lerp(
        MaxEyeSphereRadius, MinEyeSphereRadius, Ease(-Finite(pitch) / LookDownFullPitch));

    /// <summary>Whether the body shader discards a point: inside the sphere round the head or the
    /// one round the eye. The same test the shader runs, for the tests and the probe.</summary>
    public static bool InCutout(Vector3 point, Vector3 headCentre, float headRadius, Vector3 eye, float eyeRadius) =>
        point.DistanceTo(headCentre) < headRadius || point.DistanceTo(eye) < eyeRadius;

    private static float Finite(float value) => float.IsFinite(value) ? value : 0f;

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
    /// Whether the rig has to tick itself this frame. A dialogue pauses the world, which stops the
    /// input router and with it the only caller of <c>Tick</c>, so the push-in toward the speaker
    /// would never run. This is true only for that case: an open dialogue, in a tree the dialogue
    /// paused, in a game that is still playing (a pause menu, a load or a game over holds the camera
    /// still, and a running tree is already ticked by the router).
    /// </summary>
    public static bool TicksWhilePaused(bool dialogueOpen, bool treePaused, bool playing) =>
        dialogueOpen && treePaused && playing;

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
