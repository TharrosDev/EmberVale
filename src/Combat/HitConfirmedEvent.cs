using Embervale.Core.Events;
using Embervale.Entities;
using Godot;

namespace Embervale.Combat;

/// <summary>
/// One blow, resolved for presentation. <see cref="CombatFeedbackDirector"/> gathers the raw combat
/// events of a resolution (<see cref="DamageDealtEvent"/>, <see cref="EntityParriedEvent"/>,
/// <see cref="GuardBrokenEvent"/>, <see cref="CriticalHitEvent"/>, <see cref="EntityStaggeredEvent"/>)
/// and publishes this once, at the end of the frame, so hit-stop, sparks, the screen flash, floating
/// numbers, the mesh lurch and the damage-direction arcs all read the same outcome.
///
/// <para>Presentation is one-way: nothing that consumes this feeds back into a rule. It is published
/// for a parry (<see cref="Target"/> is the defender, <see cref="Source"/> the attacker who was
/// staggered) and for a stagger with no damage as well as for every damaging blow.</para>
/// </summary>
/// <param name="Source">The attacker, or null for a status tick or an unattributed source.</param>
/// <param name="Target">The defender.</param>
/// <param name="Amount">Post-mitigation damage; 0 for a parry.</param>
/// <param name="Type">The damage school.</param>
/// <param name="Outcome">What the blow was.</param>
/// <param name="Kind">Its weight class, inferred (see <see cref="HitOutcomes.InferKind"/>).</param>
/// <param name="Staggered">Whether the blow broke the target's poise, whatever the headline outcome.</param>
/// <param name="Point">Where to put the cue, in world space: the target's chest, or the clash point
/// between attacker and defender for a parry.</param>
/// <param name="ByPlayer">The player (or, with no player in play, the player's side) dealt it.</param>
/// <param name="OnPlayer">The player took it.</param>
public readonly record struct HitConfirmedEvent(
    IEntity? Source,
    IEntity Target,
    float Amount,
    DamageType Type,
    HitOutcome Outcome,
    HitKind Kind,
    bool Staggered,
    Vector3 Point,
    bool ByPlayer,
    bool OnPlayer) : IGameEvent;
