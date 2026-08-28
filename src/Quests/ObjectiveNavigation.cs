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

    /// <summary>The first active, incomplete objective destination for a tracked quest.</summary>
    public static string? ActiveLocationId(QuestProgress? progress)
    {
        if (progress == null)
        {
            return null;
        }

        var objectives = progress.Quest.ObjectiveList();
        for (int i = 0; i < objectives.Count; i++)
        {
            if (progress.IsObjectiveComplete(i) || !progress.IsObjectiveActive(i))
            {
                continue;
            }

            ObjectiveResource objective = objectives[i];
            string id = LocationId(objective.Type, objective.TargetId, objective.LocationId);
            return id.Length > 0 ? id : null;
        }

        return null;
    }
}
