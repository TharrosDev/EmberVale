using System;
using System.Collections.Generic;
using System.Linq;
using Embervale.Narrative;
using Xunit;

namespace Embervale.Tests;

/// <summary>Every row of the legacy-save catch-up table, idempotence, and the "a new game gets nothing" rule.</summary>
public class CampaignCatchUpTests
{
    private sealed class Save
    {
        public readonly HashSet<string> Flags = new();
        public readonly HashSet<string> Completed = new();

        /// <summary>Quest ids that exist in the database; defaults to every mission quest.</summary>
        public HashSet<string> Existing = new(CampaignCatchUp.Missions.Select(m => m.QuestId));

        public CampaignCatchUpPlan Plan() =>
            CampaignCatchUp.Plan(Flags.Contains, Completed.Contains, Existing.Contains);

        /// <summary>Applies a plan the way the quest log does.</summary>
        public void Apply(CampaignCatchUpPlan plan)
        {
            foreach (string quest in plan.CompleteQuests)
            {
                Completed.Add(quest);
            }

            foreach (string flag in plan.SetFlags)
            {
                Flags.Add(flag);
            }
        }
    }

    private static string Slug(string quest) => quest["quest.main.".Length..];

    private static IEnumerable<string> DoneFlags(int first, int last) =>
        CampaignCatchUp.Missions.Where(m => m.Index >= first && m.Index <= last).Select(m => m.DoneFlag);

    private static void AssertSets(CampaignCatchUpPlan plan, IEnumerable<string> expectedFlags, params string[] extra)
    {
        Assert.Equal(
            expectedFlags.Concat(extra).OrderBy(f => f, StringComparer.Ordinal),
            plan.SetFlags.OrderBy(f => f, StringComparer.Ordinal));
    }

    private static readonly string[] AllArcs =
    {
        CampaignCatchUp.FrostfangReady, CampaignCatchUp.AshenReady, CampaignCatchUp.SunspireReady,
    };

    [Fact]
    public void TableHasThirtyMissionsInOrderWithTheDocumentedFlags()
    {
        Assert.Equal(30, CampaignCatchUp.Missions.Count);
        Assert.Equal(Enumerable.Range(1, 30), CampaignCatchUp.Missions.Select(m => m.Index));
        Assert.Equal(CampaignCatchUp.Missions.Count, CampaignCatchUp.Missions.Select(m => m.DoneFlag).Distinct().Count());
        Assert.Equal(CampaignCatchUp.Missions.Count, CampaignCatchUp.Missions.Select(m => m.QuestId).Distinct().Count());

        // The id rule: quest.main.<slug> => flag.main.<slug>_done, except the four legacy warband quests.
        foreach (CampaignCatchUp.Mission mission in CampaignCatchUp.Missions.Where(m => m.QuestId.StartsWith("quest.main.")))
        {
            Assert.Equal($"flag.main.{Slug(mission.QuestId)}_done", mission.DoneFlag);
        }

        Assert.Equal("flag.main.bounty_done", CampaignCatchUp.Missions[2].DoneFlag);
        Assert.Equal("quest.warband.bounty", CampaignCatchUp.Missions[2].QuestId);
        Assert.Equal("flag.main.forge_done", CampaignCatchUp.Missions[4].DoneFlag);
        Assert.Equal("flag.main.remedies_done", CampaignCatchUp.Missions[5].DoneFlag);
        Assert.Equal("flag.frostfang.passage_open", CampaignCatchUp.Missions[7].DoneFlag);
        Assert.Equal("quest.warband.heart", CampaignCatchUp.Missions[7].QuestId);
        Assert.Equal("flag.main.hidden_done", CampaignCatchUp.Missions[21].DoneFlag);
        Assert.Equal("flag.main.truth_done", CampaignCatchUp.Missions[24].DoneFlag);
        Assert.Equal("flag.main.celestial_done", CampaignCatchUp.Missions[28].DoneFlag);
        Assert.Equal("flag.main.throne_done", CampaignCatchUp.Missions[29].DoneFlag);
        Assert.Equal("flag.main.smoke_over_the_square_done", CampaignCatchUp.NewFlowMarker);
    }

    [Fact]
    public void AFreshNewGameGetsNothing()
    {
        Assert.True(new Save().Plan().IsEmpty);
    }

    [Fact]
    public void AGameInTheOpeningStagesGetsNothing()
    {
        var save = new Save();
        save.Flags.Add("flag.main.opening_done");
        save.Flags.Add("flag.beat.bell_rung");

        Assert.True(save.Plan().IsEmpty);
    }

    [Theory]
    [InlineData("quest.warband.bounty", 3)]
    [InlineData("quest.warband.forge", 5)]
    [InlineData("quest.warband.remedies", 6)]
    [InlineData("quest.warband.heart", 8)]
    public void ALegacyActOneQuestCompletionMarksEveryMissionThroughIt(string quest, int through)
    {
        var save = new Save();
        save.Completed.Add(quest);

        CampaignCatchUpPlan plan = save.Plan();

        AssertSets(plan, DoneFlags(1, through));
        Assert.DoesNotContain(CampaignCatchUp.FrostfangReady, plan.SetFlags);
        Assert.DoesNotContain(quest, plan.CompleteQuests);
        Assert.Equal(through - 1, plan.CompleteQuests.Count);
    }

    [Fact]
    public void PassageOpenFlagAloneCountsAsHeartDone()
    {
        var save = new Save();
        save.Flags.Add("flag.frostfang.passage_open");

        AssertSets(save.Plan(), DoneFlags(1, 8).Where(f => f != "flag.frostfang.passage_open"));
    }

    [Fact]
    public void IronKingDefeatedMarksActOneAndOpensEveryArc()
    {
        var save = new Save();
        save.Flags.Add("flag.iron_king_defeated");

        AssertSets(save.Plan(), DoneFlags(1, 9), AllArcs);
    }

    [Theory]
    [InlineData("flag.storm_tyrant_defeated", 10, 12)]
    [InlineData("flag.beast_lord_defeated", 13, 15)]
    [InlineData("flag.crimson_prophet_defeated", 16, 18)]
    public void AnActTwoBossMarksOnlyItsOwnArc(string boss, int first, int last)
    {
        var save = new Save();
        save.Flags.Add("flag.iron_king_defeated");
        save.Flags.Add(boss);

        AssertSets(save.Plan(), DoneFlags(1, 9).Concat(DoneFlags(first, last)), AllArcs);
    }

    [Fact]
    public void TheRevealMarksAllThreeArcs()
    {
        var save = new Save();
        save.Flags.Add("flag.iron_king_defeated");
        save.Flags.Add("flag.pale_concord_revealed");

        AssertSets(save.Plan(), DoneFlags(1, 18), AllArcs);
    }

    [Fact]
    public void TheQueenDefeatedMarksThroughHidden()
    {
        var save = new Save();
        save.Flags.Add("flag.hollow_queen_defeated");

        CampaignCatchUpPlan plan = save.Plan();

        AssertSets(plan, DoneFlags(1, 22), AllArcs);
        Assert.Contains("flag.main.hidden_done", plan.SetFlags);
    }

    [Fact]
    public void TheCelestialGateMarksThroughTruth()
    {
        var save = new Save();
        save.Flags.Add("flag.celestial_gate_open");

        CampaignCatchUpPlan plan = save.Plan();

        AssertSets(plan, DoneFlags(1, 25), AllArcs);
        Assert.Contains("flag.main.truth_done", plan.SetFlags);
        Assert.DoesNotContain("flag.main.celestial_done", plan.SetFlags);
    }

    [Fact]
    public void TheKnightDefeatedLeavesTheCelestialQuestOpen()
    {
        var save = new Save();
        save.Flags.Add("flag.ashen_knight_defeated");

        CampaignCatchUpPlan plan = save.Plan();

        AssertSets(plan, DoneFlags(1, 28), AllArcs);
        Assert.DoesNotContain("flag.main.celestial_done", plan.SetFlags);
        Assert.DoesNotContain("quest.main.celestial", plan.CompleteQuests);
    }

    [Fact]
    public void MorthulDefeatedMarksTheCelestialQuest()
    {
        var save = new Save();
        save.Flags.Add("flag.morthul_defeated");

        CampaignCatchUpPlan plan = save.Plan();

        AssertSets(plan, DoneFlags(1, 29), AllArcs);
        Assert.Contains("flag.main.celestial_done", plan.SetFlags);
        Assert.DoesNotContain("flag.main.throne_done", plan.SetFlags);
    }

    [Fact]
    public void GameCompleteMarksEveryMission()
    {
        var save = new Save();
        save.Flags.Add("flag.game_complete");

        CampaignCatchUpPlan plan = save.Plan();

        AssertSets(plan, DoneFlags(1, 30), AllArcs);
        Assert.Contains("flag.main.throne_done", plan.SetFlags);
        Assert.Equal(30, plan.CompleteQuests.Count);
    }

    [Fact]
    public void ForkFlagsAreNeverBackFilled()
    {
        var save = new Save();
        save.Flags.Add("flag.game_complete");

        CampaignCatchUpPlan plan = save.Plan();

        Assert.DoesNotContain(plan.SetFlags, f => f.StartsWith("flag.fork.", StringComparison.Ordinal));
        Assert.DoesNotContain(plan.SetFlags, f => f.StartsWith("flag.rival.", StringComparison.Ordinal));
        Assert.DoesNotContain(plan.SetFlags, f => f.StartsWith("flag.testimony.", StringComparison.Ordinal));
    }

    [Fact]
    public void ApplyingThePlanTwiceIsANoOp()
    {
        foreach (string marker in new[]
                 {
                     "flag.iron_king_defeated", "flag.storm_tyrant_defeated", "flag.pale_concord_revealed",
                     "flag.hollow_queen_defeated", "flag.celestial_gate_open", "flag.ashen_knight_defeated",
                     "flag.morthul_defeated", "flag.game_complete",
                 })
        {
            var save = new Save();
            save.Flags.Add(marker);

            CampaignCatchUpPlan first = save.Plan();
            Assert.False(first.IsEmpty, marker);
            save.Apply(first);

            // The first mission's flag is now held, so the save counts as having run the chain.
            Assert.True(save.Plan().IsEmpty, marker);

            // Even with the marker removed the only thing left to do is put the marker back.
            save.Flags.Remove(CampaignCatchUp.NewFlowMarker);
            CampaignCatchUpPlan again = save.Plan();
            Assert.Equal(new[] { CampaignCatchUp.NewFlowMarker }, again.SetFlags);
            Assert.Empty(again.CompleteQuests);
        }
    }

    [Fact]
    public void ASaveThatRanTheNewChainIsLeftAlone()
    {
        // A kill of the Hollow Queen before the aftermath talk must not complete 'hidden' behind the player's back.
        var save = new Save();
        save.Flags.Add(CampaignCatchUp.NewFlowMarker);
        save.Flags.Add("flag.hollow_queen_defeated");
        save.Flags.Add("flag.iron_king_defeated");

        Assert.True(save.Plan().IsEmpty);
    }

    [Fact]
    public void MissionQuestsThatDoNotExistYetGetTheirFlagButNoJournalEntry()
    {
        var save = new Save { Existing = new HashSet<string> { "quest.warband.bounty", "quest.warband.forge" } };
        save.Completed.Add("quest.warband.forge");

        CampaignCatchUpPlan plan = save.Plan();

        AssertSets(plan, DoneFlags(1, 5));
        Assert.Equal(new[] { "quest.warband.bounty" }, plan.CompleteQuests);
    }

    [Fact]
    public void HeldFlagsAndCompletedQuestsAreNotRepeated()
    {
        var save = new Save();
        save.Completed.Add("quest.warband.bounty");
        save.Flags.Add("flag.main.bounty_done");
        save.Flags.Add("flag.main.the_pass_kept_done");
        save.Completed.Add("quest.main.the_pass_kept");

        CampaignCatchUpPlan plan = save.Plan();

        Assert.Equal(new[] { "flag.main.smoke_over_the_square_done" }, plan.SetFlags);
        Assert.Equal(new[] { "quest.main.smoke_over_the_square" }, plan.CompleteQuests);
    }

    [Fact]
    public void FlagsAndQuestsComeHighestMissionFirstSoOnlyTheFrontierAutoStarts()
    {
        var save = new Save();
        save.Flags.Add("flag.celestial_gate_open");

        CampaignCatchUpPlan plan = save.Plan();

        int[] order = plan.CompleteQuests
            .Select(q => CampaignCatchUp.Missions.Single(m => m.QuestId == q).Index)
            .ToArray();
        Assert.Equal(order.OrderByDescending(i => i), order);

        string[] doneFlags = plan.SetFlags.Where(f => !f.StartsWith("flag.arc.", StringComparison.Ordinal)).ToArray();
        int[] flagOrder = doneFlags.Select(f => CampaignCatchUp.Missions.Single(m => m.DoneFlag == f).Index).ToArray();
        Assert.Equal(flagOrder.OrderByDescending(i => i), flagOrder);
        Assert.Equal("flag.main.truth_done", doneFlags[0]);
        Assert.Equal(CampaignCatchUp.NewFlowMarker, doneFlags[^1]);
    }

    [Fact]
    public void WrittenFlagsListEveryMissionFlagAndTheArcs()
    {
        List<string> written = CampaignCatchUp.WrittenFlags().ToList();

        Assert.Equal(33, written.Count);
        Assert.Contains("flag.arc.sunspire_ready", written);
        Assert.Contains("flag.main.throne_done", written);
    }

    [Fact]
    public void ALoadRaisesTheOpeningFlagOnlyWhenItIsMissing()
    {
        Assert.True(CampaignCatchUp.LoadNeedsOpeningDone(_ => false));
        Assert.False(CampaignCatchUp.LoadNeedsOpeningDone(f => f == CampaignCatchUp.OpeningDoneFlag));
    }

    [Theory]
    // an untouched legacy main quest whose auto-start flag is not held steps back
    [InlineData(false, true, false, true, "flag.main.x_done", false, false, true)]
    // a save already on the new chain keeps everything
    [InlineData(true, true, false, true, "flag.main.x_done", false, false, false)]
    // progress, a held flag, no auto-start flag, the ledger, a side quest or a finished quest all stay
    [InlineData(false, true, false, true, "flag.main.x_done", false, true, false)]
    [InlineData(false, true, false, true, "flag.main.x_done", true, false, false)]
    [InlineData(false, true, false, true, "", false, false, false)]
    [InlineData(false, true, true, true, "flag.main.x_done", false, false, false)]
    [InlineData(false, false, false, true, "flag.main.x_done", false, false, false)]
    [InlineData(false, true, false, false, "flag.main.x_done", false, false, false)]
    public void OnlyAnUntouchedPrematureLegacyQuestIsTakenOutOfTheLog(
        bool newFlow, bool isMain, bool isLedger, bool active, string autoStart, bool autoStartHeld, bool anyProgress, bool expected) =>
        Assert.Equal(expected, CampaignCatchUp.IsPrematureLegacyQuest(newFlow, isMain, isLedger, active, autoStart, autoStartHeld, anyProgress));
}
