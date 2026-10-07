using System;
using System.Collections.Generic;
using System.Text.Json;
using Embervale.Bootstrap;
using Embervale.Debugging;
using Embervale.Narrative;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// The pure cores behind the headless gates: the flag registry and its typo check
/// (<see cref="HeadlessFlags"/>), the report's nested facts and refused exit code, the validator arm
/// filter, the <c>--story-mission</c> range, and the arena's roster, policy and statistics.
/// </summary>
public class HeadlessGateTests
{
    private static readonly string[] Session = { "--play", "--new-game", "--slot", "--quit-after", "--hudshots" };

    private static string? Validate(params string[] userArgs)
    {
        var args = new CommandLineArgs(userArgs);
        return HeadlessFlags.Validate(userArgs, args.Has, Session);
    }

    // --- flags ---------------------------------------------------------------------------

    [Fact]
    public void Validate_AcceptsEveryModeWithItsOwnAndCommonOptions()
    {
        Assert.Null(Validate("--validate"));
        Assert.Null(Validate("--validate", "--only=items,crafting", "--report=out.json", "--verbose"));
        Assert.Null(Validate("--state", "--ids=quests", "--match", "main"));
        Assert.Null(Validate("--story", "--story-only", "C"));
        Assert.Null(Validate("--arena=enemy.goblin", "--trials=5", "--seed=3", "--json"));
        Assert.Null(Validate("--save-reload", "--capture"));
        Assert.Null(Validate());
    }

    [Fact]
    public void Validate_RefusesAnOptionTheModeDoesNotTake_AndSuggestsTheNearest()
    {
        string? refusal = Validate("--validate", "--onyl=items");

        Assert.NotNull(refusal);
        Assert.Contains("--validate does not take --onyl", refusal);
        Assert.Contains("did you mean --only?", refusal);
    }

    [Fact]
    public void Validate_RefusesTwoModesTogether()
    {
        string? refusal = Validate("--validate", "--state");

        Assert.NotNull(refusal);
        Assert.Contains("--validate and --state", refusal);
    }

    [Fact]
    public void Validate_RefusesANearMissOfAModeFlag()
    {
        string? refusal = Validate("--validat");

        Assert.NotNull(refusal);
        Assert.Contains("unknown flag --validat (did you mean --validate?)", refusal);
        Assert.NotNull(Validate("--lifecyle"));
        Assert.NotNull(Validate("--stroy", "--report=x.json"));
    }

    [Fact]
    public void Validate_LeavesUnknownFlagsAlone_WhenTheyAreNotNearAMode_OrASessionFlagIsPresent()
    {
        // Another tool's flag this table has never heard of.
        Assert.Null(Validate("--bench-frames=600"));

        // A near miss beside a real session flag: the run goes somewhere, so it is not ours to stop.
        Assert.Null(Validate("--play", "--stat"));
        Assert.Null(Validate("--hudshots", "--stat"));
    }

    [Fact]
    public void Validate_DoesNotCheckTheOptionsOfAModeThatParsesItsOwn()
    {
        Assert.Null(Validate("--worldmap", "artifacts/maps", "--grid", "--scale", "4"));
        Assert.Null(Validate("--world-bake", "--world-bake-signature=abc"));
    }

    [Fact]
    public void Validate_ReadsAModeGivenBeforeTheSeparator()
    {
        // The mode was an engine argument, so only `requested` knows about it.
        string? refusal = HeadlessFlags.Validate(
            new[] { "--onyl=items" }, flag => flag == "--validate", Session);

        Assert.NotNull(refusal);
        Assert.Contains("--validate does not take --onyl", refusal);
    }

    [Fact]
    public void EveryModeFlagIsUnique_AndNoOptionShadowsACommonFlag()
    {
        var seen = new HashSet<string>();
        foreach (HeadlessMode mode in HeadlessFlags.Modes)
        {
            Assert.True(seen.Add(mode.Flag), $"{mode.Flag} is registered twice");
            Assert.Same(mode, HeadlessFlags.Find(mode.Flag));
            foreach (string option in mode.Options)
            {
                Assert.DoesNotContain(option, HeadlessFlags.Common);
            }
        }

        Assert.Null(HeadlessFlags.Find("--nothing"));
    }

    [Fact]
    public void NameOf_AndDistance()
    {
        Assert.Equal("--only", HeadlessFlags.NameOf("--only=a,b"));
        Assert.Equal("--list", HeadlessFlags.NameOf("--list"));
        Assert.Null(HeadlessFlags.NameOf("artifacts/maps"));
        Assert.Null(HeadlessFlags.NameOf("--"));
        Assert.Equal(0, HeadlessFlags.Distance("--state", "--state"));
        Assert.Equal(1, HeadlessFlags.Distance("--stat", "--state"));
        Assert.Equal(2, HeadlessFlags.Distance("--stroy", "--story"));
        Assert.Null(HeadlessFlags.Nearest("--zzzzzz", new[] { "--state" }, 2));
    }

    // --- report --------------------------------------------------------------------------

    [Fact]
    public void Report_WritesNestedFactsAsObjectsAndArrays()
    {
        var report = new HeadlessReport("arena");
        report.Fact("rows", new List<object?>
        {
            new Dictionary<string, object?>
            {
                ["enemy"] = "enemy.goblin",
                ["ttk_s"] = new Dictionary<string, object?> { ["p50"] = 4.5, ["max"] = 6 },
                ["flags"] = new List<string> { "timeouts" },
                ["missing"] = null,
            },
        });

        using JsonDocument document = JsonDocument.Parse(report.ToJson(12));
        JsonElement row = document.RootElement.GetProperty("facts").GetProperty("rows")[0];

        Assert.Equal("enemy.goblin", row.GetProperty("enemy").GetString());
        Assert.Equal(4.5, row.GetProperty("ttk_s").GetProperty("p50").GetDouble());
        Assert.Equal(6, row.GetProperty("ttk_s").GetProperty("max").GetInt32());
        Assert.Equal("timeouts", row.GetProperty("flags")[0].GetString());
        Assert.Equal(JsonValueKind.Null, row.GetProperty("missing").ValueKind);
    }

    [Fact]
    public void Report_RefuseExitsTwo_AndAFailureAloneExitsOne()
    {
        var refused = new HeadlessReport("validate");
        refused.Refuse("'itmes' is not a validator group or arm.");
        var failed = new HeadlessReport("validate");
        failed.Fail("an issue");

        Assert.Equal(2, refused.ExitCode);
        Assert.False(refused.Passed);
        Assert.Equal(1, failed.ExitCode);
        Assert.Equal(0, new HeadlessReport("validate").ExitCode);

        using JsonDocument document = JsonDocument.Parse(refused.ToJson(0));
        Assert.Equal(2, document.RootElement.GetProperty("exit_code").GetInt32());
        Assert.False(document.RootElement.GetProperty("ok").GetBoolean());
    }

    // --- validator arms ------------------------------------------------------------------

    private static readonly ValidatorArmInfo[] SomeArms =
    {
        new("DuplicateIds", "ids", false), new("Items", "items", false), new("ItemTags", "items", false),
        new("Regions", "world", false), new("Tolls", "world", false), new("StoryFlags", "quests", true),
    };

    private static List<string> Selected(string[] only, string[] skip)
    {
        Assert.Null(ValidatorArmFilter.Select(SomeArms, only, skip, out bool[] selected));
        var names = new List<string>();
        for (int i = 0; i < SomeArms.Length; i++)
        {
            if (selected[i])
            {
                names.Add(SomeArms[i].Name);
            }
        }

        return names;
    }

    [Fact]
    public void ArmFilter_NoTermsSelectsEverything()
    {
        Assert.Equal(SomeArms.Length, Selected(Array.Empty<string>(), Array.Empty<string>()).Count);
    }

    [Fact]
    public void ArmFilter_OnlyTakesGroupsArmsAndQualifiedNames_CaseInsensitively()
    {
        Assert.Equal(new[] { "Items", "ItemTags" }, Selected(new[] { "ITEMS" }, Array.Empty<string>()));
        Assert.Equal(new[] { "Regions" }, Selected(new[] { "regions" }, Array.Empty<string>()));
        Assert.Equal(new[] { "Tolls", "StoryFlags" }, Selected(new[] { "world/Tolls", "quests" }, Array.Empty<string>()));
    }

    [Fact]
    public void ArmFilter_SkipRemovesAfterOnly()
    {
        Assert.Equal(new[] { "Tolls" }, Selected(new[] { "world" }, new[] { "Regions" }));
        Assert.Equal(
            new[] { "DuplicateIds", "Items", "ItemTags", "StoryFlags" }, Selected(Array.Empty<string>(), new[] { "world" }));
    }

    [Fact]
    public void ArmFilter_RefusesATermThatMatchesNothing_AndAnEmptySelection()
    {
        string? unknown = ValidatorArmFilter.Select(SomeArms, new[] { "itmes" }, Array.Empty<string>(), out _);
        string? empty = ValidatorArmFilter.Select(SomeArms, new[] { "ids" }, new[] { "DuplicateIds" }, out _);

        Assert.NotNull(unknown);
        Assert.Contains("'itmes' is not a validator group or arm", unknown);
        Assert.Contains("ids, items, world, quests", unknown);
        Assert.Equal("the filter leaves no arm to run.", empty);
    }

    [Fact]
    public void ArmFilter_Groups_AreInFirstAppearanceOrder()
    {
        Assert.Equal(new[] { "ids", "items", "world", "quests" }, ValidatorArmFilter.Groups(SomeArms));
    }

    // --- story missions ------------------------------------------------------------------

    private static List<(int Index, string QuestId)> Missions()
    {
        var missions = new List<(int, string)>();
        foreach (CampaignCatchUp.Mission mission in CampaignCatchUp.Missions)
        {
            missions.Add((mission.Index, mission.QuestId));
        }

        return missions;
    }

    [Fact]
    public void MissionRange_ReadsAnIndexAQuestIdAndARange()
    {
        Assert.Null(StoryMissionRange.Parse("27", Missions(), out int first, out int last));
        Assert.Equal((27, 27), (first, last));

        Assert.Null(StoryMissionRange.Parse("quest.main.godfall_choir", Missions(), out first, out last));
        Assert.Equal((27, 27), (first, last));

        Assert.Null(StoryMissionRange.Parse("quest.main.pale_door..22", Missions(), out first, out last));
        Assert.Equal((19, 22), (first, last));

        Assert.Null(StoryMissionRange.Parse(" 1 .. 30 ", Missions(), out first, out last));
        Assert.Equal((1, 30), (first, last));
    }

    [Fact]
    public void MissionRange_RefusesWhatIsNotAMission()
    {
        Assert.NotNull(StoryMissionRange.Parse(string.Empty, Missions(), out _, out _));
        Assert.NotNull(StoryMissionRange.Parse("0", Missions(), out _, out _));
        Assert.NotNull(StoryMissionRange.Parse("31", Missions(), out _, out _));
        Assert.NotNull(StoryMissionRange.Parse("quest.main.nope", Missions(), out _, out _));
        Assert.NotNull(StoryMissionRange.Parse("5..3", Missions(), out _, out _));
        Assert.NotNull(StoryMissionRange.Parse("1..2..3", Missions(), out _, out _));
    }

    [Fact]
    public void MissionRange_Frontier_IsTheLatestStartThatLeavesTheMissionToPlay()
    {
        int[] frontiers = { 0, 5, 12, 18, 25, 30 };

        Assert.Equal(0, StoryMissionRange.Frontier(1, frontiers));
        Assert.Equal(0, StoryMissionRange.Frontier(5, frontiers));
        Assert.Equal(5, StoryMissionRange.Frontier(6, frontiers));
        Assert.Equal(12, StoryMissionRange.Frontier(13, frontiers));
        Assert.Equal(25, StoryMissionRange.Frontier(27, frontiers));
        Assert.Equal(25, StoryMissionRange.Frontier(30, frontiers));
    }

    // --- arena ---------------------------------------------------------------------------

    [Fact]
    public void Arena_ParseRoster_ReadsIdsAndCounts()
    {
        Assert.Null(ArenaMath.ParseRoster("enemy.goblin, enemy.soldier*3", 1, out List<(string Id, int Count)> roster));
        Assert.Equal(new[] { ("enemy.goblin", 1), ("enemy.soldier", 3) }, roster);

        Assert.Null(ArenaMath.ParseRoster("enemy.goblin", 2, out roster));
        Assert.Equal(new[] { ("enemy.goblin", 2) }, roster);
    }

    [Fact]
    public void Arena_ParseRoster_RefusesAnEmptyRosterAndABadCount()
    {
        Assert.NotNull(ArenaMath.ParseRoster(string.Empty, 1, out _));
        Assert.NotNull(ArenaMath.ParseRoster(" , ", 1, out _));
        Assert.NotNull(ArenaMath.ParseRoster("enemy.goblin*0", 1, out _));
        Assert.NotNull(ArenaMath.ParseRoster("enemy.goblin*many", 1, out _));
        Assert.NotNull(ArenaMath.ParseRoster("*3", 1, out _));
    }

    [Fact]
    public void Arena_Policy_Parses()
    {
        Assert.True(ArenaMath.TryParsePolicy(null, out ArenaPolicy policy));
        Assert.Equal(ArenaPolicy.Aggressive, policy);
        Assert.True(ArenaMath.TryParsePolicy("GUARD", out policy));
        Assert.Equal(ArenaPolicy.Guard, policy);
        Assert.True(ArenaMath.TryParsePolicy("passive", out policy));
        Assert.Equal(ArenaPolicy.Passive, policy);
        Assert.False(ArenaMath.TryParsePolicy("berserk", out _));
        Assert.False(ArenaMath.TryParsePolicy("7", out _));
    }

    [Fact]
    public void Arena_Decide_ClosesThenSwings_AndWaitsWhenWinded()
    {
        ArenaIntent far = ArenaMath.Decide(ArenaPolicy.Aggressive, new ArenaSense(5f, false, 1f), 1.3f);
        ArenaIntent near = ArenaMath.Decide(ArenaPolicy.Aggressive, new ArenaSense(0.8f, false, 1f), 1.3f);
        ArenaIntent winded = ArenaMath.Decide(ArenaPolicy.Aggressive, new ArenaSense(0.8f, false, 0.05f), 1.3f);

        Assert.Equal(new ArenaIntent(Forward: true, Attack: false, Block: false), far);
        Assert.Equal(new ArenaIntent(Forward: false, Attack: true, Block: false), near);
        Assert.Equal(new ArenaIntent(Forward: false, Attack: false, Block: false), winded);
    }

    [Fact]
    public void Arena_Decide_GuardBlocksABlowThatCanLand_AndPassiveDoesNothing()
    {
        ArenaIntent guarded = ArenaMath.Decide(ArenaPolicy.Guard, new ArenaSense(1f, true, 1f), 1.3f);
        ArenaIntent farBlow = ArenaMath.Decide(ArenaPolicy.Guard, new ArenaSense(8f, true, 1f), 1.3f);
        ArenaIntent aggressive = ArenaMath.Decide(ArenaPolicy.Aggressive, new ArenaSense(1f, true, 1f), 1.3f);
        ArenaIntent passive = ArenaMath.Decide(ArenaPolicy.Passive, new ArenaSense(0f, true, 1f), 1.3f);

        Assert.Equal(new ArenaIntent(Forward: false, Attack: false, Block: true), guarded);
        Assert.True(farBlow.Forward);
        Assert.True(aggressive.Attack);
        Assert.Equal(default, passive);
    }

    private static ArenaTrial Trial(string outcome, double seconds, double dealt, double taken, double left, double other = 0d) =>
        new(outcome, seconds, dealt, taken, left, Swings: 10, Hits: 6, EnemyAttacks: 4, EnemyHits: 2, Blocked: 1,
            Staggers: 1, Parries: 0, FirstContact: 1.5, OtherDamage: other);

    [Fact]
    public void Arena_Summarise_CountsOutcomes_AndTimesKillsOverWinsOnly()
    {
        Dictionary<string, object?> row = ArenaMath.Summarise(new[]
        {
            Trial(ArenaMath.Win, 4d, 60d, 10d, 0.9),
            Trial(ArenaMath.Win, 8d, 60d, 30d, 0.6),
            Trial(ArenaMath.Loss, 12d, 20d, 100d, 0d),
            Trial(ArenaMath.Timeout, 90d, 20d, 20d, 0.8),
        });

        Assert.Equal(4, row["trials"]);
        Assert.Equal(2, row["wins"]);
        Assert.Equal(1, row["losses"]);
        Assert.Equal(1, row["timeouts"]);
        Assert.Equal(0.5, row["win_rate"]);
        var ttk = Assert.IsType<Dictionary<string, object?>>(row["ttk_s"]);
        Assert.Equal(4d, ttk["p50"]);
        Assert.Equal(4d, ttk["min"]);
        Assert.Equal(8d, ttk["max"]);
        Assert.Equal(40d, row["dealt"]);
        Assert.Equal(40d, row["taken"]);
        Assert.Equal(Math.Round(160d / 114d, 2), row["dps"]);
        Assert.Equal(0d, row["hp_left_min"]);
        Assert.Equal(40, row["swings"]);
        Assert.Equal(new List<string> { "timeouts" }, row["flags"]);
    }

    [Fact]
    public void Arena_Summarise_FlagsABrokenOrPollutedFight()
    {
        static List<string> Flags(params ArenaTrial[] trials) =>
            Assert.IsType<List<string>>(ArenaMath.Summarise(trials)["flags"]);

        Assert.Equal(new List<string> { "no_contact", "timeouts" }, Flags(Trial(ArenaMath.Timeout, 90d, 0d, 0d, 1d)));
        Assert.Equal(new List<string> { "enemy_never_hit" }, Flags(Trial(ArenaMath.Loss, 9d, 0d, 100d, 0d)));
        Assert.Equal(new List<string> { "player_never_hit" }, Flags(Trial(ArenaMath.Win, 3d, 50d, 0d, 1d)));
        Assert.Equal(new List<string> { "third_party" }, Flags(Trial(ArenaMath.Win, 3d, 50d, 5d, 0.9, other: 12d)));
        Assert.Empty(Flags());
    }

    [Fact]
    public void Arena_Percentile_IsNearestRank()
    {
        var values = new List<double> { 1d, 2d, 3d, 4d };

        Assert.Equal(2d, ArenaMath.Percentile(values, 0.5));
        Assert.Equal(4d, ArenaMath.Percentile(values, 1d));
        Assert.Equal(1d, ArenaMath.Percentile(values, 0d));
        Assert.Equal(0d, ArenaMath.Percentile(new List<double>(), 0.5));
    }

    [Fact]
    public void Arena_Row_IsWritableAsAFact()
    {
        var report = new HeadlessReport("arena");
        report.Fact("each", new List<object?> { ArenaMath.Row(Trial(ArenaMath.Win, 4.126, 60d, 10d, 0.9)) });

        using JsonDocument document = JsonDocument.Parse(report.ToJson(0));
        JsonElement row = document.RootElement.GetProperty("facts").GetProperty("each")[0];

        Assert.Equal("win", row.GetProperty("outcome").GetString());
        Assert.Equal(4.13, row.GetProperty("s").GetDouble());
    }
}
