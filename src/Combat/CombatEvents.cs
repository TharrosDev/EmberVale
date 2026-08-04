using Embervale.Core.Events;
using Embervale.Entities;

namespace Embervale.Combat;

/// <summary>Raised after a hit is resolved against a defender (post-mitigation).</summary>
public readonly record struct DamageDealtEvent(
    IEntity? Source,
    IEntity Target,
    float Amount,
    DamageType Type,
    bool IsCrit,
    bool IsBlocked) : IGameEvent;

/// <summary>Raised when an entity's poise breaks and it is staggered.</summary>
public readonly record struct EntityStaggeredEvent(IEntity Entity) : IGameEvent;

/// <summary>Raised when a defender parries an attacker with a timed block (Phase 29F) — the attacker is
/// staggered and a riposte opens.</summary>
public readonly record struct EntityParriedEvent(IEntity Defender, IEntity? Attacker) : IGameEvent;

/// <summary>
/// Raised when an entity starts a melee attack swing — i.e. at the instant its wind-up begins.
/// <paramref name="WindupSeconds"/> is the <em>effective</em> wind-up (the weapon's time divided by
/// the attacker's attack speed), so a telegraph can last exactly as long as the window it warns
/// about instead of guessing at a constant. A faster phase shortens both together.
/// </summary>
public readonly record struct AttackPerformedEvent(
    IEntity Attacker, int ComboIndex, float WindupSeconds) : IGameEvent;

/// <summary>
/// Raised when a committed action is cancelled by a stagger before it could land — a melee swing
/// still in its wind-up, or a charge/channel in progress. The punish half of the telegraph:
/// whatever showed the wind-up coming (ring, flare, arms) uses this to stop early, which is what
/// makes the interrupt legible as a win rather than as the attack simply not happening.
/// </summary>
public readonly record struct AttackInterruptedEvent(IEntity Attacker) : IGameEvent;
