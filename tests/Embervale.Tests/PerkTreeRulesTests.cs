using System;
using System.Collections.Generic;
using System.Linq;
using Embervale.Progression;
using Embervale.UI;
using Xunit;

namespace Embervale.Tests;

public class PerkTreeRulesTests
{
    [Theory]
    [InlineData(PerkBlock.None, PerkNodeState.Learnable)]
    [InlineData(PerkBlock.Maxed, PerkNodeState.Maxed)]
    [InlineData(PerkBlock.Corruption, PerkNodeState.CorruptionGated)]
    [InlineData(PerkBlock.Prerequisite, PerkNodeState.Locked)]
    [InlineData(PerkBlock.BranchPoints, PerkNodeState.Locked)]
    [InlineData(PerkBlock.SkillPoints, PerkNodeState.NeedsPoints)]
    public void StateOf_NamesEachBlock(PerkBlock block, PerkNodeState expected)
    {
        Assert.Equal(expected, PerkTreeRules.StateOf(block));
    }

    [Fact]
    public void StateOf_CoversEveryBlock()
    {
        // A new PerkBlock must be a decision here, not a silent "Locked".
        foreach (PerkBlock block in Enum.GetValues<PerkBlock>())
        {
            Assert.True(Enum.IsDefined(PerkTreeRules.StateOf(block)), block.ToString());
        }
    }

    [Fact]
    public void BranchesWithPerks_KeepEnumOrder_AndOnlyListWhatHasPerks()
    {
        List<PerkBranch> listed = PerkTreeRules.BranchesWithPerks(
            new[] { PerkBranch.Mage, PerkBranch.Warrior, PerkBranch.Mage, PerkBranch.Ashbound });

        Assert.Equal(new[] { PerkBranch.Warrior, PerkBranch.Mage, PerkBranch.Ashbound }, listed);
        Assert.Empty(PerkTreeRules.BranchesWithPerks(Array.Empty<PerkBranch>()));
    }

    [Fact]
    public void BranchesWithPerks_ListsNoneOnlyWhenAPerkSitsOutsideTheTrees()
    {
        Assert.DoesNotContain(PerkBranch.None, PerkTreeRules.BranchesWithPerks(new[] { PerkBranch.Rogue }));
        Assert.Equal(PerkBranch.None, PerkTreeRules.BranchesWithPerks(new[] { PerkBranch.None, PerkBranch.Rogue })[0]);
    }

    [Fact]
    public void BranchKeys_AreOnePerBranch()
    {
        string[] keys = Enum.GetValues<PerkBranch>().Select(PerkTreeRules.BranchKey).ToArray();
        Assert.Equal(keys.Length, keys.Distinct().Count());
        Assert.Equal("perktree.branch.warrior", PerkTreeRules.BranchKey(PerkBranch.Warrior));
    }

    [Fact]
    public void PickBranch_PrefersTheRequestedBranchWhileItIsListed()
    {
        var branches = new List<PerkBranch> { PerkBranch.Warrior, PerkBranch.Archer, PerkBranch.Mage };
        Assert.Equal(PerkBranch.Archer, PerkTreeRules.PickBranch(branches, PerkBranch.Archer, _ => 9));
        Assert.Equal(PerkBranch.Mage, PerkTreeRules.PickBranch(branches, PerkBranch.Rogue, b => b == PerkBranch.Mage ? 4 : 0));
    }

    [Fact]
    public void PickBranch_OpensOnTheMostInvestedBranch_ElseTheFirst()
    {
        var branches = new List<PerkBranch> { PerkBranch.Warrior, PerkBranch.Archer, PerkBranch.Mage };
        Assert.Equal(PerkBranch.Mage, PerkTreeRules.PickBranch(branches, null, b => b == PerkBranch.Mage ? 6 : 2));
        Assert.Equal(PerkBranch.Warrior, PerkTreeRules.PickBranch(branches, null, _ => 0));
        Assert.Null(PerkTreeRules.PickBranch(new List<PerkBranch>(), null, _ => 0));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 2)]
    [InlineData(3, 4)]
    [InlineData(40, 5)]
    [InlineData(-3, 1)]
    public void ColumnCount_IsWidestColumnPlusOne_InsideTheAuthoringLimit(int widest, int expected)
    {
        Assert.Equal(expected, PerkTreeRules.ColumnCount(widest));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(2, 2)]
    [InlineData(5, 5)]
    [InlineData(9, 5)]
    public void RowCount_IsHighestTier_InsideTheAuthoringLimit(int highest, int expected)
    {
        Assert.Equal(expected, PerkTreeRules.RowCount(highest));
    }

    [Fact]
    public void FirstToShow_TakesTheFirstLearnable_ElseTheFirstListed()
    {
        var perks = new List<string> { "a", "b", "c" };
        Assert.Equal("b", PerkTreeRules.FirstToShow(perks, p => p != "a"));
        Assert.Equal("a", PerkTreeRules.FirstToShow(perks, _ => false));
        Assert.Null(PerkTreeRules.FirstToShow(new List<string>(), _ => true));
    }

    // Warrior-shaped: (tier, column)
    private static readonly (int Tier, int Column)[] Grid =
    {
        (1, 0), (1, 1), (1, 2), (1, 3), // 0..3
        (2, 0), (2, 1), (2, 2),         // 4..6
        (3, 1), (3, 2),                 // 7..8
        (5, 1),                         // 9: tier 4 is empty
    };

    [Theory]
    [InlineData(0, PerkTreeDirection.Right, 1)]
    [InlineData(3, PerkTreeDirection.Left, 2)]
    [InlineData(0, PerkTreeDirection.Left, -1)]
    [InlineData(3, PerkTreeDirection.Right, -1)]
    [InlineData(7, PerkTreeDirection.Left, -1)]
    [InlineData(8, PerkTreeDirection.Left, 7)]
    public void FocusTarget_SidewaysStaysInTheTierAndTakesTheNearestColumn(int from, PerkTreeDirection direction, int expected)
    {
        Assert.Equal(expected, PerkTreeRules.FocusTarget(Grid, from, direction));
    }

    [Theory]
    [InlineData(1, PerkTreeDirection.Down, 5)]   // same column
    [InlineData(3, PerkTreeDirection.Down, 6)]   // column 3 has no tier-2 perk: the closest, column 2
    [InlineData(0, PerkTreeDirection.Down, 4)]
    [InlineData(4, PerkTreeDirection.Down, 7)]   // tier 3 has no column 0: the closest, column 1
    [InlineData(5, PerkTreeDirection.Down, 7)]
    [InlineData(8, PerkTreeDirection.Down, 9)]   // skips the empty tier 4
    [InlineData(9, PerkTreeDirection.Down, -1)]
    [InlineData(9, PerkTreeDirection.Up, 7)]     // nearest tier above, same column
    [InlineData(4, PerkTreeDirection.Up, 0)]
    [InlineData(0, PerkTreeDirection.Up, -1)]
    public void FocusTarget_VerticalTakesTheNextPopulatedTierAndTheClosestColumn(int from, PerkTreeDirection direction, int expected)
    {
        Assert.Equal(expected, PerkTreeRules.FocusTarget(Grid, from, direction));
    }

    [Fact]
    public void FocusTarget_BreaksAColumnTieTowardTheLowerColumn()
    {
        var cells = new[] { (1, 1), (2, 0), (2, 2) };
        Assert.Equal(1, PerkTreeRules.FocusTarget(cells, 0, PerkTreeDirection.Down));
        Assert.Equal(2, PerkTreeRules.FocusTarget(new[] { (2, 0), (1, 2), (1, 0) }, 0, PerkTreeDirection.Up));
    }
}
