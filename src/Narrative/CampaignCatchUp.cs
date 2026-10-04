using System;
using System.Collections.Generic;

namespace Embervale.Narrative;

/// <summary>
/// What the load catch-up decided: the story flags to set and the quests to mark completed silently,
/// both already ordered for application (highest mission first, see <see cref="CampaignCatchUp"/>).
/// </summary>
public sealed record CampaignCatchUpPlan(IReadOnlyList<string> SetFlags, IReadOnlyList<string> CompleteQuests)
{
    public bool IsEmpty => SetFlags.Count == 0 && CompleteQuests.Count == 0;
}

/// <summary>
/// Maps what a pre-campaign save already holds (legacy quest completions and boss/story flags) onto the
/// campaign's mission done-flags, so a legacy save lands on the right mission after the overhaul.
/// Pure and idempotent; <c>QuestLogComponent.OnGameLoaded</c> runs it before auto-start. A fork flag is
/// never back-filled (absent means the neutral variant).
///
/// <para><b>Scope.</b> It only acts on a save that has NOT run the new first mission
/// (<see cref="NewFlowMarker"/> absent). A fresh new game has no evidence and so gets nothing; a save that
/// has been through the new chain already carries every flag the chain sets, so nothing here may shadow
/// the live flow (for example by completing the Hollow Queen aftermath talk before it was played).</para>
///
/// <para><b>Mission table</b> (<see cref="Missions"/>, 30 missions; the done flag is
/// <c>flag.main.&lt;slug&gt;_done</c>, the quest <c>quest.main.&lt;slug&gt;</c>, except the four legacy
/// warband quests, whose done flags are <c>flag.main.bounty_done</c>, <c>flag.main.forge_done</c>,
/// <c>flag.main.remedies_done</c> and <c>flag.frostfang.passage_open</c>).</para>
///
/// <para><b>Evidence rows</b> (each marks missions done; "through N" is every mission 1..N):</para>
/// <list type="table">
/// <item><term>quest.warband.bounty completed</term><description>through 3 (smoke_over_the_square, the_pass_kept, bounty)</description></item>
/// <item><term>quest.warband.forge completed</term><description>through 5 (adds crossway_whispers, forge)</description></item>
/// <item><term>quest.warband.remedies completed</term><description>through 6 (adds remedies; citadel_approach is the next, live mission)</description></item>
/// <item><term>quest.warband.heart completed, or flag.frostfang.passage_open</term><description>through 8 (adds citadel_approach, heart)</description></item>
/// <item><term>flag.iron_king_defeated</term><description>through 9 (adds iron_king) and flag.arc.frostfang_ready, flag.arc.ashen_ready, flag.arc.sunspire_ready (all three arcs open: a legacy save skips the new arc hooks)</description></item>
/// <item><term>flag.storm_tyrant_defeated</term><description>through 9, plus 10-12 (closed_hold, succession, storm_tyrant)</description></item>
/// <item><term>flag.beast_lord_defeated</term><description>through 9, plus 13-15 (last_hearth, herd_and_hearth, beast_lord)</description></item>
/// <item><term>flag.crimson_prophet_defeated</term><description>through 9, plus 16-18 (dry_wells, prophets_flock, crimson_prophet)</description></item>
/// <item><term>flag.pale_concord_revealed</term><description>through 9, plus 10-18 (the reveal needs all three Act II Flamebearers)</description></item>
/// <item><term>flag.hollow_queen_defeated</term><description>through 22 (arcs, pale_door, vesperhold, queens_count, hidden; flag.main.hidden_done)</description></item>
/// <item><term>flag.celestial_gate_open</term><description>through 25 (adds sundering_pages, deep_stacks, truth; flag.main.truth_done)</description></item>
/// <item><term>flag.ashen_knight_defeated</term><description>through 28 (adds celestial_landing, godfall_choir, sundered_stair); not celestial itself: Morthul still stands</description></item>
/// <item><term>flag.morthul_defeated</term><description>through 29 (flag.main.celestial_done)</description></item>
/// <item><term>flag.game_complete</term><description>through 30 (flag.main.throne_done)</description></item>
/// </list>
///
/// <para><b>Order of application.</b> Flags and quests are returned highest mission first. Setting a
/// done flag auto-starts the next quest unless that quest's own completion flag is already held, so
/// setting later missions first means only the first mission AFTER the frontier starts.</para>
/// </summary>
public static class CampaignCatchUp
{
    /// <summary>A mission: 1-based position in the campaign, its quest id and its done flag.</summary>
    public readonly record struct Mission(int Index, string QuestId, string DoneFlag);

    /// <summary>The first new mission's done flag. Present means the save has run the new chain.</summary>
    public const string NewFlowMarker = "flag.main.smoke_over_the_square_done";

    /// <summary>Raised when the prologue ends; the first mission's auto-start flag.</summary>
    public const string OpeningDoneFlag = "flag.main.opening_done";

    /// <summary>
    /// A load never plays the prologue (it runs on New Game only), so a save that never reached the end of
    /// it (an old save made before the first errand, or a new game closed during the narration) would
    /// otherwise start no mission at all: nothing raises <see cref="OpeningDoneFlag"/> for it. True when the
    /// flag is missing and a load should raise it, after the catch-up has set the later mission flags (a
    /// mission whose own done flag is held never auto-starts, so only the right one starts).
    /// </summary>
    public static bool LoadNeedsOpeningDone(Func<string, bool> has) => !has(OpeningDoneFlag);

    /// <summary>
    /// A legacy save can hold a main quest the campaign now starts further along its chain (the Hollow
    /// Queen, the Archivist truth, the Celestial assault), started by the old flow and untouched. Left
    /// in the journal it outranks the mission the save has actually reached (the tracker prefers the
    /// first active main quest) and aims the player at a locked fight. True for a main, non-ledger quest
    /// that is Active, has an auto-start flag that is NOT held, and has no progress; such a quest is taken
    /// out of the log and starts by itself when its flag arrives. Only for saves that have not run the new chain.
    /// </summary>
    public static bool IsPrematureLegacyQuest(
        bool newFlow, bool isMain, bool isLedger, bool active, string autoStartFlag, bool autoStartHeld, bool anyProgress) =>
        !newFlow && isMain && !isLedger && active && autoStartFlag.Length > 0 && !autoStartHeld && !anyProgress;

    public const string FrostfangReady = "flag.arc.frostfang_ready";
    public const string AshenReady = "flag.arc.ashen_ready";
    public const string SunspireReady = "flag.arc.sunspire_ready";

    // Evidence flags (legacy, written by boss defeats and the reveal/gate/ending directors).
    private const string IronKingDefeated = "flag.iron_king_defeated";
    private const string StormTyrantDefeated = "flag.storm_tyrant_defeated";
    private const string BeastLordDefeated = "flag.beast_lord_defeated";
    private const string CrimsonProphetDefeated = "flag.crimson_prophet_defeated";
    private const string PaleRevealed = "flag.pale_concord_revealed";
    private const string HollowQueenDefeated = "flag.hollow_queen_defeated";
    private const string CelestialGateOpen = "flag.celestial_gate_open";
    private const string AshenKnightDefeated = "flag.ashen_knight_defeated";
    private const string MorthulDefeated = "flag.morthul_defeated";
    private const string GameComplete = "flag.game_complete";
    private const string PassageOpen = "flag.frostfang.passage_open";

    private const string BountyQuest = "quest.warband.bounty";
    private const string ForgeQuest = "quest.warband.forge";
    private const string RemediesQuest = "quest.warband.remedies";
    private const string HeartQuest = "quest.warband.heart";

    private static Mission Main(int index, string slug) =>
        new(index, $"quest.main.{slug}", $"flag.main.{slug}_done");

    /// <summary>The 30 missions in campaign order (docs/playbook/campaign.md beat sheet).</summary>
    public static readonly IReadOnlyList<Mission> Missions = new[]
    {
        Main(1, "smoke_over_the_square"),
        Main(2, "the_pass_kept"),
        new Mission(3, BountyQuest, "flag.main.bounty_done"),
        Main(4, "crossway_whispers"),
        new Mission(5, ForgeQuest, "flag.main.forge_done"),
        new Mission(6, RemediesQuest, "flag.main.remedies_done"),
        Main(7, "citadel_approach"),
        new Mission(8, HeartQuest, PassageOpen),
        Main(9, "iron_king"),
        Main(10, "closed_hold"),
        Main(11, "succession"),
        Main(12, "storm_tyrant"),
        Main(13, "last_hearth"),
        Main(14, "herd_and_hearth"),
        Main(15, "beast_lord"),
        Main(16, "dry_wells"),
        Main(17, "prophets_flock"),
        Main(18, "crimson_prophet"),
        Main(19, "pale_door"),
        Main(20, "vesperhold"),
        Main(21, "queens_count"),
        Main(22, "hidden"),
        Main(23, "sundering_pages"),
        Main(24, "deep_stacks"),
        Main(25, "truth"),
        Main(26, "celestial_landing"),
        Main(27, "godfall_choir"),
        Main(28, "sundered_stair"),
        Main(29, "celestial"),
        Main(30, "throne"),
    };

    /// <summary>Every flag this class can write (for the validator's code-written-flag seed).</summary>
    public static IEnumerable<string> WrittenFlags()
    {
        foreach (Mission mission in Missions)
        {
            yield return mission.DoneFlag;
        }

        yield return FrostfangReady;
        yield return AshenReady;
        yield return SunspireReady;
    }

    /// <summary>
    /// Plans the catch-up. <paramref name="has"/> reads story flags, <paramref name="isCompleted"/> the
    /// quest log, <paramref name="questExists"/> the quest database (a mission whose quest does not exist
    /// yet gets its flag but no journal entry).
    /// </summary>
    public static CampaignCatchUpPlan Plan(
        Func<string, bool> has, Func<string, bool> isCompleted, Func<string, bool> questExists)
    {
        var empty = new CampaignCatchUpPlan(Array.Empty<string>(), Array.Empty<string>());
        if (has(NewFlowMarker))
        {
            return empty;
        }

        var done = new bool[Missions.Count + 1]; // 1-based
        bool arcsReady = false;

        void Through(int last)
        {
            for (int i = 1; i <= last; i++)
            {
                done[i] = true;
            }
        }

        void Range(int first, int last)
        {
            for (int i = first; i <= last; i++)
            {
                done[i] = true;
            }
        }

        if (isCompleted(BountyQuest))
        {
            Through(3);
        }

        if (isCompleted(ForgeQuest))
        {
            Through(5);
        }

        if (isCompleted(RemediesQuest))
        {
            Through(6);
        }

        if (isCompleted(HeartQuest) || has(PassageOpen))
        {
            Through(8);
        }

        if (has(IronKingDefeated))
        {
            Through(9);
            arcsReady = true;
        }

        if (has(StormTyrantDefeated))
        {
            Through(9);
            Range(10, 12);
            arcsReady = true;
        }

        if (has(BeastLordDefeated))
        {
            Through(9);
            Range(13, 15);
            arcsReady = true;
        }

        if (has(CrimsonProphetDefeated))
        {
            Through(9);
            Range(16, 18);
            arcsReady = true;
        }

        if (has(PaleRevealed))
        {
            Through(9);
            Range(10, 18);
            arcsReady = true;
        }

        if (has(HollowQueenDefeated))
        {
            Through(22);
            arcsReady = true;
        }

        if (has(CelestialGateOpen))
        {
            Through(25);
            arcsReady = true;
        }

        if (has(AshenKnightDefeated))
        {
            Through(28);
            arcsReady = true;
        }

        if (has(MorthulDefeated))
        {
            Through(29);
            arcsReady = true;
        }

        if (has(GameComplete))
        {
            Through(30);
            arcsReady = true;
        }

        var flags = new List<string>();
        var quests = new List<string>();
        for (int i = Missions.Count; i >= 1; i--)
        {
            if (!done[i])
            {
                continue;
            }

            Mission mission = Missions[i - 1];
            if (!has(mission.DoneFlag))
            {
                flags.Add(mission.DoneFlag);
            }

            if (questExists(mission.QuestId) && !isCompleted(mission.QuestId))
            {
                quests.Add(mission.QuestId);
            }
        }

        if (arcsReady)
        {
            foreach (string arc in new[] { FrostfangReady, AshenReady, SunspireReady })
            {
                if (!has(arc))
                {
                    flags.Add(arc);
                }
            }
        }

        return flags.Count == 0 && quests.Count == 0 ? empty : new CampaignCatchUpPlan(flags, quests);
    }
}
