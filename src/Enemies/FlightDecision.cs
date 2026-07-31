namespace Embervale.Enemies;

/// <summary>Where a flier is in its take-off/land cycle (Phase 35B).</summary>
public enum FlightPhase
{
    /// <summary>On the ground, fighting — the only phase a non-flier is ever in.</summary>
    Grounded,

    /// <summary>Climbing toward the hover altitude.</summary>
    TakingOff,

    /// <summary>At altitude, closing on the target.</summary>
    Airborne,

    /// <summary>Descending; ends the moment the body touches the floor.</summary>
    Landing,
}

/// <summary>
/// The pure transition rule for the aerial cycle (Phase 35B), Godot-free like its siblings
/// <see cref="DragonMelee"/>, <see cref="CasterDecision"/> and
/// <see cref="Embervale.Movement.PathSteering"/> — so the cycle can be tested without a physics
/// frame, which is the only part of flight that is testable at all.
///
/// The loop is deliberately time-boxed rather than open-ended: <c>Grounded</c> → (target out of
/// reach, or long enough on the ground) → <c>TakingOff</c> → (at altitude) → <c>Airborne</c> →
/// (window elapsed) → <c>Landing</c> → (touchdown) → <c>Grounded</c>. A dragon that could choose to
/// stay up would, and until breath lands in 35C that is a fight where nothing happens.
/// </summary>
public static class FlightDecision
{
    /// <summary>How close to the hover altitude counts as having arrived, in metres.</summary>
    public const float AltitudeTolerance = 0.5f;

    /// <summary>
    /// The phase to be in next frame.
    /// </summary>
    /// <param name="phase">The current phase.</param>
    /// <param name="elapsed">Seconds spent in the current phase.</param>
    /// <param name="distanceToTarget">Planar distance to the target.</param>
    /// <param name="altitude">Metres above the take-off ground.</param>
    /// <param name="grounded">Whether the body is touching the floor this frame.</param>
    /// <param name="takeoffRange">Distance past which it flies. 0 = it cannot fly at all.</param>
    /// <param name="hoverAltitude">Metres above the take-off ground it climbs to.</param>
    /// <param name="airborneDuration">Seconds it holds altitude before landing.</param>
    /// <param name="groundedDuration">Seconds it must fight on the ground between flights.</param>
    public static FlightPhase Next(
        FlightPhase phase,
        double elapsed,
        float distanceToTarget,
        float altitude,
        bool grounded,
        float takeoffRange,
        float hoverAltitude,
        float airborneDuration,
        float groundedDuration)
    {
        // A profile that cannot fly is always grounded — including one caught mid-air by a live
        // tuning change, which lands rather than freezing at altitude.
        if (takeoffRange <= 0f)
        {
            return phase == FlightPhase.Grounded ? FlightPhase.Grounded : FlightPhase.Landing;
        }

        return phase switch
        {
            // Out of reach is the reason to fly; the ground timer is what stops a target that stays
            // close from turning the fight into a walking stalemate.
            FlightPhase.Grounded =>
                distanceToTarget > takeoffRange || elapsed >= groundedDuration
                    ? FlightPhase.TakingOff
                    : FlightPhase.Grounded,

            FlightPhase.TakingOff =>
                altitude >= hoverAltitude - AltitudeTolerance
                    ? FlightPhase.Airborne
                    : FlightPhase.TakingOff,

            FlightPhase.Airborne =>
                elapsed >= airborneDuration ? FlightPhase.Landing : FlightPhase.Airborne,

            // Touchdown, not a height test: the floor may be higher than where it took off.
            FlightPhase.Landing =>
                grounded ? FlightPhase.Grounded : FlightPhase.Landing,

            _ => FlightPhase.Grounded,
        };
    }

    /// <summary>True while the body should be off the ground under its own power — the phases that
    /// need <see cref="Embervale.Movement.LocomotionComponent.Flying"/> on.</summary>
    public static bool IsFlying(FlightPhase phase) => phase != FlightPhase.Grounded;

    /// <summary>True only when the flier is high enough that its melee cannot reach the ground, so
    /// the AI should hold its swing rather than bite at empty air.</summary>
    public static bool IsOutOfMeleeReach(FlightPhase phase) =>
        phase is FlightPhase.Airborne or FlightPhase.TakingOff;
}
