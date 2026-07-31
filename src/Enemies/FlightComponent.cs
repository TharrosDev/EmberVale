using Embervale.Core.Services;
using Embervale.Entities;
using Embervale.Movement;
using Embervale.Player;
using Godot;

namespace Embervale.Enemies;

/// <summary>
/// Runs a flier's take-off/land cycle (Phase 35B). It owns the <em>vertical</em> axis only: it sets
/// <see cref="LocomotionComponent.Flying"/> and the target altitude, and
/// <see cref="EnemyAIComponent"/> goes on steering horizontally exactly as it steers a walker. That
/// split is the whole reason flight cost so little — there is no second pathing system, and no aerial
/// branch in the AI's state machine.
///
/// The tuning lives on the <see cref="AIProfileResource"/> (Phase 34A's data-fied AI), so flight is a
/// property of a profile rather than of the dragon: <c>TakeoffRange = 0</c> is every other archetype
/// in the game and costs them a single early return. The phase transitions themselves are
/// <see cref="FlightDecision"/>, which is pure and unit-tested.
/// </summary>
[GlobalClass]
public partial class FlightComponent : EntityComponent
{
    private EnemyAIComponent? _ai;
    private LocomotionComponent? _locomotion;
    private CharacterBody3D? _body;
    private double _elapsed;

    /// <summary>Ground level it last took off from — altitude is measured against this, not world
    /// zero, so a dragon that takes off on a ridge does not try to hover underground.</summary>
    private float _groundY;

    public FlightPhase Phase { get; private set; } = FlightPhase.Grounded;

    /// <summary>True while the body is high enough that its melee cannot reach the ground; the AI
    /// reads this to hold its swing rather than bite at empty air.</summary>
    public bool IsOutOfMeleeReach => FlightDecision.IsOutOfMeleeReach(Phase);

    protected override void OnInitialize()
    {
        _ai = Entity!.GetComponent<EnemyAIComponent>();
        _locomotion = Entity.GetComponent<LocomotionComponent>();
        _body = Entity.Body as CharacterBody3D;
        _groundY = Entity.Body.GlobalPosition.Y;
    }

    /// <summary>Puts the flier back on the ground under gravity. Called when it dies — a corpse must
    /// fall, not hang at altitude.</summary>
    public void Ground()
    {
        Phase = FlightPhase.Grounded;
        _elapsed = 0d;
        if (_locomotion != null)
        {
            _locomotion.Flying = false;
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_ai == null || _locomotion == null || _body == null)
        {
            return;
        }

        AIProfileResource profile = _ai.ActiveProfile;
        if (profile.TakeoffRange <= 0f && Phase == FlightPhase.Grounded)
        {
            return;   // the overwhelming majority of actors: a walker, costing one comparison
        }

        // Only fly at something. Out of combat it stays down — a dragon circling an empty valley
        // reads as broken, and there is nothing up there to do.
        if (_ai.State != EnemyState.Combat && Phase == FlightPhase.Grounded)
        {
            _elapsed = 0d;
            return;
        }

        _elapsed += delta;
        float distance = DistanceToTarget();
        float altitude = _body.GlobalPosition.Y - _groundY;

        FlightPhase next = FlightDecision.Next(
            Phase, _elapsed, distance, altitude, _locomotion.IsGrounded,
            profile.TakeoffRange, profile.HoverAltitude, profile.AirborneDuration, profile.GroundedDuration);
        if (next != Phase)
        {
            // Re-anchor on take-off: the ground it leaves is the ground it measures against, and it
            // may have walked a long way from where it last stood still.
            if (next == FlightPhase.TakingOff)
            {
                _groundY = _body.GlobalPosition.Y;
            }

            Phase = next;
            _elapsed = 0d;
        }

        _locomotion.Flying = FlightDecision.IsFlying(Phase);
        _locomotion.ClimbSpeed = profile.ClimbSpeed;
        // Landing drives well below the floor; MoveAndSlide stops the body and IsGrounded ends the
        // phase, which is what makes uneven ground a non-problem.
        _locomotion.TargetAltitude = Phase == FlightPhase.Landing
            ? _groundY - profile.HoverAltitude
            : _groundY + profile.HoverAltitude;
    }

    private float DistanceToTarget()
    {
        if (ServiceLocator.Instance == null || !ServiceLocator.Instance.TryGet(out PlayerCharacter player))
        {
            return 0f;
        }

        Vector3 a = _body!.GlobalPosition;
        Vector3 b = player.GlobalPosition;
        return new Vector2(a.X - b.X, a.Z - b.Z).Length();
    }
}
