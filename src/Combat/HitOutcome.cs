using Embervale.Combat.Actions;

namespace Embervale.Combat;

/// <summary>
/// What a resolved blow <em>was</em>, from the point of view of everything that presents it (hit-stop,
/// sparks, the screen flash, floating numbers, the mesh lurch). One outcome per blow, so every layer
/// agrees on it instead of each re-deriving "was that a crit or a block?" from raw flags.
/// <b>Append-only.</b>
/// </summary>
public enum HitOutcome
{
    /// <summary>An ordinary blow that landed.</summary>
    Hit = 0,

    /// <summary>The defender's guard took it: chip damage only.</summary>
    Blocked = 1,

    /// <summary>A timed block negated it and staggered the attacker.</summary>
    Parried = 2,

    /// <summary>The guard gave out under the blow (no stamina, or a heavy blow).</summary>
    GuardBroken = 3,

    /// <summary>A critical strike: a rolled crit, a riposte or a backstab.</summary>
    Critical = 4,

    /// <summary>The blow broke the defender's poise and staggered it.</summary>
    PoiseBroken = 5,

    /// <summary>The defender's resistance ate most of the blow.</summary>
    Resisted = 6,
}

/// <summary>Everything noted about one target during one resolution, in whatever order the events
/// arrived. <see cref="HitOutcomes.Resolve"/> turns it into a single <see cref="HitOutcome"/>.</summary>
public readonly record struct HitFacts(
    bool HasDamage,
    bool Crit,
    bool Blocked,
    bool GuardBroken,
    bool Staggered,
    bool Parried,
    bool Resisted);

/// <summary>Pure rules that turn raw combat facts into one <see cref="HitOutcome"/> and infer the kind
/// of blow. Godot-free so the precedence is unit-tested rather than read off the director.</summary>
public static class HitOutcomes
{
    /// <summary>A defence multiplier at or below this reads as "resisted" (the defender took 60% or
    /// less of the blow).</summary>
    public const float ResistedMultiplier = 0.6f;

    /// <summary>
    /// The one outcome that names a blow. Precedence, strongest first: a parry (the attacker lost the
    /// exchange), a guard break, a critical, a poise break, a block, a resist, an ordinary hit. A
    /// crit that also staggers reads as the crit (the stagger travels as a flag on the event).
    /// </summary>
    public static HitOutcome Resolve(in HitFacts facts)
    {
        if (facts.Parried)
        {
            return HitOutcome.Parried;
        }

        if (facts.GuardBroken)
        {
            return HitOutcome.GuardBroken;
        }

        if (facts.Crit)
        {
            return HitOutcome.Critical;
        }

        if (facts.Staggered)
        {
            return HitOutcome.PoiseBroken;
        }

        if (facts.Blocked)
        {
            return HitOutcome.Blocked;
        }

        return facts.Resisted ? HitOutcome.Resisted : HitOutcome.Hit;
    }

    /// <summary>Whether a defence multiplier (<c>100/(100+x)</c>, in (0, 1]) is low enough to call the
    /// blow resisted.</summary>
    public static bool IsResisted(float multiplier) => multiplier <= ResistedMultiplier;

    /// <summary>
    /// The weight class of a blow. A declared kind (a riposte or backstab from the critical event)
    /// wins; otherwise it is read off the attacker's last released action, so a heavy swing weighs more
    /// than a light one and a bolt weighs less, without <c>DamageDealtEvent</c> carrying a kind.
    /// </summary>
    public static HitKind InferKind(HitKind declared, ActionKind? lastAction, float charge)
    {
        if (declared != HitKind.Normal)
        {
            return declared;
        }

        if (charge > 0.05f)
        {
            return HitKind.Charged;
        }

        return lastAction switch
        {
            ActionKind.HeavyAttack => HitKind.Heavy,
            ActionKind.Ranged => HitKind.Ranged,
            ActionKind.Cast => HitKind.Spell,
            _ => HitKind.Normal,
        };
    }

    /// <summary>How much a blow of this kind weighs in hit-stop and in the mesh lurch (1 = a light swing).</summary>
    public static float KindWeight(HitKind kind) => kind switch
    {
        HitKind.Heavy => 1.35f,
        HitKind.Charged => 1.6f,
        HitKind.Riposte => 1.6f,
        HitKind.Backstab => 1.3f,
        HitKind.Plunge => 1.45f,
        HitKind.Ranged => 0.5f,
        HitKind.Spell => 0.4f,
        _ => 1f,
    };
}
