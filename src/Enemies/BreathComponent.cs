using Embervale.Core.Services;
using Embervale.Entities;
using Embervale.Magic;
using Embervale.Player;
using Godot;

namespace Embervale.Enemies;

/// <summary>
/// The rule for when a breath fires (Phase 35C). Pure, in the shape of <see cref="DragonMelee"/> and
/// <see cref="FlightDecision"/>, so the trigger is testable while the casting is not.
///
/// On the ground a dragon must <em>turn</em> to breathe — with `ai.dragon` slewing at 55°/s that is a
/// real beat, and the same tactic that makes flanking work in 35A also denies the breath. In the air
/// it is directly above you and pitches its aim down, so there is nothing to turn toward and the
/// facing gate would only make the hover window fire at random.
/// </summary>
public static class BreathWindow
{
    /// <summary>
    /// Whether to start a breath now.
    /// </summary>
    /// <param name="ready">The spell is off cooldown and affordable (<c>SpellcastingComponent.CanCast</c>).</param>
    /// <param name="airborne">At altitude — the swoop's payoff window.</param>
    /// <param name="bearingDegrees">Signed angle from the body's facing to the target.</param>
    /// <param name="distance">Planar distance to the target.</param>
    /// <param name="coneLength">The breath's reach.</param>
    /// <param name="coneAngleDegrees">The breath's full opening angle.</param>
    public static bool ShouldBreathe(
        bool ready, bool airborne, float bearingDegrees, float distance, float coneLength, float coneAngleDegrees)
    {
        if (!ready || distance > coneLength)
        {
            return false;
        }

        if (airborne)
        {
            return true;
        }

        float offAxis = bearingDegrees < 0f ? -bearingDegrees : bearingDegrees;
        return offAxis <= coneAngleDegrees * 0.5f;
    }
}

/// <summary>
/// Drives a creature's breath weapon (Phase 35C). The breath itself is **not** a bespoke attack: it
/// is an ordinary <see cref="SpellResource"/> with <see cref="SpellDelivery.Cone"/> and
/// <see cref="CastMode.Channeled"/>, so it flows through <see cref="SpellResolver"/>, school
/// resistances, <see cref="SchoolIdentity"/> and status effects exactly as any player spell does.
/// All this component contributes is the thing an enemy had no way to do: hold a channel open.
///
/// It aims by pointing the actor's <c>CastOrigin</c> node at the target before casting — which is how
/// a dragon hovering 12 m up breathes <em>down</em> at you rather than over your head, since the body
/// itself is kept level by the AI's flat <c>FaceTowards</c>.
/// </summary>
[GlobalClass]
public partial class BreathComponent : EntityComponent
{
    /// <summary>Which spell this creature breathes. Must also be in its <c>KnownSpellIds</c>.</summary>
    [Export] public string BreathSpellId { get; set; } = string.Empty;

    /// <summary>Seconds the channel is held open once started.</summary>
    [Export] public float BreathDuration { get; set; } = 1.6f;

    private SpellcastingComponent? _casting;
    private EnemyAIComponent? _ai;
    private FlightComponent? _flight;
    private Node3D? _aim;
    private SpellResource? _spell;
    private double _channelElapsed;

    public bool IsBreathing { get; private set; }

    protected override void OnInitialize()
    {
        _casting = Entity!.GetComponent<SpellcastingComponent>();
        _ai = Entity.GetComponent<EnemyAIComponent>();
        _flight = Entity.GetComponent<FlightComponent>();
        _aim = _casting?.AimNode;
        _spell = SpellDatabase.Get(BreathSpellId);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_casting == null || _ai == null || _spell == null)
        {
            return;
        }

        // Dying, or losing the target, must not leave a channel running — a corpse breathing fire is
        // the aerial-corpse bug of 35B in another costume.
        if (_ai.State != EnemyState.Combat)
        {
            Stop();
            return;
        }

        if (IsBreathing)
        {
            _channelElapsed += delta;
            AimAtTarget();
            _casting.UpdateCast(delta);
            if (_channelElapsed >= BreathDuration || !_casting.IsChanneling)
            {
                Stop();
            }

            return;
        }

        if (!TryGetTarget(out Vector3 targetPos))
        {
            return;
        }

        bool airborne = _flight is { Phase: FlightPhase.Airborne };
        float distance = Planar(Entity!.Body.GlobalPosition, targetPos);
        if (!BreathWindow.ShouldBreathe(
                _casting.CanCast(_spell), airborne, _ai.BearingTo(targetPos),
                distance, _spell.ImpactRadius, _spell.ConeAngleDegrees))
        {
            return;
        }

        AimAtTarget();
        if (_casting.BeginCastById(BreathSpellId))
        {
            IsBreathing = true;
            _channelElapsed = 0d;
        }
    }

    private void Stop()
    {
        if (!IsBreathing)
        {
            return;
        }

        IsBreathing = false;
        _channelElapsed = 0d;
        _casting?.EndCast();
    }

    /// <summary>Points the cast origin at the target, pitch included. <c>SpellcastingComponent.Aim</c>
    /// reads this node's forward, so every delivery shape follows it without knowing why.</summary>
    private void AimAtTarget()
    {
        if (_aim == null || !TryGetTarget(out Vector3 targetPos))
        {
            return;
        }

        // LookAt throws on a degenerate direction; a target standing exactly on the muzzle is not a
        // case worth aiming at anyway.
        if (_aim.GlobalPosition.DistanceSquaredTo(targetPos) > 0.01f)
        {
            _aim.LookAt(targetPos, Vector3.Up);
        }
    }

    private bool TryGetTarget(out Vector3 position)
    {
        position = Vector3.Zero;
        if (ServiceLocator.Instance == null || !ServiceLocator.Instance.TryGet(out PlayerCharacter player))
        {
            return false;
        }

        // Chest height, not the feet: a cone aimed at the ground clips a standing target's shins.
        position = player.GlobalPosition + (Vector3.Up * 1f);
        return true;
    }

    private static float Planar(Vector3 a, Vector3 b) => new Vector2(a.X - b.X, a.Z - b.Z).Length();
}
