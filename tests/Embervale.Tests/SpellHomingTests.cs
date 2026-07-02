using Embervale.Magic;
using Godot;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// Pins the pure homing-steer maths behind Ball Lightning (Phase 29.5G). The physics query that
/// finds the target lives in <c>SpellProjectile</c> (in-engine); the turn-toward behaviour is here.
/// </summary>
public class SpellHomingTests
{
    private static readonly Vector3 Forward = new(0f, 0f, -1f);

    [Fact]
    public void Steer_TurnsTowardTarget()
    {
        Vector3 toTarget = new(1f, 0f, 0f); // target square to the right
        Vector3 steered = SpellHoming.Steer(Forward, toTarget, turnPerSecond: 2f, dt: 0.1f);

        Assert.True(steered.Dot(toTarget.Normalized()) > Forward.Dot(toTarget.Normalized()),
            "steering should close the angle to the target");
        Assert.Equal(1f, steered.Length(), 3); // stays a unit vector
    }

    [Fact]
    public void Steer_FullTurnFactor_SnapsToTarget()
    {
        Vector3 toTarget = new(5f, 0f, 0f);
        Vector3 steered = SpellHoming.Steer(Forward, toTarget, turnPerSecond: 10f, dt: 1f); // t clamps to 1

        Assert.Equal(1f, steered.Dot(toTarget.Normalized()), 3);
    }

    [Fact]
    public void Steer_NegligibleTargetOffset_LeavesDirectionUnchanged()
    {
        Vector3 steered = SpellHoming.Steer(Forward, Vector3.Zero, turnPerSecond: 3.5f, dt: 0.016f);

        Assert.Equal(Forward, steered);
    }

    [Fact]
    public void Steer_ZeroDt_LeavesDirectionUnchanged()
    {
        Vector3 steered = SpellHoming.Steer(Forward, new Vector3(1f, 0f, 0f), turnPerSecond: 3.5f, dt: 0f);

        Assert.Equal(Forward, steered);
    }

    [Fact]
    public void Steer_DegenerateDirection_FallsBackToForward()
    {
        Vector3 steered = SpellHoming.Steer(Vector3.Zero, Vector3.Zero, turnPerSecond: 3.5f, dt: 0.016f);

        Assert.Equal(Vector3.Forward, steered);
    }
}
