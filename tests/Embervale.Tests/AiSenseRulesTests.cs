using Embervale.Enemies;
using Godot;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// The perception and coordination guarantees. Every one of these was a branch inside the 1229-line
/// enemy brain with a paragraph of comment above it naming a bug it had cost, and not one of them
/// had a test — the branch sat inside a Godot node xUnit cannot construct.
/// </summary>
public class AiSenseRulesTests
{
    // --- The vertical vision gate (the Ancient Aerie cliff) -------------------

    [Theory]
    [InlineData(0f)]
    [InlineData(7.9f)]
    [InlineData(-7.9f)]
    [InlineData(8f)]
    [InlineData(-8f)]
    public void AGroundActorSeesWithinTheVerticalLimit(float deltaY)
    {
        Assert.True(AiSenseRules.PassesVerticalVisionGate(deltaY, canFly: false));
    }

    [Theory]
    [InlineData(30f)]   // the player on the Aerie rim, the creature in the trench
    [InlineData(-30f)]  // and the same drop the other way round
    [InlineData(8.01f)]
    public void AGroundActorDoesNotEngageAcrossABigDrop(float deltaY)
    {
        Assert.False(AiSenseRules.PassesVerticalVisionGate(deltaY, canFly: false));
    }

    [Theory]
    [InlineData(30f)]
    [InlineData(-30f)]
    public void AFlierIsExemptInBothDirections(float deltaY)
    {
        // Whether or not it is off the ground this instant: closing a vertical gap is what it does.
        Assert.True(AiSenseRules.PassesVerticalVisionGate(deltaY, canFly: true));
    }

    // --- The melee swing gates ----------------------------------------------

    [Fact]
    public void AnAirborneActorDoesNotSwing()
    {
        Assert.False(AiSenseRules.CanSwing(airborne: true, deltaY: 0f));
    }

    [Fact]
    public void AGroundedActorSwingsAtSomethingLevelWithIt()
    {
        Assert.True(AiSenseRules.CanSwing(airborne: false, deltaY: 0f));
    }

    [Theory]
    [InlineData(2.5f)]
    [InlineData(-2.5f)]
    public void ReachIsInclusiveAtTheLimit(float deltaY)
    {
        Assert.True(AiSenseRules.CanSwing(airborne: false, deltaY));
    }

    [Theory]
    [InlineData(2.51f)]   // a target on a ledge, out of reach
    [InlineData(-2.51f)]  // and one in a pit below
    [InlineData(6f)]      // a flier hovering overhead: horizontal range says "in reach"
    public void ReachIsVerticalTooAndCutsOffAboveAndBelow(float deltaY)
    {
        Assert.False(AiSenseRules.CanSwing(airborne: false, deltaY));
    }

    // --- Answering an alert --------------------------------------------------

    [Fact]
    public void AnActorDoesNotAnswerItsOwnShout()
    {
        Assert.False(AiSenseRules.AnswersAlert(
            isOwnShout: true, isAmbusher: false, "faction.goblins", "faction.goblins", playerIsTarget: true));
    }

    [Fact]
    public void AnAmbusherHoldsItsTrapWhenThePackShouts()
    {
        Assert.False(AiSenseRules.AnswersAlert(
            isOwnShout: false, isAmbusher: true, "faction.goblins", "faction.goblins", playerIsTarget: true));
    }

    [Fact]
    public void OnlyTheShoutersOwnKindAnswers()
    {
        // Without this a goblin's yell put the town guard onto the player.
        Assert.False(AiSenseRules.AnswersAlert(
            isOwnShout: false, isAmbusher: false, "faction.crossway_wardens", "faction.goblins", playerIsTarget: true));
    }

    [Fact]
    public void FactionMatchingIsOrdinalNotLoose()
    {
        Assert.False(AiSenseRules.AnswersAlert(
            isOwnShout: false, isAmbusher: false, "faction.Goblins", "faction.goblins", playerIsTarget: true));
    }

    [Fact]
    public void UnfactionedActorsAnswerOtherUnfactionedShouts()
    {
        // Empty matches empty: the sandbox's unfactioned camp still coordinates.
        Assert.True(AiSenseRules.AnswersAlert(
            isOwnShout: false, isAmbusher: false, "", "", playerIsTarget: true));
    }

    [Fact]
    public void AnActorWithNoQuarrelDoesNotGoLookingForOne()
    {
        Assert.False(AiSenseRules.AnswersAlert(
            isOwnShout: false, isAmbusher: false, "faction.goblins", "faction.goblins", playerIsTarget: false));
    }

    [Fact]
    public void AHostileClanmateAnswers()
    {
        Assert.True(AiSenseRules.AnswersAlert(
            isOwnShout: false, isAmbusher: false, "faction.goblins", "faction.goblins", playerIsTarget: true));
    }

    // --- Hearing an alert ----------------------------------------------------

    [Fact]
    public void AShoutIsMeasuredInThreeDimensions()
    {
        // Horizontal distance let a shout carry up a thirty-metre cliff face.
        var listener = new Vector3(0f, 30f, 0f);
        var shout = new Vector3(2f, 0f, 0f);

        Assert.False(AiSenseRules.HearsAlert(listener, shout, shoutRadius: 14f));
    }

    [Fact]
    public void AShoutCarriesToSomethingWithinItsRadius()
    {
        Assert.True(AiSenseRules.HearsAlert(new Vector3(10f, 0f, 0f), Vector3.Zero, shoutRadius: 14f));
    }

    [Fact]
    public void TheRadiusIsInclusive()
    {
        Assert.True(AiSenseRules.HearsAlert(new Vector3(14f, 0f, 0f), Vector3.Zero, shoutRadius: 14f));
    }

    // --- Shouting, and the ambusher's silence --------------------------------

    [Fact]
    public void ASilentProfileDoesNotGiveThePackAway()
    {
        Assert.False(AiSenseRules.ShoutsOnEngage(0f));
    }

    [Fact]
    public void AnActorWithAnAlertRadiusShouts()
    {
        Assert.True(AiSenseRules.ShoutsOnEngage(14f));
    }

    [Fact]
    public void SilentIsNotDeaf()
    {
        // The two are separate rules and must stay separate: a silent ambusher is excluded from
        // hearing by AnswersAlert's ambusher filter, not by its own alert radius being zero.
        Assert.False(AiSenseRules.ShoutsOnEngage(0f));
        Assert.True(AiSenseRules.AnswersAlert(
            isOwnShout: false, isAmbusher: false, "faction.goblins", "faction.goblins", playerIsTarget: true));
    }

    // --- Springing an ambush -------------------------------------------------

    [Fact]
    public void AnAmbusherHoldsUntilTheTargetIsInsideItsRange()
    {
        Assert.False(AiSenseRules.SpringsAmbush(isAmbusher: true, distance: 9f, ambushRange: 6f));
        Assert.True(AiSenseRules.SpringsAmbush(isAmbusher: true, distance: 6f, ambushRange: 6f));
    }

    [Fact]
    public void AProfileWithNoAmbushRangeSpringsOnSight()
    {
        Assert.True(AiSenseRules.SpringsAmbush(isAmbusher: false, distance: 40f, ambushRange: 0f));
    }
}
