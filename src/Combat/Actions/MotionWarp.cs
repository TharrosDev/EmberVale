using Godot;

namespace Embervale.Combat.Actions;

/// <summary>
/// Pure motion-warping arithmetic: how far and how much to turn an actor, per frame, so a committed
/// attack lands on the target it was aimed at.
///
/// <para><b>Why warping rather than root motion.</b> Root motion reads displacement out of the clip.
/// The Meshy clips carry none — a walk's hips travel 0.4 cm across 4.2 seconds, because every action
/// in that catalogue is authored in place — so there is nothing to read. Inventing displacement and
/// calling it root motion would be inventing animation. Warping is the honest version of what root
/// motion is actually wanted for here: closing the last half metre of a lunge so the sword arrives
/// where the swing is pointing.</para>
///
/// <para><b>The whole thing is bounded, and the bounds are the design.</b> A warp that can travel
/// any distance is a teleport; one that can turn any amount is a homing missile. Both read as the
/// game cheating. <see cref="ActionDefinitionResource.MaxWarpDistance"/> and
/// <see cref="ActionDefinitionResource.MaxWarpDegrees"/> cap them per action, and the caller sweeps
/// the translation through the physics world so the actor can never warp through a wall.</para>
/// </summary>
public static class MotionWarp
{
    /// <summary>
    /// How much of the remaining gap to close this frame.
    ///
    /// The warp is spread across the STARTUP window only — it is over by the time the blow lands, so
    /// the hit is decided from a settled position rather than mid-slide. Progress past
    /// <paramref name="activeFrom"/> returns 0.
    /// </summary>
    public static float Fraction(float progress, float activeFrom, double delta, double duration)
    {
        if (progress >= activeFrom || activeFrom <= 0f || duration <= 0d)
        {
            return 0f;
        }

        // The share of the startup this frame represents. Framed as "of the time left" so a dropped
        // frame catches up rather than losing ground.
        double remaining = (activeFrom - progress) * duration;
        if (remaining <= 0d)
        {
            return 1f;
        }

        double share = delta / remaining;
        return share >= 1d ? 1f : (float)share;
    }

    /// <summary>
    /// The translation to apply this frame: toward the target, stopping short by
    /// <paramref name="reach"/>, never covering more than <paramref name="maxDistance"/> in total.
    ///
    /// Returns zero when the target is already within reach — <b>a warp must never push an actor
    /// backwards or shove one that is already in position</b>, which is what makes a crowd of
    /// attackers stay put instead of jostling.
    /// </summary>
    public static Vector3 Step(
        Vector3 from, Vector3 to, float reach, float maxDistance, float fraction)
    {
        Vector3 flat = new(to.X - from.X, 0f, to.Z - from.Z);
        float gap = flat.Length();
        if (gap <= reach || gap <= 0.0001f || fraction <= 0f || maxDistance <= 0f)
        {
            return Vector3.Zero;
        }

        float travel = Mathf.Min(gap - reach, maxDistance);
        return flat / gap * (travel * Mathf.Clamp(fraction, 0f, 1f));
    }

    /// <summary>
    /// The yaw that makes an actor face along <paramref name="flat"/> (a horizontal offset).
    ///
    /// ⚠️ <b>FORWARD IS -Z, SO IT IS <c>atan2(-x, -z)</c>.</b> Rotating -Z by yaw θ about Y gives
    /// <c>(-sin θ, 0, -cos θ)</c>. This was <c>atan2(x, z)</c> — the direction facing directly
    /// <i>away</i> from the point — and a test encoded it ("a target at +X is +90°"), so a warping
    /// swing at a target dead ahead turned the actor up to <c>MaxWarpDegrees</c> away from it.
    /// <c>AiNavigator</c> and <c>MountGait.YawOf</c> already use the correct form.
    /// </summary>
    public static float YawToFace(Vector3 flat) => Mathf.Atan2(-flat.X, -flat.Z);

    /// <summary>
    /// Whether a target is inside the cone a committed swing may still close on: within twice the
    /// action's turn allowance of where the actor already faces. A swing that lunges at someone
    /// standing behind it would have to spin on the spot, and that reads as a homing missile.
    /// </summary>
    public static bool Reachable(float currentYaw, Vector3 from, Vector3 to, float maxDegrees)
    {
        Vector3 flat = new(to.X - from.X, 0f, to.Z - from.Z);
        if (flat.LengthSquared() <= 0.0001f)
        {
            return true;
        }

        float wanted = YawToFace(flat);
        float difference = Mathf.Abs(Mathf.Wrap(wanted - currentYaw, -Mathf.Pi, Mathf.Pi));
        return difference <= Mathf.DegToRad(Mathf.Max(0f, maxDegrees) * 2f);
    }

    /// <summary>The share of an untargeted advance (a directional lunge, a back-step) to travel this
    /// frame: the whole remaining vector times <paramref name="fraction"/>, so it spends itself
    /// across the startup exactly as the warp does.</summary>
    public static Vector3 AdvanceStep(Vector3 remaining, float fraction) =>
        remaining * Mathf.Clamp(fraction, 0f, 1f);

    /// <summary>
    /// The yaw a committed action allows this frame: the change since the last tick, capped at
    /// <paramref name="degreesPerSecond"/> for <paramref name="delta"/> seconds. Returns the yaw to
    /// restore the body to. A negative allowance means unrestricted.
    /// </summary>
    public static float LimitedYaw(float lastYaw, float currentYaw, float degreesPerSecond, double delta)
    {
        if (degreesPerSecond < 0f)
        {
            return currentYaw;
        }

        float change = Mathf.Wrap(currentYaw - lastYaw, -Mathf.Pi, Mathf.Pi);
        float cap = Mathf.DegToRad(degreesPerSecond) * (float)delta;
        return lastYaw + Mathf.Clamp(change, -cap, cap);
    }

    /// <summary>
    /// The yaw change to apply this frame, in radians, capped by the action's total allowance.
    ///
    /// ⚠️ <b>The cap is per action, not per frame</b>, and that is the difference between "the swing
    /// corrects onto a target that stepped aside" and "the swing tracks a circling target through
    /// its whole animation". <paramref name="remainingDegrees"/> is what the caller has left to
    /// spend; it decrements as the action runs.
    /// </summary>
    public static float YawStep(
        float currentYaw, Vector3 from, Vector3 to, float remainingDegrees, float fraction)
    {
        if (remainingDegrees <= 0f || fraction <= 0f)
        {
            return 0f;
        }

        Vector3 flat = new(to.X - from.X, 0f, to.Z - from.Z);
        if (flat.LengthSquared() <= 0.0001f)
        {
            return 0f;
        }

        float wanted = YawToFace(flat);
        float difference = Mathf.Wrap(wanted - currentYaw, -Mathf.Pi, Mathf.Pi);
        float step = difference * Mathf.Clamp(fraction, 0f, 1f);
        float cap = Mathf.DegToRad(remainingDegrees);
        return Mathf.Clamp(step, -cap, cap);
    }
}
