namespace Embervale.Quests;

/// <summary>
/// Resolves the canonical map location associated with a quest objective.
///
/// Reach and Defend already use <c>TargetId</c> as their location authority; every other
/// navigable objective uses the optional <c>LocationId</c> fallback. Keeping that distinction here
/// prevents the full map, minimap and tracker from independently re-learning the quest schema.
/// </summary>
public static class ObjectiveNavigation
{
    /// <summary>The objective's canonical <c>location.*</c> id, or an empty string.</summary>
    public static string LocationId(ObjectiveType type, string? targetId, string? locationId) =>
        type is ObjectiveType.Reach or ObjectiveType.Defend
            ? targetId ?? string.Empty
            : locationId ?? string.Empty;

    /// <summary>
    /// The first active, incomplete objective destination for a tracked quest. Required objectives
    /// come first; an optional one is used only when no required objective is live. A Milestone has
    /// no world position of its own, so one with no authored <c>LocationId</c> is skipped (the next
    /// objective's destination is the useful answer) rather than ending the search with nothing.
    /// </summary>
    public static string? ActiveLocationId(QuestProgress? progress)
    {
        if (progress == null)
        {
            return null;
        }

        var objectives = progress.Quest.ObjectiveList();
        for (int pass = 0; pass < 2; pass++)
        {
            bool optionalPass = pass == 1;
            for (int i = 0; i < objectives.Count; i++)
            {
                ObjectiveResource objective = objectives[i];
                if (objective.IsOptional != optionalPass ||
                    progress.IsObjectiveComplete(i) || !progress.IsObjectiveActive(i))
                {
                    continue;
                }

                string id = LocationId(objective.Type, objective.TargetId, objective.LocationId);
                if (id.Length == 0 && objective.Type == ObjectiveType.Milestone)
                {
                    continue;
                }

                return id.Length > 0 ? id : null;
            }
        }

        return null;
    }
}
