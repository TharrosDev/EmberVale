using Embervale.Companions;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// Covers the pure follower brain (Phase 32A). The steering/animation runs in-engine, but the
/// arbitration between "stay with the player" and "fight what's in front of me" is load-bearing —
/// get the leash wrong and a companion either abandons the player chasing a runner, or refuses to
/// fight anything at all — so the rule is pinned here.
/// </summary>
public class CompanionDecisionTests
{
    private const float Leash = 18f;
    private const float Slot = 1.4f;
    private const float Reach = 2.1f;

    [Fact]
    public void InFormation_NoTarget_Holds()
    {
        Assert.Equal(
            CompanionAction.Hold,
            CompanionDecision.Decide(0.5f, hasTarget: false, 0f, Leash, Slot, Reach));
    }

    [Fact]
    public void OutOfFormation_NoTarget_Regroups()
    {
        Assert.Equal(
            CompanionAction.Regroup,
            CompanionDecision.Decide(6f, hasTarget: false, 0f, Leash, Slot, Reach));
    }

    [Fact]
    public void TargetOutOfReach_Chases()
    {
        Assert.Equal(
            CompanionAction.Chase,
            CompanionDecision.Decide(4f, hasTarget: true, 9f, Leash, Slot, Reach));
    }

    [Fact]
    public void TargetInReach_Attacks()
    {
        Assert.Equal(
            CompanionAction.Attack,
            CompanionDecision.Decide(4f, hasTarget: true, 1.8f, Leash, Slot, Reach));
    }

    [Fact]
    public void ExactlyAtReach_Attacks()
    {
        // The boundary belongs to attacking: a companion that keeps closing at exactly its reach
        // walks into the target instead of swinging.
        Assert.Equal(
            CompanionAction.Attack,
            CompanionDecision.Decide(4f, hasTarget: true, Reach, Leash, Slot, Reach));
    }

    [Fact]
    public void PastLeash_BreaksOffMidFight()
    {
        // The crux: a hostile in reach does NOT hold the companion out past the leash — otherwise a
        // running enemy can drag the party across the map away from the player.
        Assert.Equal(
            CompanionAction.Regroup,
            CompanionDecision.Decide(Leash + 1f, hasTarget: true, 1f, Leash, Slot, Reach));
    }

    [Fact]
    public void ExactlyAtLeash_StillFights()
    {
        // Only *past* the leash breaks off, so a companion fighting right at the edge doesn't
        // oscillate between engaging and regrouping every frame.
        Assert.Equal(
            CompanionAction.Attack,
            CompanionDecision.Decide(Leash, hasTarget: true, 1f, Leash, Slot, Reach));
    }

    [Fact]
    public void WithinSlotTolerance_DoesNotJitter()
    {
        Assert.Equal(
            CompanionAction.Hold,
            CompanionDecision.Decide(Slot, hasTarget: false, 0f, Leash, Slot, Reach));
    }
}
