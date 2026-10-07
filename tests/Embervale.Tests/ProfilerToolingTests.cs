using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using Embervale.Analytics;
using Embervale.Debugging;
using Embervale.World;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// The pure halves of the profiling, integrity, analytics and repro tooling: frame statistics
/// (<see cref="FrameStats"/>), the integrity report's two forms (<see cref="IntegrityReport"/>),
/// session totals (<see cref="AnalyticsAggregate"/>), the flight recorder's ring
/// (<see cref="FlightRecorder"/>), repro script parsing (<see cref="ReproHarness"/>) and the
/// <c>--perf-report</c> run length (<see cref="SessionPerfReport.Plan"/>). What samples a live
/// frame or reads the engine is in-engine.
/// </summary>
public class ProfilerToolingTests
{
    // --- FrameStats ------------------------------------------------------------------------------

    [Fact]
    public void FrameStats_CountsHitchesInNestedBands()
    {
        var stats = new FrameStats();
        foreach (double ms in new[] { 16.0, 16.5, 34.0, 51.0, 120.0 })
        {
            stats.Add(ms, 0.0);
        }

        Assert.Equal(5, stats.Count);
        Assert.Equal(3, stats.Over33);
        Assert.Equal(2, stats.Over50);
        Assert.Equal(1, stats.Over100);
        Assert.Equal(120.0, stats.Worst);
    }

    [Fact]
    public void FrameStats_KeepsTheWorstFiveHitchesWorstFirstWithTheirCause()
    {
        var stats = new FrameStats();
        for (int i = 0; i < 8; i++)
        {
            stats.Add(40.0 + i, i, i == 7 ? "gc2" : string.Empty);
        }

        Assert.Equal(FrameStats.WorstKept, stats.WorstHitches.Count);
        Assert.Equal(47.0, stats.WorstHitches[0].Milliseconds);
        Assert.Equal("gc2", stats.WorstHitches[0].Cause);
        Assert.Equal(7.0, stats.WorstHitches[0].AtSeconds);
        Assert.Equal(43.0, stats.WorstHitches[^1].Milliseconds);
    }

    [Fact]
    public void FrameStats_DistributionUsesTheWorldMonitorsPercentileRule()
    {
        var stats = new FrameStats();
        var samples = new List<double>();
        for (int i = 1; i <= 100; i++)
        {
            stats.Add(i, 0.0);
            samples.Add(i);
        }

        WorldFrameDistribution expected = WorldPerformanceRules.Distribution(samples);
        Assert.Equal(expected, stats.Distribution());
        Assert.Equal(50.0, stats.Distribution().P50);
        Assert.Equal(95.0, stats.Distribution().P95);
        Assert.Equal(100.0, stats.Distribution().Worst);
    }

    [Fact]
    public void FrameStats_IgnoresNonFiniteAndNegativeFrames_AndIsZeroWhenEmpty()
    {
        var stats = new FrameStats();
        stats.Add(double.NaN, 0.0);
        stats.Add(double.PositiveInfinity, 0.0);
        stats.Add(-1.0, 0.0);

        Assert.Equal(0, stats.Count);
        Assert.Equal(default, stats.Distribution());
        Assert.Equal(0.0, FrameStats.Median(Array.Empty<double>()));
        Assert.Equal(3.0, FrameStats.Median(new[] { 5.0, 1.0, 3.0 }));
    }

    // --- SessionPerfReport.Plan ------------------------------------------------------------------

    [Theory]
    [InlineData(10.0, -1.0, 3.0, 13.0, 3.0)]    // --perf-report=10: warm-up plus ten seconds
    [InlineData(-1.0, 20.0, 3.0, 19.75, 3.0)]   // bare flag: ends a quarter second before --quit-after
    [InlineData(-1.0, -1.0, 3.0, 23.0, 3.0)]    // neither: warm-up plus the default twenty
    [InlineData(60.0, 10.0, 3.0, 9.75, 3.0)]    // --quit-after is the earlier deadline and wins
    [InlineData(-1.0, 2.0, 3.0, 1.75, 0.875)]   // a warm-up longer than the run leaves half of it
    public void Plan_ChoosesTheRunLengthAndFitsTheWarmupInside(
        double own, double quitAfter, double warmup, double expectedTotal, double expectedWarmup)
    {
        double total = SessionPerfReport.Plan(own, quitAfter, ref warmup);

        Assert.Equal(expectedTotal, total, 6);
        Assert.Equal(expectedWarmup, warmup, 6);
    }

    // --- IntegrityReport -------------------------------------------------------------------------

    [Fact]
    public void IntegrityReport_CleanAndBrokenForms()
    {
        var report = new IntegrityReport();
        Assert.True(report.Ok);
        Assert.Equal("Integrity OK.", report.ToText());
        Assert.Equal("{\"ok\":true,\"issues\":[]}", report.ToJson());

        report.Add("orphans.leaked", "3 orphan node(s) leaked");
        report.Add("player.fell", "player is under the world");

        Assert.False(report.Ok);
        Assert.Equal(new[] { "orphans.leaked", "player.fell" }, report.Codes());
        Assert.StartsWith("Integrity: 2 issue(s).", report.ToText());
        Assert.Contains("• orphans.leaked: 3 orphan node(s) leaked", report.ToText());

        using JsonDocument json = JsonDocument.Parse(report.ToJson());
        Assert.False(json.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("player.fell", json.RootElement.GetProperty("issues")[1].GetProperty("code").GetString());
        Assert.DoesNotContain('\n', report.ToJson());
    }

    // --- AnalyticsAggregate ----------------------------------------------------------------------

    private static JsonElement Totals(AnalyticsAggregate totals)
    {
        using var stream = new MemoryStream();
        using (var json = new Utf8JsonWriter(stream))
        {
            json.WriteStartObject();
            totals.WriteTo(json);
            json.WriteEndObject();
        }

        using JsonDocument document = JsonDocument.Parse(Encoding.UTF8.GetString(stream.ToArray()));
        return document.RootElement.Clone();
    }

    [Fact]
    public void Aggregate_TotalsDamageBySourceAndTarget_LargestFirst()
    {
        var totals = new AnalyticsAggregate();
        Assert.True(totals.IsEmpty);
        totals.Damage("player", "enemy.goblin", 10);
        totals.Damage("player", "enemy.goblin", 15.5);
        totals.Damage("enemy.goblin", "player", 40);
        totals.Damage("player", "enemy.goblin", 0);          // a blocked hit for nothing is not a hit
        totals.Damage("player", "enemy.goblin", double.NaN);

        Assert.False(totals.IsEmpty);
        Assert.Equal(25.5, totals.DamageFrom("player"));
        JsonElement damage = Totals(totals).GetProperty("damage");
        Assert.Equal(2, damage.GetArrayLength());
        Assert.Equal("enemy.goblin", damage[0].GetProperty("source").GetString());
        Assert.Equal(40.0, damage[0].GetProperty("total").GetDouble());
        Assert.Equal(2, damage[1].GetProperty("hits").GetInt32());
    }

    [Fact]
    public void Aggregate_SplitsGoldInAndOutBySource()
    {
        var totals = new AnalyticsAggregate();
        totals.Gold(40, "pickup");
        totals.Gold(100, "quest");
        totals.Gold(-60, "menu");
        totals.Gold(25, "menu");
        totals.Gold(0, "other");

        Assert.Equal(165, totals.GoldIn);
        Assert.Equal(60, totals.GoldOut);
        JsonElement gold = Totals(totals).GetProperty("gold");
        Assert.Equal(60, gold.GetProperty("by_source").GetProperty("menu").GetProperty("out").GetInt32());
        Assert.Equal(25, gold.GetProperty("by_source").GetProperty("menu").GetProperty("in").GetInt32());
        Assert.False(gold.GetProperty("by_source").TryGetProperty("other", out _));
    }

    [Fact]
    public void Aggregate_SeparatesPlayerDeathsFromKills()
    {
        var totals = new AnalyticsAggregate();
        totals.Death("enemy.goblin", AnalyticsAggregate.Player);
        totals.Death("enemy.goblin", AnalyticsAggregate.Player);
        totals.Death("enemy.wolf", "companion.kael"); // not the player's kill
        totals.Death(AnalyticsAggregate.Player, "enemy.troll");
        totals.Death(AnalyticsAggregate.Player, string.Empty);

        Assert.Equal(2, totals.PlayerDeaths);
        JsonElement row = Totals(totals);
        Assert.Equal(2, row.GetProperty("kills").GetProperty("enemy.goblin").GetInt32());
        Assert.False(row.GetProperty("kills").TryGetProperty("enemy.wolf", out _));
        Assert.Equal(1, row.GetProperty("deaths_by_killer").GetProperty("enemy.troll").GetInt32());
        Assert.Equal(1, row.GetProperty("deaths_by_killer").GetProperty("unknown").GetInt32());
    }

    [Fact]
    public void Aggregate_TimesAQuestInPlaySeconds_AndSaysWhenItNeverSawTheStart()
    {
        var totals = new AnalyticsAggregate();
        totals.QuestStarted("quest.a", 10);
        totals.QuestStarted("quest.b", 12);

        Assert.Equal(32.5, totals.QuestEnded("quest.a", 42.5, "complete"));
        Assert.Null(totals.QuestEnded("quest.carried", 50, "complete"));

        JsonElement quests = Totals(totals).GetProperty("quests");
        Assert.Equal(32.5, quests.GetProperty("quest.a").GetProperty("seconds").GetDouble());
        Assert.Equal("open", quests.GetProperty("quest.b").GetProperty("result").GetString());
        Assert.False(quests.GetProperty("quest.carried").TryGetProperty("seconds", out _));
    }

    // --- FlightRecorder --------------------------------------------------------------------------

    [Fact]
    public void FlightRecorder_KeepsOnlyTheMostRecentNotes_OldestFirst()
    {
        var recorder = new FlightRecorder(3);
        for (int i = 1; i <= 5; i++)
        {
            recorder.Note("cmd", $"step {i}");
        }

        List<string> lines = recorder.Lines();
        Assert.Equal(3, recorder.Count);
        Assert.Equal(3, lines.Count);
        Assert.Contains("\"detail\":\"step 3\"", lines[0]);
        Assert.Contains("\"detail\":\"step 5\"", lines[2]);

        recorder.Clear();
        Assert.Empty(recorder.Lines());
    }

    [Fact]
    public void FlightRecorder_DumpWritesAHeaderThenTheNotes_AndReportsAnUnwritablePath()
    {
        var recorder = new FlightRecorder(8);
        recorder.Note("row", "{\"type\":\"death\"}");
        recorder.Note("error", "player \"fell\"");
        string directory = Path.Combine(Path.GetTempPath(), "embervale-flight-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "nested", "flight.jsonl");
        try
        {
            Assert.True(recorder.Dump(path, "player is under the world"));
            string[] lines = File.ReadAllLines(path);
            Assert.Equal(3, lines.Length);
            using JsonDocument header = JsonDocument.Parse(lines[0]);
            Assert.Equal("dump", header.RootElement.GetProperty("kind").GetString());
            Assert.Equal("player is under the world", header.RootElement.GetProperty("detail").GetString());
            using JsonDocument last = JsonDocument.Parse(lines[2]);
            Assert.Equal("player \"fell\"", last.RootElement.GetProperty("detail").GetString());

            // A directory where the file should be: the dump fails quietly.
            Assert.False(recorder.Dump(directory, "again"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    // --- ReproHarness ----------------------------------------------------------------------------

    [Fact]
    public void Repro_ParseSkipsBlanksAndComments_AndTrims()
    {
        List<string> commands = ReproHarness.Parse("# a swarm\r\nseed 1\r\n\r\n   spawn 6  \n#spawn 99\n");

        Assert.Equal(new[] { "seed 1", "spawn 6" }, commands);
    }

    [Theory]
    [InlineData("swarm", "res://tools/repro/swarm.txt")]
    [InlineData("tools/repro/swarm.txt", "tools/repro/swarm.txt")]
    [InlineData("C:\\bugs\\one.txt", "C:/bugs/one.txt")]
    [InlineData("mine.txt", "mine.txt")]
    public void Repro_PathFor_TreatsABareNameAsAShippedScript(string given, string expected)
    {
        Assert.Equal(expected, ReproHarness.PathFor(given));
    }

    [Fact]
    public void Repro_ExecuteRunsInOrderAndStopsAtTheFirstFailedStep()
    {
        var ran = new List<string>();
        string Console(string line)
        {
            ran.Add(line);
            return line.StartsWith("bogus", StringComparison.Ordinal) ? "unknown command 'bogus'" : "ok";
        }

        ReproResult passed = ReproHarness.Execute("fine", new[] { "seed 1", "spawn 6" }, Console);
        Assert.True(passed.Passed);
        Assert.Equal(2, passed.Steps);
        Assert.Contains("spawn 6 → ok", passed.Transcript);

        ran.Clear();
        ReproResult failed = ReproHarness.Execute("broken", new[] { "seed 1", "bogus", "spawn 6" }, Console);
        Assert.False(failed.Passed);
        Assert.Equal("bogus", failed.FailedStep);
        Assert.Equal(2, failed.Steps);
        Assert.Equal(new[] { "seed 1", "bogus" }, ran);
        Assert.Contains("FAILED", failed.Transcript);
    }

    [Fact]
    public void Repro_EveryShippedScriptIsPlainConsoleInputThatStartsBySeeding()
    {
        string root = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(root, "project.godot")))
        {
            root = Path.GetDirectoryName(root) ?? throw new InvalidOperationException("project root not found");
        }

        string[] scripts = Directory.GetFiles(Path.Combine(root, "tools", "repro"), "*" + ReproHarness.ScriptExtension);
        Assert.True(scripts.Length >= 6, "the six original scenarios are shipped as files");
        foreach (string script in scripts)
        {
            List<string> commands = ReproHarness.Parse(File.ReadAllText(script));
            Assert.True(commands.Count >= 2, $"{script} has a seed and at least one command");
            Assert.StartsWith("seed ", commands[0]);
        }
    }
}
