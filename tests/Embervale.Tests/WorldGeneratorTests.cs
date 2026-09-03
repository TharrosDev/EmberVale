using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Embervale.World;
using Xunit;

namespace Embervale.Tests;

public sealed class WorldGeneratorTests
{
    private static readonly WorldGenerationSettings Settings = new()
    {
        Seed = 72581, Version = 2, MacroScale = 360f, MacroRelief = 9f,
        MountainScale = 180f, MountainHeight = 24f, ValleyStrength = 7f,
        LocalRelief = 1.8f, HydrologyCellSize = 10f, RiverThreshold = 8f,
    };

    private static WorldHeightfield Field() => new(Settings, -220f, -220f, 220f, 220f);

    [Fact]
    public void SameSeedVersionAndCoordinates_AreBitDeterministic()
    {
        WorldSample a = Field().Sample(37.25f, -81.5f);
        WorldSample b = Field().Sample(37.25f, -81.5f);
        Assert.Equal(a, b);
        Assert.Equal(Settings.Signature, (Settings with { }).Signature);
    }

    [Fact]
    public void GenerationOrder_DoesNotChangeSamples()
    {
        var points = new[] { (-120f, 91f), (12f, 8f), (179f, -143f), (-7f, -199f) };
        WorldHeightfield forwardField = Field();
        WorldSample[] forward = points.Select(p => forwardField.Sample(p.Item1, p.Item2)).ToArray();
        WorldHeightfield reverseField = Field();
        WorldSample[] reverse = points.Reverse().Select(p => reverseField.Sample(p.Item1, p.Item2)).Reverse().ToArray();
        Assert.Equal(forward, reverse);
    }

    [Fact]
    public async Task WorkerAndSynchronousGeneration_AreEquivalent()
    {
        WorldHeightfield field = Field();
        WorldSample expected = field.Sample(-43.5f, 117.75f);
        WorldSample actual = await Task.Run(() => field.Sample(-43.5f, 117.75f));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ClippedViews_ShareHeightsNormalsAndHydrologyAtSeams()
    {
        WorldHeightfield field = Field();
        WorldHeightfield west = field.ForBounds(-100f, -100f, 0f, 100f);
        WorldHeightfield east = field.ForBounds(0f, -100f, 100f, 100f);
        for (int z = -100; z <= 100; z += 10)
        {
            Assert.Equal(west.Height(0f, z), east.Height(0f, z));
            Assert.Equal(west.NormalAt(0f, z), east.NormalAt(0f, z));
            Assert.Equal(west.GeneratedWaterSurface(0f, z), east.GeneratedWaterSurface(0f, z));
        }
    }

    [Fact]
    public void SamplesAreFiniteAndBiomeWeightsAreNormalised()
    {
        WorldHeightfield field = Field();
        for (int z = -200; z <= 200; z += 13)
        for (int x = -200; x <= 200; x += 13)
        {
            WorldSample s = field.Sample(x, z);
            foreach (float value in new[] { s.Elevation, s.BaseElevation, s.NormalX, s.NormalY,
                         s.NormalZ, s.Slope, s.Curvature, s.Temperature, s.Moisture,
                         s.RiverInfluence, s.Wetness })
                Assert.True(float.IsFinite(value));
            Assert.InRange(s.BiomeWeightSum, 0.9999f, 1.0001f);
            Assert.InRange(s.Temperature, 0f, 1f);
            Assert.InRange(s.Moisture, 0f, 1f);
        }
    }

    [Fact]
    public void MacroTerrainContainsDistinctLandformRegimes()
    {
        WorldHeightfield field = Field();
        var samples = new List<WorldSample>();
        for (int z = -200; z <= 200; z += 10)
        for (int x = -200; x <= 200; x += 10)
            samples.Add(field.SampleEnvironment(x, z));
        Assert.True(samples.Max(s => s.Elevation) - samples.Min(s => s.Elevation) > 12f);
        Assert.True(samples.Max(s => s.Mountain) > 0.45f);
        Assert.True(samples.Max(s => s.Valley) > 0.45f);
    }

    [Fact]
    public void HydrologyCarvesGroundAndProducesContinuousWaterSegments()
    {
        WorldHeightfield field = Field();
        int wet = 0;
        for (int z = -200; z <= 200; z += 4)
        for (int x = -200; x <= 200; x += 4)
        {
            WorldSample s = field.SampleEnvironment(x, z);
            if (s.RiverInfluence > 0.5f)
            {
                wet++;
                Assert.True(s.Elevation < s.BaseElevation);
                float? surface = field.GeneratedWaterSurface(x, z);
                Assert.NotNull(surface);
                Assert.True(surface > s.Elevation);
            }
        }
        Assert.True(wet > 8, $"expected a drainage network, found {wet} wet samples");
    }

    [Fact]
    public void AuthoredRoadsAndPadsRemainExactConstraints()
    {
        WorldHeightfield baseField = Field();
        var road = new WorldTerrainMath.Path(-80f, 0f, 80f, 0f, 6f, 2f,
            baseField.BaseHeight(-80f, 0f), baseField.BaseHeight(80f, 0f));
        var pad = new WorldTerrainMath.GroundArea(30f, 30f, 9f, 8f, 3f, 1f, 5.25f);
        WorldHeightfield field = baseField.WithAuthoredSurfaces(new[] { road }, new[] { pad });
        Assert.Equal(5.25f, field.Height(30f, 30f), 3);
        float expectedRoad = (road.StartHeight + road.EndHeight) * 0.5f;
        Assert.Equal(expectedRoad, field.Height(0f, 0f), 3);
    }

    [Fact]
    public void FixedChunkHashChangesWithGeneratorVersionButNotReload()
    {
        static int Hash(WorldHeightfield field)
        {
            var hash = new HashCode();
            for (int z = -64; z <= 64; z += 4)
            for (int x = -64; x <= 64; x += 4)
                hash.Add(BitConverter.SingleToInt32Bits(field.Height(x, z)));
            return hash.ToHashCode();
        }
        int first = Hash(Field());
        Assert.Equal(first, Hash(Field()));
        var next = Settings with { Version = 3 };
        Assert.NotEqual(first, Hash(new WorldHeightfield(next, -220f, -220f, 220f, 220f)));
    }
}
