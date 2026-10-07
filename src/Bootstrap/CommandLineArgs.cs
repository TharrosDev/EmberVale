using System;
using System.Collections.Generic;
using System.Globalization;

namespace Embervale.Bootstrap;

/// <summary>
/// The one command-line parser, over a plain list of arguments so it can be unit-tested without the
/// engine. <see cref="HeadlessArgs"/> is the same thing bound to the running process.
///
/// <para>A flag is present when an argument equals it (<c>--play</c>) or assigns it
/// (<c>--slot=demo</c>). A value is read from <c>--key=value</c> or from the argument after
/// <c>--key</c>, unless that argument is itself a <c>--flag</c>. The first occurrence wins.</para>
/// </summary>
public sealed class CommandLineArgs
{
    private readonly List<string> _args = new();

    public CommandLineArgs(IEnumerable<string> args)
    {
        _args.AddRange(args);
    }

    /// <summary>Every argument, in the order given.</summary>
    public IReadOnlyList<string> All => _args;

    /// <summary>True when <paramref name="flag"/> was passed, bare or with a value.</summary>
    public bool Has(string flag) => IndexOf(flag) >= 0;

    /// <summary>The value given to <paramref name="flag"/>, or null when the flag is absent or bare.
    /// <c>--key=</c> is an empty string, not null.</summary>
    public string? Value(string flag)
    {
        int index = IndexOf(flag);
        if (index < 0)
        {
            return null;
        }

        string arg = _args[index];
        if (arg.Length > flag.Length)
        {
            return arg[(flag.Length + 1)..];
        }

        if (index + 1 < _args.Count && !_args[index + 1].StartsWith("--", StringComparison.Ordinal))
        {
            return _args[index + 1];
        }

        return null;
    }

    /// <summary>The flag's value as an integer; <paramref name="fallback"/> when it is absent or
    /// does not parse.</summary>
    public int Int(string flag, int fallback) =>
        int.TryParse(Value(flag), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
            ? parsed
            : fallback;

    /// <summary>The flag's value as a number (invariant culture); <paramref name="fallback"/> when
    /// it is absent, does not parse, or is not finite.</summary>
    public float Float(string flag, float fallback) =>
        float.TryParse(Value(flag), NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed) &&
        float.IsFinite(parsed)
            ? parsed
            : fallback;

    /// <summary>The flag's comma-separated value as trimmed, non-empty entries. Empty when the flag
    /// is absent or bare.</summary>
    public IReadOnlyList<string> List(string flag) =>
        (Value(flag) ?? string.Empty).Split(
            ',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private int IndexOf(string flag)
    {
        for (int i = 0; i < _args.Count; i++)
        {
            string arg = _args[i];
            if (arg == flag ||
                (arg.Length > flag.Length && arg[flag.Length] == '=' && arg.StartsWith(flag, StringComparison.Ordinal)))
            {
                return i;
            }
        }

        return -1;
    }
}
