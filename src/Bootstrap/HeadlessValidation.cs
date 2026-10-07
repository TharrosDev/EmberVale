using System.Collections.Generic;
using Embervale.Debugging;
using Embervale.Localization;
using Godot;

namespace Embervale.Bootstrap;

/// <summary>
/// Headless content-validation entry point. Launching the game with the <c>--validate</c>
/// command-line argument runs the full <see cref="ContentValidator"/> battery (cross-references,
/// well-formedness, and graph reachability) and quits — without entering gameplay — exiting
/// non-zero if anything is broken:
/// <code>godot --headless --path . -- --validate</code>
/// (the <c>--</c> forwards <c>--validate</c> as a user argument). <see cref="ApplicationRoot"/>
/// defers to this before building the sandbox.
///
/// <para><b>Options.</b> <c>--list</c> prints every arm as <c>group/Arm</c> and quits 0.
/// <c>--only=a,b</c> and <c>--skip=a,b</c> take group or arm names (<see cref="ValidatorArmFilter"/>);
/// a filtered run is marked <c>partial</c> and is not the gate. <c>--slowest=N</c> sets how many
/// arm timings the result carries (default 5); <c>--json</c> carries every arm.</para>
///
/// <para><b>Output.</b> Each issue once, on stderr, as <c>[ERROR] validate: [group/Arm] issue</c>;
/// then <c>validate: PASS|FAIL</c> and the <c>EMBERVALE_RESULT</c> line. <c>--verbose</c> adds the
/// old bullet report. Exit 0 pass, 1 issues, 2 a filter that names nothing.</para>
/// </summary>
public static class HeadlessValidation
{
    /// <summary>The command-line argument that triggers headless validation.</summary>
    public const string FlagArgument = "--validate";

    /// <summary>True when <see cref="FlagArgument"/> was passed on the command line (whether as a
    /// user argument after <c>--</c> or as a raw engine argument).</summary>
    public static bool Requested() => HasFlag(FlagArgument);

    /// <summary>Whether <paramref name="flag"/> was passed, as a user argument after <c>--</c> or as a
    /// raw engine argument. Shared with <see cref="HeadlessEconomy"/> (38N1) so a second headless
    /// entry point does not mean a second copy of this loop — and so a flag that works one way for
    /// <c>--validate</c> works the same way for every other.</summary>
    public static bool HasFlag(string flag) => HeadlessArgs.Has(flag);

    /// <summary>Loads the content databases, runs the selected arms, and quits the tree with exit
    /// code 0 (OK), 1 (issues found) or 2 (bad filter). Call from a node already in the tree.</summary>
    public static void Run(SceneTree tree)
    {
        HeadlessReport report = HeadlessGate.Begin("validate");
        List<ValidatorArmInfo> arms = ContentValidator.ArmInfos();

        if (HeadlessArgs.Has("--list"))
        {
            var names = new List<string>();
            foreach (ValidatorArmInfo arm in arms)
            {
                names.Add($"{arm.Group}/{arm.Name}");
            }

            report.Fact("groups", ValidatorArmFilter.Groups(arms)).Fact("arms", names);
            HeadlessGate.Finish(tree, report, legacyLine: false);
            return;
        }

        IReadOnlyList<string> only = HeadlessArgs.List("--only");
        IReadOnlyList<string> skip = HeadlessArgs.List("--skip");
        string? refusal = ValidatorArmFilter.Select(arms, only, skip, out bool[] selected);
        if (refusal != null)
        {
            report.Refuse(refusal);
            HeadlessGate.Finish(tree, report, legacyLine: false);
            return;
        }

        ContentDatabases.InitializeAll();

        // The validator's companion checks resolve display keys through Loc, which only the gameplay
        // bootstrap used to initialize — headless runs saw an empty catalogue and reported every
        // authored key as missing. Idempotent, so this is a no-op when the bootstrap got there first.
        Loc.Initialize();

        List<ValidatorArmResult> results = ContentValidator.RunArms(selected);

        bool filtered = only.Count > 0 || skip.Count > 0;
        int issues = 0;
        var failed = new List<string>();
        foreach (ValidatorArmResult result in results)
        {
            foreach (string issue in result.Issues)
            {
                report.Fail($"[{result.Group}/{result.Name}] {issue}");
                HeadlessGate.Say($"  • {issue}");
            }

            issues += result.Issues.Count;
            if (result.Issues.Count > 0)
            {
                failed.Add($"{result.Group}/{result.Name}:{result.Issues.Count}");
            }
        }

        report.Fact("arms_run", results.Count).Fact("arms_total", arms.Count).Fact("issues", issues)
            .Fact("failed_arms", failed);
        if (filtered)
        {
            report.Fact("partial", true).Fact("only", only).Fact("skip", skip);
            report.Warn("filtered run (--only/--skip): not the gate.");
        }

        if (HeadlessGate.Json)
        {
            var all = new List<object?>();
            foreach (ValidatorArmResult result in results)
            {
                all.Add(new Dictionary<string, object?>
                {
                    ["arm"] = $"{result.Group}/{result.Name}", ["ms"] = result.Milliseconds, ["issues"] = result.Issues.Count,
                });
            }

            report.Fact("arms", all);
        }
        else
        {
            report.Fact("slowest", Slowest(results, HeadlessArgs.Int("--slowest", 5)));
        }

        HeadlessGate.Finish(tree, report);
    }

    /// <summary>The slowest arms as <c>group/Arm ms</c>, longest first.</summary>
    private static List<string> Slowest(List<ValidatorArmResult> results, int count)
    {
        var sorted = new List<ValidatorArmResult>(results);
        sorted.Sort((a, b) => b.Milliseconds.CompareTo(a.Milliseconds));
        var lines = new List<string>();
        for (int i = 0; i < sorted.Count && i < count; i++)
        {
            lines.Add($"{sorted[i].Group}/{sorted[i].Name} {sorted[i].Milliseconds}");
        }

        return lines;
    }
}
