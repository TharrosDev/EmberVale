namespace Embervale.Combat.Actions;

/// <summary>
/// Pure rules for a held (heavy / charged) melee attack. Godot-free so every number a designer tunes
/// is unit-tested; <see cref="CharacterActionComponent"/> applies them.
///
/// <para><b>Hold to commit.</b> A tap is a light swing. Holding past <see cref="HoldThreshold"/> starts a
/// charge: the actor braces (slowed, stamina draining), and releasing swings a heavy blow whose weight
/// scales with how long it was held. Released early it is a plain <see cref="HitKind.Heavy"/>; held
/// past <see cref="ChargedFrom"/> it is a <see cref="HitKind.Charged"/> one; near full it is
/// hyperarmoured, so the greedy option is also the brave one.</para>
/// </summary>
public static class ChargeRules
{
    /// <summary>Seconds the attack button must stay down before a press stops being a tap.</summary>
    public const float HoldThreshold = 0.22f;

    /// <summary>Charge fraction at which a heavy blow counts as a properly charged one.</summary>
    public const float ChargedFrom = 0.3f;

    /// <summary>Charge fraction from which the swing cannot be interrupted by a stagger.</summary>
    public const float HyperarmorFrom = 0.8f;

    /// <summary>Charge fraction from which the overhead clip replaces the ordinary heavy one.</summary>
    public const float OverheadFrom = 0.5f;

    /// <summary>Charge as 0..1 for <paramref name="seconds"/> held past the threshold.</summary>
    public static float Fraction(float seconds, float maxSeconds)
    {
        if (maxSeconds <= 0f || seconds <= 0f)
        {
            return 0f;
        }

        float f = seconds / maxSeconds;
        return f >= 1f ? 1f : f;
    }

    /// <summary>The kind of blow a release at <paramref name="charge"/> delivers.</summary>
    public static HitKind KindOf(float charge) => charge >= ChargedFrom ? HitKind.Charged : HitKind.Heavy;

    /// <summary>Damage multiplier on top of the heavy action's own scale: 1 at no charge,
    /// <c>1 + bonus</c> at full.</summary>
    public static float DamageMultiplier(float charge, float bonus) => 1f + (Clamp01(charge) * bonus);

    /// <summary>Poise multiplier: a charged blow breaks guards and stances harder than it hurts.</summary>
    public static float PoiseMultiplier(float charge, float bonus) => 1f + (Clamp01(charge) * bonus * 1.25f);

    /// <summary>Stamina the release costs: the base cost, rising by half again at full charge.</summary>
    public static float ReleaseCost(float baseCost, float charge) => baseCost * (1f + (0.5f * Clamp01(charge)));

    /// <summary>Speed-up applied to the released swing: a charge pays its wind-up in advance, so the
    /// blow itself comes faster the longer it was held (1 at none, 1.4 at full).</summary>
    public static float ReleaseSpeed(float charge) => 1f + (0.4f * Clamp01(charge));

    /// <summary>Whether a swing released at this charge shrugs off a stagger.</summary>
    public static bool GrantsHyperarmor(float charge) => charge >= HyperarmorFrom;

    private static float Clamp01(float v) => v <= 0f ? 0f : v >= 1f ? 1f : v;
}
