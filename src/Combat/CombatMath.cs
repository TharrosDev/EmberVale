using Embervale.Stats;
using Godot;

namespace Embervale.Combat;

/// <summary>
/// Pure combat formulas, kept separate from the components that use them so they
/// can be unit-reasoned and tuned in one place. Two clear sides:
///   * <see cref="RollAttack"/> — attacker-side: base + power scaling + crit roll.
///   * <see cref="Mitigate"/>   — defender-side: armor / resistance reduction.
/// </summary>
public static class CombatMath
{
    /// <summary>How much of the attacker's PhysicalPower is added per swing.</summary>
    private const float PowerScaling = 0.5f;

    /// <summary>Builds the outgoing damage amount and rolls for a critical hit.</summary>
    public static (float Amount, bool IsCrit) RollAttack(float baseDamage, StatsComponent? attacker)
    {
        float amount = baseDamage;
        if (attacker != null)
        {
            amount += attacker.GetValue(StatType.PhysicalPower) * PowerScaling;
        }

        bool isCrit = false;
        float critChance = attacker?.GetValue(StatType.CritChance) ?? 0f;
        if (GD.Randf() < critChance)
        {
            isCrit = true;
            float critDamage = attacker?.GetValue(StatType.CritDamage) ?? 1.5f;
            amount *= critDamage;
        }

        return (amount, isCrit);
    }

    /// <summary>
    /// Reduces incoming damage by the defender's mitigation. Physical damage uses
    /// the classic armor curve <c>100 / (100 + armor)</c> (diminishing returns,
    /// never reaching full immunity). Elemental types are unmitigated until
    /// per-type resistances arrive; <see cref="DamageType.True"/> always bypasses.
    /// </summary>
    public static float Mitigate(float amount, DamageType type, StatsComponent? defender)
    {
        if (defender == null || type == DamageType.True)
        {
            return amount;
        }

        if (type == DamageType.Physical)
        {
            float armor = Mathf.Max(0f, defender.GetValue(StatType.Armor));
            amount *= 100f / (100f + armor);
        }

        return amount;
    }
}
