namespace Embervale.Companions;

/// <summary>
/// Named bands of a companion's loyalty, from the day they join to the day they'd die for you.
/// Ordered low→high so threshold comparisons ("at least Trusted") work directly — the same shape as
/// <see cref="Factions.ReputationTier"/>, because it answers the same kind of question.
/// </summary>
// APPEND ONLY: ordinals persist in .tres/saves — never reorder/insert/remove (EnumStabilityTests).
public enum LoyaltyTier
{
    /// <summary>Travelling with you, not yet sure about you.</summary>
    Wary,

    /// <summary>A working trust: they'll follow your lead.</summary>
    Steady,

    /// <summary>They believe in you. Personal stories open here.</summary>
    Trusted,

    /// <summary>They'd follow you into the Ash. Their ending is yours.</summary>
    Sworn,
}

/// <summary>
/// The pure loyalty rules (Phase 32C): clamping, the value→tier bands, and the combat edge a
/// companion's belief in you buys. Loyalty is the per-companion mirror of faction reputation — a
/// standing that gates banter, abilities and (Phase 32E/44) ending flags — so it is kept Godot-free
/// and unit-tested rather than scattered across the roster.
/// </summary>
public static class CompanionLoyalty
{
    public const int Min = 0;
    public const int Max = 100;

    /// <summary>Loyalty at or above this is <see cref="LoyaltyTier.Steady"/>.</summary>
    public const int SteadyThreshold = 35;

    /// <summary>Loyalty at or above this is <see cref="LoyaltyTier.Trusted"/>.</summary>
    public const int TrustedThreshold = 65;

    /// <summary>Loyalty at or above this is <see cref="LoyaltyTier.Sworn"/>.</summary>
    public const int SwornThreshold = 90;

    /// <summary>Clamps a raw loyalty value into range.</summary>
    public static int Clamp(int value) => value < Min ? Min : value > Max ? Max : value;

    /// <summary>The tier a loyalty value falls into.</summary>
    public static LoyaltyTier Of(int value) => Clamp(value) switch
    {
        >= SwornThreshold => LoyaltyTier.Sworn,
        >= TrustedThreshold => LoyaltyTier.Trusted,
        >= SteadyThreshold => LoyaltyTier.Steady,
        _ => LoyaltyTier.Wary,
    };

    /// <summary>The <c>Loc</c> key naming a tier in the UI.</summary>
    public static string NameKey(LoyaltyTier tier) => tier switch
    {
        LoyaltyTier.Steady => "companion.loyalty.steady",
        LoyaltyTier.Trusted => "companion.loyalty.trusted",
        LoyaltyTier.Sworn => "companion.loyalty.sworn",
        _ => "companion.loyalty.wary",
    };

    /// <summary>
    /// The fractional bonus a tier grants to the companion's power and health — the mechanical face
    /// of loyalty. A wary recruit fights at their own strength; a sworn one fights harder for you.
    /// Kept modest so loyalty is a reward, not a requirement.
    /// </summary>
    public static float CombatBonus(LoyaltyTier tier) => tier switch
    {
        LoyaltyTier.Steady => 0.05f,
        LoyaltyTier.Trusted => 0.12f,
        LoyaltyTier.Sworn => 0.20f,
        _ => 0f,
    };
}
