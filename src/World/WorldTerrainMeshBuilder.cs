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
        WorldEnvironmentProfileResource? profile = region.EnvironmentProfile;
        int seed = profile?.TerrainSeed ?? 3800;
        float relief = profile?.Relief ?? 1f;
        float detailScale = profile?.DetailScale ?? 2.5f;

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
                landforms.Add(new WorldTerrainMath.Landform(
                    form.Shape == 1 ? WorldTerrainMath.LandformShape.Ridge : WorldTerrainMath.LandformShape.Mound,
                    cell.Center.X + form.Center.X, cell.Center.Z + form.Center.Y,
                    cell.Center.X + form.End.X, cell.Center.Z + form.End.Y,
                    form.Extent.X, form.Extent.Y, form.Rotation, form.Height, form.Falloff, form.Flatten,
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

        // Roads grade between the base field at their own endpoints, so those two samples must be
        // taken against noise + landforms only. Resolve them once here rather than recursing.
        var baseField = new WorldHeightfield(seed, relief, detailScale, landforms);
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
            areas.Add(new WorldTerrainMath.GroundArea(
                origin.X + area.Center.X, origin.Z + area.Center.Y,
                area.Radius.X, area.Radius.Y, area.Feather, area.SurfaceBlend, area.Elevation));
        }

        return new WorldHeightfield(seed, relief, detailScale, landforms, paths, areas);
    }

    /// <summary>The field clipped to one cell, plus the widest shoulder any authoring can reach with.</summary>
    public static WorldHeightfield ViewFor(
        WorldHeightfield field, WorldCellPresentationResource cell, Vector3 worldOrigin) =>
        field.ForBounds(
            worldOrigin.X - (cell.Width * 0.5f), worldOrigin.Z - (cell.Depth * 0.5f),
            worldOrigin.X + (cell.Width * 0.5f), worldOrigin.Z + (cell.Depth * 0.5f));

    public static ArrayMesh Build(WorldHeightfield field, WorldCellPresentationResource cell, Vector3 worldOrigin)
    {
        int resolution = Mathf.Clamp(cell.TopologyResolution, 4, 160);
        int row = resolution + 1;
        float stepX = cell.Width / resolution;
        float stepZ = cell.Depth / resolution;
        var vertices = new Vector3[row * row];
        var normals = new Vector3[row * row];
        var uvs = new Vector2[row * row];
        var colors = new Color[row * row];
        var indices = new int[resolution * resolution * 6];

        for (int z = 0; z <= resolution; z++)
        {
            float localZ = (-cell.Depth * 0.5f) + (z * stepZ);
            float worldZ = worldOrigin.Z + localZ;
            for (int x = 0; x <= resolution; x++)
            {
                float localX = (-cell.Width * 0.5f) + (x * stepX);
                float worldX = worldOrigin.X + localX;
                int index = (z * row) + x;
                vertices[index] = new Vector3(localX, field.Height(worldX, worldZ), localZ);
                uvs[index] = new Vector2(x / (float)resolution, z / (float)resolution);
                colors[index] = new Color(field.PathMask(worldX, worldZ), field.AreaMask(worldX, worldZ), 0f, 1f);

                // Sampled across the border too: the neighbour's first interior row produces the
                // same four samples, so lighting does not crease at a seam and nothing needs faking.
                float left = field.Height(worldX - stepX, worldZ);
                float right = field.Height(worldX + stepX, worldZ);
                float back = field.Height(worldX, worldZ - stepZ);
                float forward = field.Height(worldX, worldZ + stepZ);
                normals[index] = new Vector3(left - right, stepX + stepZ, back - forward).Normalized();
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
                // ⚠️ AND THE RENDER MESH IS WOUND THE SAME WAY AS THE COLLISION. It was not, and
                // nobody could see it: the surface skin used to sit 1.2 cm above a solid BoxMesh
                // floor, so a back-facing terrain mesh was simply invisible and the floor underneath
                // was what the player looked at. Delete the floor — which is the whole point of the
                // 2026-08-29 overhaul — and the realm renders as props floating over open sky.
                indices[cursor++] = topLeft;
                indices[cursor++] = topRight;
                indices[cursor++] = bottomLeft;
                indices[cursor++] = topRight;
                indices[cursor++] = bottomRight;
                indices[cursor++] = bottomLeft;
            }
        }

        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices;
        arrays[(int)Mesh.ArrayType.Normal] = normals;
        arrays[(int)Mesh.ArrayType.TexUV] = uvs;
        arrays[(int)Mesh.ArrayType.Color] = colors;
        arrays[(int)Mesh.ArrayType.Index] = indices;

        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return mesh;
    }

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
        int resolution = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(cell.Width, cell.Depth) / 2.5f), 4, 96);
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

        var shape = new ConcavePolygonShape3D();
        shape.SetFaces(faces);
        return shape;
    }
}
