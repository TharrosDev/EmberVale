using System.Collections.Generic;

namespace Embervale.Localization;

/// <summary>
/// Pure audit of the localization CSV for the content validator (Phase 25.5F). Two failure modes
/// slip past <see cref="LocCatalog.Parse"/>, which dedupes and tolerates gaps so the game still
/// boots: a <em>duplicate key</em> (the parser keeps the last, silently dropping a string) and a
/// key with no value in the default locale (it falls back to the raw key, so the player sees
/// <c>hud.compass.n</c> instead of "N"). Godot-free so it is unit-testable; <see cref="Loc"/>'s
/// engine load path is unchanged.
/// </summary>
public static class LocaleAudit
{
    /// <summary>Returns one issue string per problem found (empty = clean).</summary>
    public static List<string> Audit(string csv, string defaultLocale)
    {
        var issues = new List<string>();
        if (string.IsNullOrEmpty(csv))
        {
            return issues;
        }

        // Duplicate keys: LocCatalog.Parse dedupes last-wins, so a duplicate is invisible to it —
        // walk the raw lines ourselves. ponytail: keys are simple dotted ids (no commas/quotes), so
        // a split on the first ',' is enough; revisit if a key ever needs quoting.
        var seen = new HashSet<string>();
        foreach (string rawLine in csv.Replace("\r", string.Empty).Split('\n'))
        {
            string line = rawLine.TrimStart();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            int comma = rawLine.IndexOf(',');
            string key = (comma < 0 ? rawLine : rawLine[..comma]).Trim();
            if (key.Length == 0 || key == "keys")
            {
                continue; // header's key column is the literal "keys"
            }

            if (!seen.Add(key))
            {
                issues.Add($"duplicate key '{key}'");
            }
        }

        // Missing default-locale value: every key present in any locale must have a defaultLocale entry.
        Dictionary<string, Dictionary<string, string>> byLocale = LocCatalog.Parse(csv);
        if (!byLocale.TryGetValue(defaultLocale, out Dictionary<string, string>? defaultMap))
        {
            issues.Add($"catalog has no '{defaultLocale}' (default locale) column");
            return issues;
        }

        var allKeys = new SortedSet<string>();
        foreach (Dictionary<string, string> map in byLocale.Values)
        {
            foreach (string key in map.Keys)
            {
                allKeys.Add(key);
            }
        }

        foreach (string key in allKeys)
        {
            if (!defaultMap.ContainsKey(key))
            {
                issues.Add($"key '{key}' has no '{defaultLocale}' value (would show the raw key)");
            }
        }

        return issues;
    }
}
