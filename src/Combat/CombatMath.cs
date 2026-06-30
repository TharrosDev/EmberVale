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

    /// <summary>How much of the caster's SpellPower is added per spell cast.</summary>
    private const float SpellScaling = 0.6f;

    /// <summary>How much of the caster's Intelligence is added per spell cast (Phase 29.5C) — magic's
    /// secondary scaling attribute alongside the gear-driven SpellPower.</summary>
    private const float IntelligenceScaling = 0.2f;

    /// <summary>Builds the outgoing damage amount and rolls for a critical hit.</summary>
    public static (float Amount, bool IsCrit) RollAttack(float baseDamage, StatsComponent? attacker)
    {
        float amount = ScaleDamage(baseDamage, attacker?.GetValue(StatType.PhysicalPower) ?? 0f, PowerScaling);
        return RollCrit(amount, attacker);
    }

    /// <summary>The offensive scaling behind every hit and cast: a flat base plus a share of the
    /// source's power stat (<c>base + power × scaling</c>). Pure (Godot-free) so the damage formula is
    /// unit-testable apart from the crit roll. Both <see cref="RollAttack"/> (PhysicalPower) and
    /// <see cref="RollSpell"/> (SpellPower) route through it.</summary>
    public static float ScaleDamage(float baseDamage, float power, float scaling)
    {
        return baseDamage + (power * scaling);
    }

    /// <summary>
    /// Attacker-side spell roll: base + SpellPower scaling + an Intelligence share + crit. The mirror
    /// of <see cref="RollAttack"/> for magic — spells scale off SpellPower (gear) the way melee scales
    /// off PhysicalPower, plus off Intelligence (the caster's magic attribute, Phase 29.5C).
    /// </summary>
    public static (float Amount, bool IsCrit) RollSpell(float baseDamage, StatsComponent? caster)
    {
        float amount = ScaleDamage(baseDamage, caster?.GetValue(StatType.SpellPower) ?? 0f, SpellScaling)
            + ((caster?.GetValue(StatType.Intelligence) ?? 0f) * IntelligenceScaling);
        return RollCrit(amount, caster);
    }

    /// <summary>Rolls a critical hit against the source's crit stats, scaling the amount.</summary>
    private static (float Amount, bool IsCrit) RollCrit(float amount, StatsComponent? source)
    {
        bool isCrit = false;
        float critChance = source?.GetValue(StatType.CritChance) ?? 0f;
        if (GD.Randf() < critChance)
        {
            isCrit = true;
            float critDamage = source?.GetValue(StatType.CritDamage) ?? 1.5f;
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
            amount *= ArmorMultiplier(defender.GetValue(StatType.Armor));
        }

        return amount;
    }

    /// <summary>The physical-damage multiplier from armor: the classic <c>100 / (100 + armor)</c>
    /// curve — diminishing returns, always in (0, 1], never full immunity. Negative armor clamps to
    /// no reduction (×1). Pure (Godot-free) so the load-bearing defence formula is unit-testable.</summary>
    public static float ArmorMultiplier(float armor)
    {
        float clamped = armor < 0f ? 0f : armor;
        return 100f / (100f + clamped);
    }
}
