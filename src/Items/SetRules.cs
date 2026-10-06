using System.Collections.Generic;

namespace Embervale.Items;

/// <summary>
/// The counting behind item sets, free of the engine so it is unit-testable: how many pieces of
/// each set are worn, and which bonus thresholds that switches on.
/// <see cref="EquipmentComponent"/> applies the result.
/// </summary>
public static class SetRules
{
    /// <summary>
    /// Pieces worn per set id. Each distinct item id counts once, so a set can never be completed by
    /// wearing the same piece twice. Items with no set are skipped.
    /// </summary>
    public static Dictionary<string, int> CountPieces(IEnumerable<(string ItemId, string SetId)> worn)
    {
        var seen = new HashSet<(string, string)>();
        var counts = new Dictionary<string, int>();
        foreach ((string itemId, string setId) in worn)
        {
            if (string.IsNullOrEmpty(setId) || !seen.Add((itemId, setId)))
            {
                continue;
            }

            counts[setId] = counts.TryGetValue(setId, out int n) ? n + 1 : 1;
        }

        return counts;
    }

    /// <summary>A bonus row is on once that many pieces are worn. A threshold below 1 is never on:
    /// it would otherwise grant its bonus to someone wearing nothing.</summary>
    public static bool IsActive(int piecesRequired, int piecesWorn) =>
        piecesRequired >= 1 && piecesWorn >= piecesRequired;

    /// <summary>The next threshold above what is worn, for a tooltip's "2 more for ..." line; 0 when
    /// every threshold is already met.</summary>
    public static int NextThreshold(IEnumerable<int> thresholds, int piecesWorn)
    {
        int next = 0;
        foreach (int threshold in thresholds)
        {
            if (threshold > piecesWorn && (next == 0 || threshold < next))
            {
                next = threshold;
            }
        }

        return next;
    }
}
