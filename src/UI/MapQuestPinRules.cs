using System.Collections.Generic;
using Embervale.World;

namespace Embervale.UI;

/// <summary>A quest pin: the location, whether its quest is the main thread, and whether it is the tracked one.</summary>
public readonly record struct QuestPin(string LocationId, bool IsMain, bool Tracked);

/// <summary>One quest as the pin builder sees it.</summary>
public sealed record QuestPinSource(
    string QuestId, bool IsMain, bool IsLedger, bool Active, bool Tracked, IReadOnlyList<QuestObjectiveSite> Sites);

/// <summary>
/// The map's quest pins: the current objective's place for every live non-ledger quest, with the ring
/// reserved for the tracked one. What a quest may reveal, and when, is <see cref="MapQuestReveal"/>.
/// </summary>
public static class MapQuestPinRules
{
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
