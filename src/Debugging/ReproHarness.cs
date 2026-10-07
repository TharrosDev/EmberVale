using System;
using System.Collections.Generic;
using System.Text;
using Embervale.Analytics;
using Godot;

namespace Embervale.Debugging;

/// <summary>What running one repro script did: how many commands ran, which one stopped it (null
/// when all ran), and the transcript.</summary>
public sealed record ReproResult(string Name, int Steps, string? FailedStep, string Transcript)
{
    public bool Passed => FailedStep == null;
}

/// <summary>
/// Replays a repro script: a plain text file of dev-console commands, one per line, run in order
/// through the console. The scripts live in <c>tools/repro/&lt;name&gt;.txt</c>; blank lines and lines
/// starting with <c>#</c> are skipped. A script that needs repeatable randomness starts with the
/// console's own <c>seed &lt;n&gt;</c> command, so the file is nothing but console input and any
/// console-script runner can execute it unchanged.
///
/// <para>Three ways in: <c>repro &lt;name&gt;</c> in the F1 console, <c>-- --repro=&lt;name&gt;</c> from
/// a shell (<see cref="ReproRun"/>), and a path instead of a name for a file kept elsewhere. A new
/// scenario is a new file; nothing here lists them.</para>
///
/// <para>⚠️ <see cref="Embervale.Loot.LootGenerator"/> keeps its own RNG and randomises it, so
/// <c>seed</c> does not make a loot roll repeat.</para>
/// </summary>
public static class ReproHarness
{
    public const string ScriptDirectory = "res://tools/repro";
    public const string ScriptExtension = ".txt";

    /// <summary>The scripts in <see cref="ScriptDirectory"/>, by name.</summary>
    public static IEnumerable<string> Names
    {
        get
        {
            var names = new List<string>();
            foreach (string file in DirAccess.GetFilesAt(ScriptDirectory))
            {
                if (file.EndsWith(ScriptExtension, StringComparison.OrdinalIgnoreCase))
                {
                    names.Add(file[..^ScriptExtension.Length]);
                }
            }

            names.Sort(StringComparer.Ordinal);
            return names;
        }
    }

    /// <summary>The commands in a script's text: every line that is not blank and not a comment,
    /// trimmed. Pure.</summary>
    public static List<string> Parse(string text)
    {
        var commands = new List<string>();
        foreach (string raw in text.Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length > 0 && line[0] != '#')
            {
                commands.Add(line);
            }
        }

        return commands;
    }

    /// <summary>Where a script lives: a bare name is a file in <see cref="ScriptDirectory"/>;
    /// anything with a slash or the extension is used as the path it is. Pure.</summary>
    public static string PathFor(string nameOrPath)
    {
        bool isPath = nameOrPath.Contains('/') || nameOrPath.Contains('\\') ||
                      nameOrPath.EndsWith(ScriptExtension, StringComparison.OrdinalIgnoreCase);
        return isPath ? nameOrPath.Replace('\\', '/') : $"{ScriptDirectory}/{nameOrPath}{ScriptExtension}";
    }

    /// <summary>Runs a script through <paramref name="exec"/> (the console's command runner) and
    /// returns the transcript. The F1 <c>repro</c> command prints this.</summary>
    public static string Run(string name, Func<string, string> exec) => Execute(name, exec).Transcript;

    /// <summary>Runs a script by name or path, stopping at the first command whose output says it
    /// did not reach its prerequisite.</summary>
    public static ReproResult Execute(string name, Func<string, string> exec)
    {
        string path = PathFor(name);
        if (!FileAccess.FileExists(path))
        {
            return new ReproResult(name, 0, "(load)",
                $"unknown scenario '{name}' ({path}) — try: {string.Join(", ", Names)}");
        }

        return Execute(name, Parse(FileAccess.GetFileAsString(path)), exec);
    }

    /// <summary>Runs commands already parsed. Pure apart from what <paramref name="exec"/> does.</summary>
    public static ReproResult Execute(string name, IReadOnlyList<string> commands, Func<string, string> exec)
    {
        var sb = new StringBuilder($"repro '{name}' ({commands.Count} command(s)):\n");
        int steps = 0;
        foreach (string command in commands)
        {
            FlightRecorder.Shared.Note("cmd", command);
            string output = exec(command);
            steps++;
            sb.Append($"  {command} → {output}\n");
            if (Failed(output))
            {
                sb.Append("  FAILED: scenario stopped because this step did not reach its prerequisite");
                return new ReproResult(name, steps, command, sb.ToString());
            }
        }

        return new ReproResult(name, steps, null, sb.ToString().TrimEnd());
    }

    private static bool Failed(string output) =>
        string.IsNullOrWhiteSpace(output) || output.StartsWith("error:", StringComparison.OrdinalIgnoreCase) ||
        output.StartsWith("unknown ", StringComparison.OrdinalIgnoreCase) ||
        output.StartsWith("no player", StringComparison.OrdinalIgnoreCase) ||
        output.StartsWith("missing ", StringComparison.OrdinalIgnoreCase);
}
