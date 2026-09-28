using System;
using Embervale.Combat;
using Godot;
using Xunit;

namespace Embervale.Tests;

/// <summary>Pins the bow's pure rules: the draw curve and stamina, the ballistic launch, scatter,
/// headshot height, hit-zone routing and the aim-assist pull.</summary>
public class RangedTests
{
    private const float Dt = 1f / 60f;

    // ---- draw ----

    [Fact]
    public void HoldingBuildsTheDrawAndSpendsStamina()
    {
        var draw = default(RangedMath.Draw);
        float spent = 0f;
        for (int i = 0; i < 30; i++)
        {
            draw = RangedMath.Advance(draw, Dt, true, 100f, out float s);
            spent += s;
        }

        Assert.Equal(0.5f, draw.Held, 3);
        Assert.Equal(RangedMath.DrawStaminaPerSecond * 0.5f, spent, 3);
        Assert.False(draw.Released);
    }

    [Fact]
    public void ADrawStopsCostingAtFull()
    {
        var draw = new RangedMath.Draw(RangedMath.DrawSeconds, false, false);
        draw = RangedMath.Advance(draw, Dt, true, 100f, out float spent);
        Assert.Equal(0f, spent);
        Assert.Equal(1f, RangedMath.Charge(draw.Held));
    }

    [Fact]
    public void LettingGoLatchesAndTheDrawCannotBeResumed()
    {
        var draw = new RangedMath.Draw(0.3f, false, false);
        draw = RangedMath.Advance(draw, Dt, false, 100f, out _);
        Assert.True(draw.Released);

        RangedMath.Draw again = RangedMath.Advance(draw, Dt, true, 100f, out float spent);
        Assert.Equal(0.3f, again.Held);
        Assert.Equal(0f, spent);
    }

    [Fact]
    public void NoStaminaStopsTheStringAndStrainsTheArms()
    {
        var draw = new RangedMath.Draw(0.2f, false, false);
        draw = RangedMath.Advance(draw, Dt, true, 0.01f, out float spent);

        Assert.True(draw.Strained);
        Assert.Equal(0.2f, draw.Held);
        Assert.Equal(0.01f, spent, 5);
    }

    [Fact]
    public void ChargeClampsAndScalesArePinnedAtBothEnds()
    {
        Assert.Equal(0f, RangedMath.Charge(-1f));
        Assert.Equal(1f, RangedMath.Charge(9f));
        Assert.Equal(RangedMath.SnapDamage, RangedMath.DamageScale(0f), 5);
        Assert.Equal(1f, RangedMath.DamageScale(1f), 5);
        Assert.Equal(RangedMath.SnapSpeed, RangedMath.SpeedScale(0f), 5);
        Assert.Equal(1f, RangedMath.SpeedScale(1f), 5);
        Assert.Equal(RangedMath.SnapPoise, RangedMath.PoiseScale(0f), 5);
        Assert.True(RangedMath.DamageScale(0.5f) > RangedMath.DamageScale(0.2f));
    }

    // ---- spread ----

    [Fact]
    public void AFullDrawIsExactAndAnUndrawnOneScatters()
    {
        Vector3 aim = Vector3.Forward;
        Assert.Equal(aim, RangedMath.Scatter(aim, 1f, 0.9f, 0.3f));

        Vector3 loose = RangedMath.Scatter(aim, 0f, 1f, 0.25f);
        Assert.Equal(RangedMath.SnapSpreadDegrees, Mathf.RadToDeg(aim.AngleTo(loose)), 2);
        Assert.Equal(1f, loose.Length(), 4);
    }

    [Fact]
    public void ScatterNeverLeavesItsCone()
    {
        var rng = new Random(7);
        for (int i = 0; i < 200; i++)
        {
            Vector3 v = RangedMath.Scatter(Vector3.Up, 0.2f, (float)rng.NextDouble(), (float)rng.NextDouble());
            Assert.True(Mathf.RadToDeg(Vector3.Up.AngleTo(v)) <= RangedMath.SpreadDegrees(0.2f) + 0.01f);
        }
    }

    // ---- ballistics ----

    [Theory]
    [InlineData(12f, 0f, 42f)]
    [InlineData(30f, 3f, 42f)]
    [InlineData(45f, -4f, 42f)]
    [InlineData(20f, 0f, 23f)]
    public void TheLaunchDirectionCarriesTheArrowThroughTheAimPoint(float range, float rise, float speed)
    {
        var from = new Vector3(1f, 1.4f, 2f);
        var target = new Vector3(1f + range, 1.4f + rise, 2f);
        Vector3 dir = RangedMath.LaunchDirection(from, target, speed, RangedMath.ArrowGravity, 100f);

        // Fly it the way Arrow does and find the closest approach.
        Vector3 pos = from;
        Vector3 vel = dir * speed;
        float closest = float.MaxValue;
        const float h = 1f / 600f;
        for (int i = 0; i < 6000 && pos.X < target.X + 1f; i++)
        {
            vel.Y -= RangedMath.ArrowGravity * h;
            pos += vel * h;
            closest = Math.Min(closest, pos.DistanceTo(target));
        }

        Assert.True(closest < 0.15f, $"missed the aim point by {closest:0.00} m");
    }

    [Fact]
    public void WithNoGravityTheDirectionIsTheStraightLine()
    {
        Vector3 dir = RangedMath.LaunchDirection(Vector3.Zero, new Vector3(0f, 0f, -10f), 40f, 0f, 60f);
        Assert.Equal(Vector3.Forward, dir);
    }

    [Fact]
    public void AnUnreachableTargetGetsALobNotNaN()
    {
        Vector3 dir = RangedMath.LaunchDirection(Vector3.Zero, new Vector3(500f, 0f, 0f), 20f, 9.8f, 1000f);
        Assert.False(float.IsNaN(dir.Y));
        Assert.True(dir.Y > 0.5f);
    }

    [Fact]
    public void ATargetBeyondReachIsAimedAtTheFurthestReachablePoint()
    {
        Vector3 far = RangedMath.LaunchDirection(Vector3.Zero, new Vector3(200f, 0f, 0f), 42f, 9.8f, 50f);
        Vector3 edge = RangedMath.LaunchDirection(Vector3.Zero, new Vector3(50f, 0f, 0f), 42f, 9.8f, 50f);
        Assert.Equal(edge.Y, far.Y, 4);
    }

    // ---- headshots and zones ----

    [Theory]
    [InlineData(1.8f, true)]
    [InlineData(1.7f, true)]
    [InlineData(1.0f, false)]
    public void OnlyTheTopSliceOfATallBodyIsItsHead(float hitY, bool head)
    {
        Assert.Equal(head, RangedMath.IsHeadHeight(hitY, 0f, 1.8f));
    }

    [Fact]
    public void ALowSlungBodyHasNoHeadByHeight()
    {
        Assert.False(RangedMath.IsHeadHeight(0.9f, 0f, 0.9f));
    }

    [Fact]
    public void ZoneRoutingPicksTheWeakPointOfTheNearestBody()
    {
        var hits = new[]
        {
            new HitZoneRouting.Candidate(1, 1f, 0.25f),   // torso of the near body
            new HitZoneRouting.Candidate(1, 2f, 0.40f),   // its head, also touched
            new HitZoneRouting.Candidate(2, 3f, 9.00f),   // a weak point on a body further off
        };

        Assert.Equal(1, HitZoneRouting.Pick(hits));
    }

    [Fact]
    public void ZoneRoutingBreaksTiesByDistanceAndHandlesNothing()
    {
        Assert.Equal(-1, HitZoneRouting.Pick(ReadOnlySpan<HitZoneRouting.Candidate>.Empty));
        var hits = new[]
        {
            new HitZoneRouting.Candidate(1, 1f, 0.5f),
            new HitZoneRouting.Candidate(1, 1f, 0.2f),
        };
        Assert.Equal(1, HitZoneRouting.Pick(hits));
    }

    // ---- aim assist ----

    [Fact]
    public void StrengthZeroNeverMovesTheAim()
    {
        Vector3 aim = Vector3.Forward;
        Vector3 toTarget = (Vector3.Forward + (Vector3.Right * 0.02f)).Normalized();
        Assert.Equal(aim, AimAssistMath.Pull(aim, toTarget, 0f, 1f, true));
    }

    [Fact]
    public void TheConeGrowsWithStrengthAndWidensForALockedTarget()
    {
        Assert.Equal(0f, AimAssistMath.ConeRadians(0f, false));
        Assert.True(AimAssistMath.ConeRadians(1f, false) > AimAssistMath.ConeRadians(0.5f, false));
        Assert.True(AimAssistMath.ConeRadians(0.5f, true) > AimAssistMath.ConeRadians(0.5f, false));
    }

    [Fact]
    public void ATargetOutsideTheConeIsLeftAlone()
    {
        Vector3 aim = Vector3.Forward;
        Vector3 far = (Vector3.Forward + Vector3.Right).Normalized(); // 45 degrees
        Assert.Equal(aim, AimAssistMath.Pull(aim, far, 1f, 1f, true));
    }

    [Fact]
    public void ThePullMovesTowardTheTargetAndFadesAtTheEdge()
    {
        Vector3 aim = Vector3.Forward;
        float near = Mathf.DegToRad(1f);
        float edge = Mathf.DegToRad(6.5f);
        Vector3 toNear = (Vector3.Forward + (Vector3.Right * Mathf.Tan(near))).Normalized();
        Vector3 toEdge = (Vector3.Forward + (Vector3.Right * Mathf.Tan(edge))).Normalized();

        float pulledNear = aim.AngleTo(AimAssistMath.Pull(aim, toNear, 1f, 1f, false));
        float pulledEdge = aim.AngleTo(AimAssistMath.Pull(aim, toEdge, 1f, 1f, false));

        Assert.True(pulledNear > 0f);
        Assert.True(pulledNear / near > pulledEdge / edge, "the pull should be a larger share near the centre");
    }

    [Fact]
    public void ASteadierShotIsPulledHarder()
    {
        float angle = Mathf.DegToRad(3f);
        Assert.True(AimAssistMath.Weight(angle, 1f, 1f, false) > AimAssistMath.Weight(angle, 1f, 0f, false));
    }
}
