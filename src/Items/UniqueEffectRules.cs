using System;

namespace Embervale.Items;

/// <summary>
/// The arithmetic of every <see cref="UniqueEffectKind"/>, free of the engine so it is
/// unit-testable. <see cref="UniqueEffectsComponent"/> listens for the moment and applies the
/// result; nothing here knows about entities.
/// </summary>
public static class UniqueEffectRules
{
    /// <summary>How close (metres) an attacker must be for its hit to count as a melee hit.</summary>
    public const float MeleeRange = 4.5f;

    /// <summary>The shortest gap between two triggers of a per-dodge effect, so one roll through
    /// a three-hit flurry refunds once.</summary>
    public const float DodgeRefundMinGapSeconds = 0.6f;

    /// <summary>Whether a chance-gated effect triggers on a uniform <paramref name="roll"/> in
    /// [0, 1). A chance of 1 or more always does; 0 or less never does.</summary>
    public static bool Rolls(float chance, float roll) => chance >= 1f || (chance > 0f && roll < chance);

    /// <summary>A health (or any) fraction from a current and a max; 0 when there is no max.</summary>
    public static float Fraction(float current, float max) => max <= 0f ? 0f : Math.Clamp(current / max, 0f, 1f);

    /// <summary>Strictly below the threshold. A threshold of 0 is never met.</summary>
    public static bool Below(float fraction, float threshold) => threshold > 0f && fraction < threshold;

    /// <summary>The extra damage a "deals X more" effect adds on top of a hit that already landed.</summary>
    public static float BonusDamage(float amount, float magnitude) => Math.Max(0f, amount) * Math.Max(0f, magnitude);

    /// <summary>OnKillHeal: a fraction of max health.</summary>
    public static float KillHeal(float maxHealth, float magnitude) =>
        Math.Max(0f, maxHealth) * Math.Clamp(magnitude, 0f, 1f);

    /// <summary>
    /// BlockReflect: <paramref name="magnitude"/> of the damage the guard stopped. Only the chip that
    /// got through is known after the fact, so what was stopped is recovered from the guard's
    /// mitigation: a guard that lets 30% through stopped 70/30 of the chip. A guard with no
    /// mitigation stopped nothing, and one at 100% let nothing through to measure.
    /// </summary>
    public static float Reflected(float chipDamage, float blockMitigation, float magnitude)
    {
        if (chipDamage <= 0f || blockMitigation <= 0f || blockMitigation >= 1f)
        {
            return 0f;
        }

        float stopped = chipDamage * blockMitigation / (1f - blockMitigation);
        return stopped * Math.Max(0f, magnitude);
    }

    /// <summary>
    /// GoldFind: the whole coins to add to a pickup of <paramref name="quantity"/>, carrying the
    /// fraction forward so many small pickups add up to the same bonus as one large one.
    /// </summary>
    public static int GoldBonus(int quantity, float magnitude, float carry, out float newCarry)
    {
        float exact = (Math.Max(0, quantity) * Math.Max(0f, magnitude)) + Math.Max(0f, carry);
        int whole = (int)Math.Floor(exact);
        newCarry = exact - whole;
        return whole;
    }

    /// <summary>ManaShield: how much of a blow is paid from mana, limited by the mana there is.</summary>
    public static float ManaShield(float damage, float magnitude, float mana) =>
        Math.Min(Math.Max(0f, damage) * Math.Clamp(magnitude, 0f, 1f), Math.Max(0f, mana));

    /// <summary>The health fraction a target had before a hit of <paramref name="amount"/> left it
    /// at <paramref name="remaining"/>: an execute is judged on the state the blow found.</summary>
    public static float FractionBeforeHit(float remaining, float amount, float max) =>
        Fraction(Math.Max(0f, remaining) + Math.Max(0f, amount), max);

    public static bool InMeleeRange(float distanceSquared) => distanceSquared <= MeleeRange * MeleeRange;
}
