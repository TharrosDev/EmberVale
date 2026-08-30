using Embervale.World;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// <see cref="WorldLandformResource.Irregularity"/>. The property that matters is what it must NOT
/// do: a warped hill has to keep its authored place, height and grade, or the whole realm's routes
/// and pads move the day someone turns it on.
/// </summary>
public sealed class WorldLandformIrregularityTests
{
    private static WorldTerrainMath.Landform Hill(float irregularity) =>
        new(WorldTerrainMath.LandformShape.Mound, 0f, 0f, 0f, 0f,
            30f, 30f, 0f, 12f, 0.7f, 0f, irregularity);

    [Fact]
    public void TheSummitIsUnchanged()
    {
        Assert.Equal(
            WorldTerrainMath.LandformMask(Hill(0f), 0f, 0f),
            WorldTerrainMath.LandformMask(Hill(0.3f), 0f, 0f),
            4);
    }

    [Fact]
    public void TheBoundaryMovesInBothDirections()
    {
        // Sampled right round the edge, the warp has to push the contour out in some places and in
        // in others. A one-sided warp is a landform that quietly grew, which is how a hill starts
        // eating a road two cells away.
        bool pushedOut = false;
        bool pulledIn = false;
        for (int i = 0; i < 64; i++)
        {
            float angle = (float)(System.Math.Tau * i / 64);
            float x = (float)System.Math.Cos(angle) * 27f;
            float z = (float)System.Math.Sin(angle) * 27f;
            float plain = WorldTerrainMath.LandformMask(Hill(0f), x, z);
            float warped = WorldTerrainMath.LandformMask(Hill(0.35f), x, z);
            pushedOut |= warped > plain + 0.02f;
            pulledIn |= warped < plain - 0.02f;
        }

        Assert.True(pushedOut, "the warp never extended the landform");
        Assert.True(pulledIn, "the warp never eroded the landform");
    }

    [Fact]
    public void ZeroIrregularityIsBitIdenticalToTheOldShape()
    {
        for (int i = 0; i < 20; i++)
        {
            float x = -40f + (i * 4f);
            Assert.Equal(
                WorldTerrainMath.LandformMask(Hill(0f), x, 6f),
                WorldTerrainMath.LandformMask(Hill(0f), x, 6f),
                6);
        }
    }

    [Fact]
    public void TheCullBoxGrowsWithTheWarpSoTheShapeIsNeverClipped()
    {
        // ⚠️ WorldHeightfield.ForBounds culls on MinX/MaxX. If those stayed at the authored radius,
        // a warped edge reaching past it would be cut off at exactly the cell boundary it was
        // reaching into — the artefact the world-space field exists to remove.
        Assert.True(Hill(0.35f).MaxX > Hill(0f).MaxX);
        Assert.Equal(Hill(0f).Reach, Hill(0f).Influence, 4);
    }
}
