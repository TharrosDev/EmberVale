namespace Embervale.Movement;

/// <summary>
/// What a fall costs — fall damage and the landing stumble — pure, Godot-free, unit-tested.
/// <see cref="LocomotionComponent"/> measures the fall (peak height to landing height) and applies the
/// answer.
///
/// ⚠️ <b>MEASURED FROM THE PEAK, NOT FROM THE IMPACT SPEED.</b> Speed would be the more physical
/// reading, but every teleport in the game (world recovery, fast travel, a load, Blink) writes a
/// position and zeroes the velocity, and a speed-based rule would take the teleport's word for it. A
/// height is something the motor can reset when it sees the body jump several metres between frames,
/// which it does.
///
/// Skyrim is the reference: a jump off a house roof is free, a cliff is not, and there is no
/// survival layer on top — no broken legs, no limp. The damage is a slice of <em>max</em> health so it
/// scales with the character rather than becoming irrelevant by level ten.
/// </summary>
public static class FallRules
{
    /// <summary>
    /// Health lost to a fall of <paramref name="height"/> metres: nothing up to
    /// <paramref name="safeHeight"/>, then linear to the whole of <paramref name="maxHealth"/> at
    /// <paramref name="lethalHeight"/>.
    /// </summary>
    public static float Damage(float height, float safeHeight, float lethalHeight, float maxHealth)
    {
        if (!float.IsFinite(height) || !float.IsFinite(maxHealth) || maxHealth <= 0f || height <= safeHeight)
        {
            return 0f;
        }

        if (lethalHeight <= safeHeight)
        {
            return maxHealth;
        }

        float t = (height - safeHeight) / (lethalHeight - safeHeight);
        return maxHealth * (t >= 1f ? 1f : t);
    }

    /// <summary>
    /// The fall that actually lands: none at all into water at least <paramref name="cushionDepth"/>
    /// deep. There is no swimming in this game, but a leap into a deep pool is the one fall every
    /// player expects to survive, and <c>WorldRecovery</c> is what gets them out again.
    /// </summary>
    public static float Cushioned(float height, float waterDepth, float cushionDepth) =>
        float.IsFinite(waterDepth) && waterDepth >= cushionDepth ? 0f : height;

    /// <summary>
    /// How long a landing slows the body, in seconds: nothing up to <paramref name="hardHeight"/> (an
    /// ordinary jump never stumbles), then growing with the drop to <paramref name="maxSeconds"/>.
    /// </summary>
    public static float RecoverySeconds(float height, float hardHeight, float secondsPerMetre, float maxSeconds)
    {
        if (!float.IsFinite(height) || height <= hardHeight || maxSeconds <= 0f)
        {
            return 0f;
        }

        float seconds = MinimumRecovery + ((height - hardHeight) * secondsPerMetre);
        return seconds >= maxSeconds ? maxSeconds : seconds;
    }

    /// <summary>
    /// The speed multiplier <paramref name="remaining"/> seconds into a recovery of
    /// <paramref name="duration"/>: <paramref name="slowest"/> on the landing frame, easing back to 1.
    /// </summary>
    public static float RecoveryScale(float remaining, float duration, float slowest)
    {
        if (!float.IsFinite(remaining) || duration <= 0f || remaining <= 0f)
        {
            return 1f;
        }

        float left = remaining >= duration ? 1f : remaining / duration;
        return 1f - ((1f - slowest) * left);
    }

    /// <summary>A stumble shorter than this reads as a hitch in the animation rather than a landing.</summary>
    private const float MinimumRecovery = 0.2f;
}
