using System;
using System.Collections.Generic;
using Embervale.Progression;

namespace Embervale.UI;

/// <summary>How the tree draws a perk node. One state per node, derived from <see cref="PerkBlock"/>:
/// the structural reasons (<see cref="Locked"/>, <see cref="CorruptionGated"/>) read differently from
/// <see cref="NeedsPoints"/>, which is only a matter of saving up.</summary>
public enum PerkNodeState
{
    Locked,
    Learnable,
    NeedsPoints,
    CorruptionGated,
    Maxed,
}

/// <summary>The four ways a node is drawn. Each has its own frame and its own mark, so the tree still
/// reads with the colour taken away: a cut-in well and a padlock, a plate with one lit edge and a hollow
/// diamond, a plate with a spine and a filled diamond, a plate with a spine and a ringed diamond.</summary>
public enum PerkNodeVisual
{
    Locked,
    Available,
    Owned,
    Maxed,
}

/// <summary>The perk tree's decisions that need no Godot: node state, which branches get a column,
/// the branch the tree opens on, and the grid size. <see cref="PerkTreePanel"/> draws what these say.</summary>
public static class PerkTreeRules
{
    /// <summary>Below this node width (logical px) the grid switches to its compact form.</summary>
    public const float CompactNodeWidth = 150f;

    public static PerkNodeState StateOf(PerkBlock block) => block switch
    {
        PerkBlock.None => PerkNodeState.Learnable,
        PerkBlock.Maxed => PerkNodeState.Maxed,
        PerkBlock.Corruption => PerkNodeState.CorruptionGated,
        PerkBlock.SkillPoints => PerkNodeState.NeedsPoints,
        _ => PerkNodeState.Locked,
    };

    /// <summary>How a node is drawn. A perk with a rank in it is owned whatever stops the next rank; one with
    /// none is available when only skill points stand in the way (the tree is open that far) and locked
    /// behind a prerequisite, a tier gate or corruption.</summary>
    public static PerkNodeVisual VisualOf(PerkNodeState state, int rank)
    {
        if (state == PerkNodeState.Maxed)
        {
            return PerkNodeVisual.Maxed;
        }

        if (rank > 0)
        {
            return PerkNodeVisual.Owned;
        }

        return state is PerkNodeState.Learnable or PerkNodeState.NeedsPoints ? PerkNodeVisual.Available : PerkNodeVisual.Locked;
    }

    /// <summary>Locale key of a branch's name. <see cref="PerkBranch.None"/> reads "Other".</summary>
    public static string BranchKey(PerkBranch branch) => "perktree.branch." + branch.ToString().ToLowerInvariant();

    /// <summary>The branches that have at least one perk, in enum order. <see cref="PerkBranch.None"/> only
    /// appears when a perk sits outside every tree, so such a perk is never silently unreachable.</summary>
    public static List<PerkBranch> BranchesWithPerks(IEnumerable<PerkBranch> perkBranches)
    {
        var present = new HashSet<PerkBranch>(perkBranches);
        var result = new List<PerkBranch>();
        foreach (PerkBranch branch in Enum.GetValues<PerkBranch>())
        {
            if (present.Contains(branch))
            {
                result.Add(branch);
            }
        }

        return result;
    }

    /// <summary>The branch to open on: the one holding the most spent points, the first listed when nothing is
    /// spent (or <paramref name="requested"/> when it is still a listed branch). Null when there are no branches.</summary>
    public static PerkBranch? PickBranch(List<PerkBranch> branches, PerkBranch? requested, Func<PerkBranch, int> pointsIn)
    {
        if (branches.Count == 0)
        {
            return null;
        }

        if (requested is { } wanted && branches.Contains(wanted))
        {
            return wanted;
        }

        PerkBranch best = branches[0];
        int bestPoints = pointsIn(best);
        foreach (PerkBranch branch in branches)
        {
            int points = pointsIn(branch);
            if (points > bestPoints)
            {
                best = branch;
                bestPoints = points;
            }
        }

        return best;
    }

    /// <summary>Grid columns a branch needs: the widest authored <see cref="PerkResource.Column"/> plus one,
    /// kept inside what <see cref="PerkRules.MaxColumn"/> allows.</summary>
    public static int ColumnCount(int widestColumn) => Math.Clamp(widestColumn + 1, 1, PerkRules.MaxColumn + 1);

    /// <summary>Grid rows a branch needs: its highest tier, kept inside <see cref="PerkRules.MaxTier"/>.</summary>
    public static int RowCount(int highestTier) => Math.Clamp(highestTier, 1, PerkRules.MaxTier);

    /// <summary>The perk the detail pane opens on when nothing has been focused yet: the first perk that can be
    /// learned right now, else the first listed (null on an empty list).</summary>
    public static T? FirstToShow<T>(IReadOnlyList<T> inTreeOrder, Func<T, bool> learnable) where T : class
    {
        foreach (T item in inTreeOrder)
        {
            if (learnable(item))
            {
                return item;
            }
        }

        return inTreeOrder.Count > 0 ? inTreeOrder[0] : null;
    }

    /// <summary>The cell a focus step lands on, as an index into <paramref name="cells"/>, or -1 when there is no cell that
    /// way. Left and right stay in the tier and take the nearest column; up and down take the next tier that has a
    /// perk and, within it, the column closest to the one left (the lower column on a tie). Spelled out rather than left to
    /// Godot's geometry search, which stepped from a perk to the branch strip instead of the perk beside it.</summary>
    public static int FocusTarget(IReadOnlyList<(int Tier, int Column)> cells, int from, PerkTreeDirection direction)
    {
        (int tier, int column) = cells[from];
        int best = -1;
        for (int i = 0; i < cells.Count; i++)
        {
            (int otherTier, int otherColumn) = cells[i];
            bool candidate = direction switch
            {
                PerkTreeDirection.Left => otherTier == tier && otherColumn < column,
                PerkTreeDirection.Right => otherTier == tier && otherColumn > column,
                PerkTreeDirection.Up => otherTier < tier,
                _ => otherTier > tier,
            };
            if (candidate && (best < 0 || Closer(cells[best], cells[i], tier, column, direction)))
            {
                best = i;
            }
        }

        return best;
    }

    private static bool Closer((int Tier, int Column) held, (int Tier, int Column) other, int tier, int column, PerkTreeDirection direction)
    {
        if (direction is PerkTreeDirection.Left or PerkTreeDirection.Right)
        {
            return Math.Abs(other.Column - column) < Math.Abs(held.Column - column);
        }

        int heldTierGap = Math.Abs(held.Tier - tier);
        int otherTierGap = Math.Abs(other.Tier - tier);
        if (otherTierGap != heldTierGap)
        {
            return otherTierGap < heldTierGap;
        }

        int heldColumnGap = Math.Abs(held.Column - column);
        int otherColumnGap = Math.Abs(other.Column - column);
        return otherColumnGap < heldColumnGap || (otherColumnGap == heldColumnGap && other.Column < held.Column);
    }

    /// <summary>
    /// <see cref="FocusTarget"/> with up and down following the tree's lines: down lands on the nearest perk
    /// this one opens, up on the nearest perk it requires (nearest tier first, then nearest column, the lower
    /// column on a tie). A perk with no line that way falls back to the plain grid step, so every row stays
    /// reachable. Left and right are the grid step. <paramref name="edges"/> are (prerequisite, dependent)
    /// index pairs into <paramref name="cells"/>.
    /// </summary>
    public static int FocusTargetAlongEdges(
        IReadOnlyList<(int Tier, int Column)> cells,
        IReadOnlyList<(int From, int To)> edges,
        int from,
        PerkTreeDirection direction)
    {
        if (direction is PerkTreeDirection.Left or PerkTreeDirection.Right)
        {
            return FocusTarget(cells, from, direction);
        }

        (int tier, int column) = cells[from];
        int best = -1;
        foreach ((int prerequisite, int dependent) in edges)
        {
            int other = direction == PerkTreeDirection.Down
                ? (prerequisite == from ? dependent : -1)
                : (dependent == from ? prerequisite : -1);
            if (other < 0 || other >= cells.Count || other == from)
            {
                continue;
            }

            // A line only counts when it runs the way the step does: a prerequisite authored on the same
            // row or below is not "up".
            bool rightWay = direction == PerkTreeDirection.Down ? cells[other].Tier > tier : cells[other].Tier < tier;
            if (rightWay && (best < 0 || Closer(cells[best], cells[other], tier, column, direction)))
            {
                best = other;
            }
        }

        return best >= 0 ? best : FocusTarget(cells, from, direction);
    }

    /// <summary>
    /// The lines to light for a focused perk: every prerequisite line above it, followed upward until it
    /// meets a perk that is already owned. The lines above an owned perk are lit by ownership already, so
    /// what this adds is the route still to buy. Each pair is (prerequisite, dependent).
    /// </summary>
    public static List<(string From, string To)> PathTo(
        string focused, Func<string, IEnumerable<string>> prerequisitesOf, Func<string, bool> owned)
    {
        var path = new List<(string, string)>();
        var seen = new HashSet<string> { focused };
        var open = new Queue<string>();
        open.Enqueue(focused);
        while (open.Count > 0)
        {
            string node = open.Dequeue();
            foreach (string prerequisite in prerequisitesOf(node))
            {
                path.Add((prerequisite, node));
                if (!owned(prerequisite) && seen.Add(prerequisite))
                {
                    open.Enqueue(prerequisite);
                }
            }
        }

        return path;
    }
}
