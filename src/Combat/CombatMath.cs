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
        float critChance = ClampCritChance(source?.GetValue(StatType.CritChance) ?? 0f);
        if (GD.Randf() < critChance)
        {
            isCrit = true;
            amount *= ClampCritMultiplier(source?.GetValue(StatType.CritDamage) ?? 1.5f);
        }

        return (amount, isCrit);
    }

    /// <summary>Highest crit chance any build reaches. A crit that is nearly guaranteed is not a crit,
    /// it is a second damage stat that hides its own tuning.</summary>
    public const float MaxCritChance = 0.75f;

    /// <summary>Crit chance clamped to 0..<see cref="MaxCritChance"/>.</summary>
    public static float ClampCritChance(float chance) =>
        chance < 0f ? 0f : chance > MaxCritChance ? MaxCritChance : chance;

    /// <summary>Crit damage multiplier clamped to 1.25..4: a crit always reads as one, and never
    /// one-shots by stat stacking alone.</summary>
    public static float ClampCritMultiplier(float multiplier) =>
        multiplier < 1.25f ? 1.25f : multiplier > 4f ? 4f : multiplier;

    /// <summary>
    /// Reduces incoming damage by the defender's mitigation. Physical damage uses the classic armor
    /// curve <c>100 / (100 + armor)</c> (diminishing returns, never reaching full immunity); every
    /// other school runs its own resistance stat through the same curve (Phase 34E), so a fire
    /// elemental shrugs off fire exactly the way armour shrugs off steel.
    /// <see cref="DamageType.True"/> always bypasses.
    /// </summary>
    public static float Mitigate(float amount, DamageType type, StatsComponent? defender)
    {
        if (defender == null || type == DamageType.True)
        {
            return amount;
        }

        // One curve for both, so there is only ever one defence formula to balance. Resistance
        // only, never immunity: a positive value stays in (0, 1], which keeps every magic school a
        // viable spine to build around (DESIGN §"none a trap"). A negative value is a vulnerability
        // and amplifies, bounded below double (MitigationMultiplier).
        StatType mitigator = ResistanceStat(type);
        return amount * MitigationMultiplier(defender.GetValue(mitigator));
    }

    /// <summary>The smallest a landed, unblocked hit can be after mitigation. Resistance is never
    /// immunity, and a hit that rounds to nothing tells the player nothing.</summary>
    public const float MinimumHit = 1f;

    /// <summary>
    /// The chip floor: a hit that would have done damage before mitigation does at least
    /// <see cref="MinimumHit"/> (or all of it, if it was smaller) unless a guard took it. True damage
    /// is already unmitigated, so the floor never touches it.
    /// </summary>
    public static float FloorHit(float mitigated, float raw, bool blocked)
    {
        if (blocked || raw <= 0f)
        {
            return mitigated;
        }

        float floor = raw < MinimumHit ? raw : MinimumHit;
        return mitigated < floor ? floor : mitigated;
    }

    /// <summary>
    /// The signed mitigation curve. At or above zero it is <see cref="ArmorMultiplier"/>. Below zero
    /// (a vulnerability: a curse, a sundered armour, a frost creature's fire resistance) it mirrors the
    /// curve upward, <c>2 - 100 / (100 - x)</c>, so weakness amplifies damage toward but never past
    /// double, with the same diminishing shape as resistance.
    /// </summary>
    public static float MitigationMultiplier(float value) =>
        value >= 0f ? ArmorMultiplier(value) : 2f - (100f / (100f - value));

    /// <summary>The stat that mitigates a damage school. Physical answers to <c>Armor</c>; the magic
    /// schools each answer to their own resistance (Phase 34E). <see cref="DamageType.True"/> never
    /// reaches here — <see cref="Mitigate"/> returns before the lookup.</summary>
    public static StatType ResistanceStat(DamageType type) => type switch
    {
        DamageType.Fire => StatType.FireResist,
        DamageType.Frost => StatType.FrostResist,
        DamageType.Lightning => StatType.LightningResist,
        DamageType.Arcane => StatType.ArcaneResist,
        DamageType.Nature => StatType.NatureResist,
        DamageType.Necrotic => StatType.NecroticResist,
        _ => StatType.Armor,
    };

    /// <summary>
    /// Poise damage actually taken from one hit (Phase 36C). A blocked hit still chips poise, by
    /// <paramref name="blockFactor"/>, so a held guard can be broken into a stagger. On top of that,
    /// a defender caught in its own attack wind-up takes <paramref name="windupMultiplier"/> times
    /// as much — the tuning knob that makes a big telegraphed swing something to punish rather than
    /// merely dodge. A multiplier of 1 leaves the pre-36C behaviour exactly as it was.
    /// </summary>
    public static float PoiseDamage(float poiseDamage, bool blocked, float blockFactor, float windupMultiplier)
    {
        float amount = blocked ? poiseDamage * blockFactor : poiseDamage;
        return amount * System.Math.Max(0f, windupMultiplier);
    }

    /// <summary>The mitigation multiplier from a defence stat: the classic <c>100 / (100 + x)</c>
    /// curve — diminishing returns, always in (0, 1], never full immunity. A negative value clamps to
    /// no reduction (×1). Named for armour, but <see cref="Mitigate"/> runs every school's resistance
    /// through it too (Phase 34E), so there is one defence formula to balance rather than seven.
    /// Pure (Godot-free) so the load-bearing formula is unit-testable.</summary>
    public static float ArmorMultiplier(float armor)
    {
        float clamped = armor < 0f ? 0f : armor;
        return 100f / (100f + clamped);
    }
}
