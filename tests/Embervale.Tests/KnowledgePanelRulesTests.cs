using System.Collections.Generic;
using Embervale.Enemies;
using Embervale.UI;
using Godot;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// The pure rules behind the 2026-10 knowledge panels: the journal's reading measure and objective
/// marks, the map's gamepad cursor, the bestiary's staged page and the dialogue window's pace.
/// </summary>
public class KnowledgePanelRulesTests
{
    // --- Journal -------------------------------------------------------------------------

    [Fact]
    public void ProseWidth_HoldsEightyCharactersAtBodySize()
    {
        float width = JournalLayoutRules.ProseWidth(UiTheme.BodyFontSize);
        Assert.Equal(UiTheme.BodyFontSize * JournalLayoutRules.SerifAdvance * 80, width, 3);
        Assert.True(JournalLayoutRules.ProseWidth(UiTheme.HeaderFontSize) > width);
    }

    [Fact]
    public void ProseInset_TakesOnlyWhatIsBeyondTheMeasure()
    {
        float measure = JournalLayoutRules.ProseWidth(UiTheme.BodyFontSize);
        Assert.Equal(0, JournalLayoutRules.ProseInset(measure - 40f, UiTheme.BodyFontSize));
        Assert.InRange(JournalLayoutRules.ProseInset(measure + 200f, UiTheme.BodyFontSize), 199, 200);
    }

    [Theory]
    [InlineData(600f, 240f)]
    [InlineData(1000f, 320f)]
    [InlineData(3000f, 360f)]
    public void IndexWidth_IsAThirdOfThePageWithinItsBounds(float usable, float expected) =>
        Assert.Equal(expected, JournalLayoutRules.IndexWidth(usable), 3);

    [Theory]
    [InlineData(StageKind.Done, ObjectiveMark.Done)]
    [InlineData(StageKind.OptionalDone, ObjectiveMark.Done)]
    [InlineData(StageKind.Missed, ObjectiveMark.Failed)]
    [InlineData(StageKind.Current, ObjectiveMark.Current)]
    [InlineData(StageKind.Optional, ObjectiveMark.Optional)]
    [InlineData(StageKind.Locked, ObjectiveMark.Locked)]
    public void MarkOf_GivesEveryStageKindItsOwnShape(StageKind kind, ObjectiveMark expected) =>
        Assert.Equal(expected, JournalLayoutRules.MarkOf(kind));

    // --- Map -----------------------------------------------------------------------------

    [Fact]
    public void Nearest_PicksTheClosestPointInsideTheRadius()
    {
        var points = new List<Vector2> { new(100f, 0f), new(20f, 10f), new(-30f, 0f) };
        Assert.Equal(1, MapSnapRules.Nearest(points, Vector2.Zero, MapSnapRules.SnapRadius));
    }

    [Fact]
    public void Nearest_IsMinusOneWhenNothingIsInReach()
    {
        var points = new List<Vector2> { new(100f, 0f), new(0f, -80f) };
        Assert.Equal(-1, MapSnapRules.Nearest(points, Vector2.Zero, MapSnapRules.SnapRadius));
        Assert.Equal(-1, MapSnapRules.Nearest(new List<Vector2>(), Vector2.Zero, MapSnapRules.SnapRadius));
    }

    [Fact]
    public void Nearest_KeepsTheEarlierOfTwoEquallyNearPoints()
    {
        var points = new List<Vector2> { new(10f, 0f), new(-10f, 0f) };
        Assert.Equal(0, MapSnapRules.Nearest(points, Vector2.Zero, MapSnapRules.SnapRadius));
    }

    [Fact]
    public void ClearsWaypoint_OnlyWhenTheCursorIsOnTheMark()
    {
        Assert.True(MapSnapRules.ClearsWaypoint(new Vector2(5f, 5f), Vector2.Zero, 20f));
        Assert.False(MapSnapRules.ClearsWaypoint(new Vector2(50f, 0f), Vector2.Zero, 20f));
        Assert.False(MapSnapRules.ClearsWaypoint(null, Vector2.Zero, 20f));
    }

    [Fact]
    public void StepZoom_StepsBothWaysAndStopsAtTheLimits()
    {
        Assert.Equal(4f * MapSnapRules.ZoomStep, MapSnapRules.StepZoom(4f, 1), 3);
        Assert.Equal(4f / MapSnapRules.ZoomStep, MapSnapRules.StepZoom(4f, -1), 3);
        Assert.Equal(4f, MapSnapRules.StepZoom(4f, 0), 3);
        Assert.Equal(MapProjection.MaxZoom, MapSnapRules.StepZoom(MapProjection.MaxZoom, 1), 3);
        Assert.Equal(MapProjection.MinZoom, MapSnapRules.StepZoom(MapProjection.MinZoom, -1), 3);
    }

    [Theory]
    [InlineData(MapProjection.MinZoom, 100)]
    [InlineData(MapProjection.DefaultZoom, 25)]
    [InlineData(MapTiers.DetailZoom, 10)]
    [InlineData(MapProjection.MaxZoom, 2)]
    public void ScaleBarMetres_IsTheLongestRoundLengthThatFits(float zoom, int expected)
    {
        int metres = MapSnapRules.ScaleBarMetres(zoom, 120f);
        Assert.Equal(expected, metres);
        Assert.True(metres * zoom <= 120f);
    }

    // --- Bestiary ------------------------------------------------------------------------

    [Fact]
    public void ASealedPage_ShowsNothingAndAWrittenOneShowsTheStudy()
    {
        Assert.False(BestiaryFactRules.ShowsName(BestiaryStage.Unseen));
        Assert.True(BestiaryFactRules.ShowsName(BestiaryStage.Sighted));
        Assert.True(BestiaryFactRules.ShowsProgress(BestiaryStage.Sighted));
        Assert.False(BestiaryFactRules.ShowsStudy(BestiaryStage.Sighted));
        Assert.True(BestiaryFactRules.ShowsStudy(BestiaryStage.Known));
        Assert.False(BestiaryFactRules.ShowsProgress(BestiaryStage.Known));
    }

    [Fact]
    public void Resists_NamesTheStrongWardsStrongestFirst()
    {
        var wards = new List<SchoolResist> { new(1, 4f), new(2, 12f), new(3, 0f), new(4, 6f), new(5, 12f) };
        Assert.Equal(new[] { 2, 5, 4 }, BestiaryFactRules.Resists(wards));
    }

    [Fact]
    public void Resists_IsCappedAtThreeChips()
    {
        var wards = new List<SchoolResist> { new(1, 9f), new(2, 9f), new(3, 9f), new(4, 9f) };
        Assert.Equal(BestiaryFactRules.MaxChips, BestiaryFactRules.Resists(wards).Count);
    }

    [Fact]
    public void OpenTo_NamesUnwardedSchoolsOnlyBesideAWard()
    {
        var warded = new List<SchoolResist> { new(1, 8f), new(2, 0f), new(3, 0f) };
        Assert.Equal(new[] { 2, 3 }, BestiaryFactRules.OpenTo(warded));

        // A creature with no ward at all is not "open to" everything: there is nothing to contrast.
        var plain = new List<SchoolResist> { new(1, 0f), new(2, 0f) };
        Assert.Empty(BestiaryFactRules.OpenTo(plain));
        Assert.Empty(BestiaryFactRules.Resists(plain));
    }

    // --- Dialogue ------------------------------------------------------------------------

    [Theory]
    [InlineData(1000UL, 1000UL, true)]
    [InlineData(1299UL, 1000UL, true)]
    [InlineData(1300UL, 1000UL, false)]
    [InlineData(5000UL, 1000UL, false)]
    [InlineData(1000UL, 0UL, false)]
    [InlineData(100UL, 0UL, false)]
    [InlineData(900UL, 1000UL, false)]
    public void InGrace_OnlyJustAfterALineFinishedByItself(ulong now, ulong finished, bool expected)
    {
        Assert.Equal(expected, DialoguePaceRules.InGrace(now, finished));
    }

    [Fact]
    public void Seconds_ScaleWithTheLineAndNeverGoNegative()
    {
        Assert.Equal(0f, DialoguePaceRules.Seconds(0));
        Assert.Equal(0f, DialoguePaceRules.Seconds(-5));
        Assert.Equal(2f, DialoguePaceRules.Seconds((int)(DialoguePaceRules.CharsPerSecond * 2f)), 3);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(3, 3)]
    [InlineData(5, 5)]
    [InlineData(9, 5)]
    public void OptionRows_StopAtFive(int options, int expected) =>
        Assert.Equal(expected, DialoguePaceRules.OptionRows(options));

    [Fact]
    public void MarkOf_ReadsTheConsequenceThenWhetherItLeaves()
    {
        var quest = new List<ConsequenceTag> { new(ConsequenceKind.Reputation, "faction.x", 5), new(ConsequenceKind.Quest, "quest.x", 0) };
        var story = new List<ConsequenceTag> { new(ConsequenceKind.Story, string.Empty, 0) };
        var standing = new List<ConsequenceTag> { new(ConsequenceKind.Reputation, "faction.x", -5) };

        Assert.Equal(OptionMark.Plot, DialoguePaceRules.MarkOf(quest, chosenBefore: false, ends: false));
        Assert.Equal(OptionMark.Plot, DialoguePaceRules.MarkOf(story, chosenBefore: false, ends: true));
        Assert.Equal(OptionMark.Special, DialoguePaceRules.MarkOf(standing, chosenBefore: false, ends: true));
        Assert.Equal(OptionMark.Leave, DialoguePaceRules.MarkOf(null, chosenBefore: false, ends: true));
        Assert.Equal(OptionMark.None, DialoguePaceRules.MarkOf(new List<ConsequenceTag>(), chosenBefore: false, ends: false));
    }

    [Fact]
    public void MarkOf_AnOptionAlreadyAskedIsExhaustedWhateverItCarries()
    {
        var quest = new List<ConsequenceTag> { new(ConsequenceKind.Quest, "quest.x", 0) };
        Assert.Equal(OptionMark.Exhausted, DialoguePaceRules.MarkOf(quest, chosenBefore: true, ends: false));
        Assert.Equal(OptionMark.Exhausted, DialoguePaceRules.MarkOf(null, chosenBefore: true, ends: true));
    }

    [Fact]
    public void Backlog_RemembersChoicesUntilTheConversationEnds()
    {
        var backlog = new DialogueBacklog();
        Assert.False(backlog.WasChosen("a>b"));

        backlog.MarkChosen("a>b");
        Assert.True(backlog.WasChosen("a>b"));
        Assert.False(backlog.WasChosen("a>c"));

        backlog.Clear();
        Assert.False(backlog.WasChosen("a>b"));
    }
}
