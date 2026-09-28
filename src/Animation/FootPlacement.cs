using Godot;

namespace Embervale.Animation;

/// <summary>
/// Pure foot-placement arithmetic: given where a foot is and where the ground under it actually is,
/// how far to lift it and how far to drop the pelvis.
///
/// <para>Godot-free so the rules that matter — the limits, the smoothing, the disable conditions —
/// are testable without an engine. <see cref="FootIkComponent"/> does the raycasts and writes the
/// bones.</para>
/// </summary>
public static class FootPlacement
{
    /// <summary>
    /// How far a foot should move vertically to meet the ground, clamped.
    ///
    /// <see cref="FootIkComponent"/> passes the BODY's floor as <paramref name="footY"/>, not the
    /// animated foot: the answer is then how much higher or lower this foot's ground is than the
    /// ground the animation was authored on, and a foot mid-swing keeps its swing on top of it.
    ///
    /// ⚠️ <b>The clamp is what stops the fix being worse than the defect.</b> An unclamped
    /// correction follows a raycast down a cliff edge and stretches the leg to the valley floor, or
    /// snaps it to a passing physics body. Beyond the limit the honest answer is "this foot is not
    /// on this ground", and the correction fades out rather than reaching.
    /// </summary>
    public static float FootLift(float footY, float groundY, float maxLift, float maxDrop)
    {
        float delta = groundY - footY;
        return delta > 0f ? Mathf.Min(delta, maxLift) : Mathf.Max(delta, -maxDrop);
    }

    /// <summary>
    /// How far the pelvis drops so the LOWER foot can reach its ground without the other leg
    /// hyperextending.
    ///
    /// On a slope the two feet want different heights; lifting only the low one stretches that leg
    /// straight. Dropping the hips by the deepest required drop keeps both knees bent, which is what
    /// makes a character stand on a hillside rather than tiptoe down it.
    /// </summary>
    public static float PelvisDrop(float leftLift, float rightLift, float maxDrop)
    {
        float lowest = Mathf.Min(leftLift, rightLift);
        return lowest >= 0f ? 0f : Mathf.Max(lowest, -maxDrop);
    }

    /// <summary>
    /// Whether foot placement should be applied at all this frame.
    ///
    /// ⚠️ <b>Airborne is the important one.</b> A jumping character has no ground under it worth
    /// meeting, and a correction that keeps reaching down turns a jump into a stretch. Root-motion
    /// and warping actions are excluded for the same reason: something else owns the body's
    /// position that frame and IK fighting it produces a shimmer.
    ///
    /// <para><b>Mounted and dashing</b> are the same rule from the other side. A rider's legs are
    /// posed around a saddle, and a ray from a stirrup finds the road a metre below it; a dodge roll
    /// tumbles the whole body through the ground plane, and planting a foot mid-tumble snaps the leg
    /// straight for a frame. Both belong to the pose, not to the ground.</para>
    /// </summary>
    public static bool ShouldPlace(bool grounded, bool acting, bool visible, float distanceToCamera,
        float maxDistance, bool mounted = false, bool dashing = false) =>
        grounded && !acting && !mounted && !dashing && visible && distanceToCamera <= maxDistance;

    /// <summary>
    /// Frame-rate-independent easing toward <paramref name="target"/>: the fraction closed per second
    /// is fixed by <paramref name="sharpness"/> whatever the frame time.
    ///
    /// ⚠️ <b>This replaces a flat <c>Lerp(current, target, 0.4)</c> per frame</b>, which settled
    /// twice as fast at 120 fps as at 60 and, at a hitch, not at all. A foot stepping onto a stair
    /// edge wants its correction to arrive over a few frames rather than in one, or the ankle pops;
    /// the same easing on every foot and on the pelvis is what keeps the two from disagreeing.
    /// </summary>
    public static float Smooth(float current, float target, float sharpness, float delta)
    {
        if (sharpness <= 0f || delta <= 0f)
        {
            return sharpness <= 0f ? target : current;
        }

        return current + ((target - current) * (1f - Mathf.Exp(-sharpness * delta)));
    }

    /// <summary>
    /// How planted a foot is, 1 on the ground and 0 in the swing, from its animated height above the
    /// sole line. Only a planted foot is laid flat on the slope: tilting a foot that is mid-stride to
    /// the ground under it makes the toe dig in on every uphill step.
    /// </summary>
    public static float Planted(float heightAboveSole, float plantedBelow, float liftedAbove)
    {
        if (liftedAbove <= plantedBelow)
        {
            return heightAboveSole <= plantedBelow ? 1f : 0f;
        }

        float t = Mathf.Clamp((heightAboveSole - plantedBelow) / (liftedAbove - plantedBelow), 0f, 1f);
        return 1f - (t * t * (3f - (2f * t)));
    }

    /// <summary>
    /// Analytic two-bone IK for one leg: the rotations that bring the foot from
    /// <paramref name="foot"/> to <paramref name="target"/> while keeping both bone lengths and the
    /// knee in the plane it was animated in.
    ///
    /// <para><b>Why a real solve.</b> The first version of this component moved the foot BONE up and
    /// down on its own, which detached the ankle from the shin — at a 20 cm correction the boot
    /// floated beside a leg that had not moved. Rotating the thigh and the shin is the only honest
    /// way to put a foot somewhere else.</para>
    ///
    /// <para>Returned as world-frame (skeleton-space) delta rotations: the thigh's global rotation
    /// becomes <c>upper * thigh</c>, and the shin's becomes <c>upper * lower * shin</c>. The target is
    /// clamped to the leg's reach, so an unreachable target straightens the leg toward it rather than
    /// tearing it. <paramref name="pole"/> — the way the knee points — is only used when the leg is
    /// animated dead straight and the bend plane is undefined. False when the chain is degenerate,
    /// and the caller then leaves the leg alone.</para>
    /// </summary>
    public static bool SolveTwoBone(Vector3 hip, Vector3 knee, Vector3 foot, Vector3 target,
        Vector3 pole, out Quaternion upper, out Quaternion lower)
    {
        upper = Quaternion.Identity;
        lower = Quaternion.Identity;

        Vector3 thigh = knee - hip;
        Vector3 shin = foot - knee;
        Vector3 toFoot = foot - hip;
        Vector3 toTarget = target - hip;
        float lab = thigh.Length();
        float lcb = shin.Length();
        if (lab < 1e-5f || lcb < 1e-5f || toFoot.LengthSquared() < 1e-10f ||
            toTarget.LengthSquared() < 1e-10f)
        {
            return false;
        }

        // Never fully straight and never folded flat: both extremes make the knee's direction
        // undefined, and the frame after that is a knee pointing backwards.
        float reach = lab + lcb;
        float lat = Mathf.Clamp(toTarget.Length(), Mathf.Abs(lab - lcb) + (reach * 0.001f), reach * 0.999f);

        Vector3 ac = toFoot.Normalized();
        Vector3 ab = thigh / lab;
        Vector3 bc = shin / lcb;
        Vector3 at = toTarget.Normalized();

        float acAb0 = SafeAcos(ac.Dot(ab));
        float baBc0 = SafeAcos((-ab).Dot(bc));
        float acAt0 = SafeAcos(ac.Dot(at));
        float acAb1 = SafeAcos(((lcb * lcb) - (lab * lab) - (lat * lat)) / (-2f * lab * lat));
        float baBc1 = SafeAcos(((lat * lat) - (lab * lab) - (lcb * lcb)) / (-2f * lab * lcb));

        Vector3 axis0 = ac.Cross(ab);
        if (axis0.LengthSquared() < 1e-8f)
        {
            axis0 = ac.Cross(pole);
        }

        if (axis0.LengthSquared() < 1e-8f)
        {
            return false;
        }

        axis0 = axis0.Normalized();
        Quaternion r0 = new(axis0, acAb1 - acAb0);
        Quaternion r1 = new(axis0, baBc1 - baBc0);

        Vector3 axis1 = ac.Cross(at);
        Quaternion r2 = axis1.LengthSquared() < 1e-10f
            ? Quaternion.Identity
            : new Quaternion(axis1.Normalized(), acAt0);

        upper = (r2 * r0).Normalized();
        lower = r1.Normalized();
        return true;
    }

    private static float SafeAcos(float cosine) => Mathf.Acos(Mathf.Clamp(cosine, -1f, 1f));

    /// <summary>
    /// The eased weight for this frame — the fade that keeps the correction from popping on and off
    /// as a character leaves the ground or walks out of range.
    /// </summary>
    public static float StepWeight(float current, bool wanted, float delta, float seconds)
    {
        if (seconds <= 0f)
        {
            return wanted ? 1f : 0f;
        }

        float target = wanted ? 1f : 0f;
        return Mathf.MoveToward(current, target, delta / seconds);
    }

    /// <summary>
    /// The rotation that lays a foot flat on a slope, limited so a steep face does not snap the
    /// ankle past what a leg can do.
    /// </summary>
    public static Basis AlignToSlope(Basis current, Vector3 normal, float maxDegrees, float weight)
    {
        Quaternion tilt = SlopeRotation(current.Y, normal, maxDegrees, weight);
        return tilt.IsEqualApprox(Quaternion.Identity) ? current : new Basis(tilt) * current;
    }

    /// <summary>
    /// The rotation that tips <paramref name="up"/> toward the ground <paramref name="normal"/>,
    /// limited to <paramref name="maxDegrees"/> and scaled by <paramref name="weight"/>. It is a
    /// delta applied in front of a foot's animated rotation, so it works whatever axis a rig's foot
    /// bone happens to point along — a Mixamo-profile foot's own Y runs toward the toes, not up.
    /// </summary>
    public static Quaternion SlopeRotation(Vector3 up, Vector3 normal, float maxDegrees, float weight)
    {
        if (weight <= 0f || normal.LengthSquared() <= 0.0001f || up.LengthSquared() <= 0.0001f)
        {
            return Quaternion.Identity;
        }

        Vector3 from = up.Normalized();
        Vector3 target = normal.Normalized();
        float angle = from.AngleTo(target);
        Vector3 axis = from.Cross(target);
        if (angle <= 0.0001f || axis.LengthSquared() <= 0.000001f)
        {
            return Quaternion.Identity;
        }

        float applied = Mathf.Min(angle, Mathf.DegToRad(maxDegrees)) * Mathf.Clamp(weight, 0f, 1f);
        return new Quaternion(axis.Normalized(), applied);
    }
}
