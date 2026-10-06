using System;
using System.Collections.Generic;
using Embervale.Magic.Vfx;
using Godot;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// A lightning bolt's shape is generated, seeded, and rebuilt every frame while a beam's ends move.
/// So it has to be the same shape for the same seed (or a held beam would boil), it has to start and
/// end exactly where it was told (or a chain would miss the body it arcs to), and it must not wander
/// further from its line than the bound the renderer's culling and the eye both assume.
/// </summary>
public class VfxBoltPathTests
{
    private static readonly Vector3 From = new(2f, 1f, -3f);
    private static readonly Vector3 To = new(11f, 4f, 6f);

    private static List<Vector3> Path(int segments, float jitter, int seed, Vector3? from = null, Vector3? to = null)
    {
        var points = new List<Vector3>();
        VfxBoltPath.Generate(from ?? From, to ?? To, segments, jitter, seed, points);
        return points;
    }

    private static float DistanceFromLine(Vector3 point, Vector3 from, Vector3 to)
    {
        Vector3 axis = (to - from).Normalized();
        Vector3 offset = point - from;
        return (offset - (axis * offset.Dot(axis))).Length();
    }

    [Theory]
    [InlineData(6)]
    [InlineData(8)]
    [InlineData(12)]
    [InlineData(16)]
    [InlineData(24)]
    public void ABoltHasOnePointMoreThanItsSegments(int segments)
    {
        Assert.Equal(segments + 1, Path(segments, 0.1f, 7).Count);
    }

    [Theory]
    [InlineData(6, 1)]
    [InlineData(12, 99)]
    [InlineData(24, -5)]
    [InlineData(5, 123456)]
    public void TheEndsAreExactlyWhereTheyWereAsked(int segments, int seed)
    {
        List<Vector3> points = Path(segments, 0.15f, seed);

        Assert.Equal(From, points[0]);
        Assert.Equal(To, points[^1]);
    }

    [Fact]
    public void TheSameSeedIsTheSameBolt()
    {
        List<Vector3> first = Path(16, 0.11f, 4242);
        List<Vector3> second = Path(16, 0.11f, 4242);

        Assert.Equal(first, second);
    }

    [Fact]
    public void ADifferentSeedIsADifferentBolt()
    {
        List<Vector3> first = Path(16, 0.11f, 1);
        List<Vector3> second = Path(16, 0.11f, 2);

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void ABoltIsActuallyJagged()
    {
        List<Vector3> points = Path(16, 0.11f, 31);

        float furthest = 0f;
        foreach (Vector3 point in points)
        {
            furthest = MathF.Max(furthest, DistanceFromLine(point, From, To));
        }

        Assert.True(furthest > 0.05f, "a bolt with jitter should leave its straight line");
    }

    [Theory]
    [InlineData(6, 0.05f)]
    [InlineData(12, 0.11f)]
    [InlineData(16, 0.2f)]
    [InlineData(24, 0.35f)]
    public void NoPointStraysPastTheBound(int segments, float jitter)
    {
        float length = From.DistanceTo(To);
        float bound = VfxBoltPath.MaxOffset(length, jitter) + 0.0005f;

        for (int seed = 0; seed < 200; seed++)
        {
            foreach (Vector3 point in Path(segments, jitter, seed))
            {
                Assert.True(
                    DistanceFromLine(point, From, To) <= bound,
                    $"seed {seed}: a point sits further than {bound} from the line");
            }
        }
    }

    [Fact]
    public void ThePointsAdvanceEvenlyAlongTheLine()
    {
        // The kinks are sideways only: along the line the points are evenly spaced, so a ribbon's
        // texture does not stretch and a short bolt never doubles back on itself.
        List<Vector3> points = Path(12, 0.2f, 77);
        Vector3 axis = (To - From).Normalized();
        float step = From.DistanceTo(To) / 12f;

        for (int i = 0; i < points.Count; i++)
        {
            Assert.Equal(step * i, (points[i] - From).Dot(axis), 3);
        }
    }

    [Fact]
    public void NoJitterIsAStraightLine()
    {
        foreach (Vector3 point in Path(8, 0f, 5))
        {
            Assert.True(DistanceFromLine(point, From, To) < 0.0005f);
        }
    }

    [Fact]
    public void ABoltOfNoLengthIsAllOnePoint()
    {
        List<Vector3> points = Path(8, 0.3f, 5, From, From);

        Assert.Equal(9, points.Count);
        Assert.All(points, point => Assert.Equal(From, point));
    }

    [Fact]
    public void AVerticalBoltIsAsGoodAsAnyOther()
    {
        // The sideways axes are built from "up"; a bolt that IS up must not collapse onto its line.
        var top = new Vector3(0f, 20f, 0f);
        List<Vector3> points = Path(16, 0.15f, 9, Vector3.Zero, top);

        Assert.Equal(Vector3.Zero, points[0]);
        Assert.Equal(top, points[^1]);
        Assert.All(points, point => Assert.False(float.IsNaN(point.X) || float.IsNaN(point.Y) || float.IsNaN(point.Z)));
        Assert.Contains(points, point => DistanceFromLine(point, Vector3.Zero, top) > 0.05f);
    }

    [Fact]
    public void FewerSegmentsThanOneIsStillALine()
    {
        Assert.Equal(2, Path(0, 0.2f, 3).Count);
    }

    [Fact]
    public void BranchesForkOffTheTrunkAndStayNearIt()
    {
        List<Vector3> trunk = Path(16, 0.11f, 12);
        var points = new List<Vector3>();
        var starts = new List<int>();
        var scratch = new List<Vector3>();

        int made = VfxBoltPath.Branches(trunk, 3, 5, 0.15f, 12, points, starts, scratch);

        Assert.Equal(3, made);
        Assert.Equal(3, starts.Count);
        Assert.Equal(3 * 6, points.Count);

        float length = From.DistanceTo(To);
        float reach = (length * VfxBoltPath.BranchReach) + VfxBoltPath.MaxOffset(length * VfxBoltPath.BranchReach, 0.15f) + 0.001f;
        for (int b = 0; b < starts.Count; b++)
        {
            Vector3 root = points[starts[b]];

            // A branch begins on the trunk, and never at either end of it.
            int at = trunk.IndexOf(root);
            Assert.InRange(at, 1, trunk.Count - 2);

            int end = b + 1 < starts.Count ? starts[b + 1] : points.Count;
            for (int i = starts[b]; i < end; i++)
            {
                Assert.True(points[i].DistanceTo(root) <= reach, "a branch ran further than its reach");
            }
        }
    }

    [Fact]
    public void BranchesAreAsRepeatableAsTheTrunk()
    {
        List<Vector3> trunk = Path(16, 0.11f, 12);
        var first = new List<Vector3>();
        var second = new List<Vector3>();

        VfxBoltPath.Branches(trunk, 2, 5, 0.15f, 5, first, new List<int>(), new List<Vector3>());
        VfxBoltPath.Branches(trunk, 2, 5, 0.15f, 5, second, new List<int>(), new List<Vector3>());

        Assert.Equal(first, second);
    }

    [Fact]
    public void ATrunkTooShortToForkMakesNoBranches()
    {
        List<Vector3> trunk = Path(2, 0.1f, 1);
        var points = new List<Vector3>();

        Assert.Equal(0, VfxBoltPath.Branches(trunk, 3, 4, 0.1f, 1, points, new List<int>(), new List<Vector3>()));
        Assert.Empty(points);
    }

    [Fact]
    public void TheGeneratorStaysInRangeAndSurvivesAZeroSeed()
    {
        uint state = 0u;
        for (int i = 0; i < 2000; i++)
        {
            Assert.InRange(VfxBoltPath.Signed(ref state), -1f, 1f);
            Assert.InRange(VfxBoltPath.Unit(ref state), 0f, 1f);
            Assert.NotEqual(0u, state);
        }
    }
}
