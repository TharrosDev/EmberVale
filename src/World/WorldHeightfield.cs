using System;
using System.Collections.Generic;

namespace Embervale.World;

/// <summary>
/// The one ground surface of a whole region: the generated geography, the region's cached
/// hydrology, and every cell's authored landforms, roads and yards, all in <b>world</b> X/Z and
/// pooled into a single continuous function.
///
/// ⚠️ <b>THIS IS WHY CELL SEAMS DISAPPEAR</b> (the 2026-08-29 geography overhaul). Terrain,
/// collision, navigation, prop conforming, water, scatter, traversal QA and every gameplay ground
/// query read this one object, so two abutting cells cannot disagree about the ground between them —
/// they are literally evaluating the same function.
///
/// ⚠️ <b>AND THE HYDROLOGY MAP IS SHARED BY REFERENCE, NOT REBUILT PER VIEW.</b> That is the second
/// half of the same contract and it is the easy one to break: <see cref="ForBounds"/> and
/// <see cref="WithAuthored"/> clip or replace the authored primitive lists and hand the SAME
/// <see cref="WorldHydrologyMap"/> instance on. Rebuilding it for a clipped view would give each
/// cell its own drainage solution over its own apron, and a river would change width, depth and
/// course at every cell border — the exact rectangular artefact the world-space field exists to
/// remove, reintroduced by a river.
///
/// The evaluation order is fixed and each stage may only see the one before it:
///   1. <see cref="WorldGenerator"/> — macro geography, climate, then the hydrology carve;
///   2. authored <see cref="WorldTerrainMath.Landform"/>s;
///   3. authored <see cref="WorldTerrainMath.Path"/>s (roads);
///   4. authored <see cref="WorldTerrainMath.GroundArea"/>s (yards, pads, floors).
///
/// Engine-free on purpose — <see cref="WorldTerrainMeshBuilder.HeightfieldFor"/> is the Godot-side
/// factory, and the unit suite constructs one directly.
/// </summary>
public sealed class WorldHeightfield
{
    private readonly WorldGenerationSettings _settings;
    private readonly WorldMacroField? _macro;
    private readonly WorldHydrologyMap? _hydrology;
    private readonly IReadOnlyList<WorldTerrainMath.Landform> _landforms;
    private readonly IReadOnlyList<WorldTerrainMath.Path> _paths;
    private readonly IReadOnlyList<WorldTerrainMath.GroundArea> _areas;
    private readonly IReadOnlyList<WorldTerrainMath.Path> _corridorPaths;
    private readonly IReadOnlyList<WorldTerrainMath.GroundArea> _corridorAreas;

    /// <summary>
    /// A region's field: the settings plus one hydrology solution covering
    /// [<paramref name="minX"/>..<paramref name="maxX"/>] × [<paramref name="minZ"/>..<paramref name="maxZ"/>].
    /// Build this ONCE per region and derive every other view from it.
    /// </summary>
    public WorldHeightfield(
        WorldGenerationSettings settings, float minX, float minZ, float maxX, float maxZ)
        : this(settings, minX, minZ, maxX, maxZ, BuildMacro(settings, minX, minZ, maxX, maxZ))
    {
    }

    /// <summary>⚠️ The macro cache is built BEFORE the drainage solve and handed to it, because the
    /// drainage solve reads preliminary elevations at every one of its own grid cells. Letting it
    /// compute them from raw noise would make the hydrology build the most expensive thing in a
    /// region load and — worse — would let the two disagree about the ground by a rounding error,
    /// so a river would sit a few centimetres off the valley it carved.</summary>
    private WorldHeightfield(
        WorldGenerationSettings settings, float minX, float minZ, float maxX, float maxZ,
        WorldMacroField macro)
        : this(settings, macro, WorldHydrologyMap.Build(settings, macro, minX, minZ, maxX, maxZ),
            null, null, null, null, null)
    {
    }

    private static WorldMacroField BuildMacro(
        WorldGenerationSettings settings, float minX, float minZ, float maxX, float maxZ) =>
        WorldMacroField.Build(settings, minX, minZ, maxX, maxZ);

    /// <summary>
    /// ⚠️ The legacy noise-only field, kept because it is the <c>Version = 1</c> generator and the
    /// unit suite's whole seam battery is written against it. It builds no hydrology: a v1 world has
    /// no drainage, which is exactly what it had before the generator landed.
    /// </summary>
    public WorldHeightfield(
        int seed, float relief, float detailScale,
        IReadOnlyList<WorldTerrainMath.Landform>? landforms = null,
        IReadOnlyList<WorldTerrainMath.Path>? paths = null,
        IReadOnlyList<WorldTerrainMath.GroundArea>? areas = null)
        : this(
            new WorldGenerationSettings
            {
                Seed = seed, Version = 1, LocalRelief = relief, DetailScale = detailScale,
            },
            null, null, landforms, paths, areas, null, null)
    {
    }

    private WorldHeightfield(
        WorldGenerationSettings settings, WorldMacroField? macro, WorldHydrologyMap? hydrology,
        IReadOnlyList<WorldTerrainMath.Landform>? landforms,
        IReadOnlyList<WorldTerrainMath.Path>? paths,
        IReadOnlyList<WorldTerrainMath.GroundArea>? areas,
        IReadOnlyList<WorldTerrainMath.Path>? corridorPaths,
        IReadOnlyList<WorldTerrainMath.GroundArea>? corridorAreas)
    {
        _settings = settings;
        _macro = macro;
        _hydrology = hydrology;
        _landforms = landforms ?? Array.Empty<WorldTerrainMath.Landform>();
        _paths = paths ?? Array.Empty<WorldTerrainMath.Path>();
        _areas = areas ?? Array.Empty<WorldTerrainMath.GroundArea>();
        _corridorPaths = corridorPaths ?? Array.Empty<WorldTerrainMath.Path>();
        _corridorAreas = corridorAreas ?? Array.Empty<WorldTerrainMath.GroundArea>();
    }

    public WorldGenerationSettings Settings => _settings;
    public IReadOnlyList<WorldTerrainMath.Landform> Landforms => _landforms;
    public IReadOnlyList<WorldTerrainMath.Path> Paths => _paths;
    public IReadOnlyList<WorldTerrainMath.GroundArea> Areas => _areas;

    /// <summary>Approximate bytes held by the shared hydrology cache, for the performance overlay.</summary>
    public long HydrologyBytes =>
        (_hydrology?.ApproximateBytes ?? 0L) + (_macro?.ApproximateBytes ?? 0L);

    /// <summary>The same world, with authored geography attached. Shares the hydrology solution.</summary>
    public WorldHeightfield WithAuthored(
        IReadOnlyList<WorldTerrainMath.Landform>? landforms,
        IReadOnlyList<WorldTerrainMath.Path>? paths,
        IReadOnlyList<WorldTerrainMath.GroundArea>? areas) =>
        new(_settings, _macro, _hydrology, landforms, paths, areas, _corridorPaths, _corridorAreas);

    /// <summary>The same world and landforms, with roads and yards attached. Roads are levelled
    /// against the field WITHOUT them, so this is the second of the mesh builder's two passes.</summary>
    public WorldHeightfield WithAuthoredSurfaces(
        IReadOnlyList<WorldTerrainMath.Path>? paths,
        IReadOnlyList<WorldTerrainMath.GroundArea>? areas) =>
        new(_settings, _macro, _hydrology, _landforms, paths, areas, _corridorPaths, _corridorAreas);

    /// <summary>
    /// The authored circulation whose presence calms the generated macro relief around it — the one
    /// way authored content reaches back into the generator (see
    /// <see cref="WorldGenerationSettings.RouteCalm"/>).
    ///
    /// WARNING: THESE SURVIVE WithAuthored AND ForBounds DELIBERATELY. They are set BEFORE road
    /// endpoint heights are resolved and must still be in effect when they are, or a road would be
    /// graded against the un-calmed mountain it is supposed to have calmed and the whole exercise
    /// buys nothing. Geometry only: no heights, so nothing here recurses.
    /// </summary>
    public WorldHeightfield WithCorridors(
        IReadOnlyList<WorldTerrainMath.Path>? paths,
        IReadOnlyList<WorldTerrainMath.GroundArea>? areas) =>
        new(_settings, _macro, _hydrology, _landforms, _paths, _areas, paths, areas);

    /// <summary>How strongly authored circulation calms the macro relief here, 0..1.</summary>
    public float RouteCalmAt(float worldX, float worldZ) =>
        _corridorPaths.Count == 0 && _corridorAreas.Count == 0
            ? 0f
            : WorldTerrainMath.RouteCalm(
                worldX, worldZ, _settings.RouteCalm, _corridorPaths, _corridorAreas);

    /// <summary>The generated ground before any authoring: macro geography, carved by hydrology.</summary>
    public float GeneratedElevation(float worldX, float worldZ) =>
        WorldGenerator.Sample(
            _settings, _macro, _hydrology, worldX, worldZ, RouteCalmAt(worldX, worldZ)).Elevation;

    /// <summary>Generated ground plus landforms — what a road or yard is graded against.</summary>
    public float BaseHeight(float worldX, float worldZ) =>
        WorldTerrainMath.BaseHeight(GeneratedElevation(worldX, worldZ), worldX, worldZ, _landforms);

    /// <summary>The finished ground height at a world point.</summary>
    public float Height(float worldX, float worldZ) =>
        WorldTerrainMath.Height(
            GeneratedElevation(worldX, worldZ), worldX, worldZ, _landforms, _paths, _areas);

    /// <summary>Route/activity masks a shader or a scatter planner needs, in world space.</summary>
    public float PathMask(float worldX, float worldZ) => WorldTerrainMath.PathMask(worldX, worldZ, _paths);

    public float AreaMask(float worldX, float worldZ) => WorldTerrainMath.GroundAreaMask(worldX, worldZ, _areas);

    /// <summary>
    /// The surface of generated water at a point, or null on dry ground. ⚠️ This is a natural
    /// river or lake found by the drainage solve, NOT an authored <c>WorldWaterResource</c>;
    /// <see cref="WorldWater"/> consults both so the non-swimming safety contract covers each.
    /// </summary>
    public float? GeneratedWaterSurface(float worldX, float worldZ)
    {
        if (_hydrology == null)
        {
            return null;
        }

        float? surface = WorldGenerator.Sample(
            _settings, _macro, _hydrology, worldX, worldZ, RouteCalmAt(worldX, worldZ)).WaterSurface;
        // A river carved into ground an author then raised — a causeway, a bridge pier, a levelled
        // pad — is not water any more. Compare against the FINISHED ground, not the carved one.
        return surface != null && surface.Value > Height(worldX, worldZ) ? surface : null;
    }

    /// <summary>
    /// Whether the drainage solve put any channel at all inside a rectangle. Reads the coarse
    /// hydrology grid directly, so it costs a few dozen array lookups rather than a full sample per
    /// square metre.
    ///
    /// WARNING: IT IS A CHEAP "NO", NOT A CHEAP "YES". It answers on the flow grid alone, so it can
    /// say true for a rectangle whose water all turns out to sit below the finished ground. That is
    /// the right way round for its one job - letting a dry cell skip building a water grid it would
    /// have thrown away - and the wrong way round for anything that needs to know where water IS,
    /// which is what GeneratedWaterSurface is for.
    /// </summary>
    public bool MayHaveGeneratedWater(float minX, float minZ, float maxX, float maxZ) =>
        _hydrology?.HasChannel(minX, minZ, maxX, maxZ) ?? false;

    /// <summary>
    /// One complete deterministic query: elevation, surface geometry, climate, biome weights,
    /// hydrology and authored influence at a world point. This is the query ecology, materials,
    /// landmark suitability and the debug visualiser all read, so none of them can disagree with
    /// the ground.
    /// </summary>
    public WorldSample Sample(float worldX, float worldZ) => Build(worldX, worldZ, authored: true);

    /// <summary>The generated half alone — no roads, no yards, no landforms in the elevation. What
    /// a suitability test wants when it is asking what KIND of country this is.</summary>
    public WorldSample SampleEnvironment(float worldX, float worldZ) =>
        Build(worldX, worldZ, authored: false);

    private WorldSample Build(float x, float z, bool authored)
    {
        ProceduralSample p = WorldGenerator.Sample(_settings, _macro, _hydrology, x, z, RouteCalmAt(x, z));
        float here = authored
            ? WorldTerrainMath.Height(p.Elevation, x, z, _landforms, _paths, _areas)
            : p.Elevation;

        // Finite differences on the SAME function the caller gets back, so a normal always belongs
        // to the elevation beside it. One metre matches the mesh builder's own normal step closely
        // enough that shading and gameplay agree about a slope.
        const float step = 1f;
        float left = HeightFor(x - step, z, authored);
        float right = HeightFor(x + step, z, authored);
        float back = HeightFor(x, z - step, authored);
        float forward = HeightFor(x, z + step, authored);

        float nx = left - right;
        float ny = 2f * step;
        float nz = back - forward;
        float length = MathF.Sqrt((nx * nx) + (ny * ny) + (nz * nz));
        if (length <= 0f)
        {
            nx = 0f;
            ny = 1f;
            nz = 0f;
            length = 1f;
        }

        float dx = (right - here) / step;
        float dz = (forward - here) / step;
        float slope = MathF.Sqrt((dx * dx) + (dz * dz));
        // Positive is a bowl, negative is a crest. Valley floors and ridge lines are the two things
        // ecology and materials most want to tell apart, and neither shows up in the slope alone.
        float curvature = (left + right + back + forward - (4f * here)) / (step * step);

        return new WorldSample(
            here, p.UncarvedElevation, nx / length, ny / length, nz / length,
            slope, curvature, p.Continentalness, p.Mountain, p.Erosion, p.Valley,
            p.Temperature, p.Moisture, p.Lowland, p.Wetland, p.Alpine, p.Barren,
            p.River, p.WaterProximity, p.Wetness,
            authored ? PathMask(x, z) : 0f,
            authored ? AreaMask(x, z) : 0f);
    }

    private float HeightFor(float x, float z, bool authored)
    {
        ProceduralSample p = WorldGenerator.Sample(_settings, _macro, _hydrology, x, z, RouteCalmAt(x, z));
        return authored
            ? WorldTerrainMath.Height(p.Elevation, x, z, _landforms, _paths, _areas)
            : p.Elevation;
    }

    /// <summary>
    /// A view over only the primitives that can influence the rectangle
    /// [<paramref name="minX"/>..<paramref name="maxX"/>] × [<paramref name="minZ"/>..<paramref name="maxZ"/>].
    /// Callers pass the cell envelope plus a margin large enough for the widest shoulder.
    /// ⚠️ The hydrology solution is passed on by reference and is NOT clipped — see the type remarks.
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

        // WARNING: THE CORRIDORS ARE CLIPPED TOO, AND THE MARGIN HAS TO COVER THEIR CALM RADIUS.
        // They were left unclipped at first on the theory that a road just outside a cell must still
        // calm the ground inside it - which is true, and is what the margin is for. Unclipped, every
        // ground query in the realm walked all 142 authored routes and 39 yards, five times per
        // vertex for the normal, and a region load went from 4.6 seconds to 14.3. Clipping with a
        // margin of the calm reach is EXACT rather than approximate: a corridor further away than
        // its own reach contributes exactly zero, so both cells sharing an edge keep the same set
        // along that edge and the seam still cannot move.
        float corridorMargin = _settings.RouteCalm + margin;
        var corridorPaths = new List<WorldTerrainMath.Path>();
        foreach (WorldTerrainMath.Path path in _corridorPaths)
        {
            float reach = (path.Width * 0.5f) + path.Shoulder + corridorMargin;
            if (MathF.Max(path.StartX, path.EndX) + reach >= minX &&
                MathF.Min(path.StartX, path.EndX) - reach <= maxX &&
                MathF.Max(path.StartZ, path.EndZ) + reach >= minZ &&
                MathF.Min(path.StartZ, path.EndZ) - reach <= maxZ)
            {
                corridorPaths.Add(path);
            }
        }

        var corridorAreas = new List<WorldTerrainMath.GroundArea>();
        foreach (WorldTerrainMath.GroundArea area in _corridorAreas)
        {
            float reachX = area.RadiusX + area.Feather + corridorMargin;
            float reachZ = area.RadiusZ + area.Feather + corridorMargin;
            if (area.X + reachX >= minX && area.X - reachX <= maxX &&
                area.Z + reachZ >= minZ && area.Z - reachZ <= maxZ)
            {
                corridorAreas.Add(area);
            }
        }

        return new WorldHeightfield(
            _settings, _macro, _hydrology, landforms, paths, areas, corridorPaths, corridorAreas);
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
