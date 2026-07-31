using Embervale.Enemies;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// The take-off/land cycle (Phase 35B). The vertical servo and the physics are Godot-bound and
/// verified by build/run; the transition table is the part that decides whether a dragon fights in
/// cycles or gets stuck at altitude, so it is the part pinned down here.
///
/// <see cref="AIProfileResource"/> is a Godot <c>Resource</c> and so stays out of this project by the
/// csproj's no-GodotObject rule — which is why <see cref="FlightDecision"/> takes the four tuning
/// numbers directly, the way <see cref="PackFlank"/> and <see cref="CasterDecision"/> do.
/// </summary>
public class FlightDecisionTests
{
    private const float TakeoffRange = 16f;
    private const float HoverAltitude = 12f;
    private const float AirborneDuration = 4.5f;
    private const float GroundedDuration = 8f;

    private static FlightPhase Next(
        FlightPhase phase, double elapsed, float distance, float altitude, bool grounded,
        float takeoffRange = TakeoffRange) =>
        FlightDecision.Next(
            phase, elapsed, distance, altitude, grounded,
            takeoffRange, HoverAltitude, AirborneDuration, GroundedDuration);

    [Fact]
    public void Walker_NeverLeavesTheGround()
    {
        Assert.Equal(
            FlightPhase.Grounded,
            Next(FlightPhase.Grounded, 999d, 500f, 0f, grounded: true, takeoffRange: 0f));
    }

    [Fact]
    public void Walker_CaughtAirborne_Lands()
    {
        // Tuning can change under a live actor; it must come down rather than freeze at altitude.
        Assert.Equal(
            FlightPhase.Landing,
            Next(FlightPhase.Airborne, 1d, 5f, 12f, grounded: false, takeoffRange: 0f));
    }

    [Fact]
    public void Grounded_TakesOffWhenTheTargetIsOutOfReach()
    {
        Assert.Equal(FlightPhase.TakingOff, Next(FlightPhase.Grounded, 0.5d, 20f, 0f, grounded: true));
    }

    [Fact]
    public void Grounded_TakesOffOnTheTimerEvenWithTheTargetClose()
    {
        // Otherwise a target that stays in melee turns the fight into a walking stalemate.
        Assert.Equal(FlightPhase.TakingOff, Next(FlightPhase.Grounded, GroundedDuration, 3f, 0f, grounded: true));
    }

    [Fact]
    public void Grounded_HoldsWhileFightingInReach()
    {
        Assert.Equal(FlightPhase.Grounded, Next(FlightPhase.Grounded, 2d, 3f, 0f, grounded: true));
    }

    [Fact]
    public void TakingOff_BecomesAirborneOnlyAtAltitude()
    {
        Assert.Equal(FlightPhase.TakingOff, Next(FlightPhase.TakingOff, 1d, 20f, 4f, false));
        Assert.Equal(FlightPhase.Airborne, Next(FlightPhase.TakingOff, 1d, 20f, HoverAltitude, false));
    }

    [Fact]
    public void TakingOff_ToleranceCountsAsArrived()
    {
        // Exact equality never happens against a servo running on frame deltas.
        float justUnder = HoverAltitude - (FlightDecision.AltitudeTolerance * 0.5f);
        Assert.Equal(FlightPhase.Airborne, Next(FlightPhase.TakingOff, 1d, 20f, justUnder, false));
    }

    [Fact]
    public void Airborne_IsTimeBoxed()
    {
        Assert.Equal(FlightPhase.Airborne, Next(FlightPhase.Airborne, 2d, 20f, HoverAltitude, false));
        Assert.Equal(FlightPhase.Landing, Next(FlightPhase.Airborne, AirborneDuration, 20f, HoverAltitude, false));
    }

    [Fact]
    public void Landing_EndsOnTouchdownNotOnHeight()
    {
        // The floor it lands on may be higher than the one it left, so altitude cannot end this phase.
        Assert.Equal(FlightPhase.Landing, Next(FlightPhase.Landing, 1d, 5f, 0.2f, grounded: false));
        Assert.Equal(FlightPhase.Grounded, Next(FlightPhase.Landing, 1d, 5f, 6f, grounded: true));
    }

    [Fact]
    public void OnlyGrounded_IsNotFlying()
    {
        Assert.False(FlightDecision.IsFlying(FlightPhase.Grounded));
        Assert.True(FlightDecision.IsFlying(FlightPhase.TakingOff));
        Assert.True(FlightDecision.IsFlying(FlightPhase.Airborne));
        Assert.True(FlightDecision.IsFlying(FlightPhase.Landing));
    }

    [Fact]
    public void MeleeResumesOnTheWayDown()
    {
        // Landing keeps its bite: the descent is the swoop's payoff, not a helpless phase.
        Assert.True(FlightDecision.IsOutOfMeleeReach(FlightPhase.Airborne));
        Assert.True(FlightDecision.IsOutOfMeleeReach(FlightPhase.TakingOff));
        Assert.False(FlightDecision.IsOutOfMeleeReach(FlightPhase.Landing));
        Assert.False(FlightDecision.IsOutOfMeleeReach(FlightPhase.Grounded));
    }

    [Fact]
    public void AFullCycleReturnsToTheGround()
    {
        // The property that matters most: no phase is a dead end.
        FlightPhase phase = FlightPhase.Grounded;

        phase = Next(phase, 9d, 20f, 0f, grounded: true);
        Assert.Equal(FlightPhase.TakingOff, phase);
        phase = Next(phase, 2d, 20f, HoverAltitude, grounded: false);
        Assert.Equal(FlightPhase.Airborne, phase);
        phase = Next(phase, 5d, 4f, HoverAltitude, grounded: false);
        Assert.Equal(FlightPhase.Landing, phase);
        phase = Next(phase, 2d, 4f, 0f, grounded: true);
        Assert.Equal(FlightPhase.Grounded, phase);
    }
}
