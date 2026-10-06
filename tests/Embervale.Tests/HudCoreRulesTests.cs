using System;
using System.Collections.Generic;
using Embervale.Items;
using Embervale.UI;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// The decisions of the HUD core that are not drawing: the damage chunk behind a bar, a hotbar cell's
/// state and cooldown reading, which objectives the tracker keeps, how a prompt is split, and what
/// counts as news for a Dynamic element.
/// </summary>
public class HudCoreRulesTests
{
    // --- JuicedBarRules ----------------------------------------------------

    [Fact]
    public void AChunk_HoldsThenClosesOnTheValue()
    {
        double lag = JuicedBarRules.OnDrop(0.4, 1.0);
        Assert.Equal(1.0, lag);

        // Held: the edge does not move until the hold has run out.
        (double held, double hold) = JuicedBarRules.Step(lag, JuicedBarRules.LagHoldSeconds, 0.4, 0.1);
        Assert.Equal(1.0, held);
        Assert.True(hold > 0d);
        Assert.False(JuicedBarRules.Settled(held, 0.4));

        // Released: it closes at the drain rate and never passes the value.
        (double closing, _) = JuicedBarRules.Step(held, 0d, 0.4, 0.1);
        Assert.Equal(1.0 - (0.1 * JuicedBarRules.LagDrainPerSecond), closing, 6);
        (double done, _) = JuicedBarRules.Step(closing, 0d, 0.4, 10.0);
        Assert.Equal(0.4, done);
        Assert.True(JuicedBarRules.Settled(done, 0.4));
    }

    [Fact]
    public void ASecondHit_ExtendsTheChunkInFlight_AndAHealSwallowsIt()
    {
        // The chunk is already at 0.9 when the fill (at 0.6) drops again: the edge stays at 0.9.
        Assert.Equal(0.9, JuicedBarRules.OnDrop(0.9, 0.6));

        (double healed, double hold) = JuicedBarRules.Step(0.9, 0.2, 0.95, 0.016);
        Assert.Equal(0.95, healed);
        Assert.Equal(0d, hold);
    }

    [Fact]
    public void ANegativeFrame_NeverGrowsTheChunk()
    {
        (double lag, _) = JuicedBarRules.Step(0.8, 0d, 0.2, -5.0);
        Assert.Equal(0.8, lag);
    }

    // --- HotbarRules -------------------------------------------------------

    [Theory]
    [InlineData(0f, 0)]
    [InlineData(-1f, 0)]
    [InlineData(float.NaN, 0)]
    [InlineData(0.01f, 1)]
    [InlineData(1f, 1)]
    [InlineData(8.2f, 9)]
    [InlineData(9f, 9)]
    [InlineData(9.01f, 0)]
    [InlineData(30f, 0)]
    public void TheNumeral_ShowsOnlyForTheLastNineSeconds(float remaining, int expected) =>
        Assert.Equal(expected, HotbarRules.Numeral(remaining));

    [Fact]
    public void TheWipe_StepsFromFullToNothing()
    {
        Assert.Equal(HotbarRules.WipeSteps, HotbarRules.WipeStep(1f));
        Assert.Equal(0, HotbarRules.WipeStep(0f));
        Assert.Equal(0, HotbarRules.WipeStep(float.NaN));
        Assert.Equal(HotbarRules.WipeSteps, HotbarRules.WipeStep(7f));
        Assert.True(HotbarRules.WipeStep(0.5f) > HotbarRules.WipeStep(0.25f));

        // A sliver still to run is still a step, so the last wedge is drawn and then wiped.
        Assert.Equal(1, HotbarRules.WipeStep(0.001f));
    }

    [Theory]
    [InlineData(false, 0, 0f, ConsumeRefusal.None, HotbarSlotState.Empty)]
    [InlineData(true, 3, 0f, ConsumeRefusal.None, HotbarSlotState.Ready)]
    [InlineData(true, 0, 0f, ConsumeRefusal.None, HotbarSlotState.Depleted)]
    [InlineData(true, 0, 4f, ConsumeRefusal.LevelTooLow, HotbarSlotState.Depleted)]
    [InlineData(true, 2, 4f, ConsumeRefusal.OnCooldown, HotbarSlotState.Cooling)]
    [InlineData(true, 2, 0f, ConsumeRefusal.OnCooldown, HotbarSlotState.Cooling)]
    [InlineData(true, 2, 4f, ConsumeRefusal.LevelTooLow, HotbarSlotState.Locked)]
    [InlineData(true, 2, 0f, ConsumeRefusal.LevelTooLow, HotbarSlotState.Locked)]
    [InlineData(true, 2, 0f, ConsumeRefusal.AlreadyFull, HotbarSlotState.Unusable)]
    [InlineData(true, 2, 0f, ConsumeRefusal.NothingToCure, HotbarSlotState.Unusable)]
    [InlineData(true, 2, 3f, ConsumeRefusal.AlreadyFull, HotbarSlotState.Cooling)]
    public void ACell_IsInExactlyOneState(
        bool filled, int count, float cooldown, ConsumeRefusal refusal, HotbarSlotState expected) =>
        Assert.Equal(expected, HotbarRules.State(filled, count, cooldown, refusal));

    // --- TrackerFoldRules --------------------------------------------------

    private static ObjectiveState Step(int index, bool optional = false, bool active = true, bool complete = false,
        bool inBranch = true) => new(index, optional, active, complete, inBranch);

    [Fact]
    public void AShortQuest_IsNotFolded()
    {
        var states = new List<ObjectiveState> { Step(0, complete: true), Step(1), Step(2, active: false) };
        Assert.Equal(new[] { 0, 1, 2 }, TrackerFoldRules.Visible(states, current: 1));
        Assert.Equal(0, TrackerFoldRules.Hidden(states));
    }

    [Fact]
    public void AFiveStepSequentialQuest_KeepsTheCurrentStepAndWhatFollows()
    {
        // Done, current, optional and live, then two still locked: the harness fixture's shape.
        var states = new List<ObjectiveState>
        {
            Step(0, complete: true),
            Step(1),
            Step(2, optional: true),
            Step(3, active: false),
            Step(4, active: false),
        };

        Assert.Equal(new[] { 1, 2, 3 }, TrackerFoldRules.Visible(states, current: 1));
        Assert.Equal(2, TrackerFoldRules.Hidden(states));
    }

    [Fact]
    public void LiveRequiredSteps_OutrankOptionalOnes_AndFinishedOnesGoFirst()
    {
        var states = new List<ObjectiveState>
        {
            Step(0, complete: true),
            Step(1, complete: true),
            Step(2, optional: true),
            Step(3),
            Step(4),
            Step(5),
        };

        // Current is 3; 4 and 5 are live and required, so the optional step loses its line.
        Assert.Equal(new[] { 3, 4, 5 }, TrackerFoldRules.Visible(states, current: 3));
        Assert.Equal(3, TrackerFoldRules.Hidden(states));
    }

    [Fact]
    public void WithRoomToSpare_TheMostRecentFinishedStepIsTheOneKept()
    {
        var states = new List<ObjectiveState>
        {
            Step(0, complete: true),
            Step(1, complete: true),
            Step(2, complete: true),
            Step(3),
        };

        Assert.Equal(new[] { 1, 2, 3 }, TrackerFoldRules.Visible(states, current: 3));
        Assert.Equal(1, TrackerFoldRules.Hidden(states));
    }

    [Fact]
    public void TheOtherBranch_IsNeverDrawnAndNeverCounted()
    {
        var states = new List<ObjectiveState>
        {
            Step(0, complete: true),
            Step(1, inBranch: false),
            Step(2, inBranch: false),
            Step(3),
            Step(4, active: false),
        };

        Assert.Equal(new[] { 0, 3, 4 }, TrackerFoldRules.Visible(states, current: 3));
        Assert.Equal(0, TrackerFoldRules.Hidden(states));
    }

    [Fact]
    public void NoLines_DrawsNothing()
    {
        var states = new List<ObjectiveState> { Step(0), Step(1) };
        Assert.Empty(TrackerFoldRules.Visible(states, current: 0, max: 0));
        Assert.Equal(2, TrackerFoldRules.Hidden(states, max: 0));
    }

    // --- PromptRules -------------------------------------------------------

    [Theory]
    [InlineData("Loot Iron chest", "Iron chest", "Loot", "Iron chest")]
    [InlineData("Talk to Kael", "Kael", "Talk to", "Kael")]
    [InlineData("Pick up Iron sword (3)", "Iron sword", "Pick up Iron sword (3)", "")]
    [InlineData("Kael wants a word", "Kael", "Kael wants a word", "")]
    [InlineData("Open", "Door", "Open", "")]
    [InlineData("Door", "Door", "Door", "")]
    [InlineData("Unlock the Trapdoor", "door", "Unlock the Trapdoor", "")]
    [InlineData("Open", "", "Open", "")]
    [InlineData("Open", null, "Open", "")]
    public void APrompt_SplitsOnlyWhenItEndsWithTheNameAsAWord(
        string prompt, string? noun, string verb, string thing)
    {
        (string gotVerb, string gotNoun) = PromptRules.Split(prompt, noun);
        Assert.Equal(verb, gotVerb);
        Assert.Equal(thing, gotNoun);
    }

    // --- VitalsRules -------------------------------------------------------

    [Theory]
    [InlineData(1f, 0)]
    [InlineData(0.31f, 0)]
    [InlineData(0.30f, 1)]
    [InlineData(0.16f, 1)]
    [InlineData(0.15f, 2)]
    [InlineData(0f, 2)]
    public void HealthBands_BreakOnTheTicksTheBarDraws(float fraction, int band) =>
        Assert.Equal(band, VitalsRules.HealthBand(fraction));

    // --- HudChangeRules ----------------------------------------------------

    [Fact]
    public void ATurn_IsNewsOnlyPastTheStep_AndAcrossNorth()
    {
        Assert.False(HudChangeRules.HeadingMoved(0f, HudChangeRules.HeadingStep * 0.9f));
        Assert.True(HudChangeRules.HeadingMoved(0f, HudChangeRules.HeadingStep * 1.1f));
        Assert.True(HudChangeRules.HeadingMoved(0f, -HudChangeRules.HeadingStep * 1.1f));

        // Either side of north is a small turn, not a full circle.
        float justWest = (MathF.PI * 2f) - 0.05f;
        Assert.False(HudChangeRules.HeadingMoved(0.05f, justWest));
    }

    [Fact]
    public void ADestination_IsNewsWhenItAppearsGoesOrJumps_NotWhenItIsWalkedToward()
    {
        Assert.True(HudChangeRules.TargetMoved(false, 0f, 0f, true, 10f, 10f));
        Assert.True(HudChangeRules.TargetMoved(true, 10f, 10f, false, 0f, 0f));
        Assert.False(HudChangeRules.TargetMoved(false, 0f, 0f, false, 50f, 50f));
        Assert.False(HudChangeRules.TargetMoved(true, 10f, 10f, true, 12f, 11f));
        Assert.True(HudChangeRules.TargetMoved(true, 10f, 10f, true, 10f + HudChangeRules.TargetStepMetres, 10f));
    }

    // --- HudCoreMetrics ----------------------------------------------------

    [Theory]
    [InlineData(1280f, 186f)]
    [InlineData(853f, 186f)]
    [InlineData(0f, 186f)]
    [InlineData(float.NaN, 186f)]
    [InlineData(1505f, 219f)]
    [InlineData(3440f, 232f)]
    public void TheMinimap_IsItsOldSizeAtTheReferenceAndGrowsToACap(float layoutWidth, float side) =>
        Assert.Equal(side, HudCoreMetrics.MinimapSide(layoutWidth));

    [Fact]
    public void TheHotbarCell_IsPressable_AndTheBarsKeepTheirHierarchy()
    {
        Assert.True(HudCoreMetrics.HotbarCell >= UiTheme.ControlHeight);
        Assert.True(HudCoreMetrics.BarHeight > HudCoreMetrics.BarMinorHeight);
        Assert.True(HudCoreMetrics.BarMinorHeight > HudCoreMetrics.BarThinHeight);
        Assert.True(HudCoreMetrics.HotbarIcon < HudCoreMetrics.HotbarCell);
    }

    [Fact]
    public void AReading_HasRoomForFourDigitsEitherSide_AndGrowsWithTheType()
    {
        Assert.True(HudCoreMetrics.ReadingWidth(15) >= 74f);
        Assert.True(HudCoreMetrics.ReadingWidth(22) > HudCoreMetrics.ReadingWidth(15));
        Assert.True(HudCoreMetrics.ReadingWidth(0) > 0f);
    }

    [Fact]
    public void TheHudSurfaces_AreTranslucentAndTheKeylineIsDark()
    {
        // Light chrome: a plate lets the world through, and what holds an edge is the keyline.
        Assert.InRange(UiTheme.HudPlateBg.A, 0.4f, 0.85f);
        Assert.InRange(UiTheme.HudSlotBg.A, 0.4f, 0.9f);
        Assert.True(UiTheme.Keyline.Luminance < UiTheme.HudInnerEdge.Luminance);
        Assert.True(UiTheme.HudChunk.Luminance > UiTheme.Health.Luminance);
    }
}
