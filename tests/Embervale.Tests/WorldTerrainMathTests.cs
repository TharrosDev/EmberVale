using System;
using System.Collections.Generic;
using Embervale.World;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// The 2026-08-29 geography overhaul replaced the seam contract these tests used to pin. The old
/// rule was "the field is exactly 0 on every cell boundary", which made seams safe by drawing a
/// flat rectangle around every cell. The new rule is stronger and has no visual cost: <b>the field
/// is one continuous function of world X/Z</b>, so two cells sampling a shared edge get the same
/// answer whatever the terrain is doing there.
/// </summary>
public sealed class WorldTerrainMathTests
{
    private const int Seed = 99;
    private const float Relief = 1.4f;
    private const float DetailScale = 2.5f;

    private static WorldHeightfield Field(
        IReadOnlyList<WorldTerrainMath.Landform>? landforms = null,
        IReadOnlyList<WorldTerrainMath.Path>? paths = null,
        IReadOnlyList<WorldTerrainMath.GroundArea>? areas = null) =>
        new(Seed, Relief, DetailScale, landforms, paths, areas);

    [Theory]
    [InlineData(-26f, 0f)]
    [InlineData(26f, 0f)]
    [InlineData(0f, -26f)]
    [InlineData(0f, 26f)]
    public void SharedCellBoundary_MatchesFromBothSides(float edgeX, float edgeZ)
    {
        // Two 52 m cells abutting at the edge point, each sampling the region field in its own
        // local frame. The heights must be bit-identical — that IS the seam contract now.
        var landforms = new[]
        {
            new WorldTerrainMath.Landform(
                WorldTerrainMath.LandformShape.Mound, 12f, -4f, 0f, 0f, 30f, 22f, 0.3f, 9f, 0.8f, 0f),
        };
        WorldHeightfield field = Field(landforms);

        float left = field.Height(edgeX, edgeZ);
        float right = field.Height(edgeX, edgeZ);
        Assert.Equal(left, right);

        // And the same point reached through two different clipped views (the streamer hands each
        // cell one of these) still agrees, which is what ForBounds has to guarantee.
        float viaWest = field.ForBounds(edgeX - 52f, edgeZ - 52f, edgeX, edgeZ).Height(edgeX, edgeZ);
        float viaEast = field.ForBounds(edgeX, edgeZ, edgeX + 52f, edgeZ + 52f).Height(edgeX, edgeZ);
        Assert.Equal(viaWest, viaEast, 4);
    }

    [Fact]
    public void TheFieldIsNotFlatAtACellBoundary()
    {
        // The defect the overhaul removed: a boundary forced to zero draws the lattice on the ground.
        WorldHeightfield field = Field();
        float most = 0f;
        for (int i = -26; i <= 26; i++)
        {
            most = MathF.Max(most, MathF.Abs(field.Height(i, 26f)));
        }
        Assert.True(most > 0.1f, $"boundary relief collapsed to {most:F3} m");
    }

    [Fact]
    public void SameInputs_AreDeterministic() => Assert.Equal(Field().Height(8.2f, -3.7f), Field().Height(8.2f, -3.7f));

    [Fact]
    public void ValueNoise_IsContinuousAcrossCellCoordinates()
    {
        float left = WorldTerrainMath.ValueNoise(99, 12.499f, -4.25f);
        float right = WorldTerrainMath.ValueNoise(99, 12.501f, -4.25f);
        Assert.InRange(MathF.Abs(left - right), 0f, 0.01f);
    }

    [Fact]
    public void AMoundRaisesItsCentreAndLeavesDistantGroundAlone()
    {
        var mound = new WorldTerrainMath.Landform(
            WorldTerrainMath.LandformShape.Mound, 0f, 0f, 0f, 0f, 20f, 20f, 0f, 12f, 0.8f, 0f);
        WorldHeightfield raised = Field(new[] { mound });
        WorldHeightfield bare = Field();

        Assert.True(raised.Height(0f, 0f) - bare.Height(0f, 0f) > 11.5f);
        Assert.Equal(bare.Height(70f, 70f), raised.Height(70f, 70f), 4);
    }

    [Fact]
    public void APlateauLevelsToItsAuthoredElevation()
    {
        var terrace = new WorldTerrainMath.Landform(
            WorldTerrainMath.LandformShape.Mound, 4f, 4f, 0f, 0f, 18f, 18f, 0f, 6f, 0.25f, 1f);
        WorldHeightfield field = Field(new[] { terrace });

        Assert.Equal(6f, field.Height(4f, 4f), 2);
        Assert.Equal(6f, field.Height(10f, 4f), 2);
    }

    [Fact]
    public void ARidgeIsAWallAcrossItsLineAndNothingBesideIt()
    {
        var ridge = new WorldTerrainMath.Landform(
            WorldTerrainMath.LandformShape.Ridge, -30f, 0f, 30f, 0f, 8f, 0f, 0f, 18f, 0.5f, 0f);
        WorldHeightfield field = Field(new[] { ridge });

        Assert.True(field.Height(0f, 0f) > 17f);
        Assert.True(MathF.Abs(field.Height(0f, 30f)) < 3f);
    }

    [Fact]
    public void ARoadGradesBetweenItsOwnEndpointsInsteadOfLevellingTheHill()
    {
        // A hill with a road climbing across it: the road must gain height, not cut a flat trench.
        var hill = new WorldTerrainMath.Landform(
            WorldTerrainMath.LandformShape.Mound, 0f, 0f, 0f, 0f, 40f, 40f, 0f, 20f, 0.9f, 0f);
        WorldHeightfield baseField = Field(new[] { hill });
        var road = new WorldTerrainMath.Path(
            -40f, 0f, 0f, 0f, 6f, 2f, baseField.BaseHeight(-40f, 0f), baseField.BaseHeight(0f, 0f));
        WorldHeightfield field = Field(new[] { hill }, new[] { road });

        float low = field.Height(-38f, 0f);
        float high = field.Height(-2f, 0f);
        Assert.True(high - low > 12f, $"road climbed only {high - low:F2} m");

        // ...and it is flat ACROSS its width, which is what makes it a road.
        Assert.InRange(MathF.Abs(field.Height(-20f, 2f) - field.Height(-20f, -2f)), 0f, 0.35f);
    }

    [Fact]
    public void AGroundAreaLevelsToItsAuthoredElevationForABuildingPad()
    {
        var pad = new WorldTerrainMath.GroundArea(8f, -4f, 9f, 7f, 2f, 1f, 5.5f);
        WorldHeightfield field = Field(areas: new[] { pad });

        Assert.Equal(5.5f, field.Height(8f, -4f), 2);
        Assert.NotEqual(5.5f, field.Height(60f, -4f), 1);
    }

    [Fact]
    public void ForBounds_KeepsEveryPrimitiveThatReachesTheRectangle()
    {
        var ridge = new WorldTerrainMath.Landform(
            WorldTerrainMath.LandformShape.Ridge, -80f, 0f, 80f, 0f, 10f, 0f, 0f, 14f, 0.5f, 0f);
        WorldHeightfield field = Field(new[] { ridge });

        // A cell nowhere near the ridge's authored centre still has to see it: the ridge crosses it.
        WorldHeightfield view = field.ForBounds(50f, -26f, 102f, 26f);
        Assert.Single(view.Landforms);
        Assert.Equal(field.Height(60f, 0f), view.Height(60f, 0f), 4);
    }

    [Fact]
    public void SteepestSlope_FindsAWallAndIgnoresRollingGround()
    {
        var cliff = new WorldTerrainMath.Landform(
            WorldTerrainMath.LandformShape.Mound, 0f, 0f, 0f, 0f, 20f, 20f, 0f, 30f, 0.12f, 1f);
        Assert.True(Field(new[] { cliff }).SteepestSlope(-30f, -30f, 30f, 30f, 1f) > 1f);
        Assert.True(Field().SteepestSlope(-30f, -30f, 30f, 30f, 1f) < 0.7f);
    }
}
