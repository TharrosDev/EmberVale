using System.Collections.Generic;
using System.Text;
using Embervale.Bootstrap;
using Godot;

namespace Embervale.Debugging;

/// <summary>
/// The dev-console command reference without a session:
/// <code>godot --headless --path . -- --console-help        (one "usage — summary" line per command)
/// godot --headless --path . -- --console-help=md     (a Markdown table, for the docs)
/// godot --headless --path . -- --console-help=json   (one JSON array)</code>
/// It registers the commands on a console that never enters the tree, so the table is the one the
/// game builds and cannot drift from it. The script runner's own verbs and the <c>get</c> keys are
/// listed after the commands. Ends with an <c>EMBERVALE_RESULT</c> line and exits 0.
/// </summary>
public static class ConsoleHelp
{
    public const string FlagArgument = "--console-help";

    /// <summary>The runner verbs, in the same usage/summary shape as a command.</summary>
    public static readonly (string Usage, string Summary)[] RunnerVerbs =
    {
        ("wait <seconds>", "Runner: let game time pass (scaled by timescale)."),
        ("frames <n>", "Runner: let n process frames pass."),
        ("wait-until <key> <op> <value> [seconds=10]", "Runner: poll a get key each frame until it compares true; fails after that many real seconds."),
        ("assert <key> <op> <value>", "Runner: fail unless a get key compares true. Ops: eq ne gt ge lt le contains."),
        ("expect <text>", "Runner: fail unless the previous command's reply contains the text."),
        ("shot <name>", "Runner: write the viewport to <output>/<name>.png two frames later. Needs a window."),
        ("input <action> [frames=2]", "Runner: press an InputMap action, hold it that many frames, release it."),
        ("quit", "Runner: stop the script here."),
    };

    public static bool Requested() => HeadlessArgs.Has(FlagArgument);

    public static void Run(SceneTree tree)
    {
        var console = new DevConsole();
        DevCommands.RegisterAll(console);
        var commands = new List<ConsoleCommand>(console.Commands.Values);
        console.Free();

        string format = (HeadlessArgs.Value(FlagArgument) ?? string.Empty).ToLowerInvariant();
        GD.Print(Render(commands, format));
        new HeadlessReport("console-help")
            .Fact("commands", commands.Count)
            .Fact("runner_verbs", RunnerVerbs.Length)
            .Fact("get_keys", DevCommands.QueryKeys.Length)
            .FinishAndQuit(tree);
    }

    /// <summary>The reference in one of the three formats (<c>md</c>, <c>json</c>, else text).</summary>
    public static string Render(IReadOnlyList<ConsoleCommand> commands, string format)
    {
        var rows = new List<(string Usage, string Summary)>();
        foreach (ConsoleCommand command in commands)
        {
            rows.Add((command.Usage, command.Summary));
        }

        rows.AddRange(RunnerVerbs);
        var sb = new StringBuilder();
        switch (format)
        {
            case "json":
            {
                var table = new Godot.Collections.Array();
                foreach ((string usage, string summary) in rows)
                {
                    table.Add(new Godot.Collections.Dictionary { ["usage"] = usage, ["summary"] = summary });
                }

                return Json.Stringify(new Godot.Collections.Dictionary
                {
                    ["commands"] = table, ["get_keys"] = DevCommands.QueryKeys,
                });
            }
            case "md":
                sb.Append("| Command | What it does |\n| --- | --- |\n");
                foreach ((string usage, string summary) in rows)
                {
                    sb.Append($"| `{usage.Replace("|", "\\|")}` | {summary.Replace("|", "\\|")} |\n");
                }

                sb.Append($"\n`get` keys: `{string.Join("` `", DevCommands.QueryKeys)}`\n");
                return sb.ToString();
            default:
                foreach ((string usage, string summary) in rows)
                {
                    sb.Append($"{usage}  — {summary}\n");
                }

                sb.Append($"get keys: {string.Join(" ", DevCommands.QueryKeys)}\n");
                return sb.ToString();
        }
    }
}
