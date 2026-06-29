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

/// <summary>Raised when an entity starts a melee attack swing.</summary>
public readonly record struct AttackPerformedEvent(IEntity Attacker, int ComboIndex) : IGameEvent;
