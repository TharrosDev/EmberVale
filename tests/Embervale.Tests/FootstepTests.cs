using Embervale.Player;
using Embervale.World;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// The 2026-09 footstep upgrade: animation-timed footfalls, gait-scaled volume and pitch, a detune
/// that never repeats, landings scaled by fall speed, wading, and a surface for ground nobody
/// tagged. Every rule here is pure; <see cref="FootstepComponent"/> only gathers its inputs.
/// </summary>
public class FootstepTests
{
    // --- When a footfall happens ---

    [Fact]
    public void AFootfallFiresWhenTheLowerFootChanges()
    {
        var gait = new FootstepGait { Margin = 0.03f, MinSpacing = 0.1f, FallbackDistance = 10f };

        // Left is standing when the character sets off: that is not a new contact.
        Assert.Equal(Footfall.None, gait.Step(0.2f, 0.10f, 0.25f));
        // Right swings down past it and takes the weight.
        Assert.Equal(Footfall.None, gait.Step(0.2f, 0.12f, 0.13f)); // level, inside the margin
        Assert.Equal(Footfall.Right, gait.Step(0.2f, 0.20f, 0.10f));
        Assert.Equal(Footfall.None, gait.Step(0.2f, 0.25f, 0.10f)); // still on the right
        Assert.Equal(Footfall.Left, gait.Step(0.2f, 0.10f, 0.22f));
    }

    [Fact]
    public void AnimatedFootfallsAlternateByConstruction()
    {
        var gait = new FootstepGait { MinSpacing = 0f, FallbackDistance = 0f };
        gait.Step(0.3f, 0f, 0.2f);
        Footfall previous = Footfall.Left;
        for (int i = 0; i < 6; i++)
        {
            bool leftDown = i % 2 == 1;
            Footfall f = gait.Step(0.3f, leftDown ? 0f : 0.2f, leftDown ? 0.2f : 0f);
            Assert.NotEqual(Footfall.None, f);
            Assert.NotEqual(previous, f);
            previous = f;
        }
    }

    [Fact]
    public void AStumbleInTheBlendCannotDoubleFire()
    {
        var gait = new FootstepGait { MinSpacing = 0.25f, FallbackDistance = 10f };
        gait.Step(0.3f, 0f, 0.2f);
        Assert.Equal(Footfall.Right, gait.Step(0.3f, 0.2f, 0f));
        // Flickers straight back a few centimetres later: too soon to be a real step.
        Assert.Equal(Footfall.None, gait.Step(0.05f, 0f, 0.2f));
    }

    [Fact]
    public void ARigWithNoFeetFallsBackToTheStrideAndStillAlternates()
    {
        var gait = new FootstepGait { FallbackDistance = 2f };
        Assert.Equal(Footfall.None, gait.Step(1.5f, null, null));
        Footfall first = gait.Step(0.6f, null, null);
        Assert.NotEqual(Footfall.None, first);
        Assert.Equal(Footfall.None, gait.Step(1.9f, null, null));
        Footfall second = gait.Step(0.2f, null, null);
        Assert.NotEqual(Footfall.None, second);
        Assert.NotEqual(first, second);
    }

    [Fact]
    public void ResetKeepsTheLastFootSoTheNextStepStillAlternates()
    {
        var gait = new FootstepGait { FallbackDistance = 1f };
        Footfall first = gait.Step(1f, null, null);
        gait.Reset();
        Assert.Equal(Footfall.None, gait.Step(0.5f, null, null)); // travelled distance was cleared
        Assert.NotEqual(first, gait.Step(0.5f, null, null));
    }

    // --- How loud, at what pitch ---

    [Fact]
    public void ASprintIsLouderAndBrighterThanAWalk()
    {
        float walk = FootstepAudio.Intensity(2f, 2.5f, 8f);
        float sprint = FootstepAudio.Intensity(9f, 2.5f, 8f);
        Assert.Equal(0f, walk, 4);
        Assert.Equal(1f, sprint, 4);
        Assert.Equal(-8f, FootstepAudio.VolumeDb(walk, -8f, 1f), 4);
        Assert.Equal(1f, FootstepAudio.VolumeDb(sprint, -8f, 1f), 4);
        Assert.True(FootstepAudio.Pitch(2, 5, 0.06f, sprint, 0.06f) >
                    FootstepAudio.Pitch(2, 5, 0.06f, walk, 0.06f));
    }

    [Fact]
    public void DetunesSpreadEvenlyAcrossTheJitter()
    {
        Assert.Equal(0.94f, FootstepAudio.Pitch(0, 5, 0.06f, 0f, 0f), 4);
        Assert.Equal(1.00f, FootstepAudio.Pitch(2, 5, 0.06f, 0f, 0f), 4);
        Assert.Equal(1.06f, FootstepAudio.Pitch(4, 5, 0.06f, 0f, 0f), 4);
        Assert.Equal(1f, FootstepAudio.Pitch(0, 1, 0.06f, 0f, 0f), 4);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.3)]
    [InlineData(0.6)]
    [InlineData(0.9999)]
    public void TheSameVariantNeverPlaysTwiceRunning(double sample)
    {
        for (int last = 0; last < 5; last++)
        {
            int next = FootstepAudio.NextVariant(last, 5, sample);
            Assert.NotEqual(last, next);
            Assert.InRange(next, 0, 4);
        }
    }

    [Fact]
    public void EveryOtherVariantIsReachable()
    {
        var seen = new System.Collections.Generic.HashSet<int>();
        for (int i = 0; i < 100; i++)
        {
            seen.Add(FootstepAudio.NextVariant(2, 5, i / 100d));
        }

        Assert.Equal(new[] { 0, 1, 3, 4 }, System.Linq.Enumerable.OrderBy(seen, v => v));
    }

    [Fact]
    public void NoPreviousVariantOrASingleOneIsHandled()
    {
        Assert.InRange(FootstepAudio.NextVariant(-1, 5, 0.99), 0, 4);
        Assert.Equal(0, FootstepAudio.NextVariant(0, 1, 0.5));
    }

    // --- Landings ---

    [Fact]
    public void AKerbDropIsNotALanding()
    {
        Assert.Null(FootstepAudio.LandingWeight(1.5f, 2.5f, 12f));
    }

    [Fact]
    public void ALandingIsHeavierTheFasterItFell()
    {
        float light = FootstepAudio.LandingWeight(3f, 2.5f, 12f)!.Value;
        float heavy = FootstepAudio.LandingWeight(11f, 2.5f, 12f)!.Value;
        Assert.True(heavy > light);
        Assert.Equal(1f, FootstepAudio.LandingWeight(40f, 2.5f, 12f)!.Value, 4);
        Assert.True(FootstepAudio.LandingPitch(heavy, 0.2f) < FootstepAudio.LandingPitch(light, 0.2f));
        Assert.Equal(0.8f, FootstepAudio.LandingPitch(1f, 0.2f), 4);
    }

    // --- Wading ---

    [Fact]
    public void FeetUnderTheWaterlineWade()
    {
        Assert.True(FootstepAudio.IsWading(10.3f, 10f, 0.08f));
        Assert.False(FootstepAudio.IsWading(10.05f, 10f, 0.08f)); // a wet shore, not a wade
        Assert.False(FootstepAudio.IsWading(null, 10f, 0.08f));   // no declared water here
    }

    [Fact]
    public void ABridgeOverTheRiverIsDry()
    {
        // The surface is compared with the FEET, not with the terrain under them, so planks above
        // the water stay planks.
        Assert.False(FootstepAudio.IsWading(10f, 11.5f, 0.08f));
    }

    // --- What surface ---

    [Theory]
    [InlineData(0.8f, 0f, 0.1f, SurfaceType.Snow)]
    [InlineData(0f, 0f, 1.3f, SurfaceType.Stone)]
    [InlineData(0f, 0.7f, 0.1f, SurfaceType.Stone)]
    [InlineData(0.1f, 0.1f, 0.2f, SurfaceType.Grass)]
    public void TerrainIsHeardAsItsBiomeAndSlope(float alpine, float barren, float slope, SurfaceType expected) =>
        Assert.Equal(expected, Surfaces.FromTerrain(alpine, barren, slope));

    [Theory]
    [InlineData("FloorShape", "wood")]
    [InlineData("CrateA", "wood")]
    [InlineData("Bridge", "wood")]
    [InlineData("RocksB", "stone")]
    [InlineData("RuinPillar", "stone")]
    [InlineData("HayA", "grass")]
    [InlineData("Collider", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void AColliderNameSaysWhatItIsMadeOf(string? name, string? expected) =>
        Assert.Equal(expected, Surfaces.TagFromName(name));

    [Fact]
    public void WaterHasItsOwnCue()
    {
        Assert.Equal(Surfaces.WaterCue, Surfaces.CueId(SurfaceType.Water));
        Assert.Equal(Surfaces.WaterCue, Surfaces.CueFromTag("shallows"));
        Assert.Equal("step.grass", Surfaces.CueFromTag("mud"));
    }
}
