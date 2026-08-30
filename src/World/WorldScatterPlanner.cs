using System;
using System.Collections.Generic;

namespace Embervale.World;

/// <summary>A circular no-dressing area in cell-local X/Z space.</summary>
public readonly record struct WorldScatterExclusion(float X, float Z, float Radius);

/// <summary>One deterministic, cell-local cosmetic placement.</summary>
public readonly record struct WorldScatterPlacement(float X, float Z, float Yaw, float ScaleUnit);

/// <summary>
/// Engine-free rejection sampler for cosmetic biome dressing. The planner owns the placement
/// contract; the Godot renderer only turns the accepted points into MultiMesh transforms.
///
/// ⚠️ <b><paramref name="terrainAccepts"/> IS WHY THE ATTEMPT BUDGET IS GENEROUS.</b> Slope and
/// altitude limits can refuse most of a cell — a snowfield layer banded above 30 m over ground that
/// is mostly below it — and a rejection sampler that runs out of attempts silently thins a layer
/// instead of failing. Forty attempts per requested instance absorbs that; a layer that still comes
/// back short is a layer whose band does not match its cell, which is worth seeing in the census.
/// </summary>
public static class WorldScatterPlanner
{
    public static IReadOnlyList<WorldScatterPlacement> Plan(
        int seed,
        int requestedCount,
        float width,
        float depth,
        float edgePadding,
        float minimumSpacing,
        IReadOnlyList<WorldScatterExclusion>? exclusions = null,
        IReadOnlyList<WorldTerrainMath.Path>? paths = null,
        IReadOnlyList<WorldTerrainMath.GroundArea>? groundAreas = null,
        Func<float, float, bool>? terrainAccepts = null)
    {
        var accepted = new List<WorldScatterPlacement>(Math.Max(0, requestedCount));
        if (requestedCount <= 0 || width <= 0f || depth <= 0f)
        {
            return accepted;
        }

        // ⚠️ THE SPACING TEST IS BUCKETED, NOT LINEAR, AND ON A 200 m CELL THAT IS THE WHOLE COST OF
        // THIS FUNCTION. A layer at density 620 over a 200 x 110 cell wants 1,364 instances and takes
        // about fifty thousand attempts to place them; comparing each attempt against every accepted
        // point is seventy million distance checks per layer, per cell, and the realm has four layers
        // on sixteen cells. A uniform grid at the spacing radius turns it into nine buckets.
        var buckets = new Dictionary<long, List<WorldScatterPlacement>>();
        float bucketSize = MathF.Max(0.5f, minimumSpacing);

        float halfWidth = MathF.Max(0f, (width * 0.5f) - edgePadding);
        float halfDepth = MathF.Max(0f, (depth * 0.5f) - edgePadding);
        float spacingSquared = MathF.Max(0f, minimumSpacing * minimumSpacing);
        int attemptBudget = Math.Max(128, requestedCount * 40);

        for (int attempt = 0; attempt < attemptBudget && accepted.Count < requestedCount; attempt++)
        {
            float x = ((WorldSceneryMath.Unit(seed, attempt * 4) * 2f) - 1f) * halfWidth;
            float z = ((WorldSceneryMath.Unit(seed, (attempt * 4) + 1) * 2f) - 1f) * halfDepth;

            // ⚠️ THE ORDER OF THESE TESTS IS A PERFORMANCE DECISION, NOT A STYLE ONE.
            // TooClose is O(accepted) and `accepted` reaches four figures on a 200 m cell, so it is
            // by far the most expensive test here and it goes LAST. When the terrain gate was added
            // above it, every candidate the gate would reject still paid for a full spacing scan
            // first, and region load time went up by two and a half seconds across the realm.
            if (InsidePath(x, z, paths, minimumSpacing * 0.25f) ||
                InsideGroundArea(x, z, groundAreas, minimumSpacing * 0.25f) ||
                InsideExclusion(x, z, exclusions) ||
                (terrainAccepts != null && !terrainAccepts(x, z)) ||
                TooClose(x, z, buckets, bucketSize, spacingSquared))
            {
                continue;
            }

            var placement = new WorldScatterPlacement(
                x,
                z,
                WorldSceneryMath.Unit(seed, (attempt * 4) + 2) * MathF.Tau,
                WorldSceneryMath.Unit(seed, (attempt * 4) + 3));
            accepted.Add(placement);
            long key = BucketKey(x, z, bucketSize);
            if (!buckets.TryGetValue(key, out List<WorldScatterPlacement>? bucket))
            {
                bucket = new List<WorldScatterPlacement>();
                buckets[key] = bucket;
            }
            bucket.Add(placement);
        }

        return accepted;
    }

    private static bool InsidePath(
        float x, float z, IReadOnlyList<WorldTerrainMath.Path>? paths, float extra)
    {
        if (paths == null)
        {
            return false;
        }
        foreach (WorldTerrainMath.Path path in paths)
        {
            if (WorldTerrainMath.InsidePath(x, z, path, extra))
            {
                return true;
            }
        }
        return false;
    }

    private static bool InsideGroundArea(
        float x, float z, IReadOnlyList<WorldTerrainMath.GroundArea>? areas, float extra)
    {
        if (areas == null)
        {
            return false;
        }
        foreach (WorldTerrainMath.GroundArea area in areas)
        {
            if (WorldTerrainMath.InsideGroundArea(x, z, area, extra))
            {
                return true;
            }
        }
        return false;
    }

    private static bool InsideExclusion(
        float x, float z, IReadOnlyList<WorldScatterExclusion>? exclusions)
    {
        if (exclusions == null)
        {
            return false;
        }

        foreach (WorldScatterExclusion exclusion in exclusions)
        {
            float dx = x - exclusion.X;
            float dz = z - exclusion.Z;
            if ((dx * dx) + (dz * dz) < exclusion.Radius * exclusion.Radius)
            {
                return true;
            }
        }

        return false;
    }

    private static long BucketKey(float x, float z, float bucketSize)
    {
        long column = (long)MathF.Floor(x / bucketSize);
        long row = (long)MathF.Floor(z / bucketSize);
        return (column << 32) ^ (row & 0xFFFFFFFFL);
    }

    /// <summary>
    /// Is anything already placed within <paramref name="spacingSquared"/> of this point? Only the
    /// nine buckets around it can hold such a point, because a bucket is exactly one spacing wide.
    /// <b>The placement set is bit-identical to the old linear scan</b> — same seed, same attempt
    /// order, same accept/reject decision — so this is a speed change and not a world change.
    /// </summary>
    private static bool TooClose(
        float x, float z, Dictionary<long, List<WorldScatterPlacement>> buckets,
        float bucketSize, float spacingSquared)
    {
        if (spacingSquared <= 0f)
        {
            return false;
        }

        long column = (long)MathF.Floor(x / bucketSize);
        long row = (long)MathF.Floor(z / bucketSize);
        for (long dc = -1; dc <= 1; dc++)
        {
            for (long dr = -1; dr <= 1; dr++)
            {
                long key = ((column + dc) << 32) ^ ((row + dr) & 0xFFFFFFFFL);
                if (!buckets.TryGetValue(key, out List<WorldScatterPlacement>? bucket))
                {
                    continue;
                }
                foreach (WorldScatterPlacement placement in bucket)
                {
                    float dx = x - placement.X;
                    float dz = z - placement.Z;
                    if ((dx * dx) + (dz * dz) < spacingSquared)
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }
}
