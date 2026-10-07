using System;
using System.Collections.Generic;

namespace Embervale.Bootstrap;

/// <summary>One command-line mode and the options only it reads.</summary>
/// <param name="Flag">The mode flag, e.g. <c>--validate</c>.</param>
/// <param name="Help">One line for <c>--gates</c>.</param>
/// <param name="Options">Flags the mode accepts beside the common ones.</param>
/// <param name="Open">The mode parses its own arguments (positional ones included), so its
/// options are not checked.</param>
public sealed record HeadlessMode(string Flag, string Help, string[] Options, bool Open = false);

/// <summary>
/// Every headless mode and its options, and the check that a command line names one of them
/// correctly. Pure: <see cref="HeadlessGate"/> binds it to the running process.
///
/// <para>It exists because a misspelt flag used to be silent. <c>--validat</c> matched nothing, the
/// game booted to the title screen with no window, and the caller waited out its timeout. A typo now
/// ends the run at once with exit code 2 and the flag it probably meant.</para>
/// </summary>
public static class HeadlessFlags
{
    /// <summary>Flags every mode accepts.</summary>
    public static readonly string[] Common =
    {
        "--report", "--verbose", "--strict-build", "--seed", "--no-boot-validate", "--capture",
        "--max-seconds", "--json",
    };

    /// <summary>The modes, in the order <c>ApplicationRoot</c> tries them.</summary>
    public static readonly HeadlessMode[] Modes =
    {
        new("--gates", "List every headless mode and its options as JSON.", Array.Empty<string>()),
        new("--world-bake", "Engine half of the world bake (run through tools/world_bake.py).",
            Array.Empty<string>(), Open: true),
        new("--validate", "Content gate: cross-references, well-formedness, graph reachability.",
            new[] { "--only", "--skip", "--list", "--slowest" }),
        new("--economy", "Arbitrage report.", new[] { "--top" }),
        new("--worldgen", "World-generation report per region.", new[] { "--region" }),
        new("--worldmap", "Render region maps to PNG.", Array.Empty<string>(), Open: true),
        new("--state", "Content census and id queries.", new[] { "--count", "--ids", "--match", "--get" }),
        new("--lifecycle", "New Game / save / load / teardown leak gate.", new[] { "--cycles" }),
        new("--save-reload", "Quick-load and pause-menu reload audit (needs EMBERVALE_USER_DIR).",
            Array.Empty<string>()),
        new("--story", "Campaign playthrough gate.",
            new[] { "--story-only", "--story-mission", "--story-list" }),
        new("--arena", "Fight simulator: a bot plays the player against an enemy roster.",
            new[]
            {
                "--trials", "--level", "--weapon", "--equip", "--policy", "--count", "--speed",
                "--max-fight-s", "--budget-s", "--distance", "--setup", "--list",
            }),
    };

    /// <summary>The mode registered for <paramref name="flag"/>, or null.</summary>
    public static HeadlessMode? Find(string flag)
    {
        foreach (HeadlessMode mode in Modes)
        {
            if (mode.Flag == flag)
            {
                return mode;
            }
        }

        return null;
    }

    /// <summary>The name of a <c>--flag</c> or <c>--flag=value</c> argument; null for anything else
    /// (a value, a positional argument).</summary>
    public static string? NameOf(string argument)
    {
        if (!argument.StartsWith("--", StringComparison.Ordinal) || argument.Length < 3)
        {
            return null;
        }

        int equals = argument.IndexOf('=');
        return equals < 0 ? argument : argument[..equals];
    }

    /// <summary>
    /// Why this command line cannot run, or null when it can.
    ///
    /// <para>With one mode requested, every <c>--flag</c> among the user arguments has to be that
    /// mode's, a common one, or one of <paramref name="sessionFlags"/>. Two modes together are
    /// refused: only the first would have run, silently.</para>
    ///
    /// <para>With no mode requested the check is deliberately narrow, because other tools own flags
    /// this table has never heard of: it refuses only a flag that is a near miss of a mode flag,
    /// and only when no flag on the line is a known session flag (so the run would have sat on the
    /// title screen).</para>
    /// </summary>
    /// <param name="userArgs">The arguments after <c>--</c>.</param>
    /// <param name="requested">Whether a mode flag was passed, on either side of <c>--</c>.</param>
    /// <param name="sessionFlags">Flags the shell and the harness table read.</param>
    public static string? Validate(
        IReadOnlyList<string> userArgs, Func<string, bool> requested, IReadOnlyCollection<string> sessionFlags)
    {
        var modes = new List<HeadlessMode>();
        foreach (HeadlessMode mode in Modes)
        {
            if (requested(mode.Flag))
            {
                modes.Add(mode);
            }
        }

        if (modes.Count > 1)
        {
            var names = new List<string>();
            foreach (HeadlessMode mode in modes)
            {
                names.Add(mode.Flag);
            }

            return $"{string.Join(" and ", names)} were given together; a run is one mode (only {names[0]} would have run).";
        }

        if (modes.Count == 1)
        {
            HeadlessMode mode = modes[0];
            if (mode.Open)
            {
                return null;
            }

            foreach (string argument in userArgs)
            {
                string? name = NameOf(argument);
                if (name == null || name == mode.Flag || Contains(Common, name) || Contains(mode.Options, name) ||
                    Contains(sessionFlags, name))
                {
                    continue;
                }

                var known = new List<string>(mode.Options);
                known.AddRange(Common);
                string? near = Nearest(name, known, 3);
                return $"{mode.Flag} does not take {name}" +
                       (near != null ? $" (did you mean {near}?)" : string.Empty) +
                       $". Its options: {string.Join(" ", mode.Options)}; common: {string.Join(" ", Common)}.";
            }

            return null;
        }

        foreach (string argument in userArgs)
        {
            if (NameOf(argument) is { } name && Contains(sessionFlags, name))
            {
                return null;
            }
        }

        var modeFlags = new List<string>();
        foreach (HeadlessMode mode in Modes)
        {
            modeFlags.Add(mode.Flag);
        }

        foreach (string argument in userArgs)
        {
            if (NameOf(argument) is { } name && Nearest(name, modeFlags, 2) is { } near)
            {
                return $"unknown flag {name} (did you mean {near}?). Nothing on this command line starts a mode, " +
                       "so the game would have waited on the title screen.";
            }
        }

        return null;
    }

    /// <summary>The candidate within <paramref name="maxDistance"/> edits of
    /// <paramref name="name"/>, closest first; null when none is that close.</summary>
    public static string? Nearest(string name, IEnumerable<string> candidates, int maxDistance)
    {
        string? best = null;
        int bestDistance = maxDistance + 1;
        foreach (string candidate in candidates)
        {
            int distance = Distance(name, candidate);
            if (distance < bestDistance)
            {
                best = candidate;
                bestDistance = distance;
            }
        }

        return best;
    }

    /// <summary>Levenshtein distance.</summary>
    public static int Distance(string a, string b)
    {
        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];
        for (int j = 0; j <= b.Length; j++)
        {
            previous[j] = j;
        }

        for (int i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            for (int j = 1; j <= b.Length; j++)
            {
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
            }

            (previous, current) = (current, previous);
        }

        return previous[b.Length];
    }

    private static bool Contains(IEnumerable<string> values, string value)
    {
        foreach (string candidate in values)
        {
            if (candidate == value)
            {
                return true;
            }
        }

        return false;
    }
}
