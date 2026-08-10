using Embervale.Movement;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// The mount's gallop pacing (Phase 39A). Every case here is a frame loop rather than a single call,
/// because the rule being tested is a <em>latch</em> and a latch is only wrong across frames.
/// </summary>
public class MountRulesTests
{
    private const float Frame = 1f / 60f;

    /// <summary>Runs the pool forward and returns where it ended up.</summary>
    private static MountRules.GallopState Run(MountRules.GallopState state, bool gallop, float seconds)
    {
        for (float t = 0f; t < seconds; t += Frame)
        {
            state = MountRules.Step(state, gallop, Frame);
        }

        return state;
    }

    [Fact]
    public void AFreshMountGallopsOnDemand()
    {
        MountRules.GallopState state = MountRules.Step(MountRules.Fresh, wantGallop: true, Frame);

        Assert.True(state.Galloping);
        Assert.False(state.Exhausted);
        Assert.True(state.Stamina < MountRules.StaminaMax);
    }

    [Fact]
    public void HoldingSprintEmptiesThePoolInAboutFiveSeconds()
    {
        MountRules.GallopState state = Run(MountRules.Fresh, gallop: true, seconds: 4f);
        Assert.True(state.Galloping, "four seconds is inside the five the pool is worth");

        state = Run(state, gallop: true, seconds: 1.2f);
        Assert.True(state.Exhausted);
        Assert.False(state.Galloping);

        // ⚠️ The pool is NOT still zero here, and that is the rule rather than a leak: a blown horse
        // recovers while it walks, even with sprint still held. What stops it running again is the
        // latch, not an empty pool — asserting "stamina == 0" would have been asserting the wrong
        // mechanism, and it passes for one frame only.
        Assert.True(state.Stamina > 0f);
    }

    /// <summary>
    /// The reason <see cref="MountRules"/> is a type. Without the latch a blown horse regenerates one
    /// frame's worth, gallops, empties, and stutters between gaits several times a second — and with
    /// a latch that clears on the mark alone it does the same thing at a 3-second period, which was
    /// the first version of this rule and is what this test caught.
    /// </summary>
    [Fact]
    public void ABlownHorseStaysBlownForAsLongAsSprintIsHeld()
    {
        MountRules.GallopState state = Run(MountRules.Fresh, gallop: true, seconds: 6f);
        Assert.True(state.Exhausted);

        // Ten seconds of held sprint: long enough to refill the pool twice over. It must never
        // gallop, because the player has never once stopped asking.
        for (int i = 0; i < 600; i++)
        {
            state = MountRules.Step(state, wantGallop: true, Frame);
            Assert.False(state.Galloping);
        }

        Assert.True(state.Exhausted);
        Assert.Equal(MountRules.StaminaMax, state.Stamina);
    }

    [Fact]
    public void RecoveringPastTheMarkClearsTheLatch()
    {
        MountRules.GallopState state = Run(MountRules.Fresh, gallop: true, seconds: 6f);
        Assert.True(state.Exhausted);

        state = Run(state, gallop: false, seconds: 2f);

        Assert.False(state.Exhausted);
        Assert.True(state.Stamina >= MountRules.RecoverAt);
        Assert.True(MountRules.Step(state, wantGallop: true, Frame).Galloping);
    }

    [Fact]
    public void RestingRefillsToFullAndNoFurther()
    {
        MountRules.GallopState state = Run(MountRules.Fresh, gallop: true, seconds: 3f);
        state = Run(state, gallop: false, seconds: 30f);

        Assert.Equal(MountRules.StaminaMax, state.Stamina);
        Assert.False(state.Galloping);
    }

    /// <summary>
    /// ⚠️ 37F's invariant, at the door it would come in through. The multiplier this pool gates feeds
    /// <c>StatType.MoveSpeed</c>, which <c>LocomotionComponent</c> multiplies into a
    /// <c>CharacterBody3D</c>'s velocity — and a poisoned velocity is permanent for that body.
    /// </summary>
    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(-1f)]
    public void ANonFiniteDeltaCannotPoisonThePool(float delta)
    {
        MountRules.GallopState state = MountRules.Step(MountRules.Fresh, wantGallop: true, delta);

        Assert.False(float.IsNaN(state.Stamina));
        Assert.False(float.IsInfinity(state.Stamina));
        Assert.Equal(MountRules.StaminaMax, state.Stamina);
    }

    [Fact]
    public void ANonFinitePoolIsTreatedAsRested()
    {
        var poisoned = new MountRules.GallopState(float.NaN, false, false);

        MountRules.GallopState state = MountRules.Step(poisoned, wantGallop: false, Frame);

        Assert.False(float.IsNaN(state.Stamina));
        Assert.Equal(MountRules.StaminaMax, state.Stamina);
    }
}
