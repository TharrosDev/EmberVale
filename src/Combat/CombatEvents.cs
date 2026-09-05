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
/// Raised at the instant an action's active window opens — the frame a sword's edge goes live, a
/// spell leaves the hand, or an arrow leaves the string.
///
/// <para><b>This is the one hook everything that happens "on the hit" hangs off.</b> A melee swing
/// opens its hitbox here; a cast delivers its spell here; a bow spawns its arrow here. Before it,
/// each of those fired on its own trigger — a cast on key-down, an arrow on a timer — which is the
/// same desynchronisation between what is shown and what happens that the action timeline exists to
/// end. Presentation (VFX, sound, trails) consumes this rather than reaching into gameplay classes,
/// which is what keeps §21's hooks one-way.</para>
/// </summary>
public readonly record struct ActionReleasedEvent(
    IEntity Actor, string ActionId, Actions.ActionKind Kind) : IGameEvent;

/// <summary>
/// Raised when a committed action is cancelled by a stagger before it could land — a melee swing
/// still in its wind-up, or a charge/channel in progress. The punish half of the telegraph:
/// whatever showed the wind-up coming (ring, flare, arms) uses this to stop early, which is what
/// makes the interrupt legible as a win rather than as the attack simply not happening.
/// </summary>
public readonly record struct AttackInterruptedEvent(IEntity Attacker) : IGameEvent;
