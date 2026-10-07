using System;
using System.Collections.Generic;
using Godot;

namespace Embervale.Magic.Vfx;

/// <summary>
/// The shape of a lightning bolt: a straight line broken by midpoint displacement, with side
/// branches forking off it. Pure and seeded with its own generator (never the engine's), so a bolt
/// is the same bolt for the same seed (which is what lets a sustained beam be rebuilt every frame
/// as its ends move and only change shape when it is re-jittered) and its bounds are a test:
/// no point strays further from the line than <see cref="MaxOffset"/>.
/// </summary>
public static class VfxBoltPath
{
    /// <summary>How much of the remaining displacement each level of subdivision keeps.</summary>
    public const float Roughness = 0.55f;

    /// <summary>The longest a branch runs, as a fraction of its trunk.</summary>
    public const float BranchReach = 0.4f;

    private const float FirstAmplitude = 0.5f;

    /// <summary>The next value of a xorshift generator. A zero state is nudged off zero.</summary>
    public static uint Next(ref uint state)
    {
        if (state == 0u)
        {
            state = 0x9E3779B9u;
        }

        state ^= state << 13;
        state ^= state >> 17;
        state ^= state << 5;
        return state;
    }

    /// <summary>A repeatable value in -1..1.</summary>
    public static float Signed(ref uint state) => ((Next(ref state) & 0xFFFFFF) / (float)0x800000) - 1f;

    /// <summary>A repeatable value in 0..1.</summary>
    public static float Unit(ref uint state) => (Next(ref state) & 0xFFFFFF) / (float)0x1000000;

    /// <summary>The furthest any point of a bolt of <paramref name="length"/> metres and
    /// <paramref name="jitter"/> (a fraction of the length) can sit from its straight line.</summary>
    public static float MaxOffset(float length, float jitter) =>
        MathF.Abs(length * jitter) * FirstAmplitude / (1f - Roughness) * MathF.Sqrt(2f);

    /// <summary>The jitter (a fraction of the length) at which a bolt of <paramref name="length"/>
    /// metres sways at most <paramref name="sway"/> metres from its line: the inverse of
    /// <see cref="MaxOffset"/>. A long bolt at a fixed fraction is bent like a tube; held to a sway in
    /// metres it stays a jagged line.</summary>
    public static float JitterFor(float length, float sway) =>
        MathF.Abs(sway) * (1f - Roughness) / (MathF.Max(0.0001f, MathF.Abs(length)) * FirstAmplitude * MathF.Sqrt(2f));

    /// <summary>
    /// Fills <paramref name="points"/> (cleared first) with <c>segments + 1</c> points from
    /// <paramref name="from"/> to <paramref name="to"/>. The ends are exact; the points between are
    /// evenly spaced along the line and pushed sideways by up to <see cref="MaxOffset"/>.
    /// </summary>
    public static void Generate(Vector3 from, Vector3 to, int segments, float jitter, int seed, List<Vector3> points)
    {
        points.Clear();
        segments = Math.Max(1, segments);
        Vector3 line = to - from;
        float length = line.Length();
        if (length < 0.0001f || jitter == 0f)
        {
            for (int i = 0; i <= segments; i++)
            {
                points.Add(from + (line * ((float)i / segments)));
            }

            return;
        }

        Vector3 axis = line / length;
        Vector3 reference = MathF.Abs(axis.Y) < 0.95f ? Vector3.Up : Vector3.Right;
        Vector3 sideA = axis.Cross(reference).Normalized();
        Vector3 sideB = axis.Cross(sideA);

        // Displace on a power-of-two lattice, then read it off at the spacing asked for, so the
        // shape of a bolt does not depend on how many segments the tier can afford.
        int levels = 1;
        while ((1 << levels) < segments)
        {
            levels++;
        }

        int cells = 1 << levels;
        Span<float> offsetA = cells + 1 <= 129 ? stackalloc float[cells + 1] : new float[cells + 1];
        Span<float> offsetB = cells + 1 <= 129 ? stackalloc float[cells + 1] : new float[cells + 1];
        offsetA.Clear();
        offsetB.Clear();

        uint state = unchecked((uint)seed * 2654435761u) + 1u;
        float amplitude = length * jitter * FirstAmplitude;
        for (int step = cells; step > 1; step /= 2)
        {
            int half = step / 2;
            for (int i = half; i < cells; i += step)
            {
                offsetA[i] = ((offsetA[i - half] + offsetA[i + half]) * 0.5f) + (Signed(ref state) * amplitude);
                offsetB[i] = ((offsetB[i - half] + offsetB[i + half]) * 0.5f) + (Signed(ref state) * amplitude);
            }

            amplitude *= Roughness;
        }

        for (int i = 0; i <= segments; i++)
        {
            float t = (float)i / segments;
            float at = t * cells;
            int low = Math.Min((int)at, cells - 1);
            float blend = at - low;
            float a = offsetA[low] + ((offsetA[low + 1] - offsetA[low]) * blend);
            float b = offsetB[low] + ((offsetB[low + 1] - offsetB[low]) * blend);
            points.Add(from + (line * t) + (sideA * a) + (sideB * b));
        }

        points[0] = from;
        points[segments] = to;
    }

    /// <summary>
    /// Forks up to <paramref name="count"/> branches off <paramref name="trunk"/>, appending each
    /// one's points to <paramref name="points"/> and its first index to <paramref name="starts"/>
    /// (neither is cleared). A branch begins on a trunk point, leans away from the trunk and runs at
    /// most <see cref="BranchReach"/> of its length. Returns how many were made.
    /// </summary>
    public static int Branches(
        IReadOnlyList<Vector3> trunk, int count, int segments, float jitter, int seed, List<Vector3> points,
        List<int> starts, List<Vector3> scratch)
    {
        if (count <= 0 || trunk.Count < 4)
        {
            return 0;
        }

        Vector3 line = trunk[trunk.Count - 1] - trunk[0];
        float length = line.Length();
        if (length < 0.0001f)
        {
            return 0;
        }

        Vector3 axis = line / length;
        Vector3 reference = MathF.Abs(axis.Y) < 0.95f ? Vector3.Up : Vector3.Right;
        Vector3 sideA = axis.Cross(reference).Normalized();
        Vector3 sideB = axis.Cross(sideA);

        uint state = unchecked((uint)seed * 40503u) + 7u;
        segments = Math.Max(2, segments);
        for (int b = 0; b < count; b++)
        {
            // Never the first or the last point: a fork off an end reads as a second bolt.
            int at = 1 + (int)(Unit(ref state) * (trunk.Count - 2));
            at = Math.Clamp(at, 1, trunk.Count - 2);
            Vector3 lean = (axis + (sideA * Signed(ref state) * 0.9f) + (sideB * Signed(ref state) * 0.9f)).Normalized();
            float reach = length * BranchReach * (0.5f + (0.5f * Unit(ref state)));
            Vector3 start = trunk[at];

            Generate(start, start + (lean * reach), segments, jitter, unchecked(seed + (b * 7919) + 13), scratch);
            starts.Add(points.Count);
            points.AddRange(scratch);
        }

        return count;
    }
}
