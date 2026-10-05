using System.Collections.Generic;

namespace Embervale.Progression;

/// <summary>
/// Whole-catalogue rules, Godot-free: how many points a branch is worth, and the fewest skill points a
/// player must spend to reach a perk. The validator and the catalogue tests both ask these, so "the
/// capstone is reachable within the supply" is one rule with one answer.
/// </summary>
public static class PerkCatalogue
{
    /// <summary>Skill points a character can earn: 49 from levels 2-50 plus the 5 milestone points
    /// (levels 10/20/30/40/50) the XP polish phase adds. Every catalogue rule is checked against this.</summary>
    public const int SkillPointSupply = 54;

    /// <summary>The fewest points a main branch is worth at full rank. A branch is meant to total about 40 of the
    /// <see cref="SkillPointSupply"/>, so a build chooses rather than takes everything.</summary>
    public const int BranchTotalMin = 34;

    /// <summary>The most points a main branch is worth at full rank.</summary>
    public const int BranchTotalMax = 46;

    /// <summary>The branch points a perk of <paramref name="tier"/> needs: 0, 2, 5, 9, then 14 for the capstone.</summary>
    public static int TierGate(int tier) => tier switch
    {
        <= 1 => 0,
        2 => 2,
        3 => 5,
        4 => 9,
        _ => 14,
    };

    /// <summary>Whether a branch is one of the six open trees. Ashbound perks are opened by corruption, so it is a short,
    /// capstone-less branch by design.</summary>
    public static bool IsMainBranch(PerkBranch branch) => branch is not (PerkBranch.None or PerkBranch.Ashbound);

    /// <summary>Points the branch costs with every perk at full rank.</summary>
    public static int BranchTotal(IEnumerable<PerkNode> nodes, PerkBranch branch)
    {
        int total = 0;
        foreach (PerkNode node in nodes)
        {
            if (node.Branch == branch)
            {
                total += node.MaxRank * node.Cost;
            }
        }

        return total;
    }

    /// <summary>
    /// The skill points one constructive plan spends to learn <paramref name="targetId"/> from nothing: take
    /// the first unmet prerequisite, and when its branch-points gate is short, buy the cheapest rank that is
    /// learnable. An upper bound on the true minimum, so a result within the supply proves the perk reachable.
    /// -1 when the perk cannot be reached at all (an unknown id, a prerequisite loop, a gate no spending meets).
    /// </summary>
    public static int PointsToReach(IReadOnlyDictionary<string, PerkNode> nodes, string targetId)
    {
        if (!nodes.ContainsKey(targetId))
        {
            return -1;
        }

        var ranks = new Dictionary<string, int>();
        var branchSpent = new Dictionary<PerkBranch, int>();
        int spent = 0;

        // Every step buys one rank, and no perk set holds more ranks than this.
        int guard = 0;
        foreach (PerkNode node in nodes.Values)
        {
            guard += node.MaxRank;
        }

        while (guard-- > 0)
        {
            if (ranks.GetValueOrDefault(targetId) > 0)
            {
                return spent;
            }

            string? step = FirstUnmet(nodes, ranks, targetId, new HashSet<string>());
            if (step == null)
            {
                return -1;
            }

            PerkNode next = nodes[step];
            if (branchSpent.GetValueOrDefault(next.Branch) < next.BranchPointsRequired)
            {
                step = Cheapest(nodes, ranks, branchSpent, next.Branch);
                if (step == null)
                {
                    return -1;
                }

                next = nodes[step];
            }

            ranks[step] = ranks.GetValueOrDefault(step) + 1;
            branchSpent[next.Branch] = branchSpent.GetValueOrDefault(next.Branch) + next.Cost;
            spent += next.Cost;
        }

        return -1;
    }

    /// <summary>The deepest perk, walking prerequisites from <paramref name="id"/>, whose own prerequisites all
    /// have a rank; null on a loop or an unknown prerequisite.</summary>
    private static string? FirstUnmet(
        IReadOnlyDictionary<string, PerkNode> nodes, Dictionary<string, int> ranks, string id, HashSet<string> path)
    {
        if (!path.Add(id) || !nodes.TryGetValue(id, out PerkNode node))
        {
            return null;
        }

        foreach (string prerequisite in node.Prerequisites)
        {
            if (!nodes.ContainsKey(prerequisite))
            {
                return null;
            }

            if (ranks.GetValueOrDefault(prerequisite) <= 0)
            {
                return FirstUnmet(nodes, ranks, prerequisite, path);
            }
        }

        return id;
    }

    /// <summary>The cheapest rank in <paramref name="branch"/> that can be bought right now (prerequisites and
    /// gate met, not maxed), ties broken by tier then id so the plan is deterministic; null when none.</summary>
    private static string? Cheapest(
        IReadOnlyDictionary<string, PerkNode> nodes, Dictionary<string, int> ranks,
        Dictionary<PerkBranch, int> branchSpent, PerkBranch branch)
    {
        PerkNode? best = null;
        foreach (PerkNode node in nodes.Values)
        {
            if (node.Branch != branch
                || ranks.GetValueOrDefault(node.Id) >= node.MaxRank
                || branchSpent.GetValueOrDefault(branch) < node.BranchPointsRequired
                || !AllHeld(node.Prerequisites, ranks))
            {
                continue;
            }

            if (best == null || (node.Cost, node.Tier, node.Id).CompareTo((best.Value.Cost, best.Value.Tier, best.Value.Id)) < 0)
            {
                best = node;
            }
        }

        return best?.Id;
    }

    private static bool AllHeld(IReadOnlyList<string> prerequisites, Dictionary<string, int> ranks)
    {
        foreach (string id in prerequisites)
        {
            if (ranks.GetValueOrDefault(id) <= 0)
            {
                return false;
            }
        }

        return true;
    }
}
