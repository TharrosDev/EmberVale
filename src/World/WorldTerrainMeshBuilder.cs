using System.Collections.Generic;
using Godot;

namespace Embervale.World;

/// <summary>
/// Turns the region's <see cref="WorldHeightfield"/> into the geometry one cell needs: a rendered
/// surface and the collision the player, the navmesh baker and every conformed prop stand on.
///
/// ⚠️ <b>NOTHING IS FADED AT THE EDGES ANY MORE (the 2026-08-29 geography overhaul).</b> Vertices and
/// normals are sampled straight out of the world-space field, including one row beyond each border,
/// so an abutting cell computes bit-identical heights and normals along the shared edge. The old
/// contract flattened the outer 24% of every cell to y = 0 instead, which met the letter of "seam
/// safe" by drawing the lattice on the ground in relief.
/// </summary>
public static class WorldTerrainMeshBuilder
{
    /// <summary>
    /// Pools every cell's authored geography, routes and yards into one world-space field for the
    /// whole region. Call once per region; hand cells <see cref="WorldHeightfield.ForBounds"/> views.
    /// </summary>
    public static WorldHeightfield HeightfieldFor(RegionResource region)
    {
        // ⚠️ CORRIDORS FIRST, AND BEFORE ANY HEIGHT IS RESOLVED. Authored routes and yards calm the
        // generated macro relief around themselves, and both a road grade and a levelling landform's
        // target are measured against the ground beneath them - so the calming has to already be in
        // the field those samples come out of. Attaching it afterwards measures everything against a
        // mountain the finished world does not have.
        WorldHeightfield world = WorldFor(region);

        var landforms = new List<WorldTerrainMath.Landform>();
        var pathSources = new List<(WorldPathSegmentResource Path, Vector3 Origin)>();
        var areaSources = new List<(WorldGroundAreaResource Area, Vector3 Origin)>();

        foreach (RegionCellResource cell in region.Cells)
        {
            WorldCellPresentationResource? presentation = cell?.Presentation;
            if (cell == null || presentation == null)
            {
                continue;
            }

            // A cell's relief scale is a local dial on a global noise field. Applying it per cell
            // would step the noise at every seam, so the field takes the region's relief and the
            // scale survives only as an amplitude the mesh builder cannot honour per vertex — which
            // is why TopologyHeightScale is documented as a noise dial and left at 1 nearly always.
            foreach (WorldLandformResource? form in presentation.Landforms)
            {
                if (form == null)
                {
                    continue;
                }
                float centreX = cell.Center.X + form.Center.X;
                float centreZ = cell.Center.Z + form.Center.Y;
                // ⚠️ AGAINST THE GENERATED GROUND, NOT BaseHeight. A levelling landform's target has
                // to be measured against the country the generator made, with no landforms applied —
                // including this one. Measuring against BaseHeight would resolve each landform
                // against the ones already stamped, so the order they happen to be authored in would
                // change the shape of the world.
                float height = form.ElevationMode == 1 && form.Flatten > 0.5f
                    ? world.GeneratedElevation(centreX, centreZ) + form.Height
                    : form.Height;
                landforms.Add(new WorldTerrainMath.Landform(
                    form.Shape == 1 ? WorldTerrainMath.LandformShape.Ridge : WorldTerrainMath.LandformShape.Mound,
                    centreX, centreZ,
                    cell.Center.X + form.End.X, cell.Center.Z + form.End.Y,
                    form.Extent.X, form.Extent.Y, form.Rotation, height, form.Falloff, form.Flatten,
                    form.Irregularity));
            }

            foreach (WorldPathSegmentResource? path in presentation.Paths)
            {
                if (path != null && path.Width > 0f)
                {
                    pathSources.Add((path, cell.Center));
                }
            }

            foreach (WorldGroundAreaResource? area in presentation.GroundAreas)
            {
                if (area != null && area.Radius.X > 0f && area.Radius.Y > 0f)
                {
                    areaSources.Add((area, cell.Center));
                }
            }
        }

        // ⚠️ TWO PASSES, AND THE ORDER IS THE PRIORITY MODEL. The region's generated world comes
        // first and knows nothing about authoring; landforms are stamped onto it; only then can a
        // road or a pad be levelled, because both are levelled AGAINST the ground beneath them.
        // Resolving those samples here rather than recursing is what keeps Height() non-recursive.
        WorldHeightfield baseField = world.WithAuthored(landforms, null, null);

        var paths = new List<WorldTerrainMath.Path>(pathSources.Count);
        foreach ((WorldPathSegmentResource path, Vector3 origin) in pathSources)
        {
            float startX = origin.X + path.Start.X;
            float startZ = origin.Z + path.Start.Y;
            float endX = origin.X + path.End.X;
            float endZ = origin.Z + path.End.Y;
            paths.Add(new WorldTerrainMath.Path(
                startX, startZ, endX, endZ, path.Width, path.Shoulder,
                baseField.BaseHeight(startX, startZ), baseField.BaseHeight(endX, endZ)));
        }

        var areas = new List<WorldTerrainMath.GroundArea>(areaSources.Count);
        foreach ((WorldGroundAreaResource area, Vector3 origin) in areaSources)
        {
            float centreX = origin.X + area.Center.X;
            float centreZ = origin.Z + area.Center.Y;
            // A RelativeToBase pad is an offset from the ground the generator put under it, resolved
            // to an absolute world Y exactly once, here, against the same pre-road field the roads
            // are graded against. After this line a pad is as absolute as it ever was — which is
            // what every collider, prop and building placed on it needs it to be.
            float elevation = area.ElevationMode == 1
                ? baseField.BaseHeight(centreX, centreZ) + area.Elevation
                : area.Elevation;
            areas.Add(new WorldTerrainMath.GroundArea(
                centreX, centreZ, area.Radius.X, area.Radius.Y, area.Feather, area.SurfaceBlend,
                elevation));
        }

        return baseField.WithAuthoredSurfaces(paths, areas);
    }

    /// <summary>
    /// The region's generated world with no authoring on it at all: the generator settings plus one
    /// hydrology solution over the region's bounds, grown by a margin so the drainage that reaches
    /// the edge cells was solved with real country beyond them rather than with a wall.
    ///
    /// ⚠️ <b>A REGION WITH NO <c>GenerationProfile</c> GETS THE PRE-GENERATOR NOISE FIELD.</b> That
    /// is deliberate and it is not a default: it reproduces a legacy region's exact old ground
    /// instead of moving the world under its buildings on the day someone forgets to author a
    /// profile. <c>ValidateRegions</c> fails a region that omits one, so the fallback can only be
    /// reached by a test or a region mid-authoring.
    /// </summary>
    public static WorldHeightfield WorldFor(RegionResource region)
    {
        const float hydrologyMargin = 96f;
        Aabb bounds = region.Bounds;
        float minX = bounds.Position.X - hydrologyMargin;
        float minZ = bounds.Position.Z - hydrologyMargin;
        float maxX = bounds.Position.X + bounds.Size.X + hydrologyMargin;
        float maxZ = bounds.Position.Z + bounds.Size.Z + hydrologyMargin;

        if (region.GenerationProfile is { } generation)
        {
            // Corridors up front: the drainage solve inside the constructor needs them.
            return new WorldHeightfield(
                generation.Settings(), minX, minZ, maxX, maxZ,
                CorridorPaths(region), CorridorAreas(region));
        }

        WorldEnvironmentProfileResource? profile = region.EnvironmentProfile;
        return new WorldHeightfield(
            profile?.TerrainSeed ?? 3800, profile?.Relief ?? 1f, profile?.DetailScale ?? 2.5f);
    }

    /// <summary>Every authored route in the region as world-space geometry, for the calm mask only.
    /// Heights are deliberately left at zero: nothing reads them here, and filling them in would
    /// make the mask depend on the field it is used to build.</summary>
    private static List<WorldTerrainMath.Path> CorridorPaths(RegionResource region)
    {
        var paths = new List<WorldTerrainMath.Path>();
        foreach (RegionCellResource? cell in region.Cells)
        {
            WorldCellPresentationResource? presentation = cell?.Presentation;
            if (cell == null || presentation == null)
            {
                continue;
            }

            foreach (WorldPathSegmentResource? path in presentation.Paths)
            {
                if (path != null && path.Width > 0f)
                {
                    paths.Add(new WorldTerrainMath.Path(
                        cell.Center.X + path.Start.X, cell.Center.Z + path.Start.Y,
                        cell.Center.X + path.End.X, cell.Center.Z + path.End.Y,
                        path.Width, path.Shoulder));
                }
            }
        }
        return paths;
    }

    /// <summary>Every authored yard as world-space geometry, for the calm mask only. Elevation is
    /// left at zero for the same reason: a relative pad has not been resolved yet.</summary>
    private static List<WorldTerrainMath.GroundArea> CorridorAreas(RegionResource region)
    {
        var areas = new List<WorldTerrainMath.GroundArea>();
        foreach (RegionCellResource? cell in region.Cells)
        {
            WorldCellPresentationResource? presentation = cell?.Presentation;
            if (cell == null || presentation == null)
            {
                continue;
            }

            foreach (WorldGroundAreaResource? area in presentation.GroundAreas)
            {
                if (area != null && area.Radius.X > 0f && area.Radius.Y > 0f)
                {
                    areas.Add(new WorldTerrainMath.GroundArea(
                        cell.Center.X + area.Center.X, cell.Center.Z + area.Center.Y,
                        area.Radius.X, area.Radius.Y, area.Feather, area.SurfaceBlend));
                }
            }
        }
        return areas;
    }

    /// <summary>The field clipped to one cell, plus the widest shoulder any authoring can reach with.</summary>
    public static WorldHeightfield ViewFor(
        WorldHeightfield field, WorldCellPresentationResource cell, Vector3 worldOrigin) =>
        field.ForBounds(
            worldOrigin.X - (cell.Width * 0.5f), worldOrigin.Z - (cell.Depth * 0.5f),
            worldOrigin.X + (cell.Width * 0.5f), worldOrigin.Z + (cell.Depth * 0.5f));

    /// <summary>
    /// One cell's rendered surface, sampled straight out of the world-space field.
    ///
    /// WARNING: NOTHING IS FADED AT THE EDGES (the 2026-08-29 geography overhaul). Vertices are
    /// taken from world coordinates, so an abutting cell computes bit-identical heights along the
    /// shared edge and seams match by construction rather than by flattening.
    ///
    /// WARNING: AND THE NORMAL STEP IS FIXED AT ONE METRE RATHER THAN THE CELL'S OWN VERTEX
    /// SPACING, WHICH IS NOT WHAT IT USED TO BE. Sampling the normal at (stepX, stepZ) looks more
    /// natural and is wrong at every seam in the realm: the Ember Crown's cells run from 50x90 to
    /// 200x110 at resolutions from 28 to 64, so two cells meeting at an edge sampled their shared
    /// vertices over different distances and got different normals from the same ground. That is a
    /// lighting crease down a seam whose geometry is perfect - the one artefact that survived the
    /// overhaul because the heights were checked and the normals were not. WorldSample uses one
    /// metre everywhere, so the whole realm agrees.
    ///
    /// The four vertex channels and UV2 carry the generated environment to the shader. They are what
    /// makes a biome an ECOTONE rather than a rectangle: the cell's authored biome profile still
    /// chooses the palette, and these continuous fields choose how much of each of its six layers is
    /// showing, across a cell boundary as smoothly as within one.
    /// </summary>
    public static ArrayMesh Build(
        WorldHeightfield field, WorldCellPresentationResource cell, Vector3 worldOrigin) =>
        Assemble(BuildData(field, cell, worldOrigin));

    /// <summary>
    /// The arithmetic half: coordinates in, arrays out, no Godot object touched.
    ///
    /// ⚠️ <b>THIS RUNS ON A WORKER THREAD</b> (see <see cref="WorldTerrainJobs"/>), so nothing in
    /// here may construct a Resource or a Node, and nothing may read mutable global state. The one
    /// piece of global state it does read is the developer visualiser's mode, which is captured once
    /// at the top rather than per vertex - a mode changed mid-build would otherwise paint half a
    /// cell with one field and half with another.
    /// </summary>
    public static WorldTerrainData BuildData(
        WorldHeightfield field, WorldCellPresentationResource cell, Vector3 worldOrigin)
    {
        int resolution = Mathf.Clamp(cell.TopologyResolution, 4, 160);
        int row = resolution + 1;
        float stepX = cell.Width / resolution;
        float stepZ = cell.Depth / resolution;
        var vertices = new Vector3[row * row];
        var normals = new Vector3[row * row];
        var uvs = new Vector2[row * row];
        var uv2s = new Vector2[row * row];
        var colors = new Color[row * row];
        var indices = new int[resolution * resolution * 6];

        WorldGenerationDebugMode debug = WorldGenerationDebug.Mode;

        for (int z = 0; z <= resolution; z++)
        {
            float localZ = (-cell.Depth * 0.5f) + (z * stepZ);
            float worldZ = worldOrigin.Z + localZ;
            for (int x = 0; x <= resolution; x++)
            {
                float localX = (-cell.Width * 0.5f) + (x * stepX);
                float worldX = worldOrigin.X + localX;
                int index = (z * row) + x;

                WorldSample sample = field.Sample(worldX, worldZ);
                vertices[index] = new Vector3(localX, sample.Elevation, localZ);
                normals[index] = new Vector3(sample.NormalX, sample.NormalY, sample.NormalZ);
                uvs[index] = new Vector2(x / (float)resolution, z / (float)resolution);

                if (debug == WorldGenerationDebugMode.None)
                {
                    // r road, g yard, b wet margin, a altitude cap. UV2 carries the two the colour
                    // ran out of room for.
                    colors[index] = new Color(
                        sample.RoadInfluence, sample.AuthoredInfluence, sample.Wetness,
                        sample.AlpineWeight);
                    uv2s[index] = new Vector2(sample.Moisture, sample.Mountain);
                }
                else
                {
                    float value = DebugValue(debug, sample, field, worldX, worldZ);
                    colors[index] = new Color(value, value, value, 1f);
                    uv2s[index] = new Vector2(value, value);
                }
            }
        }

        int cursor = 0;
        for (int z = 0; z < resolution; z++)
        {
            for (int x = 0; x < resolution; x++)
            {
                int topLeft = (z * row) + x;
                int topRight = topLeft + 1;
                int bottomLeft = topLeft + row;
                int bottomRight = bottomLeft + 1;
                // WARNING: AND THE RENDER MESH IS WOUND THE SAME WAY AS THE COLLISION. It was not,
                // and nobody could see it: the surface skin used to sit 1.2 cm above a solid BoxMesh
                // floor, so a back-facing terrain mesh was simply invisible and the floor underneath
                // was what the player looked at. Delete the floor - which is the whole point of the
                // 2026-08-29 overhaul - and the realm renders as props floating over open sky.
                indices[cursor++] = topLeft;
                indices[cursor++] = topRight;
                indices[cursor++] = bottomLeft;
                indices[cursor++] = topRight;
                indices[cursor++] = bottomRight;
                indices[cursor++] = bottomLeft;
            }
        }

        return new WorldTerrainData(
            vertices, normals, uvs, uv2s, colors, indices,
            BuildCollisionFaces(field, cell, worldOrigin));
    }

    /// <summary>The Godot half: arrays in, a mesh out. Main thread only.</summary>
    public static ArrayMesh Assemble(WorldTerrainData data)
    {
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = data.Vertices;
        arrays[(int)Mesh.ArrayType.Normal] = data.Normals;
        arrays[(int)Mesh.ArrayType.TexUV] = data.Uvs;
        arrays[(int)Mesh.ArrayType.TexUV2] = data.Uv2s;
        arrays[(int)Mesh.ArrayType.Color] = data.Colors;
        arrays[(int)Mesh.ArrayType.Index] = data.Indices;

        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return mesh;
    }

    /// <summary>Wraps pre-computed faces in the collision shape. Main thread only.</summary>
    public static ConcavePolygonShape3D AssembleCollision(WorldTerrainData data)
    {
        var shape = new ConcavePolygonShape3D();
        shape.SetFaces(data.CollisionFaces);
        return shape;
    }

    /// <summary>
    /// One generated field, remapped to 0..1 for the developer visualiser. Turn it on with the
    /// `worldgen &lt;mode&gt;` console command and reload the region.
    ///
    /// WARNING: the ranges here are display ranges, not clamps on the generator. Elevation is shown
    /// over a fixed 120 m window so two regions can be compared by eye; a field that already lives
    /// in 0..1 is passed through untouched.
    /// </summary>
    private static float DebugValue(
        WorldGenerationDebugMode mode, WorldSample sample, WorldHeightfield field, float x, float z) =>
        mode switch
        {
            WorldGenerationDebugMode.Elevation => Mathf.Clamp((sample.Elevation + 40f) / 120f, 0f, 1f),
            WorldGenerationDebugMode.Continentalness => sample.Continentalness,
            WorldGenerationDebugMode.Mountains => sample.Mountain,
            WorldGenerationDebugMode.Erosion => sample.Erosion,
            WorldGenerationDebugMode.Valleys => sample.Valley,
            WorldGenerationDebugMode.Temperature => sample.Temperature,
            WorldGenerationDebugMode.Moisture => sample.Moisture,
            WorldGenerationDebugMode.LowlandBiome => sample.LowlandWeight,
            WorldGenerationDebugMode.WetlandBiome => sample.WetlandWeight,
            WorldGenerationDebugMode.AlpineBiome => sample.AlpineWeight,
            WorldGenerationDebugMode.BarrenBiome => sample.BarrenWeight,
            WorldGenerationDebugMode.Slope => Mathf.Clamp(sample.Slope, 0f, 1f),
            WorldGenerationDebugMode.Rivers => sample.RiverInfluence,
            WorldGenerationDebugMode.WaterProximity => sample.WaterProximity,
            WorldGenerationDebugMode.Wetness => sample.Wetness,
            WorldGenerationDebugMode.Roads => sample.RoadInfluence,
            WorldGenerationDebugMode.AuthoredStamps =>
                Mathf.Max(sample.AuthoredInfluence, field.RouteCalmAt(x, z)),
            _ => 0f,
        };

    /// <summary>
    /// The collision face soup for one cell. Built at its own coarser resolution: the navmesh voxel
    /// grid is 0.3–0.5 m and a walking capsule cannot feel a 2.5 m triangle, so paying render
    /// tessellation twice would multiply the physics broadphase for nothing.
    ///
    /// ⚠️ <b>THE WINDING MATCHES THE RENDER MESH'S EXACTLY, AND IT HAS TO.</b> The comment that stood
    /// here said the two were deliberately opposite; they are not, and had not been since the flip it
    /// describes. Both loops emit (topLeft, topRight, bottomLeft) + (topRight, bottomRight,
    /// bottomLeft) over the same lattice, which is Godot's own <c>PlaneMesh</c> order and faces +Y.
    /// That is what the navmesh baker needs: a soup wound the other way is read as NOTHING BUT
    /// ROOFTOPS, which is what shipped for an afternoon — six transitional cells baked zero
    /// navigation polygons and the town hub baked 59 (its buildings) instead of 576. Physics does not
    /// care either way, so nothing but the bake will tell you. If nav ever comes back empty on a cell
    /// with no props in it, compare this loop against <see cref="Build"/>'s: they must agree.
    /// </summary>
    public static ConcavePolygonShape3D BuildCollision(
        WorldHeightfield field, WorldCellPresentationResource cell, Vector3 worldOrigin)
    {
        var shape = new ConcavePolygonShape3D();
        shape.SetFaces(BuildCollisionFaces(field, cell, worldOrigin));
        return shape;
    }

    /// <summary>The face soup as plain vectors, so it can be computed on a worker.</summary>
    private static Vector3[] BuildCollisionFaces(
        WorldHeightfield field, WorldCellPresentationResource cell, Vector3 worldOrigin)
    {
        // ⚠️ 1.5 m, NOT THE 2.5 m THIS SHIPPED WITH, AND THE OLD NUMBER WAS RIGHT FOR THE OLD WORLD.
        // The comment above still holds - a walking capsule cannot feel a triangle - but what it can
        // feel is the ground being in a different place from the picture of it. A collision lattice
        // interpolates across its own quads, so its disagreement with the rendered surface is set by
        // how much the terrain moves between collision vertices. Over a field that was two octaves
        // of noise and never a metre and a half from zero, 2.5 m of spacing cost centimetres. Over
        // real relief it cost up to 72 cm on ground the player can WALK, which is more than the half
        // metre they can step: they float over a rise and sink into a dip, and it reads as the
        // terrain being wrong rather than the collider being coarse. The test that found it is
        // CollisionSamplesAgreeWithRenderedGround, and it only checks ground under the walk limit
        // because a coarse lattice cutting the corner off a cliff is a true statement about cliffs.
        int resolution = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(cell.Width, cell.Depth) / 2.0f), 4, 128);
        int row = resolution + 1;
        float stepX = cell.Width / resolution;
        float stepZ = cell.Depth / resolution;
        var grid = new Vector3[row * row];
        for (int z = 0; z <= resolution; z++)
        {
            float localZ = (-cell.Depth * 0.5f) + (z * stepZ);
            for (int x = 0; x <= resolution; x++)
            {
                float localX = (-cell.Width * 0.5f) + (x * stepX);
                grid[(z * row) + x] = new Vector3(
                    localX, field.Height(worldOrigin.X + localX, worldOrigin.Z + localZ), localZ);
            }
        }

        var faces = new Vector3[resolution * resolution * 6];
        int cursor = 0;
        for (int z = 0; z < resolution; z++)
        {
            for (int x = 0; x < resolution; x++)
            {
                Vector3 topLeft = grid[(z * row) + x];
                Vector3 topRight = grid[(z * row) + x + 1];
                Vector3 bottomLeft = grid[((z + 1) * row) + x];
                Vector3 bottomRight = grid[((z + 1) * row) + x + 1];
                faces[cursor++] = topLeft;
                faces[cursor++] = topRight;
                faces[cursor++] = bottomLeft;
                faces[cursor++] = topRight;
                faces[cursor++] = bottomRight;
                faces[cursor++] = bottomLeft;
            }
        }

        return faces;
    }
}
