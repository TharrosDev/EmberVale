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
    /// </summary>
    public static bool ShouldPlace(bool grounded, bool acting, bool visible, float distanceToCamera,
        float maxDistance) =>
        grounded && !acting && visible && distanceToCamera <= maxDistance;

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
        if (weight <= 0f || normal.LengthSquared() <= 0.0001f)
        {
            return current;
        }

        Vector3 up = current.Y.Normalized();
        Vector3 target = normal.Normalized();
        float angle = up.AngleTo(target);
        if (angle <= 0.0001f)
        {
            return current;
        }

        float limit = Mathf.DegToRad(maxDegrees);
        float applied = Mathf.Min(angle, limit) * Mathf.Clamp(weight, 0f, 1f);
        Vector3 axis = up.Cross(target);
        return axis.LengthSquared() <= 0.000001f
            ? current
            : new Basis(axis.Normalized(), applied) * current;
    }
}
