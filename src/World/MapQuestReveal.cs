using System.Collections.Generic;
using Embervale.Quests;

namespace Embervale.World;

/// <summary>One objective's map facts: where it points and whether it is live (in the player's branch,
/// unlocked and not yet met).</summary>
public readonly record struct QuestObjectiveSite(string LocationId, bool Optional, bool Live);

/// <summary>
/// What a quest is allowed to put on the map, and when. Reveal is spoiler-safe: a quest reveals only the
/// places its currently live objectives name, and each later place the moment its objective opens, so the
/// map never discloses the end of a quest at its start and never both branches of a fork.
/// </summary>
public static class MapQuestReveal
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

    /// <summary>The objectives of a live quest as sites. The place follows
    /// <see cref="ObjectiveNavigation.LocationId"/> (Reach and Defend name it in TargetId).</summary>
    public static List<QuestObjectiveSite> SitesOf(QuestProgress progress)
    {
        List<ObjectiveResource> objectives = progress.Quest.ObjectiveList();
        var sites = new List<QuestObjectiveSite>(objectives.Count);
        for (int i = 0; i < objectives.Count; i++)
        {
            ObjectiveResource objective = objectives[i];
            bool live = progress.IsObjectiveInBranch(i) && progress.IsObjectiveActive(i) && !progress.IsObjectiveComplete(i);
            sites.Add(new QuestObjectiveSite(
                ObjectiveNavigation.LocationId(objective.Type, objective.TargetId, objective.LocationId),
                objective.IsOptional,
                live));
        }

        return sites;
    }
}
