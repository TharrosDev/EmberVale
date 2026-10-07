using System;
using System.Collections.Generic;
using System.Globalization;

namespace Embervale.Bootstrap;

/// <summary>
/// The pure half of <c>--story --story-mission=</c>: which missions were asked for, and which
/// mid-campaign save to start from.
///
/// <para>The value is one mission or a range, each end a 1-based index or a quest id:
/// <c>27</c>, <c>quest.main.godfall_choir</c>, <c>26..28</c>, <c>quest.main.pale_door..22</c>.</para>
/// </summary>
public static class StoryMissionRange
{
    /// <summary>
    /// Reads <paramref name="value"/> against the campaign's missions (index and quest id, in
    /// order). Returns null and the 1-based inclusive range, or why it cannot be read.
    /// </summary>
    public static string? Parse(
        string value, IReadOnlyList<(int Index, string QuestId)> missions, out int first, out int last)
    {
        first = 0;
        last = 0;
        string[] ends = value.Split("..", StringSplitOptions.TrimEntries);
        if (value.Trim().Length == 0 || ends.Length > 2)
        {
            return "--story-mission needs a mission or a range: an index (27), a quest id, or first..last.";
        }

        if (!TryResolve(ends[0], missions, out first))
        {
            return $"--story-mission: '{ends[0]}' is not a mission index (1..{missions.Count}) or a campaign quest id (--story --story-list).";
        }

        last = first;
        if (ends.Length == 2 && !TryResolve(ends[1], missions, out last))
        {
            return $"--story-mission: '{ends[1]}' is not a mission index (1..{missions.Count}) or a campaign quest id (--story --story-list).";
        }

        return last < first ? $"--story-mission: the range {first}..{last} runs backwards." : null;
    }

    /// <summary>
    /// The save to start from: the greatest of <paramref name="frontiers"/> (each "every mission
    /// through N is done") that still leaves mission <paramref name="first"/> to play. Zero, a
    /// plain New Game, when none qualifies.
    /// </summary>
    public static int Frontier(int first, IEnumerable<int> frontiers)
    {
        int best = 0;
        foreach (int frontier in frontiers)
        {
            if (frontier < first && frontier > best)
            {
                best = frontier;
            }
        }

        return best;
    }

    private static bool TryResolve(string end, IReadOnlyList<(int Index, string QuestId)> missions, out int index)
    {
        if (int.TryParse(end, NumberStyles.Integer, CultureInfo.InvariantCulture, out index))
        {
            foreach ((int each, string _) in missions)
            {
                if (each == index)
                {
                    return true;
                }
            }

            return false;
        }

        foreach ((int each, string questId) in missions)
        {
            if (questId == end)
            {
                index = each;
                return true;
            }
        }

        index = 0;
        return false;
    }
}
