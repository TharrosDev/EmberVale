using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Embervale.Narrative;

/// <summary>
/// One data-authored story rule (<c>data/story/rules/*.json</c>): when every flag in <see cref="All"/>
/// is held and none in <see cref="None"/> is, <see cref="Set"/> is held and <see cref="Beat"/> (if any)
/// is announced once. A rule with <see cref="On"/> set fires on that trigger instead of on flag state.
/// </summary>
public sealed record StoryRule(
    string Id, IReadOnlyList<string> All, IReadOnlyList<string> None, IReadOnlyList<string> Set,
    string Beat, string On)
{
    /// <summary>The only trigger a rule may name today.</summary>
    public const string OpeningFinished = "opening_finished";

    public bool IsTriggered => On.Length > 0;
}

/// <summary>
/// Pure rule engine behind <c>StoryRuleDirector</c>. Rules are DERIVATIONS: while a rule's conditions
/// hold, its <c>set</c> flags are held (it never clears anything). Evaluation runs to a fixed point, so
/// a rule reading a flag another rule sets resolves in one call, and is capped as a backstop against an
/// authoring cycle. A rule's beat fires at most once per engine instance (a session), never in
/// <c>silent</c> mode (the load catch-up), which still marks the rule as having fired.
/// </summary>
public sealed class StoryRuleEngine
{
    /// <summary>Backstop on fixed-point passes; a rule set that has not settled by then is a cycle.</summary>
    public const int MaxPasses = 32;

    private readonly List<StoryRule> _rules;
    private readonly HashSet<string> _fired = new();

    public StoryRuleEngine(IEnumerable<StoryRule> rules) => _rules = new List<StoryRule>(rules);

    public IReadOnlyList<StoryRule> Rules => _rules;

    /// <summary>Ids of the rules that have fired this session.</summary>
    public IReadOnlyCollection<string> Fired => _fired;

    /// <summary>
    /// Applies every flag-state rule whose conditions hold. <paramref name="set"/> must make
    /// <paramref name="has"/> true for the flag (and may raise events that re-enter the caller, which
    /// should guard against it). Returns the number of flags newly set.
    /// </summary>
    public int Evaluate(Func<string, bool> has, Action<string> set, Action<string>? beat, bool silent)
    {
        int total = 0;
        for (int pass = 0; pass < MaxPasses; pass++)
        {
            int changed = 0;
            foreach (StoryRule rule in _rules)
            {
                if (!rule.IsTriggered && Holds(rule, has))
                {
                    changed += Apply(rule, has, set, beat, silent);
                }
            }

            total += changed;
            if (changed == 0)
            {
                break;
            }
        }

        return total;
    }

    /// <summary>Fires the rules bound to <paramref name="trigger"/> (their guards must hold), then
    /// settles flag-state rules that the new flags satisfy.</summary>
    public int Trigger(string trigger, Func<string, bool> has, Action<string> set, Action<string>? beat)
    {
        int total = 0;
        foreach (StoryRule rule in _rules)
        {
            if (rule.On == trigger && Holds(rule, has))
            {
                total += Apply(rule, has, set, beat, silent: false);
            }
        }

        return total + Evaluate(has, set, beat, silent: false);
    }

    private static bool Holds(StoryRule rule, Func<string, bool> has)
    {
        foreach (string flag in rule.All)
        {
            if (!has(flag))
            {
                return false;
            }
        }

        foreach (string flag in rule.None)
        {
            if (has(flag))
            {
                return false;
            }
        }

        return true;
    }

    private int Apply(StoryRule rule, Func<string, bool> has, Action<string> set, Action<string>? beat, bool silent)
    {
        int changed = 0;
        foreach (string flag in rule.Set)
        {
            if (!has(flag))
            {
                set(flag);
                changed++;
            }
        }

        // Once per session: a rule's beat is an announcement, not a state.
        if (_fired.Add(rule.Id) && !silent && rule.Beat.Length > 0)
        {
            beat?.Invoke(rule.Beat);
        }

        return changed;
    }
}

/// <summary>Parses rule files (<c>{"rules":[...]}</c>) and holds the code-owned built-in rules.</summary>
public static class StoryRuleData
{
    public const string RulesDirectory = "res://data/story/rules";

    /// <summary>The five Act III testimony flags; all held sets <see cref="TestimoniesAllFlag"/>.</summary>
    public static readonly string[] TestimonyFlags =
    {
        "flag.testimony.iron", "flag.testimony.storm", "flag.testimony.beast", "flag.testimony.prophet",
        "flag.testimony.queen",
    };

    public const string TestimoniesAllFlag = "flag.testimonies_all";

    /// <summary>Prefix of the derived party-mirror flags (<c>flag.party.companion.kael</c>).</summary>
    public const string PartyFlagPrefix = "flag.party.";

    public static string PartyFlag(string companionId) => PartyFlagPrefix + companionId;

    /// <summary>The rules written in code rather than data.</summary>
    public static IReadOnlyList<StoryRule> BuiltIn() => new[]
    {
        new StoryRule(
            "rule.builtin.testimonies_all", TestimonyFlags, Array.Empty<string>(),
            new[] { TestimoniesAllFlag }, string.Empty, string.Empty),
    };

    /// <summary>Every flag the code writes by itself (built-in rules and the party mirror), for the
    /// validator's writer seed. <paramref name="companionIds"/> are all authored companions.</summary>
    public static IEnumerable<string> CodeWrittenFlags(IEnumerable<string> companionIds)
    {
        foreach (StoryRule rule in BuiltIn())
        {
            foreach (string flag in rule.Set)
            {
                yield return flag;
            }
        }

        foreach (string id in companionIds)
        {
            yield return PartyFlag(id);
        }
    }

    /// <summary>
    /// Parses one rule file. Problems are appended to <paramref name="errors"/> (prefixed with
    /// <paramref name="source"/>) and the offending rule is skipped; a malformed document yields none.
    /// </summary>
    public static List<StoryRule> Parse(string json, string source, List<string> errors)
    {
        var rules = new List<StoryRule>();
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });
        }
        catch (JsonException e)
        {
            errors.Add($"{source}: not valid JSON ({e.Message})");
            return rules;
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("rules", out JsonElement list) ||
                list.ValueKind != JsonValueKind.Array)
            {
                errors.Add($"{source}: expected an object with a \"rules\" array");
                return rules;
            }

            var ids = new HashSet<string>(StringComparer.Ordinal);
            int index = 0;
            foreach (JsonElement element in list.EnumerateArray())
            {
                string where = $"{source} rules[{index++}]";
                if (element.ValueKind != JsonValueKind.Object)
                {
                    errors.Add($"{where}: not an object");
                    continue;
                }

                string id = Str(element, "id");
                if (!id.StartsWith("rule.", StringComparison.Ordinal) || id.Length <= 5)
                {
                    errors.Add($"{where}: id '{id}' must start with 'rule.'");
                    continue;
                }

                where = $"{source} rule '{id}'";
                if (!ids.Add(id))
                {
                    errors.Add($"{where}: duplicate id within the file");
                    continue;
                }

                string on = Str(element, "on");
                if (on.Length > 0 && on != StoryRule.OpeningFinished)
                {
                    errors.Add($"{where}: unknown trigger '{on}' (only '{StoryRule.OpeningFinished}')");
                    continue;
                }

                List<string> all = Flags(element, "all", where, errors, out bool okAll);
                List<string> none = Flags(element, "none", where, errors, out bool okNone);
                List<string> set = Flags(element, "set", where, errors, out bool okSet);
                string beat = Str(element, "beat");
                if (!okAll || !okNone || !okSet)
                {
                    continue;
                }

                if (on.Length == 0 && all.Count == 0)
                {
                    errors.Add($"{where}: needs a non-empty \"all\" (or an \"on\" trigger); a rule with no condition would fire at once");
                    continue;
                }

                if (set.Count == 0 && beat.Length == 0)
                {
                    errors.Add($"{where}: sets no flag and names no beat, so it does nothing");
                    continue;
                }

                if (beat.Length > 0 && !IsBeatKey(beat))
                {
                    errors.Add($"{where}: beat '{beat}' must be lowercase letters, digits, '_' or '.'");
                    continue;
                }

                rules.Add(new StoryRule(id, all, none, set, beat, on));
            }
        }

        return rules;
    }

    private static bool IsBeatKey(string key)
    {
        foreach (char c in key)
        {
            if (!(c is >= 'a' and <= 'z' or >= '0' and <= '9' or '_' or '.'))
            {
                return false;
            }
        }

        return true;
    }

    private static string Str(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() ?? string.Empty
            : string.Empty;

    private static List<string> Flags(JsonElement element, string name, string where, List<string> errors, out bool ok)
    {
        ok = true;
        var flags = new List<string>();
        if (!element.TryGetProperty(name, out JsonElement list))
        {
            return flags;
        }

        if (list.ValueKind != JsonValueKind.Array)
        {
            errors.Add($"{where}: \"{name}\" must be an array of flag ids");
            ok = false;
            return flags;
        }

        foreach (JsonElement item in list.EnumerateArray())
        {
            string flag = item.ValueKind == JsonValueKind.String ? item.GetString() ?? string.Empty : string.Empty;
            if (!flag.StartsWith("flag.", StringComparison.Ordinal) || flag.Length <= 5)
            {
                errors.Add($"{where}: \"{name}\" entry '{flag}' must be a flag id starting with 'flag.'");
                ok = false;
                continue;
            }

            flags.Add(flag);
        }

        return flags;
    }
}

/// <summary>Pure derivation of the party-mirror flags (<c>flag.party.&lt;companion id&gt;</c>).</summary>
public static class PartyFlagMirror
{
    /// <summary>
    /// Which mirror flags to set and which to clear so the flags equal "this companion is in the party".
    /// Only flags that differ are returned, so applying the plan twice changes nothing.
    /// </summary>
    public static (List<string> Set, List<string> Clear) Plan(
        IEnumerable<string> allCompanionIds, ICollection<string> partyIds, Func<string, bool> has)
    {
        var set = new List<string>();
        var clear = new List<string>();
        foreach (string id in allCompanionIds)
        {
            string flag = StoryRuleData.PartyFlag(id);
            bool inParty = partyIds.Contains(id);
            if (inParty && !has(flag))
            {
                set.Add(flag);
            }
            else if (!inParty && has(flag))
            {
                clear.Add(flag);
            }
        }

        return (set, clear);
    }
}
