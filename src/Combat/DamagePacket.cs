using Embervale.Entities;

namespace Embervale.Combat;

/// <summary>
/// What kind of blow a <see cref="DamagePacket"/> carries. The attacker side stamps it; the defender
/// side and presentation read it (a riposte crits, a backstab ignores the guard arc, a heavy hit
/// weighs more in hit-stop). <b>Append-only</b> — the values are pinned by <c>EnumStabilityTests</c>.
/// </summary>
public enum HitKind
{
    Normal = 0,
    Heavy = 1,
    Charged = 2,
    Riposte = 3,
    Backstab = 4,
    Plunge = 5,
    Ranged = 6,
    Spell = 7,
}

/// <summary>
/// A self-contained description of an incoming hit. The attacker side builds it
/// (rolling crit and base damage from weapon + stats); the defender side applies
/// mitigation. Keeping it a value type avoids per-hit allocations in combat.
/// <para><paramref name="Kind"/> and <paramref name="Charge"/> (0..1, how far a charged blow was
/// wound up) default so every existing construction site stays valid.</para>
/// </summary>
public readonly record struct DamagePacket(
    float Amount,
    DamageType Type,
    IEntity? Source,
    bool IsCrit,
    float PoiseDamage,
    HitKind Kind = HitKind.Normal,
    float Charge = 0f);

/// <summary>The outcome of resolving a <see cref="DamagePacket"/> against a defender.</summary>
public readonly record struct DamageResult(
    float FinalAmount,
    bool IsCrit,
    bool IsBlocked,
    DamageType Type);
