using System.Collections.Generic;
using Embervale.World;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// The off-route traversal gate. These pin the one judgement the analysis makes that a human could
/// not: that walking DOWN and walking UP are different edges, so a bowl with steep sides is a trap
/// and the same bowl with one ramp is not.
/// </summary>
public sealed class WorldTraversalAnalysisTests
{
    private const int Seed = 4242;

    /// <summary>Flat ground with no noise, so a test's landforms are the only geography in it.</summary>
    private static WorldHeightfield Field(params WorldTerrainMath.Landform[] landforms) =>
        new(Seed, 0f, 1f, landforms);

    private static WorldTerrainMath.Landform Bowl(float radius, float depth, float falloff) =>
        new(WorldTerrainMath.LandformShape.Mound, 0f, 0f, 0f, 0f,
            radius, radius, 0f, depth, falloff, 1f);

    /// <summary>⚠️ A wall so sheer that a sample-to-sample drop exceeds
    /// <see cref="WorldTraversalAnalysis.FallAllowance"/> is NOT a trap and these tests must not use
    /// one: the player who goes over it is falling to their death, and death already recovers them.
    /// A trap is specifically a hole with a SURVIVABLE way in and no way out.</summary>
    private static WorldTraversalAnalysis.Region Area =>
        new(-60f, -60f, 60f, 60f);

    [Fact]
    public void FlatGroundHasNoTrapsAndNoPockets()
    {
        WorldTraversalAnalysis.Result result =
            WorldTraversalAnalysis.Analyse(Field(), Area, (50f, 50f));

        Assert.True(result.Clean);
        Assert.Empty(result.Pockets);
    }

    [Fact]
    public void ASteepSidedPitIsATrap()
    {
        // 18 m deep over a 12 m transition: a 1.5 grade. Each 3 m sample drops 4.5 m — inside
        // FallAllowance, so the player gets down there; well past the 2.1 m climb limit, so they do
        // not get back up.
        WorldTraversalAnalysis.Result result = WorldTraversalAnalysis.Analyse(
            Field(Bowl(radius: 24f, depth: -18f, falloff: 0.5f)), Area, (55f, 55f));

        Assert.NotEmpty(result.Traps);
        WorldTraversalAnalysis.Patch trap = result.Traps[0];
        Assert.InRange(trap.CentreX, -6f, 6f);
        Assert.InRange(trap.CentreZ, -6f, 6f);
        Assert.True(trap.LowestY < -15f, $"expected a deep floor, got {trap.LowestY}");
        Assert.False(trap.Flooded);
    }

    [Fact]
    public void TheSamePitIsNotATrapWhenItsSidesAreWalkable()
    {
        // 8 m deep over 22.8 m: a 0.35 grade, comfortably under the 0.7 a player can climb.
        WorldTraversalAnalysis.Result result = WorldTraversalAnalysis.Analyse(
            Field(Bowl(radius: 24f, depth: -8f, falloff: 0.95f)), Area, (55f, 55f));

        Assert.True(result.Clean, string.Join("; ", result.Traps));
    }

    [Fact]
    public void ADeclaredWaterBodyTurnsATrapIntoAFloodedBasinAndStopsBeingReported()
    {
        var water = new List<WorldWater.Body> { new(0f, 0f, 30f, 30f, -2f) };
        WorldTraversalAnalysis.Result result = WorldTraversalAnalysis.Analyse(
            Field(Bowl(radius: 24f, depth: -18f, falloff: 0.5f)), Area, (55f, 55f), water);

        // ⚠️ The basin is still unclimbable; it is simply WorldRecovery's problem rather than the
        // author's. A rule that failed here would forbid lakes.
        Assert.True(result.Clean, string.Join("; ", result.Traps));
    }

    [Fact]
    public void RimHeightIsReportedSoTheValidatorCanTellAFallFromAStroll()
    {
        WorldTraversalAnalysis.Result deep = WorldTraversalAnalysis.Analyse(
            Field(Bowl(radius: 24f, depth: -18f, falloff: 0.5f)), Area, (55f, 55f));
        // A 2.8 m step down with almost no transition: too tall to climb (the limit is 2.1 m per
        // 3 m sample) and far too short to be a fall the player would have seen coming.
        WorldTraversalAnalysis.Result shallow = WorldTraversalAnalysis.Analyse(
            Field(Bowl(radius: 24f, depth: -2.8f, falloff: 0.02f)), Area, (55f, 55f));

        Assert.True(deep.Traps[0].DeepestDrop > 8f, $"{deep.Traps[0]}");
        Assert.NotEmpty(shallow.Traps);
        Assert.True(shallow.Traps[0].DeepestDrop < 3f, $"{shallow.Traps[0]}");
    }
}
