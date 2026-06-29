namespace Embervale.Combat;

/// <summary>
/// Pure tuning maths for <b>camera shake</b> (Phase 29B): the trauma model. Combat states add trauma;
/// the visible amplitude is trauma² (so light hits barely nudge and big ones snap), and trauma decays
/// back to zero over time. Kept Godot-free so the feel curve is unit-testable; <see cref="CameraShake"/>
/// turns the amplitude into camera offset/roll. All knobs live here — tweak and re-run to retune.
/// </summary>
public static class ShakeMath
{
    /// <summary>Trauma added per combat state (clamped to 1). Crit hits hardest, a block softest.</summary>
    public const float CritTrauma = 0.7f;
    public const float StaggerTrauma = 0.55f;
    public const float BlockTrauma = 0.3f;

    /// <summary>Trauma bled off per second.</summary>
    public const float DecayPerSecond = 1.6f;

    /// <summary>Peak camera offset (metres) and roll (radians) at full trauma.</summary>
    public const float MaxOffset = 0.18f;
    public const float MaxRoll = 0.05f;

    /// <summary>Visible shake amplitude (0..1) for a trauma level — quadratic for a snappy feel.</summary>
    public static float Amplitude(float trauma)
    {
        float t = trauma < 0f ? 0f : trauma > 1f ? 1f : trauma;
        return t * t;
    }

    /// <summary>Adds trauma, clamped to [0, 1].</summary>
    public static float Add(float trauma, float amount)
    {
        float t = trauma + amount;
        return t < 0f ? 0f : t > 1f ? 1f : t;
    }

    /// <summary>Bleeds trauma toward 0 over <paramref name="dt"/> seconds, never below 0.</summary>
    public static float Decay(float trauma, float dt)
    {
        float t = trauma - (DecayPerSecond * dt);
        return t < 0f ? 0f : t;
    }
}
