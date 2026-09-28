using System;
using System.Collections.Generic;
using Embervale.Movement;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// Where a whistled horse comes from and how it runs in (the Movement upgrade).
/// </summary>
public class MountWhistleTests
{
    [Fact]
    public void TheFirstCandidateIsDirectlyBehindTheRider()
    {
        // Facing -Z (yaw 0), behind is +Z.
        IReadOnlyList<(float X, float Z)> points = MountWhistle.Candidates(0f, 0f, 0f, 18f, 8);

        Assert.Equal(0f, points[0].X, 3);
        Assert.Equal(18f, points[0].Z, 3);
    }

    [Fact]
    public void TheLastCandidateIsStraightAhead()
    {
        IReadOnlyList<(float X, float Z)> points = MountWhistle.Candidates(0f, 0f, 0f, 18f, 8);

        Assert.Equal(0f, points[^1].X, 3);
        Assert.Equal(-18f, points[^1].Z, 3);
    }

    [Fact]
    public void EveryCandidateIsAtTheWhistleDistanceAndNoneRepeat()
    {
        IReadOnlyList<(float X, float Z)> points = MountWhistle.Candidates(5f, -3f, 1.1f, 18f, 8);

        Assert.Equal(8, points.Count);
        var seen = new HashSet<(int, int)>();
        foreach ((float x, float z) in points)
        {
            float dx = x - 5f, dz = z + 3f;
            Assert.Equal(18f, MathF.Sqrt((dx * dx) + (dz * dz)), 3);
            Assert.True(seen.Add(((int)MathF.Round(x * 10f), (int)MathF.Round(z * 10f))), "a candidate repeats");
        }
    }

    [Fact]
    public void TheFlanksComeBeforeTheFront()
    {
        IReadOnlyList<(float X, float Z)> points = MountWhistle.Candidates(0f, 0f, 0f, 10f, 8);

        // Candidates 1 and 2 are the rear quarters: still behind the rider (Z > 0).
        Assert.True(points[1].Z > 0f && points[2].Z > 0f);
        Assert.True(points[1].X * points[2].X < 0f, "the pair sits on either side");
    }

    [Fact]
    public void TheRunInNeverOvershootsTheRider()
    {
        (float x, float z, float remaining) = MountWhistle.Approach(0f, 0f, 0f, 1f, 13f, 3f, 7f, 1f);

        Assert.Equal(0f, x, 4);
        Assert.Equal(1f, z, 4);
        Assert.Equal(0f, remaining, 4);
    }

    [Fact]
    public void TheHorsePullsUpAsItArrives()
    {
        float far = 20f - MountWhistle.Approach(0f, 0f, 0f, 20f, 13f, 3f, 7f, 0.1f).Remaining;
        float near = 2f - MountWhistle.Approach(0f, 0f, 0f, 2f, 13f, 3f, 7f, 0.1f).Remaining;

        Assert.Equal(1.3f, far, 3);   // full gallop outside the slow radius
        Assert.True(near < far && near >= 0.3f - 0.0001f, "eases toward the walk pace near the rider");
    }

    [Fact]
    public void ABadFrameDoesNotMoveTheHorse()
    {
        (float x, float z, float remaining) = MountWhistle.Approach(1f, 2f, 5f, 2f, 13f, 3f, 7f, float.NaN);

        Assert.Equal(1f, x);
        Assert.Equal(2f, z);
        Assert.Equal(4f, remaining, 4);
    }
}
