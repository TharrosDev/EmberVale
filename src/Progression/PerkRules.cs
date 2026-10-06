using System.Collections.Generic;

namespace Embervale.Progression;

/// <summary>
/// The rules for learning a perk, Godot-free. <see cref="PerksComponent"/> gathers the facts and asks
/// <see cref="WhyNot"/>; <see cref="FindCycle"/> is the validator's prerequisite-graph check.
/// </summary>
public static class PerkRules
{
    public const int MaxTier = 5;

    /// <summary>Columns are 0-based, so a branch is at most this many plus one wide.</summary>
    public const int MaxColumn = 4;

    /// <summary>The first reason a perk cannot be learned, or <see cref="PerkBlock.None"/>.</summary>
    public static PerkBlock WhyNot(
        int rank, int maxRank, bool corruptionMet, bool prerequisitesMet,
        int branchPoints, int branchPointsRequired, int skillPoints, int cost)
    {
        if (rank >= maxRank)
        {
            return PerkBlock.Maxed;
        }

        if (!corruptionMet)
        {
            return PerkBlock.Corruption;
        }

        if (!prerequisitesMet)
        {
            return PerkBlock.Prerequisite;
        }

        if (branchPoints < branchPointsRequired)
        {
            return PerkBlock.BranchPoints;
        }

        return skillPoints < cost ? PerkBlock.SkillPoints : PerkBlock.None;
    }

    /// <summary>An id whose prerequisites lead into a cycle, or null when the graph is acyclic. Unknown
    /// prerequisite ids are ignored here; the validator reports those separately.</summary>
    public static string? FindCycle(IReadOnlyDictionary<string, IReadOnlyList<string>> prerequisites)
    {
        var state = new Dictionary<string, int>(); // 1 = on the current path, 2 = proven clear
        foreach (string id in prerequisites.Keys)
        {
            if (Visit(id, prerequisites, state))
            {
                return id;
            }
        }

        return null;
    }

    private static bool Visit(
        string id, IReadOnlyDictionary<string, IReadOnlyList<string>> graph, Dictionary<string, int> state)
    {
        if (state.TryGetValue(id, out int seen))
        {
            return seen == 1;
        }

        state[id] = 1;
        if (graph.TryGetValue(id, out IReadOnlyList<string>? next))
        {
            foreach (string prerequisite in next)
            {
                if (Visit(prerequisite, graph, state))
                {
                    return true;
                }
            }
        }

        state[id] = 2;
        return false;
    }
}
