using System.Collections.Generic;
using System.Linq;
using Embervale.Animation;
using Embervale.Movement;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// Pins the locomotion blend space (<see cref="LocomotionBlend"/>): gaits measured against the
/// actor's own run speed, the fall timer, and the triangles the space is built from.
/// </summary>
public class LocomotionBlendTests
{
    private const float PlayerRun = 5f;

    private static LocomotionBlend.Point PointFor(string slot) =>
        LocomotionBlend.Points.Single(p => p.Slot == slot);

    [Fact]
    public void ThePointsSitOnTheMotorsOwnGaits()
    {
        // The motor's defaults: walk x0.45, run x1, sprint x1.6. If these drift apart the walk clip
        // plays at a speed the body never walks at.
        Assert.Equal(LocomotionRules.GaitScale(false, true, 1.6f, 0.45f), PointFor("walk").Y, 5);
        Assert.Equal(LocomotionRules.GaitScale(false, false, 1.6f, 0.45f), PointFor("run").Y, 5);
        Assert.Equal(LocomotionRules.GaitScale(true, false, 1.6f, 0.45f), PointFor("sprint").Y, 5);
        Assert.Equal(-PointFor("walk").Y, PointFor("walk_back").Y, 5);
        Assert.Equal(0f, PointFor("idle").Y);
    }

    [Fact]
    public void TheStrafesSitEitherSideOfTheIdle()
    {
        Assert.Equal(-1f, PointFor("strafe_left").X);
        Assert.Equal(1f, PointFor("strafe_right").X);
        Assert.Equal(0f, PointFor("strafe_left").Y);
        Assert.Equal(0f, PointFor("strafe_right").Y);
    }

    [Fact]
    public void OnlyTheStrafesBorrowAClip()
    {
        foreach (LocomotionBlend.Point point in LocomotionBlend.Points)
        {
            Assert.Equal(point.X != 0f, point.Fallback.Length > 0);
        }
    }

    [Fact]
    public void EveryPointIsInsideTheSpace()
    {
        foreach (LocomotionBlend.Point point in LocomotionBlend.Points)
        {
            Assert.InRange(point.X, LocomotionBlend.MinStrafe, LocomotionBlend.MaxStrafe);
            Assert.InRange(point.Y, LocomotionBlend.MinForward, LocomotionBlend.MaxForward);
        }
    }

    [Theory]
    [InlineData(2.25f, 0.45f)] // walking
    [InlineData(5f, 1f)]       // running
    [InlineData(8f, 1.6f)]     // sprinting
    [InlineData(-2.25f, -0.45f)]
    public void ThePlayersRealSpeeds_LandOnTheClips(float forward, float expected)
    {
        (float x, float y) = LocomotionBlend.Gait(forward, 0f, PlayerRun);

        Assert.Equal(0f, x);
        Assert.Equal(expected, y, 4);
    }

    [Fact]
    public void ASlowActorAtItsOwnRun_IsAtTheRunClip()
    {
        // The reason for normalising: a 3 m/s brute at full speed is running, not half-walking.
        Assert.Equal(1f, LocomotionBlend.Gait(3f, 0f, 3f).Y, 4);
        Assert.Equal(1f, LocomotionBlend.Gait(6.5f, 0f, 6.5f).Y, 4);
    }

    [Fact]
    public void Sideways_IsTheStrafeAxis()
    {
        Assert.Equal((-1f, 0f), LocomotionBlend.Gait(0f, -PlayerRun, PlayerRun));
        Assert.Equal((1f, 0f), LocomotionBlend.Gait(0f, PlayerRun, PlayerRun));
    }

    [Fact]
    public void StandingStill_IsExactlyTheIdle()
    {
        Assert.Equal((0f, 0f), LocomotionBlend.Gait(0.02f, -0.03f, PlayerRun));
    }

    [Fact]
    public void AShovedBody_StaysInsideTheSpace()
    {
        (float x, float y) = LocomotionBlend.Gait(40f, -40f, PlayerRun);

        Assert.InRange(x, LocomotionBlend.MinStrafe, 0f);
        Assert.Equal(LocomotionBlend.MaxForward, y);
        Assert.Equal(LocomotionBlend.MinStrafe, LocomotionBlend.Gait(0f, -40f, PlayerRun).X);
    }

    [Theory]
    [InlineData(1f, LocomotionBlend.RunGait)]
    [InlineData(1.6f, LocomotionBlend.SprintGait)]
    public void ADiagonal_IsHalfTheStrafeAndHalfTheGaitForItsSpeed(float speedOverRun, float gait)
    {
        // Equal parts forward and right at this speed. The point has to sit on the line from the
        // strafe (1, 0) to that gait (0, gait), halfway along: not out past the strafe-to-sprint
        // edge, where the engine would blend the sprint into a plain diagonal run.
        float component = speedOverRun * PlayerRun / System.MathF.Sqrt(2f);
        (float x, float y) = LocomotionBlend.Gait(component, component, PlayerRun);

        Assert.Equal(0.5f, x, 3);
        Assert.Equal(0.5f * gait, y, 3);

        (float left, float back) = LocomotionBlend.Gait(-component, -component, PlayerRun);
        Assert.Equal(-0.5f, left, 3);
        Assert.True(back < 0f);
    }

    [Fact]
    public void ASlightDrift_BarelyLeavesTheForwardAxis()
    {
        (float x, float y) = LocomotionBlend.Gait(PlayerRun, 0.05f * PlayerRun, PlayerRun);

        Assert.InRange(x, 0f, 0.06f);
        Assert.InRange(y, 0.93f, 1.01f);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(-2f)]
    [InlineData(float.NaN)]
    public void ABadRunSpeed_FallsBackRatherThanDividingByIt(float runSpeed)
    {
        (float _, float y) = LocomotionBlend.Gait(LocomotionBlend.FallbackRunSpeed, 0f, runSpeed);

        Assert.Equal(1f, y, 4);
    }

    [Fact]
    public void ANonFiniteVelocity_IsStandingStill()
    {
        Assert.Equal((0f, 0f), LocomotionBlend.Gait(float.NaN, 1f, PlayerRun));
    }

    [Fact]
    public void RunSpeed_PrefersTheStat_ThenTheMotor_ThenTheFallback()
    {
        Assert.Equal(4.2f, LocomotionBlend.RunSpeed(4.2f, 5f));
        Assert.Equal(5f, LocomotionBlend.RunSpeed(0f, 5f));
        Assert.Equal(LocomotionBlend.FallbackRunSpeed, LocomotionBlend.RunSpeed(0f, 0f));
        Assert.Equal(5f, LocomotionBlend.RunSpeed(float.NaN, 5f));
    }

    [Fact]
    public void AScheduledNpc_WalksOnTheWalkClip()
    {
        // No motor: the fallback run speed has to put a 1.6 m/s schedule walk at the walk point.
        float y = LocomotionBlend.Gait(1.6f, 0f, LocomotionBlend.FallbackRunSpeed).Y;

        Assert.InRange(y, LocomotionBlend.WalkGait - 0.02f, LocomotionBlend.WalkGait + 0.02f);
    }

    [Fact]
    public void AFall_TakesAMomentToShow()
    {
        float timer = 0f;
        timer = LocomotionBlend.AirborneStep(timer, grounded: false, verticalSpeed: -3f, delta: 0.1f);
        Assert.False(LocomotionBlend.Falling(timer), "a kerb must not flash the pose");

        timer = LocomotionBlend.AirborneStep(timer, false, -5f, 0.1f);
        Assert.True(LocomotionBlend.Falling(timer));
    }

    [Fact]
    public void LandingClearsItAtOnce()
    {
        float timer = LocomotionBlend.AirborneStep(1f, grounded: true, verticalSpeed: -9f, delta: 0.016f);

        Assert.Equal(0f, timer);
        Assert.False(LocomotionBlend.Falling(timer));
    }

    [Fact]
    public void TheTopOfAJump_DoesNotDropThePose()
    {
        // Rising for 0.4 s, then a few frames of near-zero vertical speed at the apex.
        float timer = 0f;
        for (int i = 0; i < 24; i++)
        {
            timer = LocomotionBlend.AirborneStep(timer, false, 6f, 1f / 60f);
        }

        for (int i = 0; i < 4; i++)
        {
            timer = LocomotionBlend.AirborneStep(timer, false, 0.1f, 1f / 60f);
        }

        Assert.True(LocomotionBlend.Falling(timer));
    }

    [Fact]
    public void ABodyNobodyIsStepping_NeverFalls()
    {
        // An actor that has not been moved yet reports "not on floor" with no vertical speed. It
        // must stand, not hang in the fall pose.
        float timer = 0f;
        for (int i = 0; i < 600; i++)
        {
            timer = LocomotionBlend.AirborneStep(timer, grounded: false, verticalSpeed: 0f, delta: 1f / 60f);
        }

        Assert.Equal(0f, timer);
        Assert.False(LocomotionBlend.Falling(timer));
    }

    [Fact]
    public void ABodyLeftHangingInTheAir_LetsThePoseGo()
    {
        float timer = 0.6f;
        for (int i = 0; i < 60; i++)
        {
            timer = LocomotionBlend.AirborneStep(timer, false, 0f, 1f / 60f);
        }

        Assert.False(LocomotionBlend.Falling(timer));
    }

    private static List<(float X, float Y)> Placed(params string[] slots) =>
        LocomotionBlend.Points.Where(p => slots.Contains(p.Slot)).Select(p => (p.X, p.Y)).ToList();

    private static float Area((float X, float Y) a, (float X, float Y) b, (float X, float Y) c) =>
        System.Math.Abs(((b.X - a.X) * (c.Y - a.Y)) - ((c.X - a.X) * (b.Y - a.Y))) * 0.5f;

    [Fact]
    public void TheFullSpace_IsEightTriangles_NoneFlat()
    {
        List<(float X, float Y)> points = LocomotionBlend.Points.Select(p => (p.X, p.Y)).ToList();

        List<(int A, int B, int C)> triangles = LocomotionBlend.Triangles(points);

        // Five points up the forward axis make four spans; each strafe fans to all four.
        Assert.Equal(8, triangles.Count);
        foreach ((int a, int b, int c) in triangles)
        {
            Assert.True(Area(points[a], points[b], points[c]) > 0.01f, "a flat triangle blends nothing");
            Assert.Equal(3, new HashSet<int> { a, b, c }.Count);
        }
    }

    [Fact]
    public void EveryPointIsInSomeTriangle()
    {
        List<(float X, float Y)> points = LocomotionBlend.Points.Select(p => (p.X, p.Y)).ToList();
        var used = new HashSet<int>();
        foreach ((int a, int b, int c) in LocomotionBlend.Triangles(points))
        {
            used.Add(a);
            used.Add(b);
            used.Add(c);
        }

        Assert.Equal(points.Count, used.Count);
    }

    [Fact]
    public void TheTrianglesCoverTheForwardAxisBetweenNeighbouringGaits()
    {
        // The engine blends inside a triangle, or along its nearest edge. Every neighbouring pair on
        // the forward axis must be an edge of some triangle, or a plain run would have no edge to
        // blend along.
        List<(float X, float Y)> points = LocomotionBlend.Points.Select(p => (p.X, p.Y)).ToList();
        List<int> axis = Enumerable.Range(0, points.Count)
            .Where(i => points[i].X == 0f).OrderBy(i => points[i].Y).ToList();
        List<(int A, int B, int C)> triangles = LocomotionBlend.Triangles(points);

        for (int i = 0; i + 1 < axis.Count; i++)
        {
            int lower = axis[i];
            int upper = axis[i + 1];
            Assert.Contains(triangles, t =>
                new[] { t.A, t.B, t.C }.Contains(lower) && new[] { t.A, t.B, t.C }.Contains(upper));
        }
    }

    [Fact]
    public void ABodyWithOnlyIdleAndRun_StillGetsASpaceWithArea()
    {
        // A quadruped with two clips borrows its run for the strafes: four points, two triangles.
        List<(float X, float Y)> points = Placed("idle", "run", "strafe_left", "strafe_right");

        Assert.Equal(2, LocomotionBlend.Triangles(points).Count);
    }

    [Fact]
    public void PointsAllOnOneLine_MakeNoTriangles()
    {
        Assert.Empty(LocomotionBlend.Triangles(Placed("idle", "walk", "run")));
    }

    [Fact]
    public void TheSharedLibraryIsRequiredToCarryEverySlotTheSpaceNames()
    {
        foreach (LocomotionBlend.Point point in LocomotionBlend.Points)
        {
            Assert.Contains(point.Slot, AnimationClips.SharedSlots);
        }

        Assert.Contains("fall", AnimationClips.SharedSlots);
    }
}
