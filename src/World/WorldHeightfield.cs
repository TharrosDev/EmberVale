using System;
using System.Collections.Generic;

namespace Embervale.World;

/// <summary>
/// The one ground surface of a whole region (the 2026-08-29 geography overhaul): the noise settings
/// plus every cell's authored landforms, roads and yards, all converted to <b>world</b> X/Z and
/// pooled into a single continuous function.
///
/// ⚠️ <b>THIS IS WHY CELL SEAMS DISAPPEAR.</b> Terrain, collision, navigation, prop conforming and
/// scatter all read this one object, so two abutting cells cannot disagree about the ground between
/// them — they are literally evaluating the same function. It also means a hill, a ravine or a road
/// authored on one cell continues into its neighbour without either file knowing about the other,
/// which is the only cheap way to hide a rectangular lattice.
///
/// <see cref="ForBounds"/> returns a cheap view holding only the primitives that can reach a given
/// rectangle. Sampling stays O(a handful) per point no matter how large the region grows, and the
/// view is still exact: a primitive is kept whenever it overlaps the rectangle at all, so both
/// cells sharing an edge keep the same set along that edge.
///
/// Engine-free on purpose — <see cref="WorldTerrainMeshBuilder.HeightfieldFor"/> is the Godot-side
/// factory, and the unit suite constructs one directly.
/// </summary>
public sealed class WorldHeightfield
{
    private readonly int _seed;
    private readonly float _relief;
    private readonly float _detailScale;
    private readonly IReadOnlyList<WorldTerrainMath.Landform> _landforms;
    private readonly IReadOnlyList<WorldTerrainMath.Path> _paths;
    private readonly IReadOnlyList<WorldTerrainMath.GroundArea> _areas;

    public WorldHeightfield(
        int seed, float relief, float detailScale,
        IReadOnlyList<WorldTerrainMath.Landform>? landforms = null,
        IReadOnlyList<WorldTerrainMath.Path>? paths = null,
        IReadOnlyList<WorldTerrainMath.GroundArea>? areas = null)
    {
        _seed = seed;
        _relief = relief;
        _detailScale = detailScale;
        _landforms = landforms ?? Array.Empty<WorldTerrainMath.Landform>();
        _paths = paths ?? Array.Empty<WorldTerrainMath.Path>();
        _areas = areas ?? Array.Empty<WorldTerrainMath.GroundArea>();
    }

    public IReadOnlyList<WorldTerrainMath.Landform> Landforms => _landforms;
    public IReadOnlyList<WorldTerrainMath.Path> Paths => _paths;
    public IReadOnlyList<WorldTerrainMath.GroundArea> Areas => _areas;

    /// <summary>Noise plus landforms — what a road or yard is graded against, before either applies.</summary>
    public float BaseHeight(float worldX, float worldZ) =>
        WorldTerrainMath.BaseHeight(_seed, worldX, worldZ, _relief, _detailScale, _landforms);

    /// <summary>The finished ground height at a world point.</summary>
    public float Height(float worldX, float worldZ) =>
        WorldTerrainMath.Height(_seed, worldX, worldZ, _relief, _detailScale, _landforms, _paths, _areas);

    /// <summary>Route/activity masks a shader or a scatter planner needs, in world space.</summary>
    public float PathMask(float worldX, float worldZ) => WorldTerrainMath.PathMask(worldX, worldZ, _paths);

    public float AreaMask(float worldX, float worldZ) => WorldTerrainMath.GroundAreaMask(worldX, worldZ, _areas);

    /// <summary>
    /// A view over only the primitives that can influence the rectangle
    /// [<paramref name="minX"/>..<paramref name="maxX"/>] × [<paramref name="minZ"/>..<paramref name="maxZ"/>].
    /// Callers pass the cell envelope plus a margin large enough for the widest shoulder.
    /// </summary>
    public WorldHeightfield ForBounds(float minX, float minZ, float maxX, float maxZ, float margin = 24f)
    {
        minX -= margin;
        minZ -= margin;
        maxX += margin;
        maxZ += margin;

        var landforms = new List<WorldTerrainMath.Landform>();
        foreach (WorldTerrainMath.Landform form in _landforms)
        {
            if (form.MaxX >= minX && form.MinX <= maxX && form.MaxZ >= minZ && form.MinZ <= maxZ)
            {
                landforms.Add(form);
            }
        }

        var paths = new List<WorldTerrainMath.Path>();
        foreach (WorldTerrainMath.Path path in _paths)
        {
            float reach = (path.Width * 0.5f) + path.Shoulder;
            if (MathF.Max(path.StartX, path.EndX) + reach >= minX &&
                MathF.Min(path.StartX, path.EndX) - reach <= maxX &&
                MathF.Max(path.StartZ, path.EndZ) + reach >= minZ &&
                MathF.Min(path.StartZ, path.EndZ) - reach <= maxZ)
            {
                paths.Add(path);
            }
        }

        var areas = new List<WorldTerrainMath.GroundArea>();
        foreach (WorldTerrainMath.GroundArea area in _areas)
        {
            float reachX = area.RadiusX + area.Feather;
            float reachZ = area.RadiusZ + area.Feather;
            if (area.X + reachX >= minX && area.X - reachX <= maxX &&
                area.Z + reachZ >= minZ && area.Z - reachZ <= maxZ)
            {
                areas.Add(area);
            }
        }

        return new WorldHeightfield(_seed, _relief, _detailScale, landforms, paths, areas);
    }

    /// <summary>
    /// The local grade at one point, as rise over run — the same measure the route validator, the
    /// traversal probe and the terrain shader's rock band all use, so "0.7" means one thing realm-wide.
    /// </summary>
    public float SlopeAt(float worldX, float worldZ, float step = 1f) =>
        SlopeAt(worldX, worldZ, Height(worldX, worldZ), step);

    /// <summary>The same, when the caller already has the height here. The scatter planner asks for
    /// both on every candidate placement and there can be a third of a million of those in a region,
    /// so re-sampling the centre is a measurable share of a cell's load.</summary>
    public float SlopeAt(float worldX, float worldZ, float here, float step = 1f)
    {
        step = MathF.Max(0.1f, step);
        float dx = (Height(worldX + step, worldZ) - here) / step;
        float dz = (Height(worldX, worldZ + step) - here) / step;
        return MathF.Sqrt((dx * dx) + (dz * dz));
    }

    /// <summary>The upward surface normal at a point, for standing a prop on the ground it is on.</summary>
    public (float X, float Y, float Z) NormalAt(float worldX, float worldZ, float step = 1f)
    {
        step = MathF.Max(0.1f, step);
        float left = Height(worldX - step, worldZ);
        float right = Height(worldX + step, worldZ);
        float back = Height(worldX, worldZ - step);
        float forward = Height(worldX, worldZ + step);
        float nx = left - right;
        float ny = 2f * step;
        float nz = back - forward;
        float length = MathF.Sqrt((nx * nx) + (ny * ny) + (nz * nz));
        return length <= 0f ? (0f, 1f, 0f) : (nx / length, ny / length, nz / length);
    }

    /// <summary>
    /// The steepest grade, as a slope ratio (rise over run), sampled on a grid across a rectangle.
    /// The traversal probes and <c>--validate</c> use it to catch terrain a player or an NPC cannot
    /// walk without anyone having to look at it: 1.0 is 45°, which is the engine's floor limit.
    /// </summary>
    public float SteepestSlope(float minX, float minZ, float maxX, float maxZ, float step = 2f)
    {
        step = MathF.Max(0.25f, step);
        float worst = 0f;
        for (float z = minZ; z <= maxZ; z += step)
        {
            for (float x = minX; x <= maxX; x += step)
            {
                float here = Height(x, z);
                float dx = MathF.Abs(Height(x + step, z) - here) / step;
                float dz = MathF.Abs(Height(x, z + step) - here) / step;
                worst = MathF.Max(worst, MathF.Max(dx, dz));
            }
        }
        return worst;
    }
}
