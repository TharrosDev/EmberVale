using System;
using Embervale.Movement;
using Xunit;

namespace Embervale.Tests;

/// <summary>Gait and slope speed arithmetic (<see cref="LocomotionRules"/>).</summary>
public class LocomotionRulesTests
{
    private static readonly float MaxSlope = MathF.PI / 4f; // Godot's default floor_max_angle
    private const float Penalty = 0.3f;

    [Fact]
    public void RunIsTheBaseline()
    {
        Assert.Equal(1f, LocomotionRules.GaitScale(false, false, 1.6f, 0.45f));
    }

    [Fact]
    public void WalkAndSprintApplyTheirMultipliers()
    {
        Assert.Equal(0.45f, LocomotionRules.GaitScale(false, true, 1.6f, 0.45f));
        Assert.Equal(1.6f, LocomotionRules.GaitScale(true, false, 1.6f, 0.45f));
    }

    [Fact]
    public void SprintOverridesWalk()
    {
        Assert.Equal(1.6f, LocomotionRules.GaitScale(true, true, 1.6f, 0.45f));
    }

    /// <summary>A floor rising toward +X: its normal leans toward -X.</summary>
    private static (float X, float Y, float Z) SlopeRisingTowardPlusX(float degrees)
    {
        float a = degrees * MathF.PI / 180f;
        return (-MathF.Sin(a), MathF.Cos(a), 0f);
    }

    [Fact]
    public void FlatGroundIsFullSpeed()
    {
        Assert.Equal(1f, LocomotionRules.UphillScale(0f, 1f, 0f, 1f, 0f, MaxSlope, Penalty));
    }

    [Fact]
    public void UphillIsSlowerAndSteeperIsSlowerStill()
    {
        var gentle = SlopeRisingTowardPlusX(10f);
        var steep = SlopeRisingTowardPlusX(35f);
        float a = LocomotionRules.UphillScale(gentle.X, gentle.Y, gentle.Z, 1f, 0f, MaxSlope, Penalty);
        float b = LocomotionRules.UphillScale(steep.X, steep.Y, steep.Z, 1f, 0f, MaxSlope, Penalty);
        Assert.True(a < 1f);
        Assert.True(b < a);
    }

    [Fact]
    public void TheSteepestWalkableGroundCostsTheFullPenalty()
    {
        var max = SlopeRisingTowardPlusX(45f);
        Assert.Equal(1f - Penalty, LocomotionRules.UphillScale(max.X, max.Y, max.Z, 1f, 0f, MaxSlope, Penalty), 3);
    }

    [Fact]
    public void DownhillIsNotPenalised()
    {
        var slope = SlopeRisingTowardPlusX(30f);
        Assert.Equal(1f, LocomotionRules.UphillScale(slope.X, slope.Y, slope.Z, -1f, 0f, MaxSlope, Penalty));
    }

    /// <summary>⚠️ The naive rule scales by the floor's steepness and makes every contour path a slog.</summary>
    [Fact]
    public void TraversingASlopeSidewaysIsLevelGoing()
    {
        var slope = SlopeRisingTowardPlusX(40f);
        Assert.Equal(1f, LocomotionRules.UphillScale(slope.X, slope.Y, slope.Z, 0f, 1f, MaxSlope, Penalty), 4);
    }

    [Fact]
    public void TheDirectionsMagnitudeDoesNotChangeTheGrade()
    {
        var slope = SlopeRisingTowardPlusX(20f);
        Assert.Equal(
            LocomotionRules.UphillScale(slope.X, slope.Y, slope.Z, 1f, 0f, MaxSlope, Penalty),
            LocomotionRules.UphillScale(slope.X, slope.Y, slope.Z, 0.3f, 0f, MaxSlope, Penalty), 4);
    }

    [Theory]
    [InlineData(float.NaN, 1f, 0f)]
    [InlineData(0f, 0f, 0f)]
    [InlineData(0f, float.PositiveInfinity, 0f)]
    public void ADegenerateNormalIsNeverASpeedChange(float nx, float ny, float nz)
    {
        Assert.Equal(1f, LocomotionRules.UphillScale(nx, ny, nz, 1f, 0f, MaxSlope, Penalty));
    }
}
