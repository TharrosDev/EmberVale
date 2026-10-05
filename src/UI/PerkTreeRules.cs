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
}
