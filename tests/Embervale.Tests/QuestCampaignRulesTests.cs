using Embervale.Core;
using Embervale.Narrative;
using Embervale.Quests;
using Xunit;

namespace Embervale.Tests;

/// <summary>Pure rules added by the campaign quest data-model: auto-start, tracking, the updated dot,
/// and the slice closing trigger.</summary>
public class QuestCampaignRulesTests
{
    // --- auto-start ------------------------------------------------------------------------

    [Fact]
    public void AutoStart_FiresOnItsTriggerFlag()
    {
        Assert.True(QuestCompletionRules.ShouldAutoStart(
            "flag.a", "flag.a", true, "flag.a_done", false, false));
    }

    [Fact]
    public void AutoStart_IgnoresOtherFlags()
    {
        Assert.False(QuestCompletionRules.ShouldAutoStart(
            "flag.a", "flag.b", true, "flag.a_done", false, false));
    }

    [Fact]
    public void AutoStart_OnLoadScan_UsesTheHeldFlag()
    {
        Assert.True(QuestCompletionRules.ShouldAutoStart("flag.a", null, true, "", false, false));
        Assert.False(QuestCompletionRules.ShouldAutoStart("flag.a", null, false, "", false, false));
    }

    [Fact]
    public void AutoStart_SkipsAQuestWhoseOwnCompletionFlagIsHeld()
    {
        // The legacy catch-up rule: the world is already past this quest.
        Assert.False(QuestCompletionRules.ShouldAutoStart("flag.a", null, true, "flag.a_done", true, false));
        Assert.False(QuestCompletionRules.ShouldAutoStart("flag.a", "flag.a", true, "flag.a_done", true, false));
    }

    [Fact]
    public void AutoStart_SkipsAQuestAlreadyInTheLog_AndAQuestWithNoTrigger()
    {
        Assert.False(QuestCompletionRules.ShouldAutoStart("flag.a", "flag.a", true, "", false, true));
        Assert.False(QuestCompletionRules.ShouldAutoStart("", "flag.a", true, "", false, false));
    }

    [Fact]
    public void AutoStart_EmptyCompletionFlagNeverBlocks()
    {
        Assert.True(QuestCompletionRules.ShouldAutoStart("flag.a", "flag.a", true, "", true, false));
    }

    // --- tracking --------------------------------------------------------------------------

    [Theory]
    [InlineData(false, false, TrackedKind.None, true)]   // nothing tracked: track the new quest
    [InlineData(true, false, TrackedKind.None, true)]
    [InlineData(false, false, TrackedKind.Side, false)]  // a side quest never steals from a side quest
    [InlineData(true, false, TrackedKind.Side, true)]    // main displaces a side
    [InlineData(false, false, TrackedKind.Main, false)]  // a tracked main quest is never displaced
    [InlineData(true, false, TrackedKind.Main, false)]
    [InlineData(true, true, TrackedKind.None, false)]    // a ledger is never auto-tracked
    [InlineData(false, true, TrackedKind.None, false)]
    public void ShouldTrackOnStart_Policy(bool main, bool ledger, TrackedKind current, bool expected)
    {
        Assert.Equal(expected, QuestTrackingRules.ShouldTrackOnStart(main, ledger, current));
    }

    [Fact]
    public void FallbackRank_PrefersMainThenSide_AndNeverALedger()
    {
        Assert.True(QuestTrackingRules.FallbackRank(true, false) > QuestTrackingRules.FallbackRank(false, false));
        Assert.True(QuestTrackingRules.FallbackRank(false, false) > 0);
        Assert.True(QuestTrackingRules.FallbackRank(true, true) < 0);
        Assert.True(QuestTrackingRules.FallbackRank(false, true) < 0);
    }

    // --- updated dot -----------------------------------------------------------------------

    [Fact]
    public void Signature_ChangesWithStatusAndEveryObjectiveStage()
    {
        int baseline = QuestUpdateRules.Signature(QuestStatus.Active, new[] { 1, 0 });
        Assert.Equal(baseline, QuestUpdateRules.Signature(QuestStatus.Active, new[] { 1, 0 }));
        Assert.NotEqual(baseline, QuestUpdateRules.Signature(QuestStatus.Active, new[] { 2, 1 }));
        Assert.NotEqual(baseline, QuestUpdateRules.Signature(QuestStatus.Active, new[] { 1, 1 }));
        Assert.NotEqual(baseline, QuestUpdateRules.Signature(QuestStatus.Completed, new[] { 1, 0 }));
        Assert.NotEqual(baseline, QuestUpdateRules.Signature(QuestStatus.Active, new[] { 1, 0, 0 }));
    }

    [Fact]
    public void Signature_FormulaIsPinned_BecauseItIsPersistedInSaves()
    {
        // Changing the formula would light every dot on load. h = (status+1)*7919, then h*31 + stage + 1.
        int h = 7919;
        foreach (int stage in new[] { 2, 1, 0 })
        {
            h = (h * 31) + stage + 1;
        }

        Assert.Equal(h, QuestUpdateRules.Signature(QuestStatus.Active, new[] { 2, 1, 0 }));
    }

    [Fact]
    public void Stage_CompleteBeatsActive()
    {
        Assert.Equal(QuestUpdateRules.Complete, QuestUpdateRules.Stage(true, true));
        Assert.Equal(QuestUpdateRules.Active, QuestUpdateRules.Stage(true, false));
        Assert.Equal(QuestUpdateRules.Inert, QuestUpdateRules.Stage(false, false));
    }

    [Fact]
    public void IsUpdated_OnlyWhileActiveAndSignatureDiffers()
    {
        Assert.False(QuestUpdateRules.IsUpdated(5, 5, QuestStatus.Active));      // seen
        Assert.True(QuestUpdateRules.IsUpdated(5, 6, QuestStatus.Active));       // progressed since
        Assert.True(QuestUpdateRules.IsUpdated(null, 6, QuestStatus.Active));    // brand new quest
        Assert.False(QuestUpdateRules.IsUpdated(null, 6, QuestStatus.Completed));
        Assert.False(QuestUpdateRules.IsUpdated(5, 6, QuestStatus.Failed));
    }

    // --- slice closing ---------------------------------------------------------------------

    private const string Crown = GameIds.Regions.EmberCrown;
    private const string Frost = GameIds.Regions.FrostfangReach;

    [Fact]
    public void SliceClosing_PlaysOnFirstDepartureFromTheCrownAfterTheIronKing()
    {
        Assert.True(SliceClosingRules.ShouldPlay(true, false, Crown, Frost));
        Assert.True(SliceClosingRules.ShouldPlay(true, false, Crown, GameIds.Regions.AshenWilds));
    }

    [Fact]
    public void SliceClosing_NeverBeforeTheIronKingFalls_OrTwice()
    {
        Assert.False(SliceClosingRules.ShouldPlay(false, false, Crown, Frost));
        Assert.False(SliceClosingRules.ShouldPlay(true, true, Crown, Frost));
    }

    [Fact]
    public void SliceClosing_IgnoresChangesThatAreNotADepartureFromTheCrown()
    {
        Assert.False(SliceClosingRules.ShouldPlay(true, false, Frost, Crown));      // coming back
        Assert.False(SliceClosingRules.ShouldPlay(true, false, Frost, GameIds.Regions.AshenWilds));
        Assert.False(SliceClosingRules.ShouldPlay(true, false, "", Frost));          // stale session start
        Assert.False(SliceClosingRules.ShouldPlay(true, false, Crown, Crown));
        Assert.False(SliceClosingRules.ShouldPlay(true, false, Crown, ""));
    }
}
