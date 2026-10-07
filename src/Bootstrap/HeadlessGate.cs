using System;
using System.Collections.Generic;
using Embervale.Core.Diagnostics;
using Embervale.Save;
using Godot;

namespace Embervale.Bootstrap;

/// <summary>
/// What every headless mode shares: the command-line check, quiet output, the fixed seed, save
/// isolation, the wall-clock limit and the way a run ends.
///
/// <para><b>Output contract.</b> A mode prints nothing but warnings and errors unless
/// <c>--verbose</c> was passed (it lowers <see cref="Log.MinimumLevel"/> for the run and
/// <see cref="Say"/> is silent). It ends through <see cref="Finish"/>: each failure once on stderr as
/// <c>[ERROR] gate: message</c>, the legacy <c>gate: PASS|FAIL</c> line, then the single
/// <c>EMBERVALE_RESULT {json}</c> line of <see cref="HeadlessReport"/>. Exit code 0 pass, 1 failed,
/// 2 refused (bad flag, missing prerequisite).</para>
/// </summary>
public static class HeadlessGate
{
    /// <summary>Restores the prose a mode printed before it was made quiet.</summary>
    public const string VerboseArgument = "--verbose";

    /// <summary>Seeds the global random number generator before the mode runs.</summary>
    public const string SeedArgument = "--seed";

    /// <summary>Skips the boot-time content pass a headless session gate otherwise pays for.</summary>
    public const string NoBootValidateArgument = "--no-boot-validate";

    /// <summary>Ends a session gate that is still running after this many real seconds.</summary>
    public const string MaxSecondsArgument = "--max-seconds";

    /// <summary>The larger structured form of a report mode's facts.</summary>
    public const string JsonArgument = "--json";

    private static Log.Level _levelBefore = Log.Level.Trace;
    private static bool _quiet;
    private static bool _finished;
    private static string? _ownedUserDir;

    public static bool Verbose => HeadlessArgs.Has(VerboseArgument);

    public static bool Json => HeadlessArgs.Has(JsonArgument);

    /// <summary>True when the boot content pass was switched off for this run. The run is then
    /// partial: a session gate reads <c>Invariant.Violations</c>, which that pass feeds.</summary>
    public static bool SkipBootValidate => HeadlessArgs.Has(NoBootValidateArgument) || !ReadsBootValidation;

    /// <summary>False for the modes that do not read <c>Invariant.Violations</c> and so have no use
    /// for the boot content pass: the arena measures a fight, and the two listings start no session.
    /// It costs about 35 s of every launch, more than the fight it would precede.</summary>
    private static bool ReadsBootValidation =>
        !HeadlessArgs.Has(HeadlessArena.FlagArgument) && !HeadlessArgs.Has("--story-list");

    /// <summary>The mode this command line asks for, or null.</summary>
    public static HeadlessMode? RequestedMode()
    {
        foreach (HeadlessMode mode in HeadlessFlags.Modes)
        {
            if (HeadlessArgs.Has(mode.Flag))
            {
                return mode;
            }
        }

        return null;
    }

    /// <summary>
    /// The first thing a boot does about its command line. Refuses a misspelt or contradictory one
    /// (exit 2), answers <c>--gates</c>, and for a real mode applies quiet output and the seed.
    /// Returns true when it ended the run.
    /// </summary>
    public static bool Prepare(SceneTree tree)
    {
        string? refusal = HeadlessFlags.Validate(HeadlessArgs.User.All, HeadlessArgs.Has, SessionFlags());
        if (refusal != null)
        {
            var report = new HeadlessReport(RequestedMode()?.Flag.TrimStart('-') ?? "arguments");
            report.Refuse(refusal);
            Finish(tree, report, legacyLine: false);
            return true;
        }

        HeadlessMode? mode = RequestedMode();
        if (mode == null)
        {
            return false;
        }

        if (mode.Flag == "--gates")
        {
            var report = new HeadlessReport("gates");
            var modes = new List<object?>();
            foreach (HeadlessMode each in HeadlessFlags.Modes)
            {
                modes.Add(new Dictionary<string, object?>
                {
                    ["flag"] = each.Flag,
                    ["help"] = each.Help,
                    ["options"] = each.Open ? new[] { "(parses its own arguments)" } : each.Options,
                });
            }

            report.Fact("modes", modes).Fact("common", HeadlessFlags.Common);
            Finish(tree, report, legacyLine: false);
            return true;
        }

        // A mode that parses its own arguments is a production tool with a wrapper that prints its
        // progress lines through (the world bake, the map renderer): those stay as loud as they were.
        if (!Verbose && !mode.Open)
        {
            _levelBefore = Log.MinimumLevel;
            Log.MinimumLevel = Log.Level.Warn;
            Log.MirrorWarningsToEngine = false;
            _quiet = true;
        }

        if (HeadlessArgs.Value(SeedArgument) is { } seed)
        {
            if (ulong.TryParse(seed, out ulong parsed))
            {
                GD.Seed(parsed);
            }
            else
            {
                var report = new HeadlessReport(mode.Flag.TrimStart('-'));
                report.Refuse($"{SeedArgument} needs a whole number, e.g. {SeedArgument}=1; got '{seed}'.");
                Finish(tree, report, legacyLine: false);
                return true;
            }
        }

        return false;
    }

    /// <summary>A report for <paramref name="gate"/> carrying the facts every run shares: the seed,
    /// and <c>partial</c> when the boot content pass was skipped.</summary>
    public static HeadlessReport Begin(string gate)
    {
        var report = new HeadlessReport(gate);
        if (HeadlessArgs.Value(SeedArgument) is { } seed)
        {
            report.Fact("seed", seed);
        }

        if (HeadlessArgs.Has(NoBootValidateArgument) && ReadsBootValidation)
        {
            report.Fact("boot_validate", false).Fact("partial", true);
        }

        return report;
    }

    /// <summary>Prose for a person: printed only with <c>--verbose</c>.</summary>
    public static void Say(string text)
    {
        if (Verbose)
        {
            GD.Print(text);
        }
    }

    /// <summary>
    /// Ends the run: failures once each, the <c>gate: PASS|FAIL</c> line the older callers match,
    /// the result line, and the exit code. <paramref name="label"/> replaces the gate name in the
    /// legacy line (the reload audit reports as <c>save-reload</c>).
    /// </summary>
    public static void Finish(SceneTree tree, HeadlessReport report, bool legacyLine = true, string? label = null)
    {
        // A time limit can end a gate whose own flow is still mid-await and finishes later: the
        // first result is the run's, and a second line would break the one-line protocol.
        if (_finished)
        {
            return;
        }

        _finished = true;
        ReleaseIsolation(report);
        foreach (string failure in report.Failures)
        {
            // PrintErr, not Log.Error: that one also pushes an engine error, which prints the line
            // a second time with a backtrace under it.
            GD.PrintErr($"[ERROR] {report.Gate}: {failure}");
        }

        if (_quiet)
        {
            Log.MinimumLevel = _levelBefore;
            _quiet = false;
        }

        if (legacyLine)
        {
            string name = label ?? report.Gate;
            GD.Print(report.Passed ? $"{name}: PASS" : $"{name}: FAIL ({report.Failures.Count} failure(s))");
        }

        report.FinishAndQuit(tree);
    }

    /// <summary>
    /// Keeps a session gate off the developer's own saves. Such a gate builds and destroys
    /// sessions, levels characters and writes slots; it once did that with the real autosave ring
    /// live under it, and <c>auto1..auto3</c> ended up holding a "Lifecycle Audit" character.
    ///
    /// <para>Three independent guards: autosaves are off for the whole process, save writes are
    /// forced inline (a gate reads its files back on the next line), and when no absolute
    /// <c>EMBERVALE_USER_DIR</c> isolates the run it is pointed at a temp directory of its own,
    /// removed again by <see cref="Finish"/>. An export build ignores the variable and relies on
    /// the other two.</para>
    /// </summary>
    /// <param name="gate">Names the temp directory: <c>%TEMP%/embervale-&lt;gate&gt;/&lt;pid&gt;</c>.</param>
    public static void IsolateFromPlayerSaves(string gate)
    {
        AutosaveService.Suppressed = true;
        SaveWriteQueue.ForceInline = true;

        if (System.IO.Path.IsPathFullyQualified(OS.GetEnvironment("EMBERVALE_USER_DIR")))
        {
            return;
        }

        string isolated = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), $"embervale-{gate}", System.Environment.ProcessId.ToString());
        OS.SetEnvironment("EMBERVALE_USER_DIR", isolated);
        _ownedUserDir = isolated;
        Log.Info($"{gate}: no isolated EMBERVALE_USER_DIR was given; saves for this run go to '{isolated}'.");
    }

    private static void ReleaseIsolation(HeadlessReport report)
    {
        if (_ownedUserDir == null)
        {
            return;
        }

        try
        {
            if (System.IO.Directory.Exists(_ownedUserDir))
            {
                System.IO.Directory.Delete(_ownedUserDir, true);
            }
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            report.Warn($"could not remove the temp user directory '{_ownedUserDir}': {ex.Message}");
        }

        _ownedUserDir = null;
    }

    /// <summary>
    /// Arms <c>--max-seconds=N</c> for a session gate: after N real seconds (through pause, at any
    /// time scale) the run fails with what it had and quits, so a hang costs the limit and not the
    /// caller's whole timeout. Zero or absent arms nothing. <paramref name="partial"/> may add the
    /// facts gathered so far.
    /// </summary>
    public static void ArmTimeLimit(Node host, HeadlessReport report, Action? partial = null, string? label = null)
    {
        float seconds = HeadlessArgs.Float(MaxSecondsArgument, 0f);
        if (seconds <= 0f)
        {
            return;
        }

        SceneTree tree = host.GetTree();
        SceneTreeTimer timer = tree.CreateTimer(seconds, processAlways: true, processInPhysics: false, ignoreTimeScale: true);
        timer.Timeout += () =>
        {
            partial?.Invoke();
            report.Fail($"still running after {MaxSecondsArgument}={seconds:0.#}; ended with what it had.");
            Engine.TimeScale = 1.0;
            Finish(tree, report, label: label);
        };
    }

    /// <summary>The flags the shell and the harness table read, so a mode run beside one of them
    /// (<c>--save-reload --capture</c>) is not refused and a harness flag is never taken for a typo.</summary>
    private static List<string> SessionFlags()
    {
        var flags = new List<string>
        {
            GameShellController.PlayArgument, GameShellController.NewGameArgument,
            GameShellController.SlotArgument, GameShellController.QuitAfterArgument, "--shellshots",
        };
        foreach (SessionHarness harness in SessionHarnesses.All)
        {
            flags.Add(harness.Flag);
            if (harness.Alias != null)
            {
                flags.Add(harness.Alias);
            }
        }

        return flags;
    }
}
