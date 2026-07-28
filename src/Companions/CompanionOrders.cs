namespace Embervale.Companions;

/// <summary>
/// The pure rules behind the party's standing orders (Phase 32B): what the quick command cycles to
/// next, and how each order stretches the follower's engagement envelope. Keeping the envelope in
/// one Godot-free place is what makes "engage" a real tactical difference rather than a label —
/// the same <see cref="CompanionDecision"/> runs for every order, only its distances change.
/// </summary>
public static class CompanionOrders
{
    /// <summary>How much further an engaging companion may stray before it breaks off.</summary>
    public const float EngageLeashMultiplier = 2f;

    /// <summary>How much wider an engaging companion looks for a fight.</summary>
    public const float EngageRadiusMultiplier = 1.6f;

    /// <summary>A holding companion guards its anchor rather than roaming, so it engages inside a
    /// tighter bubble than a follower does.</summary>
    public const float HoldRadiusMultiplier = 0.75f;

    /// <summary>The next order in the quick-command cycle: follow → hold → engage → follow.</summary>
    public static CompanionStance Next(CompanionStance current) => current switch
    {
        CompanionStance.Follow => CompanionStance.Hold,
        CompanionStance.Hold => CompanionStance.Engage,
        _ => CompanionStance.Follow,
    };

    /// <summary>The leash for an order, from the companion's configured base leash.</summary>
    public static float Leash(CompanionStance stance, float baseLeash) =>
        stance == CompanionStance.Engage ? baseLeash * EngageLeashMultiplier : baseLeash;

    /// <summary>The hostile-scan radius for an order, from the companion's configured base radius.</summary>
    public static float EngageRadius(CompanionStance stance, float baseRadius) => stance switch
    {
        CompanionStance.Engage => baseRadius * EngageRadiusMultiplier,
        CompanionStance.Hold => baseRadius * HoldRadiusMultiplier,
        _ => baseRadius,
    };

    /// <summary>The <c>Loc</c> key naming an order in the UI.</summary>
    public static string NameKey(CompanionStance stance) => stance switch
    {
        CompanionStance.Hold => "companion.order.hold",
        CompanionStance.Engage => "companion.order.engage",
        _ => "companion.order.follow",
    };
}
