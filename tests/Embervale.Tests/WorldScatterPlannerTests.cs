using System.Collections.Generic;
using Embervale.World;
using Xunit;

namespace Embervale.Tests;

public sealed class WorldScatterPlannerTests
{
    [Fact]
    public void SameSeed_ReproducesEveryPlacement()
    {
        IReadOnlyList<WorldScatterPlacement> first = Plan(71);
        IReadOnlyList<WorldScatterPlacement> second = Plan(71);

        Assert.Equal(first, second);
    }

    [Fact]
    public void DifferentSeed_ChangesTheLayout()
    {
        Assert.NotEqual(Plan(71), Plan(72));
    }

    [Fact]
    public void NorthSouthRoad_RemainsClear()
    {
        IReadOnlyList<WorldScatterPlacement> placements = WorldScatterPlanner.Plan(
            9, 80, 60f, 60f, 2f, 1, 8f, 3f, 0f);

        Assert.All(placements, point => Assert.True(System.MathF.Abs(point.X - 3f) >= 4f));
    }

    [Fact]
    public void LandmarkExclusion_RemainsClear()
    {
        var exclusions = new[] { new WorldScatterExclusion(4f, -3f, 9f) };
        IReadOnlyList<WorldScatterPlacement> placements = WorldScatterPlanner.Plan(
            19, 90, 80f, 80f, 2f, 0, 0f, 0f, 0f, exclusions);

        Assert.All(placements, point =>
        {
            float dx = point.X - 4f;
            float dz = point.Z + 3f;
            Assert.True((dx * dx) + (dz * dz) >= 81f);
        });
    }

    [Fact]
    public void MinimumSpacing_IsEnforced()
    {
        IReadOnlyList<WorldScatterPlacement> placements = WorldScatterPlanner.Plan(
            31, 60, 90f, 90f, 3f, 0, 0f, 0f, 5f);

        for (int i = 0; i < placements.Count; i++)
        {
            for (int j = i + 1; j < placements.Count; j++)
            {
                float dx = placements[i].X - placements[j].X;
                float dz = placements[i].Z - placements[j].Z;
                Assert.True((dx * dx) + (dz * dz) >= 25f);
            }
        }
    }

    [Fact]
    public void AuthoredCirculationAndActivityAreas_RemainClear()
    {
        var paths = new[] { new WorldTerrainMath.Path(-24f, 20f, 18f, -16f, 5f, 1.5f) };
        var areas = new[] { new WorldTerrainMath.GroundArea(9f, 7f, 8f, 6f, 2f, 0.7f) };
        IReadOnlyList<WorldScatterPlacement> placements = WorldScatterPlanner.Plan(
            51, 120, 70f, 70f, 2f, 0, 0f, 0f, 1f, null, paths, areas);

        Assert.All(placements, point =>
        {
            Assert.False(WorldTerrainMath.InsidePath(point.X, point.Z, paths[0], 0.25f));
            Assert.False(WorldTerrainMath.InsideGroundArea(point.X, point.Z, areas[0], 0.25f));
        });
    }

    private static IReadOnlyList<WorldScatterPlacement> Plan(int seed) =>
        WorldScatterPlanner.Plan(seed, 30, 60f, 50f, 3f, 2, 6f, 0f, 2.5f);
}
