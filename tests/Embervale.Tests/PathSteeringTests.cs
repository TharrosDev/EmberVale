using Embervale.Movement;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// Covers the pure navmesh-steering rule (Phase 27A). The pathing itself (NavigationAgent3D corner
/// queries) runs in-engine, but the arrival test — keep steering until the <em>final</em> target,
/// not the next corner — is load-bearing (judging by the corner stops the actor short at every bend),
/// so it is pinned here.
/// </summary>
public class PathSteeringTests
{
    private const float Stop = 1.0f;

    [Fact]
    public void FarFromTarget_CornerAhead_Steers()
    {
        Assert.True(PathSteering.ShouldSteer(distanceToCorner: 3f, distanceToFinalTarget: 12f, Stop));
    }

    [Fact]
    public void ArrivedAtFinalTarget_DoesNotSteer_EvenIfCornerFar()
    {
        // The corner can sit far away while the agent is already within stopDistance of the goal —
        // arrival is judged by the final target, so it must NOT keep steering.
        Assert.False(PathSteering.ShouldSteer(distanceToCorner: 5f, distanceToFinalTarget: 0.5f, Stop));
    }

    [Fact]
    public void CornerCoincidesWithSelf_DoesNotSteer()
    {
        // Standing on the next corner gives no meaningful direction; don't jitter.
        Assert.False(PathSteering.ShouldSteer(distanceToCorner: 0f, distanceToFinalTarget: 8f, Stop));
    }

    [Fact]
    public void ExactlyAtStopDistance_DoesNotSteer()
    {
        Assert.False(PathSteering.ShouldSteer(distanceToCorner: 4f, distanceToFinalTarget: Stop, Stop));
    }
}
