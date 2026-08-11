using System;
using System.Collections.Generic;

namespace Embervale.UI;

/// <summary>One searchable thing on the map. <paramref name="Name"/> is what the player sees;
/// <paramref name="Terms"/> is everything else that should match it — its category, its district,
/// the trade it plies, the name of whoever keeps it. Both are matched case-insensitively.</summary>
public readonly record struct MapSearchEntry(string Id, string Name, string Terms);

/// <summary>A hit, with the score that ordered it. Exposed so the tests can pin the ordering.</summary>
public readonly record struct MapSearchHit(string Id, string Name, int Score);

/// <summary>
/// Ranked search over discovered map locations (Phase 39.5A) — the brief's §17.
///
/// Engine-free so it is unit-testable. The interesting part is not the matching but the
/// <em>ordering</em>: typing "iron" must put The Iron Anvil above a merchant who merely sells
/// ironmongery, or the feature is a list rather than a search. So a name match always outranks a
/// term match, and within names an earlier, tighter match outranks a looser one.
///
/// ⚠️ <b>Callers pass discovered locations only.</b> Search is not a way to read the fog: a hit on
/// somewhere you have never been would tell you it exists, where it is, and that you should go
/// there — which is the whole of exploration handed over by a text box.
/// </summary>
public static class MapSearch
{
    private const int ExactName = 100;
    private const int NamePrefix = 80;
    private const int WordPrefix = 60;
    private const int NameContains = 40;
    private const int TermMatch = 20;

    /// <summary>
    /// Every entry matching <paramref name="query"/>, best first. An empty or whitespace query
    /// returns nothing — an empty search box shows the map, not a list of everything.
    /// </summary>
    public static IReadOnlyList<MapSearchHit> Rank(string query, IEnumerable<MapSearchEntry> entries)
    {
        var hits = new List<MapSearchHit>();
        if (entries == null || string.IsNullOrWhiteSpace(query))
        {
            return hits;
        }

        string q = query.Trim().ToLowerInvariant();

        foreach (MapSearchEntry entry in entries)
        {
            int score = Score(q, entry);
            if (score > 0)
            {
                hits.Add(new MapSearchHit(entry.Id, entry.Name, score));
            }
        }

        // Score descending, then name, then id — a total order, so the list cannot reshuffle between
        // two identical searches. Ties resolved by anything unstable make a result list that moves
        // under the cursor as the player types.
        hits.Sort(static (a, b) =>
        {
            int byScore = b.Score.CompareTo(a.Score);
            if (byScore != 0)
            {
                return byScore;
            }

            int byName = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            return byName != 0 ? byName : string.CompareOrdinal(a.Id, b.Id);
        });

        return hits;
    }

    private static int Score(string q, MapSearchEntry entry)
    {
        string name = (entry.Name ?? string.Empty).ToLowerInvariant();

        if (name.Length > 0)
        {
            if (name.Equals(q, StringComparison.Ordinal))
            {
                return ExactName;
            }

            if (name.StartsWith(q, StringComparison.Ordinal))
            {
                return NamePrefix;
            }

            if (HasWordStartingWith(name, q))
            {
                return WordPrefix;
            }

            if (name.Contains(q, StringComparison.Ordinal))
            {
                return NameContains;
            }
        }

        string terms = (entry.Terms ?? string.Empty).ToLowerInvariant();
        return terms.Length > 0 && terms.Contains(q, StringComparison.Ordinal) ? TermMatch : 0;
    }

    /// <summary>True when any whitespace-separated word of <paramref name="text"/> starts with
    /// <paramref name="prefix"/> — so "anvil" finds "The Iron Anvil" without "nvi" doing so.</summary>
    private static bool HasWordStartingWith(string text, string prefix)
    {
        int i = 0;
        while (i < text.Length)
        {
            while (i < text.Length && char.IsWhiteSpace(text[i]))
            {
                i++;
            }

            if (i < text.Length && string.CompareOrdinal(text, i, prefix, 0, prefix.Length) == 0)
            {
                return true;
            }

            while (i < text.Length && !char.IsWhiteSpace(text[i]))
            {
                i++;
            }
        }

        return false;
    }
}
