using System;
using Embervale.UI;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// Covers the pure maths behind the Phase 25F HUD compass strip. The drawing and the live player
/// facing run in-engine, but the heading/bearing/relative-angle arithmetic that decides where a
/// marker sits on the strip is pure and load-bearing, so it is pinned here.
/// </summary>
public class CompassMathTests
{
    private const float Fov = MathF.PI / 2f; // ±90°
    private const float HalfWidth = 180f;

    [Fact]
    public void WrapPi_WrapsBeyondHalfTurn()
    {
        Assert.Equal(0f, CompassMath.WrapPi(MathF.PI * 2f), 5);
        Assert.Equal(-MathF.PI / 2f, CompassMath.WrapPi(MathF.PI * 1.5f), 5);
    }

    [Fact]
    public void Angle_NorthIsZero_EastIsPositiveQuarter()
    {
        Assert.Equal(0f, CompassMath.Angle(0f, -1f), 5);          // -Z = North
        Assert.Equal(MathF.PI / 2f, CompassMath.Angle(1f, 0f), 5); // +X = East
    }

    [Fact]
    public void TargetDeadAhead_MapsToCentre()
    {
        // Facing North; target is due North → relative angle 0 → strip centre.
        float heading = CompassMath.HeadingFromForward(0f, -1f);
        float bearing = CompassMath.BearingTo(0f, -10f);
        float rel = CompassMath.Relative(bearing, heading);

        Assert.Equal(0f, rel, 5);
        Assert.Equal(0f, CompassMath.StripOffset(rel, Fov, HalfWidth), 5);
    }

    [Fact]
    public void TargetToRight_IsPositiveOffset_LeftIsNegative()
    {
        float heading = CompassMath.HeadingFromForward(0f, -1f); // facing North

        float right = CompassMath.Relative(CompassMath.BearingTo(10f, 0f), heading); // East
        float left = CompassMath.Relative(CompassMath.BearingTo(-10f, 0f), heading); // West

        Assert.True(CompassMath.StripOffset(right, Fov, HalfWidth) > 0f);
        Assert.True(CompassMath.StripOffset(left, Fov, HalfWidth) < 0f);
    }

    [Fact]
    public void OutsideFov_IsCulled()
    {
        // Facing North, target due South (180° away) → outside the ±90° window.
        float heading = CompassMath.HeadingFromForward(0f, -1f);
        float rel = CompassMath.Relative(CompassMath.BearingTo(0f, 10f), heading);

        Assert.False(CompassMath.InView(rel, Fov));
    }

    [Fact]
    public void AtFovEdge_IsInView()
    {
        float heading = CompassMath.HeadingFromForward(0f, -1f);
        float rel = CompassMath.Relative(CompassMath.BearingTo(10f, 0f), heading); // exactly +90°

        Assert.True(CompassMath.InView(rel, Fov));
        Assert.Equal(HalfWidth, CompassMath.StripOffset(rel, Fov, HalfWidth), 3);
    }
}
