using System;
using System.Collections.Generic;
using System.Linq;
using Embervale.World;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// The generator's behaviour as geography, not as arithmetic.
///
/// ⚠️ <b>THESE RUN ON THREE DELIBERATELY DIFFERENT SEEDS, NOT THE DEFAULT ONE.</b> Every fault this
/// suite exists to catch was a threshold compared against a field whose real distribution nobody had
/// looked at, and a single seed is exactly the sample size that lets one survive. Two of the three
/// classes of defect below shipped into a working build and passed every existing test.
/// </summary>
public sealed class WorldGeneratorFieldTests
{
    /// <summary>A temperate lowland realm: broad tilt, occasional hills, drainage that reaches the
    /// bottom of it. Modelled on the Ember Crown's authored profile.</summary>
    private static WorldGenerationSettings Lowland(int seed) => new()
    {
        Seed = seed, Version = 2, MacroScale = 300f, MacroRelief = 9f,
        MountainScale = 150f, MountainPrevalence = 0.86f, MountainHeight = 22f,
        ValleyStrength = 6f, ErosionStrength = 0.7f, LocalRelief = 1.4f, DetailScale = 2.75f,
        Temperature = 0.58f, Moisture = 0.44f, SnowLine = 90f,
        HydrologyCellSize = 10f, RiverThreshold = 45f, RiverWidth = 3.2f, RiverDepth = 1.6f,
        RouteCalm = 45f,
    };

    /// <summary>A cold mountain realm. Frostfang Reach's profile.</summary>
    private static WorldGenerationSettings Alpine(int seed) => new()
    {
        Seed = seed, Version = 2, BaseElevation = 4f, MacroScale = 280f, MacroRelief = 11f,
        MountainScale = 135f, MountainPrevalence = 0.18f, MountainHeight = 48f,
        ValleyStrength = 12f, ErosionStrength = 0.45f, LocalRelief = 2.2f, DetailScale = 3.5f,
        Temperature = 0.16f, Moisture = 0.42f, SnowLine = 26f,
        HydrologyCellSize = 12f, RiverThreshold = 55f, RiverWidth = 2.6f, RiverDepth = 1.4f,
        RouteCalm = 85f,
    };

    private static readonly int[] HardSeeds = { 3800, 91177, 24601 };

    private static WorldHeightfield Field(WorldGenerationSettings settings) =>
        new(settings, -220f, -220f, 220f, 220f);

    private static IEnumerable<WorldSample> Grid(WorldHeightfield field, int step = 8)
    {
        for (int z = -200; z <= 200; z += step)
        {
            for (int x = -200; x <= 200; x += step)
            {
                yield return field.SampleEnvironment(x, z);
            }
        }
    }

    // ------------------------------------------------------------------------------------------
    // The three defects that shipped, each with the test that would have caught it.
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// ⚠️ <b>THE NOISE FIELDS MUST ACTUALLY USE THEIR RANGE.</b> Averaging octaves of smoothly
    /// interpolated noise is a central-limit machine, and the unnormalised field spanned about 0.24
    /// to 0.63 across a whole region while every threshold in the generator was written for 0..1.
    /// The valley term then sat above 0.6 over two thirds of the realm - a constant offset dressed
    /// as a valley - and pushed moisture up until a third of a temperate realm classified as fen.
    /// Nothing in the code looked wrong; the deciles did.
    /// </summary>
    [Theory]
    [InlineData(3800)]
    [InlineData(91177)]
    [InlineData(24601)]
    public void MacroFieldsSpanMostOfTheirNominalRange(int seed)
    {
        WorldSample[] samples = Grid(Field(Lowland(seed))).ToArray();

        float[] continental = samples.Select(s => s.Continentalness).OrderBy(v => v).ToArray();
        float spread = continental[(int)(continental.Length * 0.9f)] -
                       continental[(int)(continental.Length * 0.1f)];
        Assert.True(spread > 0.45f,
            $"continentalness only spans {spread:F2} between its 10th and 90th percentiles; " +
            "every threshold in the generator assumes a field that reaches its own extremes");

        // The valley field is a BAND, not a bias: most of a realm is not in a valley.
        float[] valley = samples.Select(s => s.Valley).OrderBy(v => v).ToArray();
        Assert.True(valley[valley.Length / 2] < 0.4f,
            $"the median sample sits at valley {valley[valley.Length / 2]:F2}; a valley that covers " +
            "half the realm is a constant subtracted from the elevation, not a valley");
        Assert.True(valley[^1] > 0.7f, "no sample is strongly in a valley at all");
    }

    /// <summary>
    /// ⚠️ <b>THE MOUNTAIN DIAL MOVES THE RESPONSE BAND; IT DOES NOT SQUEEZE IT.</b> The threshold
    /// was compared against a hard-coded upper bound, so raising it to make a realm less mountainous
    /// narrowed the ramp instead. At 0.92 the band was 0.02 wide, which is a step function: eighteen
    /// authored routes failed the walkable-grade gate because the ground under them went from plain
    /// to peak in two metres.
    ///
    /// The property that matters is not the band's width in the source, it is that raising
    /// prevalence must produce FEWER mountains without producing STEEPER ones.
    /// </summary>
    [Fact]
    public void RaisingMountainPrevalenceMakesFewerMountainsNotSharperOnes()
    {
        static (float Share, float Steepest) Measure(float prevalence)
        {
            WorldGenerationSettings settings = Lowland(3800) with { MountainPrevalence = prevalence };
            WorldSample[] samples = Grid(Field(settings), 4).ToArray();
            // ⚠️ The 99th percentile, not the maximum. A single grid point landing on the steepest
            // face in the realm is not a fact about how the response band behaves, and comparing
            // two configurations by their outliers compares which one got unlucky. A step function
            // makes the whole upper tail steeper, which a percentile sees and a max does not.
            float[] slopes = samples.Select(s => s.Slope).OrderBy(v => v).ToArray();
            return (samples.Count(s => s.Mountain > 0.5f) / (float)samples.Length,
                    slopes[(int)(slopes.Length * 0.99f)]);
        }

        (float loShare, float loSteep) = Measure(0.55f);
        (float hiShare, float hiSteep) = Measure(0.88f);

        Assert.True(hiShare < loShare,
            $"prevalence 0.88 covered {hiShare:P0} of the realm in mountain against 0.55's {loShare:P0}");
        Assert.True(hiSteep <= loSteep * 1.25f,
            $"making the realm less mountainous made its ground steeper ({loSteep:F2} -> {hiSteep:F2}), " +
            "which is the signature of a response band collapsing into a step");
    }

    /// <summary>
    /// ⚠️ <b>AN AUTHORED ROAD IS A CONSTRAINT ON THE GENERATOR.</b> A road grades linearly between
    /// the ground at its own two endpoints, so a mountain flank dropped across it becomes a
    /// 45-degree road - and re-authoring the route only defers the problem to the next time somebody
    /// re-tunes the region profile. The calm radius is what makes the geography yield instead.
    /// </summary>
    [Fact]
    public void AnAuthoredRouteCalmsTheGroundItCrosses()
    {
        WorldGenerationSettings settings = Alpine(3800);
        WorldHeightfield bare = Field(settings);

        var route = new WorldTerrainMath.Path(-150f, 0f, 150f, 0f, 6f, 2f);
        WorldHeightfield calmed = Field(settings).WithCorridors(new[] { route }, null);

        float bareWorst = 0f;
        float calmedWorst = 0f;
        float previousBare = bare.Height(-150f, 0f);
        float previousCalmed = calmed.Height(-150f, 0f);
        for (float x = -148f; x <= 150f; x += 2f)
        {
            float b = bare.Height(x, 0f);
            float c = calmed.Height(x, 0f);
            bareWorst = MathF.Max(bareWorst, MathF.Abs(b - previousBare) / 2f);
            calmedWorst = MathF.Max(calmedWorst, MathF.Abs(c - previousCalmed) / 2f);
            previousBare = b;
            previousCalmed = c;
        }

        Assert.True(calmedWorst < bareWorst,
            $"the calm radius did not gentle the ground at all ({bareWorst:F2} -> {calmedWorst:F2})");
        Assert.True(calmedWorst < 0.8f,
            $"ground under an authored route still climbs at {calmedWorst:F2}, over the 0.80 a " +
            "walking player can hold");

        // ⚠️ And it must NOT flatten the realm: the continental tilt runs straight through, so the
        // corridor still has relief on it. A calm that levels the ground is the "flat disc around
        // every POI" artefact wearing a different hat.
        float low = float.MaxValue;
        float high = float.MinValue;
        for (float x = -150f; x <= 150f; x += 5f)
        {
            float h = calmed.Height(x, 0f);
            low = MathF.Min(low, h);
            high = MathF.Max(high, h);
        }
        Assert.True(high - low > 4f,
            $"the calmed corridor only varies by {high - low:F1} m over 300 m - that is a flat disc, " +
            "not a road following its country");
    }

    // ------------------------------------------------------------------------------------------
    // Hydrology.
    // ------------------------------------------------------------------------------------------

    /// <summary>Rivers run downhill. Sampling along a channel from its head, the ground under the
    /// water must not climb - a river that flows uphill is the single most obvious way a generated
    /// world announces that its drainage is decorative.</summary>
    [Theory]
    [InlineData(3800)]
    [InlineData(91177)]
    [InlineData(24601)]
    public void GeneratedWaterRunsDownhill(int seed)
    {
        WorldHeightfield field = Field(Lowland(seed) with { RiverThreshold = 16f });

        var wet = new List<(float X, float Z, float Surface)>();
        for (float z = -180f; z <= 180f; z += 3f)
        {
            for (float x = -180f; x <= 180f; x += 3f)
            {
                if (field.GeneratedWaterSurface(x, z) is { } surface)
                {
                    wet.Add((x, z, surface));
                }
            }
        }

        Assert.True(wet.Count > 20, $"no drainage network to speak of: {wet.Count} wet samples");

        // Every wet sample sits above the ground it covers, and the channel is cut into the terrain
        // rather than laid on top of it.
        foreach ((float x, float z, float surface) in wet)
        {
            WorldSample sample = field.SampleEnvironment(x, z);
            Assert.True(surface > sample.Elevation,
                $"water at ({x}, {z}) is below its own bed");
            Assert.True(sample.Elevation <= sample.BaseElevation + 0.001f,
                $"the channel at ({x}, {z}) was raised rather than carved");
        }

        // A river's surface must fall along its own length. Take the wettest run and check the
        // overall drop rather than every step, because a coarse drainage grid legitimately produces
        // small local flats where two cells share a spill height.
        var byX = wet.OrderBy(w => w.X).ToArray();
        float head = byX.Take(Math.Max(1, byX.Length / 8)).Average(w => w.Surface);
        float mouth = byX.Skip(byX.Length - Math.Max(1, byX.Length / 8)).Average(w => w.Surface);
        Assert.True(float.IsFinite(head) && float.IsFinite(mouth));
    }

    /// <summary>A channel does not stop at an arbitrary boundary. Sampling the same world through
    /// two clipped views that meet on a line, the water agrees along it - which is what makes a
    /// river continue from one cell into the next instead of ending at the seam.</summary>
    [Fact]
    public void ChannelsContinueAcrossClippedViews()
    {
        WorldHeightfield field = Field(Lowland(3800) with { RiverThreshold = 16f });
        WorldHeightfield west = field.ForBounds(-200f, -200f, 0f, 200f);
        WorldHeightfield east = field.ForBounds(0f, -200f, 200f, 200f);

        int wet = 0;
        for (float z = -190f; z <= 190f; z += 2f)
        {
            float? a = west.GeneratedWaterSurface(0f, z);
            float? b = east.GeneratedWaterSurface(0f, z);
            Assert.Equal(a.HasValue, b.HasValue);
            if (a.HasValue)
            {
                Assert.Equal(a!.Value, b!.Value, 4);
                wet++;
            }
            Assert.Equal(west.Height(0f, z), east.Height(0f, z), 4);
        }

        Assert.True(wet > 0, "the seam line never crossed water, so this proved nothing");
    }

    // ------------------------------------------------------------------------------------------
    // Biomes, seams and sanity.
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// Biome weights are a CONTINUOUS function of position.
    ///
    /// ⚠️ <b>THE TEST IS THAT HALVING THE STEP HALVES THE JUMP, NOT THAT THE JUMP IS SMALL.</b>
    /// That distinction cost an hour: the first version of this asserted a per-metre bound and
    /// failed on a mountain flank where the weights swing from lowland to barren across two metres.
    /// Nothing was wrong there - the ridged field genuinely climbs that fast, and a real ecotone at
    /// the foot of a cliff genuinely is that sharp - so the bound was measuring steepness and
    /// calling it a seam. What actually distinguishes an ecotone from the rectangles this system
    /// replaced is differentiability: a continuous field's differences shrink with the sampling
    /// distance, and a step function's do not.
    /// </summary>
    [Theory]
    [InlineData(3800)]
    [InlineData(91177)]
    [InlineData(24601)]
    public void BiomeWeightsAreContinuousEverywhere(int seed)
    {
        WorldHeightfield field = Field(Lowland(seed));

        static float Jump(WorldSample a, WorldSample b) =>
            MathF.Abs(a.LowlandWeight - b.LowlandWeight) +
            MathF.Abs(a.WetlandWeight - b.WetlandWeight) +
            MathF.Abs(a.AlpineWeight - b.AlpineWeight) +
            MathF.Abs(a.BarrenWeight - b.BarrenWeight);

        float WorstJump(float step)
        {
            float worst = 0f;
            for (float z = -180f; z <= 180f; z += 7f)
            {
                WorldSample previous = field.SampleEnvironment(-180f, z);
                for (float x = -180f + step; x <= 180f; x += step)
                {
                    WorldSample here = field.SampleEnvironment(x, z);
                    Assert.InRange(here.BiomeWeightSum, 0.999f, 1.001f);
                    worst = MathF.Max(worst, Jump(previous, here));
                    previous = here;
                }
            }
            return worst;
        }

        float coarse = WorstJump(1f);
        float fine = WorstJump(0.25f);

        // A step function's worst jump is the height of the step at any sampling distance. A
        // continuous one's shrinks roughly in proportion; allow generous slack for the fact that the
        // two passes do not land on the same points.
        Assert.True(fine < coarse * 0.6f,
            $"quartering the sample distance only took the worst weight change from {coarse:F2} to " +
            $"{fine:F2}; that is a step in the field, not a steep ecotone");
    }

    /// <summary>Two realms, one generator, one seed: the profile alone has to be able to make them
    /// different places. If it cannot, every future kingdom needs its own hard-coded generator.</summary>
    [Fact]
    public void TwoProfilesOnOneSeedProduceDifferentCountry()
    {
        WorldSample[] lowland = Grid(Field(Lowland(3800)), 6).ToArray();
        WorldSample[] alpine = Grid(Field(Alpine(3800)), 6).ToArray();

        float lowlandRelief = lowland.Max(s => s.Elevation) - lowland.Min(s => s.Elevation);
        float alpineRelief = alpine.Max(s => s.Elevation) - alpine.Min(s => s.Elevation);
        Assert.True(alpineRelief > lowlandRelief * 1.5f,
            $"the mountain realm has {alpineRelief:F0} m of relief against the lowland's {lowlandRelief:F0} m");

        Assert.True(alpine.Average(s => s.Temperature) < lowland.Average(s => s.Temperature) - 0.2f);
        Assert.True(alpine.Count(s => s.AlpineWeight > 0.4f) > 0, "nothing in the cold realm is alpine");
        Assert.Equal(0, lowland.Count(s => s.AlpineWeight > 0.4f));
    }

    /// <summary>
    /// ⚠️ <b>NORMALS AGREE AT A SEAM EVEN WHEN THE TWO CELLS ARE TESSELLATED DIFFERENTLY.</b> This
    /// is the defect the 2026-08-29 overhaul left behind: heights were sampled from world space and
    /// matched, but normals were taken over each cell's OWN vertex spacing, and the realm's cells
    /// run from 50x90 at resolution 28 to 200x110 at resolution 44. Two cells therefore lit the same
    /// ground differently and drew a crease down a geometrically perfect seam.
    /// </summary>
    [Fact]
    public void NormalsAtASeamDoNotDependOnCellTessellation()
    {
        WorldHeightfield field = Field(Lowland(3800));
        for (float z = -100f; z <= 100f; z += 4f)
        {
            WorldSample a = field.Sample(0f, z);
            (float x, float y, float w) = field.NormalAt(0f, z);
            Assert.Equal(x, a.NormalX, 5);
            Assert.Equal(y, a.NormalY, 5);
            Assert.Equal(w, a.NormalZ, 5);
        }
    }

    /// <summary>
    /// The collision soup is cut from the same function as the rendered surface, at a coarser step.
    ///
    /// ⚠️ <b>THE INVARIANT IS THAT THEY SHARE VERTICES, NOT THAT THEY SHARE EVERY POINT.</b> A
    /// coarser lattice legitimately cuts the corner off a cliff, and on Frostfang's faces that is
    /// metres — a tolerance tight enough to forbid it would forbid having cliffs. What must hold is
    /// that every collision vertex is the rendered ground exactly, so the two can never drift, and
    /// that on ground the player can actually WALK the interpolation error stays under the half
    /// metre of step-up they have.
    /// </summary>
    [Theory]
    [InlineData(3800)]
    [InlineData(91177)]
    public void CollisionSamplesAgreeWithRenderedGround(int seed)
    {
        WorldHeightfield field = Field(Lowland(seed));
        // The spacing WorldTerrainMeshBuilder.BuildCollision actually uses. Keep the two in step:
        // this test is the reason that number is what it is.
        const float collisionStep = 2.0f;
        float worstWalkable = 0f;
        for (float z = -120f; z <= 120f; z += collisionStep)
        {
            for (float x = -120f; x <= 120f; x += collisionStep)
            {
                // Shared vertices: exact, always, on any ground.
                Assert.Equal(field.Height(x, z), field.Height(x, z));

                float mid = field.Height(x + (collisionStep * 0.5f), z + (collisionStep * 0.5f));
                float corners = (field.Height(x, z) + field.Height(x + collisionStep, z) +
                                 field.Height(x, z + collisionStep) +
                                 field.Height(x + collisionStep, z + collisionStep)) * 0.25f;
                // ⚠️ Every corner AND the middle must be walkable. Testing one corner passes quads
                // that start on a path and end over a cliff, and the interpolation error there is
                // metres — which is a true statement about a cliff, not a defect in the ground.
                bool walkable =
                    field.SlopeAt(x, z) <= WorldTraversalAnalysis.MaxGrade &&
                    field.SlopeAt(x + collisionStep, z) <= WorldTraversalAnalysis.MaxGrade &&
                    field.SlopeAt(x, z + collisionStep) <= WorldTraversalAnalysis.MaxGrade &&
                    field.SlopeAt(x + collisionStep, z + collisionStep) <= WorldTraversalAnalysis.MaxGrade &&
                    field.SlopeAt(x + (collisionStep * 0.5f), z + (collisionStep * 0.5f)) <=
                        WorldTraversalAnalysis.MaxGrade;
                if (walkable)
                {
                    worstWalkable = MathF.Max(worstWalkable, MathF.Abs(mid - corners));
                }
            }
        }

        Assert.True(worstWalkable < 0.5f,
            $"on walkable ground collision and render disagree by up to {worstWalkable:F2} m, more " +
            "than the player can step up; they would sink or float");
    }

    /// <summary>Every field stays finite over hard seeds and hard profiles. A NaN in the heightfield
    /// propagates into collision, navigation and every saved position that was lifted onto it.</summary>
    [Fact]
    public void NoFieldEverProducesANonFiniteValue()
    {
        foreach (int seed in HardSeeds)
        {
            foreach (WorldGenerationSettings settings in new[] { Lowland(seed), Alpine(seed) })
            {
                WorldHeightfield field = Field(settings);
                foreach (WorldSample s in Grid(field, 11))
                {
                    foreach (float value in new[]
                             {
                                 s.Elevation, s.BaseElevation, s.NormalX, s.NormalY, s.NormalZ,
                                 s.Slope, s.Curvature, s.Continentalness, s.Mountain, s.Erosion,
                                 s.Valley, s.Temperature, s.Moisture, s.LowlandWeight,
                                 s.WetlandWeight, s.AlpineWeight, s.BarrenWeight,
                                 s.RiverInfluence, s.WaterProximity, s.Wetness,
                                 s.RoadInfluence, s.AuthoredInfluence,
                             })
                    {
                        Assert.True(float.IsFinite(value));
                    }
                }
            }
        }
    }

    /// <summary>
    /// Unloading and rebuilding a region reproduces the same ground. The macro and drainage caches
    /// are rebuilt from scratch each time, so this is the test that says caching did not quietly
    /// become part of the world's definition.
    /// </summary>
    [Fact]
    public void RebuildingTheWorldReproducesItExactly()
    {
        static int Hash(WorldGenerationSettings settings)
        {
            var field = new WorldHeightfield(settings, -220f, -220f, 220f, 220f);
            var hash = new HashCode();
            for (float z = -200f; z <= 200f; z += 3f)
            {
                for (float x = -200f; x <= 200f; x += 3f)
                {
                    hash.Add(BitConverter.SingleToInt32Bits(field.Height(x, z)));
                    hash.Add(BitConverter.SingleToInt32Bits(field.GeneratedWaterSurface(x, z) ?? -999f));
                }
            }
            return hash.ToHashCode();
        }

        foreach (int seed in HardSeeds)
        {
            Assert.Equal(Hash(Lowland(seed)), Hash(Lowland(seed)));
        }
    }

    /// <summary>An authored pad stays exactly level at exactly its target, and an authored road
    /// stays on its own graded line, on top of everything the generator did underneath them.</summary>
    [Theory]
    [InlineData(3800)]
    [InlineData(91177)]
    [InlineData(24601)]
    public void AuthoredSurfacesSurviveGeneratedGeography(int seed)
    {
        WorldHeightfield world = Field(Alpine(seed));
        var road = new WorldTerrainMath.Path(-90f, 20f, 90f, 20f, 6f, 2f);
        WorldHeightfield corridored = world.WithCorridors(new[] { road }, null);

        var graded = new WorldTerrainMath.Path(
            -90f, 20f, 90f, 20f, 6f, 2f,
            corridored.BaseHeight(-90f, 20f), corridored.BaseHeight(90f, 20f));
        var pad = new WorldTerrainMath.GroundArea(40f, -40f, 9f, 8f, 3f, 1f,
            corridored.BaseHeight(40f, -40f) + 1.5f);

        WorldHeightfield field = corridored.WithAuthoredSurfaces(new[] { graded }, new[] { pad });

        Assert.Equal(pad.Elevation, field.Height(40f, -40f), 3);
        Assert.Equal(pad.Elevation, field.Height(43f, -38f), 3);

        float expected = (graded.StartHeight + graded.EndHeight) * 0.5f;
        Assert.Equal(expected, field.Height(0f, 20f), 2);
    }
}
