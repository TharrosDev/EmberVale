using System.Collections.Generic;

namespace Embervale.UI;

/// <summary>One objective's map facts: where it points and whether it is live.</summary>
public readonly record struct QuestObjectiveSite(string LocationId, bool Optional, bool Live);

/// <summary>A quest pin: the location, the quest it belongs to (main or side), and whether it is the tracked one.</summary>
public readonly record struct QuestPin(string LocationId, bool IsMain, bool Tracked);

/// <summary>One quest as the pin builder sees it.</summary>
public sealed record QuestPinSource(
    string QuestId, bool IsMain, bool IsLedger, bool Active, bool Tracked, IReadOnlyList<QuestObjectiveSite> Sites);

/// <summary>
/// The map's quest rules. Reveal is spoiler-safe: a quest reveals only the places its currently live
/// objectives name, and each later place the moment its objective opens, so the map never discloses the
/// end of a quest at its start and never both branches of a fork. Pins show the current objective of every
/// live non-ledger quest, with the ring reserved for the tracked one.
/// </summary>
public static class MapQuestPinRules
{
    /// <summary>The location ids to reveal now: the place of every live objective that has one.</summary>
    public static List<string> RevealNow(IReadOnlyList<QuestObjectiveSite> sites)
    {
        var ids = new List<string>();
        foreach (QuestObjectiveSite site in sites)
        {
            if (site.Live && site.LocationId.Length > 0 && !ids.Contains(site.LocationId))
            {
                ids.Add(site.LocationId);
            }
        }

        return ids;
    }

    /// <summary>The location a single objective reveals when it activates, or null (not live, or no place).</summary>
    public static string? RevealOnActivation(QuestObjectiveSite site) =>
        site.Live && site.LocationId.Length > 0 ? site.LocationId : null;

    /// <summary>
    /// Pins for the live, non-ledger quests: each quest's current objective's place (required first, then
    /// optional). A location shared by two quests is one pin, kept as the tracked/main one so the strongest
    /// styling survives. The result is ordered tracked first, then main, then side.
    /// </summary>
    public static List<QuestPin> Pins(IEnumerable<QuestPinSource> quests)
    {
        var byLocation = new Dictionary<string, QuestPin>();
        foreach (QuestPinSource quest in quests)
        {
            if (!quest.Active || quest.IsLedger)
            {
                continue;
            }

            string? location = CurrentLocation(quest.Sites);
            if (location == null)
            {
                continue;
            }

            var pin = new QuestPin(location, quest.IsMain, quest.Tracked);
            if (!byLocation.TryGetValue(location, out QuestPin existing) || Outranks(pin, existing))
            {
                byLocation[location] = pin;
            }
        }

        var pins = new List<QuestPin>(byLocation.Values);
        pins.Sort((a, b) =>
        {
            int byTracked = b.Tracked.CompareTo(a.Tracked);
            if (byTracked != 0)
            {
                return byTracked;
            }

            int byMain = b.IsMain.CompareTo(a.IsMain);
            return byMain != 0 ? byMain : string.CompareOrdinal(a.LocationId, b.LocationId);
        });
        return pins;
    }

    private static bool Outranks(QuestPin a, QuestPin b) =>
        a.Tracked != b.Tracked ? a.Tracked : a.IsMain && !b.IsMain;

    private static string? CurrentLocation(IReadOnlyList<QuestObjectiveSite> sites)
    {
        for (int pass = 0; pass < 2; pass++)
        {
            bool optionalPass = pass == 1;
            foreach (QuestObjectiveSite site in sites)
            {
                if (site.Optional == optionalPass && site.Live)
                {
                    // Mirrors ObjectiveNavigation.ActiveLocationId: the first outstanding objective decides, and
                    // a place-less one means "no authored place" rather than "look at the next".
                    return site.LocationId.Length > 0 ? site.LocationId : null;
                }
            }
        }

        return null;
    }
}
