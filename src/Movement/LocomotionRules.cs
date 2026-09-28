using System;

namespace Embervale.Movement;

/// <summary>
/// The speed arithmetic of the ground motor — gait and slope — pure, Godot-free, unit-tested.
/// <see cref="LocomotionComponent"/> multiplies the <c>MoveSpeed</c> stat by these.
/// </summary>
public static class LocomotionRules
{
    /// <summary>
    /// The gait multiplier. Sprint wins over walk, as Skyrim's does: a player in walk mode who holds
    /// sprint is asking to run, and making them untoggle first is the kind of friction nobody notices
    /// until it gets them killed.
    /// </summary>
    public static float GaitScale(bool sprinting, bool walking, float sprintMultiplier, float walkMultiplier) =>
        sprinting ? sprintMultiplier : walking ? walkMultiplier : 1f;

    /// <summary>
    /// The uphill speed multiplier for a body moving along (<paramref name="dirX"/>,
    /// <paramref name="dirZ"/>) over a floor with the given unit normal: 1 on the flat and downhill,
    /// falling linearly to <c>1 - penaltyAtMax</c> at <paramref name="maxSlopeRadians"/> (the engine's
    /// <c>floor_max_angle</c>, beyond which the ground is a wall and this is not asked).
    ///
    /// ⚠️ <b>THE GRADE IS ALONG THE DIRECTION OF TRAVEL, NOT THE FLOOR'S STEEPNESS.</b> A body
    /// traversing a steep bank sideways is walking on the level and must not be slowed — the naive
    /// rule (scale by the floor angle) makes every contour path across a hillside a slog.
    /// </summary>
    public static float UphillScale(
        float normalX, float normalY, float normalZ, float dirX, float dirZ, float maxSlopeRadians, float penaltyAtMax)
    {
        float dirLength = MathF.Sqrt((dirX * dirX) + (dirZ * dirZ));
        if (!float.IsFinite(normalX) || !float.IsFinite(normalY) || !float.IsFinite(normalZ) ||
            !float.IsFinite(dirLength) || normalY <= 0.0001f || dirLength <= 0.0001f ||
            maxSlopeRadians <= 0f || penaltyAtMax <= 0f)
        {
            return 1f;
        }

        // The floor normal leans AWAY from the uphill direction, so moving uphill is moving against
        // its horizontal part. Rise per metre of horizontal travel:
        float rise = -((normalX * dirX) + (normalZ * dirZ)) / (dirLength * normalY);
        if (rise <= 0f)
        {
            return 1f;
        }

        float t = MathF.Atan(rise) / maxSlopeRadians;
        return 1f - (penaltyAtMax * (t >= 1f ? 1f : t));
    }
}
