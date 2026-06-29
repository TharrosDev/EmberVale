namespace Embervale.Magic;

/// <summary>
/// Pure spell-rank maths. Godot-free so the upgrade curve is unit-testable; <see cref="SpellcastingComponent"/>
/// applies it. Rank 1 (known) is the base 1×; each rank above adds <c>damagePerRank</c>.
/// </summary>
public static class SpellMastery
{
    /// <summary>Damage/healing multiplier for a spell at <paramref name="rank"/> (1 = base).</summary>
    public static float DamageMultiplier(int rank, float damagePerRank) =>
        1f + (System.Math.Max(0, rank - 1) * damagePerRank);
}
