using Embervale.Entities;

namespace Embervale.Combat;

/// <summary>
/// A self-contained description of an incoming hit. The attacker side builds it
/// (rolling crit and base damage from weapon + stats); the defender side applies
/// mitigation. Keeping it a value type avoids per-hit allocations in combat.
/// </summary>
public readonly record struct DamagePacket(
    float Amount,
    DamageType Type,
    IEntity? Source,
    bool IsCrit,
    float PoiseDamage);

/// <summary>The outcome of resolving a <see cref="DamagePacket"/> against a defender.</summary>
public readonly record struct DamageResult(
    float FinalAmount,
    bool IsCrit,
    bool IsBlocked,
    DamageType Type);
