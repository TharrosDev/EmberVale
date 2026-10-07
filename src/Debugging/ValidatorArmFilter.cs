using System;
using System.Collections.Generic;

namespace Embervale.Debugging;

/// <summary>An arm of the content battery by name: what <c>--validate --list</c> prints.</summary>
public readonly record struct ValidatorArmInfo(string Name, string Group, bool Graph);

/// <summary>What one arm found and how long it took.</summary>
public sealed record ValidatorArmResult(string Name, string Group, long Milliseconds, IReadOnlyList<string> Issues);

/// <summary>
/// Chooses which arms of <see cref="ContentValidator"/> a filtered run executes. Pure, so the
/// selection rules are unit-tested without loading a database.
///
/// <para>A term names a group (<c>items</c>), an arm (<c>Regions</c>) or both (<c>world/Regions</c>),
/// case-insensitively. <c>--only</c> keeps what any of its terms match; <c>--skip</c> then removes
/// what any of its terms match. A term that matches nothing is an error, never an empty run: a typo
/// in a filter must not read as "nothing to report".</para>
/// </summary>
public static class ValidatorArmFilter
{
    /// <summary>Marks the selected arms. Returns null, or why the filter cannot be applied.</summary>
    public static string? Select(
        IReadOnlyList<ValidatorArmInfo> arms,
        IReadOnlyList<string> only,
        IReadOnlyList<string> skip,
        out bool[] selected)
    {
        selected = new bool[arms.Count];
        foreach (string term in Concat(only, skip))
        {
            if (!MatchesAny(arms, term))
            {
                return $"'{term}' is not a validator group or arm. Groups: {string.Join(", ", Groups(arms))}. " +
                       "Arms: --validate --list.";
            }
        }

        bool any = false;
        for (int i = 0; i < arms.Count; i++)
        {
            selected[i] = (only.Count == 0 || MatchesAnyTerm(arms[i], only)) && !MatchesAnyTerm(arms[i], skip);
            any |= selected[i];
        }

        return any ? null : "the filter leaves no arm to run.";
    }

    /// <summary>The group names in first-appearance order.</summary>
    public static List<string> Groups(IReadOnlyList<ValidatorArmInfo> arms)
    {
        var groups = new List<string>();
        foreach (ValidatorArmInfo arm in arms)
        {
            if (!groups.Contains(arm.Group))
            {
                groups.Add(arm.Group);
            }
        }

        return groups;
    }

    /// <summary>True when <paramref name="term"/> names this arm, its group, or <c>group/Arm</c>.</summary>
    public static bool Matches(ValidatorArmInfo arm, string term) =>
        Same(term, arm.Name) || Same(term, arm.Group) || Same(term, $"{arm.Group}/{arm.Name}");

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static bool MatchesAnyTerm(ValidatorArmInfo arm, IReadOnlyList<string> terms)
    {
        foreach (string term in terms)
        {
            if (Matches(arm, term))
            {
                return true;
            }
        }

        return false;
    }

    private static bool MatchesAny(IReadOnlyList<ValidatorArmInfo> arms, string term)
    {
        foreach (ValidatorArmInfo arm in arms)
        {
            if (Matches(arm, term))
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<string> Concat(IReadOnlyList<string> a, IReadOnlyList<string> b)
    {
        foreach (string term in a)
        {
            yield return term;
        }

        foreach (string term in b)
        {
            yield return term;
        }
    }
}
