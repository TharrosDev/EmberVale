using System;
using Embervale.Companions;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// Covers the party load reconcile (Phase 32D). Loading a save is not "spawn what the save says" —
/// the world may already hold companions — so the planner decides who is dismissed, who is built,
/// and who merely moves. Rebuilding a companion who is already standing there would drop their live
/// state and re-fire their recruit announcement, which is exactly the bug this rule prevents.
/// </summary>
public class CompanionPartyReconcileTests
{
    private static readonly string[] None = Array.Empty<string>();

    [Fact]
    public void EmptyWorldRecruitsTheWholeSavedParty()
    {
        CompanionReconcilePlan plan = CompanionPartyReconcile.Plan(None, new[] { "companion.kael", "companion.nyra" });

        Assert.Equal(new[] { "companion.kael", "companion.nyra" }, plan.Recruit);
        Assert.Empty(plan.Dismiss);
        Assert.Empty(plan.Keep);
    }

    [Fact]
    public void EmptySaveDismissesEveryoneInTheWorld()
    {
        CompanionReconcilePlan plan = CompanionPartyReconcile.Plan(new[] { "companion.kael" }, None);

        Assert.Equal(new[] { "companion.kael" }, plan.Dismiss);
        Assert.Empty(plan.Recruit);
        Assert.Empty(plan.Keep);
    }

    [Fact]
    public void SurvivorsAreKept_NotRebuilt()
    {
        // The crux: a companion on both sides must not appear in Recruit — rebuilding them would
        // discard the live actor (and its restored component state) for no reason.
        CompanionReconcilePlan plan = CompanionPartyReconcile.Plan(
            new[] { "companion.kael" }, new[] { "companion.kael" });

        Assert.Equal(new[] { "companion.kael" }, plan.Keep);
        Assert.Empty(plan.Recruit);
        Assert.Empty(plan.Dismiss);
    }

    [Fact]
    public void MixedPartySplitsThreeWays()
    {
        CompanionReconcilePlan plan = CompanionPartyReconcile.Plan(
            new[] { "companion.kael", "companion.vex" },
            new[] { "companion.kael", "companion.orik" });

        Assert.Equal(new[] { "companion.kael" }, plan.Keep);
        Assert.Equal(new[] { "companion.orik" }, plan.Recruit);
        Assert.Equal(new[] { "companion.vex" }, plan.Dismiss);
    }

    [Fact]
    public void PlanIsDeterministicRegardlessOfInputOrder()
    {
        CompanionReconcilePlan a = CompanionPartyReconcile.Plan(
            None, new[] { "companion.vex", "companion.kael", "companion.orik" });
        CompanionReconcilePlan b = CompanionPartyReconcile.Plan(
            None, new[] { "companion.orik", "companion.vex", "companion.kael" });

        Assert.Equal(a.Recruit, b.Recruit);
    }

    [Fact]
    public void DuplicateEntriesCollapse()
    {
        // A save written by an older build (or a hand-edit) must not spawn the same companion twice.
        CompanionReconcilePlan plan = CompanionPartyReconcile.Plan(
            None, new[] { "companion.kael", "companion.kael" });

        Assert.Single(plan.Recruit);
    }

    [Fact]
    public void NullInputsAreTreatedAsEmpty()
    {
        CompanionReconcilePlan plan = CompanionPartyReconcile.Plan(null!, null!);

        Assert.Empty(plan.Recruit);
        Assert.Empty(plan.Dismiss);
        Assert.Empty(plan.Keep);
    }
}
