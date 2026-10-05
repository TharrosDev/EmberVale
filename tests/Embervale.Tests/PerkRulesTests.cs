using System;
using System.Collections.Generic;
using Embervale.Progression;
using Xunit;

namespace Embervale.Tests;

public class PerkRulesTests
{
    private static PerkBlock Check(
        int rank = 0, int maxRank = 3, bool corruption = true, bool prerequisites = true,
        int branchPoints = 10, int required = 0, int skillPoints = 5, int cost = 1)
    {
        return PerkRules.WhyNot(rank, maxRank, corruption, prerequisites, branchPoints, required, skillPoints, cost);
    }

    [Fact]
    public void LearnablePerk_HasNoBlock() => Assert.Equal(PerkBlock.None, Check());

    [Fact]
    public void EachRefusal_IsNamed()
    {
        Assert.Equal(PerkBlock.Maxed, Check(rank: 3));
        Assert.Equal(PerkBlock.Corruption, Check(corruption: false));
        Assert.Equal(PerkBlock.Prerequisite, Check(prerequisites: false));
        Assert.Equal(PerkBlock.BranchPoints, Check(branchPoints: 4, required: 5));
        Assert.Equal(PerkBlock.SkillPoints, Check(skillPoints: 0));
    }

    [Fact]
    public void BranchPointsAtTheThreshold_Pass() => Assert.Equal(PerkBlock.None, Check(branchPoints: 5, required: 5));

    [Fact]
    public void StructuralReasonsOutrankAffordability()
    {
        Assert.Equal(PerkBlock.Maxed, Check(rank: 3, corruption: false, skillPoints: 0));
        Assert.Equal(PerkBlock.Corruption, Check(corruption: false, prerequisites: false, skillPoints: 0));
        Assert.Equal(PerkBlock.Prerequisite, Check(prerequisites: false, branchPoints: 0, required: 5, skillPoints: 0));
        Assert.Equal(PerkBlock.BranchPoints, Check(branchPoints: 0, required: 5, skillPoints: 0));
    }

    [Fact]
    public void FreePerk_NeedsNoPoints() => Assert.Equal(PerkBlock.None, Check(skillPoints: 0, cost: 0));

    private static Dictionary<string, IReadOnlyList<string>> Graph(params (string Id, string[] Needs)[] nodes)
    {
        var graph = new Dictionary<string, IReadOnlyList<string>>();
        foreach ((string id, string[] needs) in nodes)
        {
            graph[id] = needs;
        }

        return graph;
    }

    [Fact]
    public void FindCycle_AcceptsATreeAndADiamond()
    {
        Assert.Null(PerkRules.FindCycle(Graph(
            ("a", Array.Empty<string>()),
            ("b", new[] { "a" }),
            ("c", new[] { "a" }),
            ("d", new[] { "b", "c" }))));
    }

    [Fact]
    public void FindCycle_FlagsSelfAndLongLoops()
    {
        Assert.NotNull(PerkRules.FindCycle(Graph(("a", new[] { "a" }))));
        Assert.NotNull(PerkRules.FindCycle(Graph(
            ("a", new[] { "c" }), ("b", new[] { "a" }), ("c", new[] { "b" }))));
    }

    [Fact]
    public void FindCycle_IgnoresUnknownPrerequisites()
    {
        Assert.Null(PerkRules.FindCycle(Graph(("a", new[] { "missing" }))));
    }
}
