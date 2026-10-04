using System.Collections.Generic;
using System.Linq;
using Embervale.Dialogue;
using Embervale.Factions;
using Embervale.Quests;
using Embervale.UI;
using Embervale.World;
using Xunit;

namespace Embervale.Tests;

/// <summary>The Godot-free rules behind the campaign quest UI: journal grouping, stage log, notices,
/// tracker timing, chapter banners, dialogue tags/backlog, compass routing, map pins and boss intro.</summary>
public class QuestUiRulesTests
{
    private static ObjectiveState Obj(int i, bool optional = false, bool active = true, bool complete = false, bool inBranch = true) =>
        new(i, optional, active, complete, inBranch);

    private static JournalEntry Q(
        string id, QuestStatus status = QuestStatus.Active, bool main = true, bool ledger = false,
        string chapter = "", int order = 0, int seq = 0) =>
        new(id, status, main, ledger, chapter, order, seq);

    // --- objective focus -------------------------------------------------------------------

    [Fact]
    public void Focus_PrefersRequiredOverOptional()
    {
        var states = new[] { Obj(0, optional: true), Obj(1) };
        Assert.Equal(1, ObjectiveFocusRules.Current(states));
    }

    [Fact]
    public void Focus_FallsBackToOptionalWhenNoRequiredIsLive()
    {
        var states = new[] { Obj(0, complete: true), Obj(1, optional: true), Obj(2, active: false) };
        Assert.Equal(1, ObjectiveFocusRules.Current(states));
    }

    [Fact]
    public void Focus_IgnoresLockedOutOfBranchAndDone()
    {
        var states = new[] { Obj(0, inBranch: false), Obj(1, active: false), Obj(2, complete: true) };
        Assert.Equal(-1, ObjectiveFocusRules.Current(states));
        Assert.Empty(ObjectiveFocusRules.LiveRequired(states));
    }

    [Fact]
    public void Focus_LiveRequiredKeepsAuthoredOrder()
    {
        var states = new[] { Obj(0), Obj(1, optional: true), Obj(2) };
        Assert.Equal(new[] { 0, 2 }, ObjectiveFocusRules.LiveRequired(states));
    }

    // --- journal index ---------------------------------------------------------------------

    [Fact]
    public void Journal_MainGroupsByChapterOrderedByEarliestQuest()
    {
        var entries = new[]
        {
            Q("quest.c", chapter: "ch.2.ashen", order: 13, seq: 3),
            Q("quest.a", chapter: "ch.1", order: 2, seq: 1),
            Q("quest.b", chapter: "ch.1", order: 1, seq: 2),
            Q("quest.d", chapter: "ch.2.frostfang", order: 10, seq: 4),
        };

        List<JournalGroup> groups = JournalIndexRules.MainGroups(entries);

        Assert.Equal(new[] { "ch.1", "ch.2.frostfang", "ch.2.ashen" }, groups.Select(g => g.ChapterKey));
        Assert.Equal(new[] { "quest.b", "quest.a" }, groups[0].Entries.Select(e => e.Id));
    }

    [Fact]
    public void Journal_ChapterTiesBreakByKey()
    {
        var entries = new[]
        {
            Q("quest.x", chapter: "ch.2.sunspire", order: 5),
            Q("quest.y", chapter: "ch.2.ashen", order: 5),
        };

        Assert.Equal(
            new[] { "ch.2.ashen", "ch.2.sunspire" },
            JournalIndexRules.MainGroups(entries).Select(g => g.ChapterKey));
    }

    [Fact]
    public void Journal_KeylessQuestsFormTheirOwnGroupAndNeedNoHeadings()
    {
        var entries = new[] { Q("quest.a", order: 2), Q("quest.b", order: 1) };
        List<JournalGroup> groups = JournalIndexRules.MainGroups(entries);

        Assert.Single(groups);
        Assert.Equal(string.Empty, groups[0].ChapterKey);
        Assert.False(JournalIndexRules.NeedsChapterHeadings(groups));
        Assert.True(JournalIndexRules.NeedsChapterHeadings(
            JournalIndexRules.MainGroups(new[] { Q("quest.a", chapter: "ch.1") })));
    }

    [Fact]
    public void Journal_MainExcludesLedgerErrandsAndFinished()
    {
        var entries = new[]
        {
            Q("quest.main"),
            Q("quest.ledger", ledger: true),
            Q("quest.side", main: false),
            Q("quest.done", status: QuestStatus.Completed),
        };

        Assert.Equal(new[] { "quest.main" }, JournalIndexRules.MainGroups(entries).SelectMany(g => g.Entries).Select(e => e.Id));
        Assert.Equal(new[] { "quest.side" }, JournalIndexRules.Errands(entries).Select(e => e.Id));
        Assert.Equal(new[] { "quest.ledger" }, JournalIndexRules.Ledger(entries).Select(e => e.Id));
    }

    [Fact]
    public void Journal_SectionsOnlyExistWithState()
    {
        Assert.Empty(JournalIndexRules.Sections(new JournalEntry[0]));

        var entries = new[] { Q("quest.a", main: false), Q("quest.b", status: QuestStatus.Failed), Q("quest.c", ledger: true) };
        Assert.Equal(
            new[] { JournalSection.Main, JournalSection.Errands, JournalSection.Failed },
            JournalIndexRules.Sections(entries));
    }

    [Fact]
    public void Journal_CompletedIsNewestFirstAndFoldsAfterFive()
    {
        JournalEntry[] entries = Enumerable.Range(0, 8)
            .Select(i => Q($"quest.{i}", status: QuestStatus.Completed, seq: i))
            .ToArray();

        List<JournalEntry> shown = JournalIndexRules.Completed(entries, showAll: false, out int hidden);
        Assert.Equal(5, shown.Count);
        Assert.Equal(3, hidden);
        Assert.Equal("quest.7", shown[0].Id);

        List<JournalEntry> all = JournalIndexRules.Completed(entries, showAll: true, out hidden);
        Assert.Equal(8, all.Count);
        Assert.Equal(0, hidden);
    }

    [Fact]
    public void Journal_FiveCompletedDoNotFold()
    {
        JournalEntry[] entries = Enumerable.Range(0, 5)
            .Select(i => Q($"quest.{i}", status: QuestStatus.Completed, seq: i))
            .ToArray();

        JournalIndexRules.Completed(entries, showAll: false, out int hidden);
        Assert.Equal(0, hidden);
    }

    [Fact]
    public void Journal_DefaultSectionFollowsSelectionThenTracked()
    {
        var entries = new[] { Q("quest.main"), Q("quest.side", main: false), Q("quest.done", status: QuestStatus.Completed) };

        Assert.Equal(JournalSection.Completed, JournalIndexRules.DefaultSection(entries, "quest.done", "quest.main"));
        Assert.Equal(JournalSection.Errands, JournalIndexRules.DefaultSection(entries, null, "quest.side"));
        Assert.Equal(JournalSection.Main, JournalIndexRules.DefaultSection(entries, "quest.gone", null));
        Assert.Null(JournalIndexRules.DefaultSection(new JournalEntry[0], null, null));
    }

    [Fact]
    public void Journal_StepWrapsBothWays()
    {
        var sections = new[] { JournalSection.Main, JournalSection.Completed };
        Assert.Equal(JournalSection.Completed, JournalIndexRules.Step(sections, JournalSection.Main, 1));
        Assert.Equal(JournalSection.Main, JournalIndexRules.Step(sections, JournalSection.Completed, 1));
        Assert.Equal(JournalSection.Completed, JournalIndexRules.Step(sections, JournalSection.Main, -1));
        Assert.Equal(JournalSection.Main, JournalIndexRules.Step(sections, JournalSection.Failed, 1));
    }

    [Fact]
    public void Journal_ChapterTitleKeysTryMainThenPale()
    {
        Assert.Equal(
            new[] { "chapter.ch.2.pale.title", "pale.chapter.ch.2.pale.title" },
            JournalIndexRules.ChapterTitleKeys("ch.2.pale"));
        Assert.Empty(JournalIndexRules.ChapterTitleKeys(string.Empty));
        Assert.Equal(
            "pale.chapter.k.title",
            JournalIndexRules.FirstResolving(JournalIndexRules.ChapterTitleKeys("k"), k => k.StartsWith("pale.")));
        Assert.Null(JournalIndexRules.FirstResolving(JournalIndexRules.ChapterTitleKeys("k"), _ => false));
    }

    // --- stage log -------------------------------------------------------------------------

    [Fact]
    public void StageLog_ReadsDoneThenCurrentThenOptionalThenLocked()
    {
        var states = new[]
        {
            Obj(0, active: false),
            Obj(1, optional: true),
            Obj(2),
            Obj(3, complete: true),
            Obj(4, optional: true, complete: true),
        };

        List<StageLine> lines = StageLogRules.Build(states, questActive: true);

        Assert.Equal(
            new[]
            {
                new StageLine(3, StageKind.Done), new StageLine(4, StageKind.OptionalDone),
                new StageLine(2, StageKind.Current), new StageLine(1, StageKind.Optional),
                new StageLine(0, StageKind.Locked),
            },
            lines);
    }

    [Fact]
    public void StageLog_HidesObjectivesOnTheBranchNotTaken()
    {
        var states = new[] { Obj(0, inBranch: false), Obj(1) };
        Assert.Equal(new[] { new StageLine(1, StageKind.Current) }, StageLogRules.Build(states, true));
    }

    [Fact]
    public void StageLog_FinishedQuestDoesNotInventCurrentSteps()
    {
        var states = new[] { Obj(0, complete: true), Obj(1, optional: true), Obj(2, active: false) };
        List<StageLine> lines = StageLogRules.Build(states, questActive: false);

        Assert.DoesNotContain(lines, l => l.Kind == StageKind.Current);
        Assert.Contains(new StageLine(2, StageKind.Missed), lines);
        Assert.Contains(new StageLine(1, StageKind.Optional), lines);
    }

    [Fact]
    public void StageLog_TextPrefersAResolvableJournalEntry()
    {
        Assert.Equal("log.key", StageLogRules.LogTextKey("log.key", "desc.key", _ => true));
        Assert.Equal("desc.key", StageLogRules.LogTextKey("log.key", "desc.key", _ => false));
        Assert.Equal("desc.key", StageLogRules.LogTextKey(string.Empty, "desc.key", _ => true));
    }

    [Fact]
    public void StageLog_HintsOnlyOnLiveLinesWithAResolvableKey()
    {
        Assert.True(StageLogRules.ShowsHint(StageKind.Current, "hint.k", _ => true));
        Assert.True(StageLogRules.ShowsHint(StageKind.Optional, "hint.k", _ => true));
        Assert.False(StageLogRules.ShowsHint(StageKind.Done, "hint.k", _ => true));
        Assert.False(StageLogRules.ShowsHint(StageKind.Current, string.Empty, _ => true));
        Assert.False(StageLogRules.ShowsHint(StageKind.Current, "hint.k", _ => false));
    }

    // --- tracker ---------------------------------------------------------------------------

    [Fact]
    public void Tracker_HintAppearsAfterNinetySecondsOnOneObjective()
    {
        var dwell = new ObjectiveDwell();
        dwell.Tick("q:0", 89f);
        Assert.False(TrackerRules.ShouldShowHint(true, dwell.Seconds));
        dwell.Tick("q:0", 1f);
        Assert.True(TrackerRules.ShouldShowHint(true, dwell.Seconds));
        Assert.False(TrackerRules.ShouldShowHint(false, dwell.Seconds));
    }

    [Fact]
    public void Tracker_DwellResetsWhenTheObjectiveChanges()
    {
        var dwell = new ObjectiveDwell();
        dwell.Tick("q:0", 120f);
        dwell.Tick("q:1", 1f);
        Assert.Equal(1f, dwell.Seconds);
        dwell.Tick(null, 50f);
        Assert.Equal(0f, dwell.Seconds);
    }

    [Fact]
    public void Tracker_TitleFlashSkipsFirstObservationThenHoldsThreeSeconds()
    {
        var flash = new TrackerTitleFlash();
        flash.Observe("quest.a", 0.016f);
        Assert.False(flash.Active);

        flash.Observe("quest.b", 0.016f);
        Assert.True(flash.Active);
        flash.Observe("quest.b", 2.9f);
        Assert.True(flash.Active);
        flash.Observe("quest.b", 0.2f);
        Assert.False(flash.Active);
    }

    [Fact]
    public void Tracker_ClearingTheTrackedQuestAnnouncesNothing()
    {
        var flash = new TrackerTitleFlash();
        flash.Observe("quest.a", 0f);
        flash.Observe(null, 0f);
        Assert.False(flash.Active);
    }

    // --- notices ---------------------------------------------------------------------------

    [Fact]
    public void Notices_StageThatFinishesOneStepAndOpensTheNextIsOneToast()
    {
        var c = new QuestNoticeCoalescer();
        c.OnObjectiveCompleted("q", 0, optional: false);
        c.OnObjectiveActivated("q", 1, optional: false);

        List<QuestNoticeIntent> intents = c.Flush();

        Assert.Equal(new[] { new QuestNoticeIntent(QuestNoticeKind.NewObjective, "q", 1, 0) }, intents);
    }

    [Fact]
    public void Notices_CompletionSwallowsEverythingElseForThatQuest()
    {
        var c = new QuestNoticeCoalescer();
        c.OnObjectiveCompleted("q", 2, optional: false);
        c.OnObjectiveCompleted("q", 3, optional: true);
        c.OnCompleted("q");

        Assert.Equal(new[] { new QuestNoticeIntent(QuestNoticeKind.Completed, "q", -1, -1) }, c.Flush());
    }

    [Fact]
    public void Notices_StartAbsorbsTheFirstObjectiveActivation()
    {
        var c = new QuestNoticeCoalescer();
        c.OnStarted("q");
        c.OnObjectiveActivated("q", 0, optional: false);

        Assert.Equal(new[] { new QuestNoticeIntent(QuestNoticeKind.Started, "q", 0, -1) }, c.Flush());
    }

    [Fact]
    public void Notices_AStepMetWithNothingNewIsAnUpdate()
    {
        var c = new QuestNoticeCoalescer();
        c.OnObjectiveCompleted("q", 1, optional: false);

        Assert.Equal(new[] { new QuestNoticeIntent(QuestNoticeKind.Updated, "q", 1, 1) }, c.Flush());
    }

    [Fact]
    public void Notices_OptionalDoneIsItsOwnToast_AndOptionalActivationIsSilent()
    {
        var c = new QuestNoticeCoalescer();
        c.OnObjectiveActivated("q", 2, optional: true);
        c.OnObjectiveCompleted("q", 2, optional: true);
        c.OnObjectiveCompleted("q", 2, optional: true);

        Assert.Equal(new[] { new QuestNoticeIntent(QuestNoticeKind.OptionalDone, "q", 2, -1) }, c.Flush());
    }

    [Fact]
    public void Notices_FailureOutranksProgress_AndFlushClears()
    {
        var c = new QuestNoticeCoalescer();
        c.OnObjectiveCompleted("q", 0, optional: false);
        c.OnFailed("q");

        Assert.Single(c.Flush(), i => i.Kind == QuestNoticeKind.Failed);
        Assert.False(c.HasPending);
        Assert.Empty(c.Flush());
    }

    [Fact]
    public void Notices_QuestsAreIndependentAndKeepArrivalOrder()
    {
        var c = new QuestNoticeCoalescer();
        c.OnObjectiveActivated("b", 0, optional: false);
        c.OnObjectiveActivated("a", 0, optional: false);

        Assert.Equal(new[] { "b", "a" }, c.Flush().Select(i => i.QuestId));
    }

    [Fact]
    public void Notices_CuesMapToTheFiveRegisteredNames()
    {
        Assert.Equal("ui.quest.started", QuestNoticeCues.For(QuestNoticeKind.Started));
        Assert.Equal("ui.quest.updated", QuestNoticeCues.For(QuestNoticeKind.NewObjective));
        Assert.Equal("ui.quest.updated", QuestNoticeCues.For(QuestNoticeKind.Updated));
        Assert.Equal("ui.quest.completed", QuestNoticeCues.For(QuestNoticeKind.Completed));
        Assert.Equal("ui.objective.optional", QuestNoticeCues.For(QuestNoticeKind.OptionalDone));
        Assert.Null(QuestNoticeCues.For(QuestNoticeKind.Failed));
    }

    // --- chapter banner --------------------------------------------------------------------

    [Theory]
    [InlineData("ch.1", 1)]
    [InlineData("ch.2.frostfang", 2)]
    [InlineData("ch.3", 3)]
    [InlineData("chapter.act1", 1)]
    [InlineData("ch.12", 12)]
    public void Banner_ActNumberIsTheFirstDigitRun(string key, int act) =>
        Assert.Equal(act, ChapterBannerRules.ActNumber(key));

    [Theory]
    [InlineData("")]
    [InlineData("prologue")]
    public void Banner_KeysWithoutDigitsHaveNoAct(string key) => Assert.Null(ChapterBannerRules.ActNumber(key));

    [Fact]
    public void Banner_RomanNumeralsAndFlag()
    {
        Assert.Equal("IV", ChapterBannerRules.Roman(4));
        Assert.Equal("11", ChapterBannerRules.Roman(11));
        Assert.Equal("flag.chapter.ch.2.pale", ChapterBannerRules.FlagFor("ch.2.pale"));
        Assert.Equal(
            new[] { "chapter.ch.3.title", "pale.chapter.ch.3.title" }, ChapterBannerRules.TitleKeys("ch.3"));
        Assert.Equal(
            new[] { "chapter.ch.3.subtitle", "pale.chapter.ch.3.subtitle" }, ChapterBannerRules.SubtitleKeys("ch.3"));
    }

    [Fact]
    public void Banner_ShownOncePerSaveAndOnlyWithText()
    {
        Assert.True(ChapterBannerRules.ShouldShow("ch.1", flagHeld: false, hasTitle: true));
        Assert.False(ChapterBannerRules.ShouldShow("ch.1", flagHeld: true, hasTitle: true));
        Assert.False(ChapterBannerRules.ShouldShow("ch.1", flagHeld: false, hasTitle: false));
        Assert.False(ChapterBannerRules.ShouldShow(string.Empty, flagHeld: false, hasTitle: true));
    }

    [Fact]
    public void Banner_QueueIsFifoAndDedupes()
    {
        var q = new BannerQueue();
        Assert.True(q.Enqueue("ch.1"));
        Assert.False(q.Enqueue("ch.1"));
        Assert.True(q.Enqueue("ch.2"));
        Assert.False(q.Enqueue(string.Empty));

        Assert.True(q.TryDequeue(out string first));
        Assert.Equal("ch.1", first);
        Assert.False(q.Enqueue("ch.1")); // still on screen
        q.Done("ch.1");
        Assert.True(q.Enqueue("ch.1"));
        Assert.Equal(2, q.Count);
    }

    [Fact]
    public void Banner_TimelineFadesInHoldsAndFadesOut()
    {
        Assert.Equal(0f, BannerTimeline.At(0f, motion: true).Alpha, 3);
        Assert.Equal(1f, BannerTimeline.At(BannerTimeline.FadeIn + 0.1f, motion: true).Alpha, 3);
        Assert.Equal(1f, BannerTimeline.At(BannerTimeline.FadeIn + BannerTimeline.Hold - 0.01f, motion: true).Alpha, 3);

        BannerFrame leaving = BannerTimeline.At(BannerTimeline.Total - 0.1f, motion: true);
        Assert.InRange(leaving.Alpha, 0.001f, 0.99f);
        Assert.False(leaving.Finished);
        Assert.True(BannerTimeline.At(BannerTimeline.Total, motion: true).Finished);
    }

    [Fact]
    public void Banner_ReducedMotionKeepsTheFadeButDropsTheRise()
    {
        BannerFrame moving = BannerTimeline.At(0.1f, motion: true);
        BannerFrame still = BannerTimeline.At(0.1f, motion: false);

        Assert.True(moving.Rise > 0f);
        Assert.Equal(0f, still.Rise);
        Assert.Equal(moving.Alpha, still.Alpha, 5);
        Assert.InRange(still.Alpha, 0.01f, 0.99f);
    }

    // --- rewards ---------------------------------------------------------------------------

    [Theory]
    [InlineData(25, ReputationTier.Honored)]
    [InlineData(5, ReputationTier.Friendly)]
    [InlineData(0, ReputationTier.Neutral)]
    [InlineData(-3, ReputationTier.Unfriendly)]
    [InlineData(-15, ReputationTier.Hostile)]
    public void Rewards_FactionTierReadsTheSignAndSizeOfTheChange(int amount, ReputationTier tier) =>
        Assert.Equal(tier, RewardRules.FactionTier(amount));

    [Fact]
    public void Rewards_SignIsAlwaysPrintedForGains() =>
        Assert.Equal(new[] { "+5", "-5", "0" }, new[] { RewardRules.Signed(5), RewardRules.Signed(-5), RewardRules.Signed(0) });

    // --- dialogue tags ---------------------------------------------------------------------

    [Fact]
    public void Tags_QuestStartCorruptionAndStory()
    {
        Assert.Equal(
            new ConsequenceTag(ConsequenceKind.Quest, "quest.x", 0),
            DialogueConsequenceTags.Of((int)DialogueEffect.StartQuest, "quest.x"));
        Assert.Equal(
            new ConsequenceTag(ConsequenceKind.Corruption, string.Empty, 5),
            DialogueConsequenceTags.Of((int)DialogueEffect.AddCorruption, "5"));
        Assert.Equal(
            new ConsequenceTag(ConsequenceKind.Corruption, string.Empty, -10),
            DialogueConsequenceTags.Of((int)DialogueEffect.AddCorruption, "-10"));
        Assert.Equal(ConsequenceKind.Story, DialogueConsequenceTags.Of((int)DialogueEffect.SetFlag, "flag.x")!.Value.Kind);
        Assert.Equal(ConsequenceKind.Story, DialogueConsequenceTags.Of(DialogueConsequenceTags.PlayCards, "cards.x")!.Value.Kind);
    }

    [Fact]
    public void Tags_ZeroOrMalformedAmountsShowNothing()
    {
        Assert.Null(DialogueConsequenceTags.Of((int)DialogueEffect.AddCorruption, "0"));
        Assert.Null(DialogueConsequenceTags.Of((int)DialogueEffect.AddCorruption, "many"));
        Assert.Null(DialogueConsequenceTags.Of(DialogueConsequenceTags.AddReputation, "faction.x:0"));
        Assert.Null(DialogueConsequenceTags.Of((int)DialogueEffect.AddCompanionLoyalty, "companion.kael:0"));
    }

    [Fact]
    public void Tags_ReputationLoyaltyItemsAndGuilds()
    {
        Assert.Equal(
            new ConsequenceTag(ConsequenceKind.Reputation, "faction.x", -4),
            DialogueConsequenceTags.Of(DialogueConsequenceTags.AddReputation, "faction.x:-4"));
        Assert.Equal(
            new ConsequenceTag(ConsequenceKind.Loyalty, "companion.kael", 1),
            DialogueConsequenceTags.Of((int)DialogueEffect.AddCompanionLoyalty, "companion.kael:1"));
        Assert.Equal(
            new ConsequenceTag(ConsequenceKind.Item, "item.x", 2),
            DialogueConsequenceTags.Of(DialogueConsequenceTags.GiveItem, "item.x:2"));
        Assert.Equal(
            new ConsequenceTag(ConsequenceKind.Item, "item.x", -1),
            DialogueConsequenceTags.Of(DialogueConsequenceTags.TakeItem, "item.x"));
        Assert.Equal(
            new ConsequenceTag(ConsequenceKind.Guild, "faction.g", 0),
            DialogueConsequenceTags.Of((int)DialogueEffect.JoinGuild, "faction.g"));
        Assert.Equal(
            new ConsequenceTag(ConsequenceKind.Guild, "faction.g", 2),
            DialogueConsequenceTags.Of((int)DialogueEffect.GuildRank, "faction.g:2"));
    }

    [Fact]
    public void Tags_NonConsequencesAreSilent()
    {
        foreach (int effect in new[]
        {
            (int)DialogueEffect.None, (int)DialogueEffect.ClearFlag, (int)DialogueEffect.OpenShop,
            (int)DialogueEffect.OpenService, (int)DialogueEffect.LearnSpell,
            DialogueConsequenceTags.TrackQuest, DialogueConsequenceTags.Banner, 99,
        })
        {
            Assert.Null(DialogueConsequenceTags.Of(effect, "x"));
        }
    }

    [Fact]
    public void Tags_ChoiceCombinesBothEffectsAndTheTargetNode_WithoutDuplicates()
    {
        List<ConsequenceTag> tags = DialogueConsequenceTags.ForChoice(
            (int)DialogueEffect.SetFlag, "flag.a",
            (int)DialogueEffect.AddCorruption, "5",
            DialogueConsequenceTags.PlayCards, "cards.b");

        Assert.Equal(2, tags.Count);
        Assert.Equal(ConsequenceKind.Story, tags[0].Kind);
        Assert.Equal(ConsequenceKind.Corruption, tags[1].Kind);
    }

    // --- dialogue backlog ------------------------------------------------------------------

    [Fact]
    public void Backlog_RecentKeepsTheLastThreeEarlierLinesWithTheChoicesBetween()
    {
        var log = new DialogueBacklog();
        log.AddLine("one");
        log.AddChoice("c1");
        log.AddLine("two");
        log.AddChoice("c2");
        log.AddLine("three");
        log.AddChoice("c3");
        log.AddLine("four");
        log.AddChoice("c4");
        log.AddLine("five"); // current line, not part of the history

        Assert.Equal(
            new[] { "two", "c2", "three", "c3", "four", "c4" },
            log.Recent().Select(e => e.Text));
    }

    [Fact]
    public void Backlog_EmptyUntilThereIsAnEarlierLine()
    {
        var log = new DialogueBacklog();
        Assert.Empty(log.Recent());
        log.AddLine("first");
        Assert.Empty(log.Recent());
    }

    [Fact]
    public void Backlog_RepeatedRebuildOfOneNodeIsOneLine()
    {
        var log = new DialogueBacklog();
        log.AddLine("same");
        log.AddLine("same");
        Assert.Equal(1, log.Count);
        log.Clear();
        Assert.Equal(0, log.Count);
    }

    [Fact]
    public void QuestContext_FindsOnlyALiveTalkObjectiveForThatDialogue()
    {
        var candidates = new List<DialogueQuestContext.TalkCandidate>
        {
            new(0, IsTalk: true, "dialogue.elder", Live: false),
            new(1, IsTalk: false, "dialogue.elder", Live: true),
            new(2, IsTalk: true, "dialogue.smith", Live: true),
            new(3, IsTalk: true, "dialogue.elder", Live: true),
        };

        Assert.Equal(3, DialogueQuestContext.Find("dialogue.elder", candidates));
        Assert.Equal(2, DialogueQuestContext.Find("dialogue.smith", candidates));
        Assert.Equal(-1, DialogueQuestContext.Find("dialogue.nobody", candidates));
        Assert.Equal(-1, DialogueQuestContext.Find(string.Empty, candidates));
    }

    // --- compass ---------------------------------------------------------------------------

    [Theory]
    [InlineData("region.a", "region.a", "region.a", false, CompassMode.Direct)]
    [InlineData("region.b", "region.a", "region.a", true, CompassMode.Portal)] // location in another realm wins over a live look-alike
    [InlineData("", "region.b", "region.a", false, CompassMode.Portal)]
    [InlineData("", "region.b", "region.a", true, CompassMode.Direct)]
    [InlineData("", "", "region.a", false, CompassMode.Direct)]
    [InlineData("region.b", "region.b", "", false, CompassMode.Direct)]
    public void Compass_ModeFollowsTheObjectiveRegion(
        string objective, string quest, string current, bool live, CompassMode expected) =>
        Assert.Equal(expected, CompassRoutingRules.ModeFor(objective, quest, current, live));

    [Fact]
    public void Compass_DestinationPrefersTheObjectiveRegion()
    {
        Assert.Equal("region.b", CompassRoutingRules.DestinationRegion("region.b", "region.c"));
        Assert.Equal("region.c", CompassRoutingRules.DestinationRegion(string.Empty, "region.c"));
    }

    [Fact]
    public void Compass_MarkStyleFollowsQuestKindAndOptionality()
    {
        Assert.Equal(CompassMarkKind.Main, CompassRoutingRules.KindOf(true));
        Assert.Equal(CompassMarkKind.Side, CompassRoutingRules.KindOf(false));
        Assert.Equal(CompassMarkState.Optional, CompassRoutingRules.StateOf(true));
        Assert.Equal(CompassMarkState.Active, CompassRoutingRules.StateOf(false));
    }

    private static Dictionary<string, IReadOnlyList<string>> Graph() => new()
    {
        ["crown"] = new[] { "frost", "ashen", "sun" },
        ["frost"] = new[] { "crown" },
        ["ashen"] = new[] { "crown" },
        ["sun"] = new[] { "crown", "pale", "celestial" },
        ["pale"] = new[] { "sun" },
        ["celestial"] = new[] { "sun" },
    };

    [Fact]
    public void Route_NextHopIsTheFirstDoorOnTheShortestRoute()
    {
        Assert.Equal("sun", CompassRoutingRules.NextHop("crown", "pale", Graph()));
        Assert.Equal("crown", CompassRoutingRules.NextHop("frost", "celestial", Graph()));
        Assert.Equal("frost", CompassRoutingRules.NextHop("crown", "frost", Graph()));
    }

    [Fact]
    public void Route_NullWhenThereOrUnreachableOrSealed()
    {
        Assert.Null(CompassRoutingRules.NextHop("crown", "crown", Graph()));
        Assert.Null(CompassRoutingRules.NextHop("crown", "nowhere", Graph()));
        Assert.Null(CompassRoutingRules.NextHop("missing", "crown", Graph()));
        Assert.Null(CompassRoutingRules.NextHop("crown", "pale", Graph(), region => region != "pale"));
        Assert.Null(CompassRoutingRules.NextHop("crown", "pale", Graph(), region => region != "sun"));
    }

    // --- map pins --------------------------------------------------------------------------

    private static QuestObjectiveSite Site(string loc, bool live = true, bool optional = false) => new(loc, optional, live);

    [Fact]
    public void Map_StartRevealsOnlyLiveObjectivePlaces()
    {
        var sites = new[] { Site("loc.a"), Site("loc.b", live: false), Site("loc.a"), Site(string.Empty), Site("loc.c") };
        Assert.Equal(new[] { "loc.a", "loc.c" }, MapQuestReveal.RevealNow(sites));
    }

    [Fact]
    public void Map_ActivationRevealsThatObjectivesPlaceOnly()
    {
        Assert.Equal("loc.x", MapQuestReveal.RevealOnActivation(Site("loc.x")));
        Assert.Null(MapQuestReveal.RevealOnActivation(Site("loc.x", live: false)));
        Assert.Null(MapQuestReveal.RevealOnActivation(Site(string.Empty)));
    }

    [Fact]
    public void Map_PinsCoverEveryLiveQuestButOnlyTheTrackedOneIsRinged()
    {
        var quests = new[]
        {
            new QuestPinSource("q.main", true, false, true, true, new[] { Site("loc.main") }),
            new QuestPinSource("q.side", false, false, true, false, new[] { Site("loc.side") }),
            new QuestPinSource("q.ledger", true, true, true, false, new[] { Site("loc.ledger") }),
            new QuestPinSource("q.done", true, false, false, false, new[] { Site("loc.done") }),
        };

        List<QuestPin> pins = MapQuestPinRules.Pins(quests);

        Assert.Equal(
            new[] { new QuestPin("loc.main", true, true), new QuestPin("loc.side", false, false) }, pins);
        Assert.Single(pins, p => p.Tracked);
    }

    [Fact]
    public void Map_PinUsesTheCurrentObjective_RequiredBeforeOptional()
    {
        var quest = new QuestPinSource(
            "q", true, false, true, true,
            new[] { Site("loc.opt", optional: true), Site("loc.req") });

        Assert.Equal("loc.req", MapQuestPinRules.Pins(new[] { quest })[0].LocationId);

        var onlyOptional = new QuestPinSource(
            "q", true, false, true, true,
            new[] { Site("loc.req", live: false), Site("loc.opt", optional: true) });
        Assert.Equal("loc.opt", MapQuestPinRules.Pins(new[] { onlyOptional })[0].LocationId);
    }

    [Fact]
    public void Map_FirstOutstandingObjectiveWithNoPlaceMeansNoPin()
    {
        var quest = new QuestPinSource("q", true, false, true, true, new[] { Site(string.Empty), Site("loc.later") });
        Assert.Empty(MapQuestPinRules.Pins(new[] { quest }));
    }

    [Fact]
    public void Map_SharedPlaceKeepsTheTrackedStyling()
    {
        var quests = new[]
        {
            new QuestPinSource("q.side", false, false, true, false, new[] { Site("loc.shared") }),
            new QuestPinSource("q.main", true, false, true, true, new[] { Site("loc.shared") }),
        };

        Assert.Equal(new[] { new QuestPin("loc.shared", true, true) }, MapQuestPinRules.Pins(quests));
    }

    // --- boss intro ------------------------------------------------------------------------

    [Fact]
    public void Boss_EpithetOnlyWhenAuthoredAndResolvable()
    {
        Assert.Equal("boss.epithet", BossIntroText.Epithet("boss.epithet", _ => true));
        Assert.Null(BossIntroText.Epithet("boss.epithet", _ => false));
        Assert.Null(BossIntroText.Epithet(string.Empty, _ => true));
    }

    [Fact]
    public void Boss_IntroFallsBackToTheGenericLine()
    {
        Assert.Equal("boss.line", BossIntroText.IntroKey("boss.line", _ => true, out bool usesName));
        Assert.False(usesName);

        Assert.Equal(BossIntroText.FallbackIntroKey, BossIntroText.IntroKey("boss.line", _ => false, out usesName));
        Assert.True(usesName);

        Assert.Equal(BossIntroText.FallbackIntroKey, BossIntroText.IntroKey(string.Empty, _ => true, out usesName));
        Assert.True(usesName);
    }
}
