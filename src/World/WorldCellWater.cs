using Godot;

namespace Embervale.World;

/// <summary>
/// Draws one cell's declared water bodies as surface grids whose per-vertex depth comes from the
/// region heightfield.
///
/// ⚠️ <b>THE GRID EXISTS SO THE SHORELINE CAN BE THE TERRAIN'S.</b> A water surface used to be a
/// two-triangle <c>BoxMesh</c>, so the only thing the shader knew about the ground was nothing, and
/// the only available coastline was the rectangle's own edge — which is why every water body in the
/// realm had to be hand-sized to fit inside its basin and still put open water on dry land at the
/// corners. Sampling the field at every vertex and baking the depth into vertex red lets the surface
/// fade out exactly where the land rises through it. A body may then be authored generously larger
/// than its basin, which is the only way the join can be invisible.
///
/// Quads whose four corners are all above the waterline are dropped, so a large declared rectangle
/// over a small pool costs a small mesh.
/// </summary>
public sealed partial class WorldCellWater : Node3D
{
    private const string ShaderPath = "res://assets/shaders/world/world_water.gdshader";

    /// <summary>Target metres between surface samples. The shoreline is only as sharp as this.</summary>
    private const float SampleStep = 1.0f;

    /// <summary>Half-range of the signed depth encoded into vertex colour. See the encode comment.</summary>
    private const float DepthRange = 12f;

    public override void _ExitTree()
    {
        foreach (Node child in GetChildren())
        {
            if (child is not MeshInstance3D instance)
            {
                continue;
            }
            Material? material = instance.MaterialOverride;
            Mesh? mesh = instance.Mesh;
            instance.MaterialOverride = null;
            instance.Mesh = null;
            material?.Dispose();
            mesh?.Dispose();
        }
    }

    public static WorldCellWater? Attach(
        Node3D cellRoot, WorldCellPresentationResource? cell, WorldHeightfield? field, Vector3 worldOrigin)
    {
        if (cell == null || field == null || cell.Water.Count == 0)
        {
            return null;
        }

        var node = new WorldCellWater { Name = "Water" };
        // ⚠️ terrain_absolute or WorldTerrainConform lifts the whole surface by the ground under the
        // cell origin — a lake climbing a hillside. A waterline is an absolute height by definition.
        node.AddToGroup(WorldTerrainConform.AbsoluteGroup);

        foreach (WorldWaterResource? water in cell.Water)
        {
            if (water == null || water.Extent.X <= 0f || water.Extent.Y <= 0f)
            {
                continue;
            }

            if (BuildSurface(water, field, worldOrigin) is { } surface)
            {
                node.AddChild(surface);
            }
        }

        if (node.GetChildCount() == 0)
        {
            node.Free();
            return null;
        }

        cellRoot.AddChild(node);
        return node;
    }

    private static MeshInstance3D? BuildSurface(
        WorldWaterResource water, WorldHeightfield field, Vector3 worldOrigin)
    {
        int columns = Mathf.Clamp(Mathf.CeilToInt(water.Extent.X * 2f / SampleStep), 2, 160);
        int rows = Mathf.Clamp(Mathf.CeilToInt(water.Extent.Y * 2f / SampleStep), 2, 160);
        float stepX = water.Extent.X * 2f / columns;
        float stepZ = water.Extent.Y * 2f / rows;

        var depths = new float[(columns + 1) * (rows + 1)];
        var vertices = new Vector3[depths.Length];
        for (int z = 0; z <= rows; z++)
        {
            float localZ = water.Center.Y - water.Extent.Y + (z * stepZ);
            for (int x = 0; x <= columns; x++)
            {
                float localX = water.Center.X - water.Extent.X + (x * stepX);
                int index = (z * (columns + 1)) + x;
                float ground = field.Height(worldOrigin.X + localX, worldOrigin.Z + localZ);
                depths[index] = water.SurfaceY - ground;
                vertices[index] = new Vector3(localX, water.SurfaceY, localZ);
            }
        }

        var indices = new System.Collections.Generic.List<int>();
        for (int z = 0; z < rows; z++)
        {
            for (int x = 0; x < columns; x++)
            {
                int a = (z * (columns + 1)) + x;
                int b = a + 1;
                int c = a + columns + 1;
                int d = c + 1;
                // Drop the quad only when every corner is dry, so the fading margin always has a
                // triangle to fade on.
                if (depths[a] <= 0f && depths[b] <= 0f && depths[c] <= 0f && depths[d] <= 0f)
                {
                    continue;
                }
                indices.Add(a);
                indices.Add(b);
                indices.Add(c);
                indices.Add(b);
                indices.Add(d);
                indices.Add(c);
            }
        }

        if (indices.Count == 0)
        {
            return null;
        }

        var colors = new Color[depths.Length];
        for (int i = 0; i < depths.Length; i++)
        {
            // ⚠️ SIGNED AND BIASED TO 0.5, BECAUSE A VERTEX COLOUR IS EIGHT BITS OF UNORM.
            // ArrayMesh stores COLOR as RGBA8, so a raw depth would clamp at 1 m — every basin in
            // the realm would have read as exactly one metre deep — and a depth clamped at 0 would
            // put the alpha ramp's start at the shoreline and its end a full sample-step out to sea,
            // so the water would visibly climb the bank. Encoding (depth / range) about 0.5 keeps
            // the sign, so the zero crossing lands where the ground truly meets the waterline, and
            // costs about 9 cm of precision over a 12 m range.
            colors[i] = new Color(
                Mathf.Clamp(0.5f + (depths[i] / (2f * DepthRange)), 0f, 1f), 0f, 0f, 1f);
        }

        var normals = new Vector3[depths.Length];
        for (int i = 0; i < normals.Length; i++)
        {
            normals[i] = Vector3.Up;
        }

        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices;
        arrays[(int)Mesh.ArrayType.Normal] = normals;
        arrays[(int)Mesh.ArrayType.Color] = colors;
        arrays[(int)Mesh.ArrayType.Index] = indices.ToArray();

        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);

        var material = new ShaderMaterial { Shader = GD.Load<Shader>(ShaderPath) };
        Color shallow = water.ShallowColor.SrgbToLinear();
        Color deep = water.DeepColor.SrgbToLinear();
        material.SetShaderParameter("shallow_color", new Vector3(shallow.R, shallow.G, shallow.B));
        material.SetShaderParameter("deep_color", new Vector3(deep.R, deep.G, deep.B));
        material.SetShaderParameter("opaque_depth", water.OpaqueDepth);
        material.SetShaderParameter("depth_range", DepthRange);

        return new MeshInstance3D
        {
            Name = water.Id,
            Mesh = mesh,
            MaterialOverride = material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
    }
}
