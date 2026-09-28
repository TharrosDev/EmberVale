using System;
using Embervale.World;
using Godot;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// The recovery contract's pure half: the safe-ground trail, facing away from the hazard, the fade,
/// and telling a void from a crypt.
/// </summary>
public sealed class WorldRecoveryTests
{
    private static bool Always(Vector3 _) => true;

    [Fact]
    public void TheTrailKeepsTheNewestPointsFirstAndDropsTheOldest()
    {
        var trail = new SafeGroundTrail(capacity: 3, minSpacing: 1f);
        for (int i = 0; i < 5; i++)
        {
            trail.Record(new Vector3(i * 10f, 0f, 0f));
        }
        Assert.Equal(3, trail.Count);
        Assert.Equal(40f, trail.Points[0].X);
        Assert.Equal(20f, trail.Points[2].X);
    }

    [Fact]
    public void StandingStillRefreshesTheNewestPointInsteadOfFloodingTheTrail()
    {
        var trail = new SafeGroundTrail(capacity: 4, minSpacing: 4f);
        trail.Record(new Vector3(0f, 0f, 0f));
        trail.Record(new Vector3(20f, 0f, 0f));
        trail.Record(new Vector3(21f, 0f, 0f));
        trail.Record(new Vector3(22f, 0f, 0f));
        Assert.Equal(2, trail.Count);
        Assert.Equal(22f, trail.Newest!.Value.X);
        Assert.Equal(0f, trail.Points[1].X);
    }

    [Fact]
    public void PickSkipsThePointOnTheLipForOneFurtherBack()
    {
        var trail = new SafeGroundTrail(capacity: 8, minSpacing: 1f);
        trail.Record(new Vector3(0f, 0f, 0f));   // well back
        trail.Record(new Vector3(9f, 0f, 0f));   // on the lip
        var hazard = new Vector3(10f, -3f, 0f);
        Assert.Equal(0f, trail.Pick(hazard, Always, minHazardDistance: 3f, maxDistance: 100f)!.Value.X);
    }

    [Fact]
    public void PickFallsBackToTheLipWhenNothingElseIsLeft()
    {
        var trail = new SafeGroundTrail(capacity: 8, minSpacing: 1f);
        trail.Record(new Vector3(9f, 0f, 0f));
        Assert.Equal(9f, trail.Pick(new Vector3(10f, 0f, 0f), Always, 3f, 100f)!.Value.X);
    }

    [Fact]
    public void PickNeverReturnsAPointThatIsNoLongerSafe()
    {
        // ⚠️ The whole reason for re-judging at recovery time: never onto a second hazard.
        var trail = new SafeGroundTrail(capacity: 8, minSpacing: 1f);
        trail.Record(new Vector3(-20f, 0f, 0f));
        trail.Record(new Vector3(-10f, 0f, 0f));
        Vector3? picked = trail.Pick(Vector3.Zero, p => p.X != -10f, 3f, 100f);
        Assert.Equal(-20f, picked!.Value.X);
        Assert.Null(trail.Pick(Vector3.Zero, _ => false, 3f, 100f));
    }

    [Fact]
    public void PickIgnoresGroundLeftBehindByATeleport()
    {
        var trail = new SafeGroundTrail(capacity: 8, minSpacing: 1f);
        trail.Record(new Vector3(900f, 0f, 0f));
        Assert.Null(trail.Pick(Vector3.Zero, Always, 3f, maxDistance: 120f));
    }

    [Fact]
    public void AnEmptyTrailPicksNothing()
    {
        var trail = new SafeGroundTrail();
        Assert.Null(trail.Newest);
        Assert.Null(trail.Pick(Vector3.Zero, Always, 3f, 120f));
    }

    [Theory]
    [InlineData(0f, -1f, 0f)]                    // hazard behind at +Z, face -Z (Godot forward)
    [InlineData(1f, 0f, -MathF.PI / 2f)]         // face +X
    [InlineData(-1f, 0f, MathF.PI / 2f)]         // face -X
    public void RecoveryFacesAwayFromTheHazard(float dx, float dz, float expectedYaw)
    {
        float? yaw = WorldRecoveryRules.FacingAwayYaw(0f, 0f, dx * 5f, dz * 5f);
        Assert.NotNull(yaw);
        Assert.Equal(expectedYaw, yaw!.Value, 4);

        // And the basis that yaw builds really does point its forward (-Z) along the escape.
        Vector3 forward = new Basis(Vector3.Up, yaw.Value) * Vector3.Forward;
        Assert.Equal(dx, forward.X, 3);
        Assert.Equal(dz, forward.Z, 3);
    }

    [Fact]
    public void NoFacingWhenTheLandingIsTheHazard()
    {
        Assert.Null(WorldRecoveryRules.FacingAwayYaw(3f, 4f, 3f, 4f));
    }

    [Fact]
    public void TheFadeGoesToBlackMovesAtThePeakAndComesBack()
    {
        const float Out = 0.25f;
        const float In = 0.5f;
        Assert.Equal(0f, WorldRecoveryRules.FadeAlpha(0f, Out, In));
        Assert.Equal(0.5f, WorldRecoveryRules.FadeAlpha(0.125f, Out, In), 4);
        Assert.False(WorldRecoveryRules.IsAtPeak(0.2f, Out));
        Assert.True(WorldRecoveryRules.IsAtPeak(0.25f, Out));
        Assert.Equal(1f, WorldRecoveryRules.FadeAlpha(0.25f, Out, In), 4);
        Assert.Equal(0.5f, WorldRecoveryRules.FadeAlpha(0.5f, Out, In), 4);
        Assert.Equal(0f, WorldRecoveryRules.FadeAlpha(0.75f, Out, In), 4);
        Assert.False(WorldRecoveryRules.IsFinished(0.7f, Out, In));
        Assert.True(WorldRecoveryRules.IsFinished(0.75f, Out, In));
    }

    [Fact]
    public void AZeroLengthFadeIsAnInstantMove()
    {
        Assert.True(WorldRecoveryRules.IsAtPeak(0.001f, 0f));
        Assert.True(WorldRecoveryRules.IsFinished(0.001f, 0f, 0f));
        Assert.Equal(0f, WorldRecoveryRules.FadeAlpha(0.001f, 0f, 0f));
    }

    [Fact]
    public void BelowTheTerrainWithAFloorIsACryptNotAVoid()
    {
        Assert.False(WorldRecoveryRules.IsFallThrough(-20f, 0f, hasFloorBelow: true, 8f, -600f));
        Assert.True(WorldRecoveryRules.IsFallThrough(-20f, 0f, hasFloorBelow: false, 8f, -600f));
    }

    [Fact]
    public void AShallowDipUnderTheHeightfieldIsNotAFall()
    {
        // A foot a metre into a collision mesh that disagrees with the analytic field.
        Assert.False(WorldRecoveryRules.IsFallThrough(-1f, 0f, hasFloorBelow: false, 8f, -600f));
    }

    [Fact]
    public void BelowTheKillFloorIsAlwaysAFall()
    {
        Assert.True(WorldRecoveryRules.IsFallThrough(-700f, -650f, hasFloorBelow: true, 8f, -600f));
        Assert.True(WorldRecoveryRules.IsFallThrough(float.NaN, 0f, hasFloorBelow: true, 8f, -600f));
    }
}
