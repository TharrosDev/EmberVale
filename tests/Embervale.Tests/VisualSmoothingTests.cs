using System;
using Embervale.Player;
using Godot;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// Pins the arithmetic behind <see cref="PlayerVisualSmoother"/>: the interpolation residual that
/// draws a physics-rate body smoothly on a faster screen, and the third-person yaw lag.
/// </summary>
public class VisualSmoothingTests
{
    private static readonly Vector3 From = new(10f, 2f, -4f);
    private static readonly Vector3 To = new(10.08f, 2.01f, -4.02f);

    [Fact]
    public void AtTheEndOfTheStep_TheBodyIsDrawnWhereItIs()
    {
        Assert.Equal(Vector3.Zero, VisualSmoothing.Residual(From, To, 1f));
    }

    [Fact]
    public void AtTheStartOfTheStep_TheBodyIsDrawnWhereItWas()
    {
        Vector3 residual = VisualSmoothing.Residual(From, To, 0f);

        Assert.True((To + residual).DistanceTo(From) < 1e-5f);
    }

    [Theory]
    [InlineData(0.25f)]
    [InlineData(0.5f)]
    [InlineData(0.9f)]
    public void InBetween_TheDrawnPositionIsTheInterpolatedOne(float fraction)
    {
        Vector3 drawn = To + VisualSmoothing.Residual(From, To, fraction);

        Assert.True(drawn.DistanceTo(From.Lerp(To, fraction)) < 1e-5f);
    }

    [Fact]
    public void TheDrawnPositionMovesEvenlyAcrossFrames()
    {
        // Three drawn frames inside one physics step: equal fractions apart must be equal distances
        // apart. This is the whole point; without the residual all three would draw at To.
        Vector3 a = To + VisualSmoothing.Residual(From, To, 0.2f);
        Vector3 b = To + VisualSmoothing.Residual(From, To, 0.5f);
        Vector3 c = To + VisualSmoothing.Residual(From, To, 0.8f);

        Assert.Equal(a.DistanceTo(b), b.DistanceTo(c), 5);
        Assert.True(a.DistanceTo(b) > 0f);
    }

    [Fact]
    public void AStandingBody_HasNoResidual()
    {
        Assert.Equal(Vector3.Zero, VisualSmoothing.Residual(From, From, 0.3f));
    }

    [Theory]
    [InlineData(-0.5f)]
    [InlineData(1.7f)]
    public void TheFractionIsClamped(float fraction)
    {
        Vector3 residual = VisualSmoothing.Residual(From, To, fraction);

        Assert.True(residual.Length() <= From.DistanceTo(To) + 1e-6f);
    }

    [Fact]
    public void ATeleport_IsNotSlidAcross()
    {
        // A respawn, a blink, a fast travel: one physics step of several metres is drawn where it
        // landed.
        Assert.Equal(Vector3.Zero, VisualSmoothing.Residual(From, From + new Vector3(6f, 0f, 0f), 0.1f));
    }

    [Fact]
    public void ASprintStepIsSmoothed_NotTreatedAsATeleport()
    {
        // 8 m/s at 60 Hz is about 13 cm a tick; even at 30 Hz it is under the cut-off.
        Vector3 step = new(0f, 0f, -8f / 30f);

        Assert.NotEqual(Vector3.Zero, VisualSmoothing.Residual(From, From + step, 0.5f));
        Assert.True(step.Length() < VisualSmoothing.MaxStep);
    }

    [Fact]
    public void ANonFiniteInput_GivesNoResidual()
    {
        Assert.Equal(Vector3.Zero, VisualSmoothing.Residual(From, new Vector3(float.NaN, 0f, 0f), 0.5f));
        Assert.Equal(Vector3.Zero, VisualSmoothing.Residual(From, To, float.NaN));
    }

    [Theory]
    [InlineData(0f, 0f)]
    [InlineData(3.5f, 3.5f - MathF.Tau)]
    [InlineData(-3.5f, MathF.Tau - 3.5f)]
    [InlineData(MathF.Tau + 0.25f, 0.25f)]
    public void WrapAngle_FoldsIntoHalfATurnEitherWay(float input, float expected)
    {
        Assert.Equal(expected, VisualSmoothing.WrapAngle(input), 4);
    }

    [Fact]
    public void ATurn_LeavesTheVisibleBodyBehindIt()
    {
        // The camera (and so the body) turns +0.2 rad in a frame: the mesh has not moved, so it now
        // sits -0.2 from the body, less a frame's decay.
        float lag = VisualSmoothing.YawLagStep(0f, 0.2f, 1f / 120f, enabled: true);

        Assert.True(lag < 0f);
        Assert.InRange(lag, -0.2f, -0.17f);
    }

    [Fact]
    public void TheLagDecaysOverATenthOfASecond()
    {
        float lag = VisualSmoothing.YawLagStep(0f, 0.3f, 0f, true);
        float start = lag;
        for (int i = 0; i < 12; i++)
        {
            lag = VisualSmoothing.YawLagStep(lag, 0f, 1f / 120f, true);
        }

        // 0.1 s later: one time constant, so about 37% left.
        Assert.InRange(lag / start, 0.3f, 0.45f);
    }

    [Fact]
    public void TheLagSettlesExactly()
    {
        float lag = VisualSmoothing.YawLagStep(0f, 0.3f, 0f, true);
        for (int i = 0; i < 240; i++)
        {
            lag = VisualSmoothing.YawLagStep(lag, 0f, 1f / 120f, true);
        }

        Assert.Equal(0f, lag);
    }

    [Fact]
    public void TheLagIsFrameRateIndependent()
    {
        float slow = VisualSmoothing.YawLagStep(0f, 0.3f, 0f, true);
        float fast = slow;
        for (int i = 0; i < 6; i++)
        {
            slow = VisualSmoothing.YawLagStep(slow, 0f, 1f / 60f, true);
        }

        for (int i = 0; i < 24; i++)
        {
            fast = VisualSmoothing.YawLagStep(fast, 0f, 1f / 240f, true);
        }

        Assert.Equal(slow, fast, 4);
    }

    [Fact]
    public void AWhipTurn_IsHeldToThirtyDegrees()
    {
        float lag = VisualSmoothing.YawLagStep(0f, 2.5f, 1f / 120f, true);

        Assert.Equal(-VisualSmoothing.MaxYawLag, lag, 5);
        Assert.Equal(30f, Mathf.RadToDeg(VisualSmoothing.MaxYawLag), 3);
    }

    [Fact]
    public void ATurnAcrossTheWrap_IsASmallTurn()
    {
        // Yaw going from just under +pi to just over -pi is a small turn, not a full one.
        float turned = VisualSmoothing.WrapAngle((-MathF.PI + 0.05f) - (MathF.PI - 0.05f));

        Assert.Equal(0.1f, turned, 4);
    }

    [Fact]
    public void Disabled_TakesUpNoTurn()
    {
        // First person, a lock, an action: the body faces exactly where the camera does.
        Assert.Equal(0f, VisualSmoothing.YawLagStep(0f, 0.4f, 1f / 60f, enabled: false));
    }

    [Fact]
    public void Disabled_BleedsOffWhatIsLeft_FasterThanEnabled()
    {
        float enabled = VisualSmoothing.YawLagStep(0.3f, 0f, 0.05f, true);
        float disabled = VisualSmoothing.YawLagStep(0.3f, 0f, 0.05f, false);

        Assert.True(disabled < enabled);
        Assert.True(disabled > 0f, "it eases out rather than snapping");
    }

    [Fact]
    public void ANegativeDelta_DoesNotGrowTheLag()
    {
        float lag = VisualSmoothing.YawLagStep(0.2f, 0f, -1f, true);

        Assert.Equal(0.2f, lag, 5);
    }
}
