using Godot;

namespace Embervale.World;

/// <summary>Builds an indexed, normal-bearing terrain mesh from the deterministic heightfield.</summary>
public static class WorldTerrainMeshBuilder
{
    public static ArrayMesh Build(
        WorldEnvironmentProfileResource region,
        WorldCellPresentationResource cell,
        Vector3 worldOrigin)
    {
        int resolution = Mathf.Clamp(cell.TopologyResolution, 4, 128);
        int row = resolution + 1;
        float stepX = cell.Width / resolution;
        float stepZ = cell.Depth / resolution;
        var vertices = new Vector3[row * row];
        var normals = new Vector3[row * row];
        var uvs = new Vector2[row * row];
        var colors = new Color[row * row];
        var indices = new int[resolution * resolution * 6];
        var paths = BuildPaths(cell);
        var areas = BuildAreas(cell);

        for (int z = 0; z <= resolution; z++)
        {
            float localZ = (-cell.Depth * 0.5f) + (z * stepZ);
            for (int x = 0; x <= resolution; x++)
            {
                float localX = (-cell.Width * 0.5f) + (x * stepX);
                int index = (z * row) + x;
                float height = Sample(region, cell, worldOrigin, localX, localZ, paths, areas);
                vertices[index] = new Vector3(localX, height, localZ);
                uvs[index] = new Vector2(x / (float)resolution, z / (float)resolution);
                colors[index] = new Color(
                    WorldTerrainMath.PathMask(localX, localZ, paths),
                    WorldTerrainMath.GroundAreaMask(localX, localZ, areas), 0f, 1f);

                if (x == 0 || z == 0 || x == resolution || z == resolution)
                {
                    normals[index] = Vector3.Up;
                }
                else
                {
                    float left = Sample(region, cell, worldOrigin, localX - stepX, localZ, paths, areas);
                    float right = Sample(region, cell, worldOrigin, localX + stepX, localZ, paths, areas);
                    float back = Sample(region, cell, worldOrigin, localX, localZ - stepZ, paths, areas);
                    float forward = Sample(region, cell, worldOrigin, localX, localZ + stepZ, paths, areas);
                    normals[index] = new Vector3(left - right, stepX + stepZ, back - forward).Normalized();
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
                indices[cursor++] = topLeft;
                indices[cursor++] = bottomLeft;
                indices[cursor++] = topRight;
                indices[cursor++] = topRight;
                indices[cursor++] = bottomLeft;
                indices[cursor++] = bottomRight;
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

    private static float Sample(
        WorldEnvironmentProfileResource region, WorldCellPresentationResource cell,
        Vector3 origin, float localX, float localZ,
        System.Collections.Generic.IReadOnlyList<WorldTerrainMath.Path> paths,
        System.Collections.Generic.IReadOnlyList<WorldTerrainMath.GroundArea> areas) =>
        WorldTerrainMath.Height(
            region.TerrainSeed + cell.Seed,
            origin.X + localX, origin.Z + localZ, localX, localZ,
            cell.Width, cell.Depth, region.Relief * cell.TopologyHeightScale, region.DetailScale,
            cell.RoadAxis, cell.RoadWidth, cell.RoadOffset, paths, areas);

    public static System.Collections.Generic.List<WorldTerrainMath.Path> BuildPaths(WorldCellPresentationResource cell)
    {
        var paths = new System.Collections.Generic.List<WorldTerrainMath.Path>(cell.Paths.Count);
        foreach (WorldPathSegmentResource? path in cell.Paths)
        {
            if (path != null && path.Width > 0f)
            {
                paths.Add(new WorldTerrainMath.Path(path.Start.X, path.Start.Y, path.End.X, path.End.Y,
                    path.Width, path.Shoulder));
            }
        }
        return paths;
    }

    public static System.Collections.Generic.List<WorldTerrainMath.GroundArea> BuildAreas(WorldCellPresentationResource cell)
    {
        var areas = new System.Collections.Generic.List<WorldTerrainMath.GroundArea>(cell.GroundAreas.Count);
        foreach (WorldGroundAreaResource? area in cell.GroundAreas)
        {
            if (area != null && area.Radius.X > 0f && area.Radius.Y > 0f)
            {
                areas.Add(new WorldTerrainMath.GroundArea(area.Center.X, area.Center.Y,
                    area.Radius.X, area.Radius.Y, area.Feather, area.SurfaceBlend));
            }
        }
        return areas;
    }
}
