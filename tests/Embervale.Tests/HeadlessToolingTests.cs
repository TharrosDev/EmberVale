using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Embervale.Bootstrap;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// The pure halves of the developer-tooling seams: command-line parsing
/// (<see cref="CommandLineArgs"/>), the gate result line (<see cref="HeadlessReport"/>), the
/// stale-binary comparison (<see cref="BuildFreshness"/>) and the <c>--new-game</c> safety rule
/// (<see cref="SessionEntryRules"/>). What reads the real command line or prints is in-engine.
/// </summary>
public class HeadlessToolingTests
{
    private static CommandLineArgs Args(params string[] args) => new(args);

    [Fact]
    public void Has_MatchesBareAndAssignedFlags_ButNotPrefixes()
    {
        CommandLineArgs args = Args("--play", "--slot=demo", "--world-bake-signature=x");

        Assert.True(args.Has("--play"));
        Assert.True(args.Has("--slot"));
        Assert.False(args.Has("--world-bake"));
        Assert.False(args.Has("--pla"));
        Assert.False(args.Has("--validate"));
    }

    [Fact]
    public void Value_ReadsEqualsAndSpaceForms()
    {
        Assert.Equal("demo", Args("--slot=demo").Value("--slot"));
        Assert.Equal("demo", Args("--slot", "demo").Value("--slot"));
        Assert.Equal("a=b", Args("--set=a=b").Value("--set"));
        Assert.Equal(string.Empty, Args("--slot=").Value("--slot"));
        Assert.Equal("-5", Args("--offset", "-5").Value("--offset"));
    }

    [Fact]
    public void Value_IsNullForAbsentOrBareFlags()
    {
        Assert.Null(Args("--play").Value("--slot"));
        Assert.Null(Args("--slot").Value("--slot"));

        // The next argument is another flag, not this flag's value.
        Assert.Null(Args("--slot", "--play").Value("--slot"));
    }

    [Fact]
    public void Value_FirstOccurrenceWins()
    {
        Assert.Equal("one", Args("--slot=one", "--slot=two").Value("--slot"));
    }

    [Fact]
    public void IntAndFloat_ParseInvariant_AndFallBack()
    {
        CommandLineArgs args = Args("--frames=120", "--quit-after", "2.5", "--bad=abc", "--nan=NaN");

        Assert.Equal(120, args.Int("--frames", 7));
        Assert.Equal(7, args.Int("--missing", 7));
        Assert.Equal(7, args.Int("--bad", 7));
        Assert.Equal(2.5f, args.Float("--quit-after", 0f));
        Assert.Equal(-1f, args.Float("--bad", -1f));
        Assert.Equal(-1f, args.Float("--nan", -1f));
        Assert.Equal(-1f, args.Float("--missing", -1f));
    }

    [Fact]
    public void List_SplitsOnCommas_TrimsAndDropsEmpties()
    {
        Assert.Equal(new[] { "a", "b", "c" }, Args("--only=a, b,,c").List("--only"));
        Assert.Empty(Args("--only").List("--only"));
        Assert.Empty(Args("--play").List("--only"));
    }

    [Fact]
    public void Report_Json_HasTheContractShape()
    {
        var report = new HeadlessReport("state");
        report.Fact("regions", 6).Fact("ratio", 0.5).Fact("open", true).Fact("name", "Ember \"Crown\"")
            .Fact("ids", new List<string> { "a", "b" }).Fact("none", null);
        report.Warn("slow");

        using JsonDocument json = JsonDocument.Parse(report.ToJson(42));
        JsonElement root = json.RootElement;

        Assert.Equal(HeadlessReport.Schema, root.GetProperty("schema").GetInt32());
        Assert.Equal("state", root.GetProperty("gate").GetString());
        Assert.True(root.GetProperty("ok").GetBoolean());
        Assert.Equal(0, root.GetProperty("exit_code").GetInt32());
        Assert.Equal(42, root.GetProperty("elapsed_ms").GetInt64());
        JsonElement facts = root.GetProperty("facts");
        Assert.Equal(6, facts.GetProperty("regions").GetInt32());
        Assert.Equal(0.5, facts.GetProperty("ratio").GetDouble());
        Assert.True(facts.GetProperty("open").GetBoolean());
        Assert.Equal("Ember \"Crown\"", facts.GetProperty("name").GetString());
        Assert.Equal(2, facts.GetProperty("ids").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, facts.GetProperty("none").ValueKind);
        Assert.Equal(0, root.GetProperty("failures").GetArrayLength());
        Assert.Equal("slow", root.GetProperty("warnings")[0].GetString());
        Assert.DoesNotContain('\n', report.ToJson(42));
    }

    [Fact]
    public void Report_Failure_SetsExitCode_AndDeduplicates()
    {
        var report = new HeadlessReport("story");
        Assert.True(report.Check(true, "unused"));
        Assert.False(report.Check(false, "act 2 did not start"));
        report.Fail("act 2 did not start");
        report.Fact("runs", 1).Fact("runs", 3);

        using JsonDocument json = JsonDocument.Parse(report.ToJson(0));
        JsonElement root = json.RootElement;

        Assert.False(report.Passed);
        Assert.Equal(1, report.ExitCode);
        Assert.False(root.GetProperty("ok").GetBoolean());
        Assert.Equal(1, root.GetProperty("exit_code").GetInt32());
        Assert.Equal(1, root.GetProperty("failures").GetArrayLength());
        Assert.Equal(3, root.GetProperty("facts").GetProperty("runs").GetInt32());
    }

    [Fact]
    public void Report_NonFiniteNumbers_StayValidJson()
    {
        var report = new HeadlessReport("perf");
        report.Fact("p95", double.NaN);

        using JsonDocument json = JsonDocument.Parse(report.ToJson(0));
        Assert.Equal(JsonValueKind.String, json.RootElement.GetProperty("facts").GetProperty("p95").ValueKind);
    }

    [Fact]
    public void Report_Emit_PrintsOneLine_AndWritesTheSameJsonToTheFile()
    {
        string directory = Path.Combine(Path.GetTempPath(), "embervale-tests-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "nested", "report.json");
        try
        {
            var report = new HeadlessReport("state");
            report.Fact("regions", 6);
            var lines = new List<string>();

            int code = report.Emit(lines.Add, path);

            Assert.Equal(0, code);
            string line = Assert.Single(lines);
            Assert.StartsWith(HeadlessReport.LinePrefix + " {", line);
            Assert.Equal(line[(HeadlessReport.LinePrefix.Length + 1)..], File.ReadAllText(path));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void Report_Emit_WithoutAPath_OnlyPrints()
    {
        var report = new HeadlessReport("state");
        report.Fail("broken");
        var lines = new List<string>();

        Assert.Equal(1, report.Emit(lines.Add, null));
        Assert.Single(lines);
    }

    [Fact]
    public void BuildFreshness_IsStaleOnlyWhenASourceIsNewerThanTheAssembly()
    {
        string root = Path.Combine(Path.GetTempPath(), "embervale-tests-" + Guid.NewGuid().ToString("N"));
        try
        {
            string source = Path.Combine(root, "src", "Deep");
            Directory.CreateDirectory(source);
            string dll = Path.Combine(root, "Embervale.dll");
            string project = Path.Combine(root, "Embervale.csproj");
            string code = Path.Combine(source, "Thing.cs");
            string other = Path.Combine(source, "notes.txt");
            foreach (string file in new[] { dll, project, code, other })
            {
                File.WriteAllText(file, "x");
            }

            DateTime built = new(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);
            File.SetLastWriteTimeUtc(dll, built);
            File.SetLastWriteTimeUtc(project, built.AddMinutes(-5));
            File.SetLastWriteTimeUtc(code, built.AddMinutes(-1));
            File.SetLastWriteTimeUtc(other, built.AddMinutes(30));

            // Only *.cs and the project file count.
            Assert.False(BuildFreshness.Evaluate(dll, Path.Combine(root, "src"), project, out string fresh));
            Assert.Equal(string.Empty, fresh);

            File.SetLastWriteTimeUtc(code, built.AddMinutes(1));
            Assert.True(BuildFreshness.Evaluate(dll, Path.Combine(root, "src"), project, out string stale));
            Assert.StartsWith(BuildFreshness.Marker + " ", stale);
            Assert.Contains("Thing.cs", stale);
            Assert.DoesNotContain('\n', stale);

            File.SetLastWriteTimeUtc(code, built.AddMinutes(-1));
            File.SetLastWriteTimeUtc(project, built.AddMinutes(1));
            Assert.True(BuildFreshness.Evaluate(dll, Path.Combine(root, "src"), project, out _));

            // Cannot tell is not stale.
            Assert.False(BuildFreshness.Evaluate(Path.Combine(root, "missing.dll"), Path.Combine(root, "src"), project, out _));
            Assert.False(BuildFreshness.Evaluate(dll, Path.Combine(root, "missing"), project, out _));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void NewGame_IsRefusedOutsideIsolationWhateverTheSlot()
    {
        Assert.NotNull(SessionEntryRules.NewGameRefusal("automation", isolated: false));
        Assert.NotNull(SessionEntryRules.NewGameRefusal("automation_combat-2", isolated: false));
        Assert.NotNull(SessionEntryRules.NewGameRefusal("quick", isolated: false));
        Assert.Null(SessionEntryRules.NewGameRefusal("automation", isolated: true));
        Assert.Null(SessionEntryRules.NewGameRefusal("automation_combat-2", isolated: true));
        Assert.Null(SessionEntryRules.NewGameRefusal("quick", isolated: true));
    }

    [Fact]
    public void NewGame_RejectsSlotNamesThatAreNotPlainDirectoryNames()
    {
        foreach (string slot in new[] { "", "../automation", "automation/x", "automation x", "automation.json", new string('a', 65) })
        {
            Assert.NotNull(SessionEntryRules.NewGameRefusal(slot, isolated: true));
        }
    }

    [Fact]
    public void Isolation_NeedsAnAbsoluteDirectory()
    {
        Assert.True(SessionEntryRules.IsIsolated(Path.GetTempPath()));
        Assert.False(SessionEntryRules.IsIsolated("relative/dir"));
        Assert.False(SessionEntryRules.IsIsolated(""));
        Assert.False(SessionEntryRules.IsIsolated(null));
    }

    [Fact]
    public void Script_NeedsIsolationOrTheExplicitFlag()
    {
        string? refusal = SessionEntryRules.ScriptRefusal("--exec", isolated: false, allowRealSave: false);
        Assert.NotNull(refusal);
        Assert.Contains("--exec", refusal);
        Assert.Contains(SessionEntryRules.AllowRealSaveArgument, refusal);
        Assert.Null(SessionEntryRules.ScriptRefusal("--exec", isolated: true, allowRealSave: false));
        Assert.Null(SessionEntryRules.ScriptRefusal("--repro", isolated: false, allowRealSave: true));
    }

    private static KeyValuePair<string, double>[] Slots(params (string, double)[] slots) =>
        System.Array.ConvertAll(slots, slot => new KeyValuePair<string, double>(slot.Item1, slot.Item2));

    [Fact]
    public void Slot_NamedByTheFlagIsThatSaveOrNothing()
    {
        KeyValuePair<string, double>[] slots = Slots(("quick", 30), ("fixture", 10), ("auto1", 20));

        Assert.Equal("fixture", SaveSlotChoice.Pick(slots, "fixture", null, out string? problem, out bool fellBack));
        Assert.Null(problem);
        Assert.False(fellBack);

        // A typo must not run the caller's script against the newest save.
        Assert.Null(SaveSlotChoice.Pick(slots, "fixtur", "quick", out problem, out fellBack));
        Assert.Contains("--slot=fixtur", problem);
        Assert.Contains("auto1, fixture, quick", problem);
        Assert.False(fellBack);

        Assert.Null(SaveSlotChoice.Pick(Slots(), "fixture", null, out problem, out _));
        Assert.Contains("no saves", problem);
    }

    [Fact]
    public void Slot_WithoutTheFlagIsTheVariableOrTheNewest()
    {
        KeyValuePair<string, double>[] slots = Slots(("quick", 30), ("fixture", 10), ("auto1", 20));

        Assert.Equal("quick", SaveSlotChoice.Pick(slots, null, null, out string? problem, out bool fellBack));
        Assert.Null(problem);
        Assert.False(fellBack);

        Assert.Equal("fixture", SaveSlotChoice.Pick(slots, "", "fixture", out _, out fellBack));
        Assert.False(fellBack);

        Assert.Equal("quick", SaveSlotChoice.Pick(slots, null, "gone", out problem, out fellBack));
        Assert.Null(problem);
        Assert.True(fellBack);

        Assert.Null(SaveSlotChoice.Pick(Slots(), null, null, out problem, out fellBack));
        Assert.Null(problem);
        Assert.False(fellBack);
    }
}
