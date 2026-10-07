namespace Embervale.Enemies;

/// <summary>What a caster wants to do with its feet this tick, given how close the target is.</summary>
public enum CasterMove
{
    /// <summary>Close the gap — the target is out of casting range.</summary>
    Approach,

    /// <summary>Hold ground and cast — the target sits in the comfortable band.</summary>
    Hold,

    /// <summary>Back away while casting — the target is too close (kiting).</summary>
    Kite,
}

/// <summary>
/// The pure positioning brain for an enemy caster (Phase 29.5F): keep the target inside the band
/// [<c>kiteDistance</c>, <c>castRange</c>] — approach when too far, kite when too close, hold otherwise.
/// Godot-free so the kiting logic is unit-testable apart from the navmesh/locomotion it drives in
/// <see cref="EnemyAIComponent"/>.
/// </summary>
public static class CasterDecision
{
    public static CasterMove Move(float distance, float kiteDistance, float castRange)
    {
        if (distance < kiteDistance)
        {
            return CasterMove.Kite;
        }

        return distance > castRange ? CasterMove.Approach : CasterMove.Hold;
    }

    /// <summary>
    /// The pitch, in radians, that points a cast origin at a target <paramref name="rise"/> metres
    /// above it (negative below) and <paramref name="flat"/> metres away on the level, held within
    /// <paramref name="maxPitch"/> either way. Positive tilts up. A target directly above or below
    /// (no level distance to speak of) gets a level aim rather than a vertical one.
    /// </summary>
    public static float AimPitch(float rise, float flat, float maxPitch)
    {
        if (flat < 0.25f || !float.IsFinite(rise) || !float.IsFinite(flat))
        {
            return 0f;
        }

        float limit = System.Math.Abs(maxPitch);
        return System.Math.Clamp(System.MathF.Atan2(rise, flat), -limit, limit);
    }
}
