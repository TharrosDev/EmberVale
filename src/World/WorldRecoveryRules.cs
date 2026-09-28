using System;

namespace Embervale.World;

/// <summary>
/// Pure rules behind <see cref="WorldRecovery"/>: when a fall has left the world, which way a
/// recovered player faces, and how the recovery fade runs. Godot-free so the unit suite drives it.
/// </summary>
public static class WorldRecoveryRules
{
    /// <summary>
    /// Has the player fallen through the world? True when the feet are more than
    /// <paramref name="fallThroughDepth"/> below the terrain AND there is no floor under them
    /// (<paramref name="hasFloorBelow"/>, a physics probe), or unconditionally below
    /// <paramref name="killFloorY"/>.
    ///
    /// ⚠️ <b>BELOW THE HEIGHTFIELD IS NOT ENOUGH ON ITS OWN.</b> The heightfield is a surface with no
    /// underside; anything built under it — a cellar, a crypt, a tunnel — is legitimately below it.
    /// The floor probe is what tells a crypt from a void.
    /// </summary>
    public static bool IsFallThrough(
        float feetY, float terrainY, bool hasFloorBelow, float fallThroughDepth, float killFloorY)
    {
        if (float.IsNaN(feetY) || feetY < killFloorY)
        {
            return true;
        }
        return feetY < terrainY - fallThroughDepth && !hasFloorBelow;
    }

    /// <summary>
    /// The body yaw (radians about +Y) that faces from <paramref name="hazard"/> towards and past
    /// <paramref name="target"/> — away from the water or pit the player was pulled out of. Godot's
    /// forward is −Z, so a yaw of 0 faces −Z. Returns null when the two points coincide
    /// horizontally, where "away" has no direction and the caller keeps the current facing.
    /// </summary>
    public static float? FacingAwayYaw(float hazardX, float hazardZ, float targetX, float targetZ)
    {
        float dx = targetX - hazardX;
        float dz = targetZ - hazardZ;
        if ((dx * dx) + (dz * dz) < 0.01f)
        {
            return null;
        }
        return MathF.Atan2(-dx, -dz);
    }

    /// <summary>
    /// Overlay opacity at <paramref name="elapsed"/> seconds into a recovery fade: up to black over
    /// <paramref name="fadeOut"/>, then back down over <paramref name="fadeIn"/>. The teleport lands
    /// at the peak (<see cref="IsAtPeak"/>), so the player never sees themselves jump.
    /// </summary>
    public static float FadeAlpha(float elapsed, float fadeOut, float fadeIn)
    {
        if (elapsed <= 0f)
        {
            return 0f;
        }
        if (elapsed < fadeOut)
        {
            return elapsed / fadeOut;
        }
        float back = elapsed - fadeOut;
        return back >= fadeIn ? 0f : 1f - (back / fadeIn);
    }

    /// <summary>Has the fade reached black (the moment to move the player)?</summary>
    public static bool IsAtPeak(float elapsed, float fadeOut) => elapsed >= fadeOut;

    /// <summary>Is the whole fade over?</summary>
    public static bool IsFinished(float elapsed, float fadeOut, float fadeIn) =>
        elapsed >= fadeOut + MathF.Max(0f, fadeIn);
}
