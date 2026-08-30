using System;
using System.Collections.Generic;

namespace Embervale.World;

/// <summary>
/// Off-route traversal QA: finds the places a player can walk into and cannot walk out of.
///
/// ⚠️ <b>EVERY EXISTING TRAVERSAL CHECK ONLY LOOKS AT AUTHORED ROUTES, AND EXPLORATION IS THE POINT
/// OF AN OPEN WORLD.</b> <c>ValidateRouteGrades</c> walks path centrelines and
/// <c>world_traversal_probe.gd</c> drives a capsule down all 142 of them, so the roads are proved.
/// Nothing looked at the other 95% of the ground — and a region-wide heightfield with 14 m ridges,
/// 6 m pits, crater rims and a glacier trench is exactly the kind of world that grows a bowl with no
/// exit as a side effect of two landforms overlapping. Neither author would see it in their own file.
///
/// <b>The model is a DIRECTED graph, and that is the whole idea.</b> Walking downhill and walking
/// uphill are not the same move: a player can drop several metres safely and cannot climb back up a
/// grade steeper than <see cref="MaxGrade"/>. So:
/// <list type="bullet">
/// <item><b>descend</b> to a neighbour if the drop is within <see cref="FallAllowance"/>;</item>
/// <item><b>ascend</b> to a neighbour only if the climb is within <c>step * MaxGrade</c>.</item>
/// </list>
/// A <b>trap</b> is a cell reachable from the region's origin down those edges whose reverse walk
/// cannot get home. A <b>pocket</b> is ground that is not reachable at all — usually intended (the
/// far side of a cliff), sometimes an accidental island. Both are reported; only traps are failed.
///
/// ⚠️ <b>WATER IS NOT A TRAP, BECAUSE <see cref="WorldWater"/> ALREADY OWNS IT.</b> A basin the
/// player cannot climb out of is exactly what Hollowreach's open water is designed to be, and
/// <see cref="WorldWaterSafety"/> recovers them from it. Failing on those would force an author to
/// choose between a believable lake and a green validator. Traps are counted only where the ground
/// is dry.
///
/// Engine-free: the validator, the CLI report and the unit suite all drive the same function.
/// </summary>
public static class WorldTraversalAnalysis
{
    /// <summary>Steepest ground a player can walk UP. <c>CharacterBody3D</c>'s floor limit is 45
    /// degrees (1.0); 0.7 is the grade a player can hold without sliding back.</summary>
    public const float MaxGrade = 0.7f;

    /// <summary>How far a player may drop between samples and still be considered to have got there
    /// on purpose. Bigger than a step-up (0.5 m) because falling is not climbing.</summary>
    public const float FallAllowance = 7f;

    /// <summary>A trap smaller than this many square metres is noise — a two-sample dimple between
    /// boulders that a player's own collision capsule will never fit into.</summary>
    public const float MinimumReportedArea = 36f;

    public readonly record struct Region(float MinX, float MinZ, float MaxX, float MaxZ);

    /// <summary>One connected patch of ground the analysis has something to say about.</summary>
    public readonly record struct Patch(
        float CentreX, float CentreZ, float Area, float LowestY, float DeepestDrop, bool Flooded)
    {
        public override string ToString() =>
            $"{Area:F0} m^2 around ({CentreX:F0}, {CentreZ:F0}) at y {LowestY:F1}" +
            (Flooded ? " [flooded]" : $", walls up to {DeepestDrop:F1} m");
    }

    public sealed record Result(
        IReadOnlyList<Patch> Traps, IReadOnlyList<Patch> Pockets, int Samples, float Step)
    {
        public bool Clean => Traps.Count == 0;
    }

    /// <summary>
    /// Sweeps a region and returns its traps and unreachable pockets.
    /// </summary>
    /// <param name="field">The region's ground.</param>
    /// <param name="area">The lattice to sweep, in world metres.</param>
    /// <param name="origin">A point known to be on the walkable network — the region's spawn.</param>
    /// <param name="water">Declared water, so flooded basins are labelled rather than failed.</param>
    /// <param name="step">Sample spacing. 3 m resolves anything a player's 0.8 m capsule cares about
    /// while keeping a 330 x 440 m realm to about sixteen thousand samples.</param>
    public static Result Analyse(
        WorldHeightfield field, Region area, (float X, float Z) origin,
        IReadOnlyList<WorldWater.Body>? water = null, float step = 3f)
    {
        step = MathF.Max(0.5f, step);
        int columns = Math.Max(2, (int)MathF.Ceiling((area.MaxX - area.MinX) / step) + 1);
        int rows = Math.Max(2, (int)MathF.Ceiling((area.MaxZ - area.MinZ) / step) + 1);
        int count = columns * rows;

        var height = new float[count];
        var flooded = new bool[count];
        for (int z = 0; z < rows; z++)
        {
            float worldZ = area.MinZ + (z * step);
            for (int x = 0; x < columns; x++)
            {
                float worldX = area.MinX + (x * step);
                int index = (z * columns) + x;
                height[index] = field.Height(worldX, worldZ);
                if (water != null)
                {
                    float? surface = WorldWater.SurfaceAt(worldX, worldZ, water);
                    flooded[index] = surface != null &&
                                     surface.Value - height[index] > WorldWater.WadeDepth;
                }
            }
        }

        float climbLimit = step * MaxGrade;
        int start = Index(area, origin.X, origin.Z, step, columns, rows);

        // Forward: everywhere the player can GET to, dropping freely and climbing within the grade.
        bool[] reachable = Flood(start, columns, rows, count, (from, to) =>
            Passable(height[from], height[to], climbLimit));

        // Reverse: everywhere that can get HOME. Same edges, walked the other way.
        bool[] canReturn = Flood(start, columns, rows, count, (from, to) =>
            Passable(height[to], height[from], climbLimit));

        var trapMask = new bool[count];
        var pocketMask = new bool[count];
        for (int i = 0; i < count; i++)
        {
            if (reachable[i] && !canReturn[i])
            {
                trapMask[i] = true;
            }
            else if (!reachable[i] && !canReturn[i])
            {
                pocketMask[i] = true;
            }
        }

        float cellArea = step * step;
        List<Patch> traps = Group(trapMask, height, flooded, area, step, columns, rows, cellArea);
        List<Patch> pockets = Group(pocketMask, height, flooded, area, step, columns, rows, cellArea);
        traps.RemoveAll(p => p.Area < MinimumReportedArea || p.Flooded);
        pockets.RemoveAll(p => p.Area < MinimumReportedArea);
        traps.Sort((a, b) => b.Area.CompareTo(a.Area));
        pockets.Sort((a, b) => b.Area.CompareTo(a.Area));
        return new Result(traps, pockets, count, step);
    }

    /// <summary>A move is possible when the CLIMB is inside the grade. A drop is bounded only by
    /// <see cref="FallAllowance"/> — past that the player is falling off something, which is a
    /// one-way move the forward pass should still allow and the reverse pass must not.</summary>
    private static bool Passable(float fromHeight, float toHeight, float climbLimit)
    {
        float rise = toHeight - fromHeight;
        return rise >= 0f ? rise <= climbLimit : -rise <= FallAllowance;
    }

    private static int Index(
        Region area, float x, float z, float step, int columns, int rows)
    {
        int cx = Math.Clamp((int)MathF.Round((x - area.MinX) / step), 0, columns - 1);
        int cz = Math.Clamp((int)MathF.Round((z - area.MinZ) / step), 0, rows - 1);
        return (cz * columns) + cx;
    }

    private static bool[] Flood(int start, int columns, int rows, int count, Func<int, int, bool> passable)
    {
        var seen = new bool[count];
        var queue = new Queue<int>();
        seen[start] = true;
        queue.Enqueue(start);
        while (queue.Count > 0)
        {
            int here = queue.Dequeue();
            int x = here % columns;
            int z = here / columns;
            for (int direction = 0; direction < 4; direction++)
            {
                int nx = x + (direction == 0 ? 1 : direction == 1 ? -1 : 0);
                int nz = z + (direction == 2 ? 1 : direction == 3 ? -1 : 0);
                if (nx < 0 || nz < 0 || nx >= columns || nz >= rows)
                {
                    continue;
                }
                int next = (nz * columns) + nx;
                if (!seen[next] && passable(here, next))
                {
                    seen[next] = true;
                    queue.Enqueue(next);
                }
            }
        }
        return seen;
    }

    private static List<Patch> Group(
        bool[] mask, float[] height, bool[] flooded, Region area, float step,
        int columns, int rows, float cellArea)
    {
        var patches = new List<Patch>();
        var visited = new bool[mask.Length];
        var queue = new Queue<int>();
        for (int seed = 0; seed < mask.Length; seed++)
        {
            if (!mask[seed] || visited[seed])
            {
                continue;
            }

            visited[seed] = true;
            queue.Clear();
            queue.Enqueue(seed);
            double sumX = 0, sumZ = 0;
            int members = 0;
            float lowest = float.MaxValue;
            float rim = float.MinValue;
            bool anyFlooded = false;

            while (queue.Count > 0)
            {
                int here = queue.Dequeue();
                int x = here % columns;
                int z = here / columns;
                members++;
                sumX += area.MinX + (x * step);
                sumZ += area.MinZ + (z * step);
                lowest = MathF.Min(lowest, height[here]);
                anyFlooded |= flooded[here];
                for (int direction = 0; direction < 4; direction++)
                {
                    int nx = x + (direction == 0 ? 1 : direction == 1 ? -1 : 0);
                    int nz = z + (direction == 2 ? 1 : direction == 3 ? -1 : 0);
                    if (nx < 0 || nz < 0 || nx >= columns || nz >= rows)
                    {
                        continue;
                    }
                    int next = (nz * columns) + nx;
                    if (mask[next])
                    {
                        if (!visited[next])
                        {
                            visited[next] = true;
                            queue.Enqueue(next);
                        }
                    }
                    else
                    {
                        // The lip: how far above the patch the ground immediately outside it stands.
                        rim = MathF.Max(rim, height[next]);
                    }
                }
            }

            patches.Add(new Patch(
                (float)(sumX / members), (float)(sumZ / members), members * cellArea,
                lowest, rim > float.MinValue ? rim - lowest : 0f, anyFlooded));
        }

        return patches;
    }

    /// <summary>The lattice a region's cells actually cover — the area worth sweeping.</summary>
    public static Region LatticeOf(RegionResource region)
    {
        float minX = float.MaxValue, minZ = float.MaxValue;
        float maxX = float.MinValue, maxZ = float.MinValue;
        foreach (RegionCellResource cell in region.Cells)
        {
            if (cell?.Presentation == null)
            {
                continue;
            }
            minX = MathF.Min(minX, cell.Center.X - (cell.Presentation.Width * 0.5f));
            maxX = MathF.Max(maxX, cell.Center.X + (cell.Presentation.Width * 0.5f));
            minZ = MathF.Min(minZ, cell.Center.Z - (cell.Presentation.Depth * 0.5f));
            maxZ = MathF.Max(maxZ, cell.Center.Z + (cell.Presentation.Depth * 0.5f));
        }
        return minX > maxX ? new Region(-1f, -1f, 1f, 1f) : new Region(minX, minZ, maxX, maxZ);
    }
}
