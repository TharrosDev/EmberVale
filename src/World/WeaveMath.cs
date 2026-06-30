namespace Embervale.World;

/// <summary>
/// Pure maths for the fading <b>Weave</b> (Phase 29.5E): how a region's magic <em>potency</em>
/// (0 = the Weave is dead here, 1 = it flows full) bends the cost and power of a cast. Godot-free so the
/// dying-world dial is tunable and unit-testable in one place; <see cref="Weave"/> holds the live value
/// and <see cref="Embervale.Magic.SpellcastingComponent"/> applies it.
///
/// The world is dying, so as potency falls:
///   * <b>ordinary</b> magic weakens and costs more — the Weave no longer answers freely;
///   * <b>corrupted</b> magic (a spell gated above <c>Untainted</c>) does the opposite — it grows
///     stronger and cheaper, because the darker path feeds on the world's death. Temptation made
///     mechanical (the 23H corruption interplay).
/// </summary>
public static class WeaveMath
{
    // Ordinary casting, at a dead Weave (potency 0): weaker and pricier than at full potency (1).
    private const float NormalPowerAtZero = 0.5f;
    private const float NormalCostAtZero = 1.5f;

    // Corrupted casting, at a dead Weave: stronger and cheaper — the dying world empowers it.
    private const float CorruptPowerAtZero = 1.4f;
    private const float CorruptCostAtZero = 0.6f;

    /// <summary>The damage/healing multiplier a cast gets at <paramref name="potency"/>.</summary>
    public static float PowerMultiplier(float potency, bool corrupted)
    {
        float p = Clamp01(potency);
        return corrupted ? Lerp(CorruptPowerAtZero, 1f, p) : Lerp(NormalPowerAtZero, 1f, p);
    }

    /// <summary>The mana-cost multiplier a cast pays at <paramref name="potency"/>.</summary>
    public static float CostMultiplier(float potency, bool corrupted)
    {
        float p = Clamp01(potency);
        return corrupted ? Lerp(CorruptCostAtZero, 1f, p) : Lerp(NormalCostAtZero, 1f, p);
    }

    private static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);

    private static float Lerp(float from, float to, float t) => from + ((to - from) * t);
}
