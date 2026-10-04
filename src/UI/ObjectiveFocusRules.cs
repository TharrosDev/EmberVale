using System.Collections.Generic;
using Embervale.Quests;

namespace Embervale.UI;

/// <summary>One objective reduced to the booleans every quest surface decides from. Godot-free so the
/// rules below are testable; <see cref="QuestProgressViews"/> builds them from a live
/// <see cref="QuestProgress"/>.</summary>
public readonly record struct ObjectiveState(int Index, bool Optional, bool Active, bool Complete, bool InBranch);

/// <summary>
/// Which objective a quest surface treats as "the current one". The HUD tracker's destination line,
/// the compass chevron, the map pins and the journal's "where" line all ask this one question, so
/// they cannot name different objectives for the same quest (invariant 2: one surface owns each fact).
///
/// Required objectives come first; an optional one is current only when no required one is live, so
/// a side objective can never pull the compass away from the thing that finishes the quest.
/// </summary>
public static class ObjectiveFocusRules
{
    /// <summary>Whether the objective is live: in the player's branch, unlocked and not yet met.</summary>
    public static bool IsLive(ObjectiveState state) => state.InBranch && state.Active && !state.Complete;

    /// <summary>The index of the current objective, or -1 when nothing is live (awaiting turn-in).</summary>
    public static int Current(IReadOnlyList<ObjectiveState> states)
    {
        for (int pass = 0; pass < 2; pass++)
        {
            bool optionalPass = pass == 1;
            foreach (ObjectiveState state in states)
            {
                if (state.Optional == optionalPass && IsLive(state))
                {
                    return state.Index;
                }
            }
        }

        return -1;
    }

    /// <summary>Every live required objective, in authored order (a quest may run several at once).</summary>
    public static List<int> LiveRequired(IReadOnlyList<ObjectiveState> states)
    {
        var live = new List<int>();
        foreach (ObjectiveState state in states)
        {
            if (!state.Optional && IsLive(state))
            {
                live.Add(state.Index);
            }
        }

        return live;
    }
}
