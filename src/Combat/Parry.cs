namespace Embervale.Combat;

/// <summary>How well a parry was timed. Pinned by <c>EnumStabilityTests</c>: append only.</summary>
public enum ParryGrade
{
    /// <summary>Not a parry: an ordinary block, or no guard at all.</summary>
    None = 0,

    /// <summary>Just past the window: a hard deflect. The blow is nearly stopped and the attacker is
    /// barely disturbed, but there is no riposte.</summary>
    Late = 1,

    /// <summary>Inside the window: the blow is negated and the attacker is staggered and open.</summary>
    Good = 2,

    /// <summary>In the first half of the window: as Good, free of stamina, a longer stagger and the
    /// biggest riposte.</summary>
    Perfect = 3,
}

/// <summary>
/// Pure parry-timing logic (Phase 29F, graded in the combat upgrade). Godot-free so the window is
/// unit-testable; <see cref="CombatComponent"/> applies it. A hit landing within <c>window</c> seconds of
/// the guard being raised is parried; the first half of that is a perfect parry and a short grace after
/// it is a late one; otherwise it is a (chip) block.
/// </summary>
public static class Parry
{
    /// <summary>A parry inside this fraction of the window is perfect.</summary>
    public const float PerfectFraction = 0.5f;

    /// <summary>How far past the window, as a fraction of it, a late parry still deflects.</summary>
    public const float LateFraction = 0.5f;

    /// <summary>Fraction of a late-parried blow's damage that is negated (a plain block negates
    /// <c>BlockMitigation</c>, 70% by default).</summary>
    public const float LateMitigation = 0.9f;

    /// <summary>True if a guard raised <paramref name="blockElapsed"/> seconds ago parries a hit now.</summary>
    public static bool IsParry(float blockElapsed, float window) =>
        blockElapsed >= 0f && blockElapsed <= window;

    /// <summary>Grades a guard raised <paramref name="blockElapsed"/> seconds ago against a hit now.</summary>
    public static ParryGrade Grade(float blockElapsed, float window)
    {
        if (blockElapsed < 0f || window <= 0f)
        {
            return ParryGrade.None;
        }

        if (blockElapsed <= window * PerfectFraction)
        {
            return ParryGrade.Perfect;
        }

        if (blockElapsed <= window)
        {
            return ParryGrade.Good;
        }

        return blockElapsed <= window * (1f + LateFraction) ? ParryGrade.Late : ParryGrade.None;
    }

    /// <summary>Multiplier on <c>ParryStaminaCost</c>: a perfect parry is free.</summary>
    public static float StaminaFactor(ParryGrade grade) => grade == ParryGrade.Perfect ? 0f : 1f;

    /// <summary>Multiplier on <c>ParryStaggerDuration</c> for the attacker.</summary>
    public static float AttackerStaggerFactor(ParryGrade grade) => grade switch
    {
        ParryGrade.Perfect => 1.3f,
        ParryGrade.Good => 1f,
        ParryGrade.Late => 0.35f,
        _ => 0f,
    };

    /// <summary>Does this grade leave the attacker open to a riposte critical?</summary>
    public static bool OpensRiposte(ParryGrade grade) => grade is ParryGrade.Good or ParryGrade.Perfect;
}
