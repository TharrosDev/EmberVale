using System.Collections.Generic;
using Embervale.Quests;

namespace Embervale.UI;

/// <summary>Builds the Godot-free <see cref="ObjectiveState"/> list for a live quest progress.</summary>
public static class QuestProgressViews
{
    public static List<ObjectiveState> States(QuestProgress progress)
    {
        List<ObjectiveResource> objectives = progress.Quest.ObjectiveList();
        var states = new List<ObjectiveState>(objectives.Count);
        for (int i = 0; i < objectives.Count; i++)
        {
            states.Add(new ObjectiveState(
                i,
                objectives[i].IsOptional,
                progress.IsObjectiveActive(i),
                progress.IsObjectiveComplete(i),
                progress.IsObjectiveInBranch(i)));
        }

        return states;
    }

    /// <summary>The objective every surface treats as current for this quest, or null.</summary>
    public static ObjectiveResource? CurrentObjective(QuestProgress progress, out int index)
    {
        index = ObjectiveFocusRules.Current(States(progress));
        return index >= 0 ? progress.Quest.ObjectiveList()[index] : null;
    }

    /// <summary>The map location id the quest's current objective points at, by the shared navigation rule
    /// (<see cref="ObjectiveNavigation.ActiveLocationId"/>: required before optional; Reach and Defend name a
    /// place in TargetId, every other type in LocationId). Null when it has no authored place.</summary>
    public static string? CurrentLocationId(QuestProgress progress) => ObjectiveNavigation.ActiveLocationId(progress);
}
