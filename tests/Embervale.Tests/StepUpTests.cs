using Embervale.Movement;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// The step-up accept/revert rule (Phase 39C).
///
/// ⚠️ <b>An earlier version of this file tested the wrong thing and passed.</b> The first step-up
/// computed its lift arithmetically (<c>height - probedDrop</c>) and six tests agreed with the
/// arithmetic — which under-reports every real step, because a capsule's rounded bottom catches a
/// step's corner rather than its top face. Walking a body at the actual 0.3 m dais is what found it
/// (<c>tools/stepup_probe.gd</c>). The climb is now simulated by the engine, and what is left to test
/// is the only judgement left in code: whether the attempt is worth keeping.
/// </summary>
public class StepUpTests
{
    [Fact]
    public void AClimbThatWentUpAndForwardIsKept()
    {
        Assert.True(StepUp.Accept(climbed: 0.3f, advanced: 0.5f, StepUp.MaxHeight));
    }

    /// <summary>
    /// ⚠️ The condition a naive rule misses. A body that rose without getting anywhere is standing on
    /// the FACE of the kerb it failed to climb — keeping that leaves it hovering in the air against a
    /// wall, which is a worse artefact than simply not climbing.
    /// </summary>
    [Fact]
    public void RisingWithoutAdvancingIsNotAStep()
    {
        Assert.False(StepUp.Accept(climbed: 0.3f, advanced: 0f, StepUp.MaxHeight));
        Assert.False(StepUp.Accept(climbed: 0.3f, advanced: -0.4f, StepUp.MaxHeight));
    }

    [Fact]
    public void AdvancingOnTheFlatIsNotAStepEither()
    {
        // Walking along level ground is the common case and must never be mistaken for a climb —
        // it would leave every body committing a rollback test on every frame it moves freely.
        Assert.False(StepUp.Accept(climbed: 0f, advanced: 0.5f, StepUp.MaxHeight));
    }

    [Fact]
    public void ARiseInsideTheEnginesOwnSnapIsNotWorthCommittingTo()
    {
        // floor_snap_length already climbs these; committing is a twitch against every kerb brushed.
        Assert.False(StepUp.Accept(StepUp.MinimumRise / 2f, 0.5f, StepUp.MaxHeight));
    }

    [Fact]
    public void AClimbTallerThanAllowedIsRefused()
    {
        // The engine resolves the moves, so a body that found itself lifted onto something taller
        // than the rule permits (a slope, a stack of geometry) must be put back rather than kept.
        Assert.False(StepUp.Accept(climbed: 0.9f, advanced: 0.5f, StepUp.MaxHeight));
    }

    [Fact]
    public void TheHeightCeilingHasCollisionMarginSlack()
    {
        // The engine lands within a margin of the requested height rather than exactly on it, so a
        // climb of exactly MaxHeight must not be rejected by floating-point bad luck.
        Assert.True(StepUp.Accept(StepUp.MaxHeight, 0.5f, StepUp.MaxHeight));
    }

    [Fact]
    public void ABodyOptedOutNeverSteps()
    {
        Assert.False(StepUp.Accept(0.3f, 0.5f, 0f));
    }

    /// <summary>
    /// ⚠️ 37F's invariant at the door it would come in through. Accepting leaves a
    /// <c>CharacterBody3D</c> at a new position, and that body keeps its state between frames — one
    /// non-finite result puts it somewhere no later frame undoes, and the crash surfaces in whatever
    /// moves it next.
    /// </summary>
    [Theory]
    [InlineData(float.NaN, 0.5f)]
    [InlineData(float.PositiveInfinity, 0.5f)]
    [InlineData(0.3f, float.NaN)]
    [InlineData(0.3f, float.NegativeInfinity)]
    public void ANonFiniteResultIsNeverKept(float climbed, float advanced)
    {
        Assert.False(StepUp.Accept(climbed, advanced, StepUp.MaxHeight));
    }

    /// <summary>
    /// The number that closes the live mismatch: no cell may ask for an <c>agent_max_climb</c> above
    /// this, or NPCs are pathed onto ground the player cannot follow them onto. (The bake then FLOORS
    /// each cell's climb to a <c>cell_height</c> voxel, so what is baked sits at or below it — 0.3 on a
    /// 0.3 grid.) <c>ContentValidator</c> holds the ceiling; this pins the constant it compares against.
    /// </summary>
    [Fact]
    public void TheStepMatchesTheNavmeshAgentClimb()
    {
        Assert.Equal(0.5f, StepUp.MaxHeight);
    }
}
