using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Embervale.Debugging;

/// <summary>What a script statement is: a dev-console command, or one of the runner's own verbs.</summary>
public enum ScriptVerb
{
    /// <summary>Anything else: handed to <see cref="DevConsole.Run"/>.</summary>
    Command,

    /// <summary><c>wait &lt;seconds&gt;</c>: game seconds (scaled by the time scale).</summary>
    Wait,

    /// <summary><c>frames &lt;n&gt;</c>: process frames.</summary>
    Frames,

    /// <summary><c>wait-until &lt;key&gt; &lt;op&gt; &lt;value&gt; [seconds=10]</c>.</summary>
    WaitUntil,

    /// <summary><c>assert &lt;key&gt; &lt;op&gt; &lt;value&gt;</c>.</summary>
    Assert,

    /// <summary><c>expect &lt;text&gt;</c>: the previous command's output contains it.</summary>
    Expect,

    /// <summary><c>shot &lt;name&gt;</c>: a PNG of the viewport.</summary>
    Shot,

    /// <summary><c>input &lt;action&gt; [frames=2]</c>: press, hold, release.</summary>
    Input,

    /// <summary><c>quit</c>: stop the script here.</summary>
    Quit,
}

/// <summary>One statement of a console script.</summary>
public readonly record struct ScriptStep(ScriptVerb Verb, string Raw, string[] Args);

/// <summary>
/// The text half of the dev console and its script runner: tokenising, splitting a script into
/// statements, telling a failed legacy reply from a good one, and comparing a queried value with an
/// expected one. Pure (no engine calls), so it is unit-tested.
/// </summary>
public static class ConsoleText
{
    /// <summary>The comparison operators <c>assert</c> and <c>wait-until</c> accept.</summary>
    public static readonly string[] Operators = { "eq", "ne", "gt", "ge", "lt", "le", "contains" };

    private static readonly string[] FailurePrefixes =
    {
        "error:", "usage:", "unknown", "no ", "cannot", "could not", "missing", "refused",
    };

    /// <summary>Replies that start like a failure and are not one: an empty listing.</summary>
    private static readonly string[] BenignPrefixes =
    {
        "no flags set", "no spells", "no travel nodes attuned", "no persistent actors",
    };

    /// <summary>Splits a command line on whitespace. A double-quoted run is one token, without the
    /// quotes: <c>expect "gave 2x"</c> is two tokens.</summary>
    public static string[] Tokenize(string line)
    {
        var tokens = new List<string>();
        var current = new StringBuilder();
        bool quoted = false;
        bool open = false;
        foreach (char c in line)
        {
            if (c == '"')
            {
                quoted = !quoted;
                open = true; // "" is an empty token, not nothing
            }
            else if (char.IsWhiteSpace(c) && !quoted)
            {
                if (open)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                    open = false;
                }
            }
            else
            {
                current.Append(c);
                open = true;
            }
        }

        if (open)
        {
            tokens.Add(current.ToString());
        }

        return tokens.ToArray();
    }

    /// <summary>Splits a script into statements on <c>;</c> and newlines. <c>#</c> at the start of a
    /// statement or after whitespace starts a comment that runs to the end of the line. Neither
    /// applies inside double quotes. Empty statements are dropped.</summary>
    public static List<string> SplitScript(string text)
    {
        var statements = new List<string>();
        var current = new StringBuilder();
        bool quoted = false;
        bool comment = false;
        foreach (char c in text)
        {
            if (c == '\n' || c == '\r')
            {
                Flush(statements, current);
                quoted = false;
                comment = false;
                continue;
            }

            if (comment)
            {
                continue;
            }

            if (c == '"')
            {
                quoted = !quoted;
            }
            else if (!quoted && c == ';')
            {
                Flush(statements, current);
                continue;
            }
            else if (!quoted && c == '#' && (current.Length == 0 || char.IsWhiteSpace(current[^1])))
            {
                comment = true;
                continue;
            }

            current.Append(c);
        }

        Flush(statements, current);
        return statements;
    }

    private static void Flush(List<string> statements, StringBuilder current)
    {
        string statement = current.ToString().Trim();
        current.Clear();
        if (statement.Length > 0)
        {
            statements.Add(statement);
        }
    }

    /// <summary>Names the verb of one statement and tokenises its arguments.</summary>
    public static ScriptStep Classify(string statement)
    {
        string[] tokens = Tokenize(statement);
        if (tokens.Length == 0)
        {
            return new ScriptStep(ScriptVerb.Command, statement, Array.Empty<string>());
        }

        ScriptVerb verb = tokens[0].ToLowerInvariant() switch
        {
            "wait" => ScriptVerb.Wait,
            "frames" => ScriptVerb.Frames,
            "wait-until" => ScriptVerb.WaitUntil,
            "assert" => ScriptVerb.Assert,
            "expect" => ScriptVerb.Expect,
            "shot" => ScriptVerb.Shot,
            "input" => ScriptVerb.Input,
            "quit" => ScriptVerb.Quit,
            _ => ScriptVerb.Command,
        };
        return new ScriptStep(verb, statement, verb == ScriptVerb.Command ? tokens : tokens[1..]);
    }

    /// <summary>Why a runner verb's arguments are unusable, or null. Checked for the whole script
    /// before the first statement runs, so a typo in step nine does not cost eight steps.</summary>
    public static string? Problem(ScriptStep step)
    {
        string[] a = step.Args;
        switch (step.Verb)
        {
            case ScriptVerb.Wait:
                return a.Length == 1 && TryNumber(a[0].TrimEnd('s'), out double seconds) && seconds >= 0 && seconds <= 600
                    ? null
                    : "usage: wait <seconds 0..600>";
            case ScriptVerb.Frames:
                return a.Length == 1 && int.TryParse(a[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) &&
                       n >= 0 && n <= 36000
                    ? null
                    : "usage: frames <n 0..36000>";
            case ScriptVerb.Assert:
                return a.Length == 3 && IsOperator(a[1]) ? null : "usage: assert <key> <eq|ne|gt|ge|lt|le|contains> <value>";
            case ScriptVerb.WaitUntil:
                return (a.Length == 3 || (a.Length == 4 && TryNumber(a[3], out double limit) && limit > 0 && limit <= 600)) &&
                       IsOperator(a[1])
                    ? null
                    : "usage: wait-until <key> <eq|ne|gt|ge|lt|le|contains> <value> [seconds 0..600, default 10]";
            case ScriptVerb.Expect:
                return a.Length == 1 ? null : "usage: expect <text> (quote it if it has spaces)";
            case ScriptVerb.Shot:
                return a.Length == 1 && IsFileName(a[0]) ? null : "usage: shot <name> (letters, digits, _ and -)";
            case ScriptVerb.Input:
                return a.Length == 1 || (a.Length == 2 && int.TryParse(a[1], out int hold) && hold >= 1 && hold <= 3600)
                    ? null
                    : "usage: input <action> [frames 1..3600, default 2]";
            case ScriptVerb.Quit:
                return a.Length == 0 ? null : "usage: quit";
            default:
                return null;
        }
    }

    public static bool IsOperator(string op) => Array.IndexOf(Operators, op.ToLowerInvariant()) >= 0;

    /// <summary>Letters, digits, <c>_</c> and <c>-</c> only, 1 to 64 of them.</summary>
    public static bool IsFileName(string name)
    {
        if (name.Length == 0 || name.Length > 64)
        {
            return false;
        }

        foreach (char c in name)
        {
            if (!(char.IsAsciiLetterOrDigit(c) || c == '_' || c == '-'))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>True when a reply from a handler that predates <see cref="DevConsole.Fail"/> reads
    /// as a failure. An empty reply is not one: <c>clear</c> returns nothing.</summary>
    public static bool LooksFailed(string output)
    {
        string text = output.TrimStart();
        foreach (string benign in BenignPrefixes)
        {
            if (text.StartsWith(benign, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        foreach (string prefix in FailurePrefixes)
        {
            if (text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        // "fast-travel service unavailable", "repro is unavailable in a shipping build".
        int line = text.IndexOf('\n');
        string first = line < 0 ? text : text[..line];
        return first.Contains("unavailable", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Compares a queried value with an expected one. Two numbers compare as numbers
    /// (<c>true</c>/<c>false</c> count as 1/0); otherwise <c>eq</c>, <c>ne</c> and <c>contains</c>
    /// compare text without case and the ordering operators are false.</summary>
    public static bool Compare(string actual, string op, string expected)
    {
        op = op.ToLowerInvariant();
        if (op == "contains")
        {
            return actual.Contains(expected, StringComparison.OrdinalIgnoreCase);
        }

        if (TryNumber(actual, out double a) && TryNumber(expected, out double b))
        {
            return op switch
            {
                "eq" => Math.Abs(a - b) < 1e-6,
                "ne" => Math.Abs(a - b) >= 1e-6,
                "gt" => a > b,
                "ge" => a >= b,
                "lt" => a < b,
                "le" => a <= b,
                _ => false,
            };
        }

        bool same = string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
        return op switch
        {
            "eq" => same,
            "ne" => !same,
            _ => false,
        };
    }

    /// <summary>A finite number in invariant culture, or <c>true</c>/<c>false</c> as 1/0.</summary>
    public static bool TryNumber(string text, out double value)
    {
        if (string.Equals(text, "true", StringComparison.OrdinalIgnoreCase))
        {
            value = 1;
            return true;
        }

        if (string.Equals(text, "false", StringComparison.OrdinalIgnoreCase))
        {
            value = 0;
            return true;
        }

        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && double.IsFinite(value);
    }

    /// <summary>A query value as the text <c>get</c> prints and <c>assert</c> compares: lower-case
    /// booleans, invariant numbers with at most three decimals.</summary>
    public static string Format(object? value) => value switch
    {
        null => string.Empty,
        bool flag => flag ? "true" : "false",
        float f => Math.Round(f, 3).ToString(CultureInfo.InvariantCulture),
        double d => Math.Round(d, 3).ToString(CultureInfo.InvariantCulture),
        IFormattable number => number.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty,
    };
}
