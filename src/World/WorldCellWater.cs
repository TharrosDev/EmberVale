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
        if (cell == null || field == null)
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

        if (BuildGeneratedSurface(cell, field, worldOrigin) is { } rivers)
        {
            node.AddChild(rivers);
        }

        if (node.GetChildCount() == 0)
        {
            node.Free();
            return null;
        }

        cellRoot.AddChild(node);
        return node;
    }

    /// <summary>Metres between samples when hunting for generated water. Coarser than
    /// <see cref="SampleStep"/> on purpose: this grid covers a whole cell rather than one authored
    /// rectangle, and a river is several metres wide, so sampling it at a metre would cost ten
    /// thousand heightfield queries per cell to find the one percent of ground that is wet.</summary>
    private const float RiverSampleStep = 2f;

    /// <summary>
    /// The rivers and lakes the drainage solve put in this cell, drawn with the same grid, the same
    /// signed-depth encoding and the same shader as an authored body.
    ///
    /// WARNING: THE SURFACE HEIGHT VARIES PER VERTEX HERE AND IT DOES NOT FOR AN AUTHORED BODY.
    /// A lake is one waterline by definition; a river runs downhill, so its surface follows the
    /// channel. That is the only structural difference between the two paths, and it is why this
    /// cannot simply call <see cref="BuildSurface"/> with a synthetic rectangle.
    ///
    /// ponytail: colours are borrowed from the cell's own authored water when it has any, and fall
    /// back to a neutral cold water otherwise. Give rivers their own palette on the generation
    /// profile if a realm ever needs water that reads differently from its lakes.
    /// </summary>
    private static MeshInstance3D? BuildGeneratedSurface(
        WorldCellPresentationResource cell, WorldHeightfield field, Vector3 worldOrigin)
    {
        // Ask the coarse drainage grid before building anything. Most cells in both realms have no
        // channel in them at all, and without this every one of them paid for several thousand full
        // generator queries to discover that.
        if (!field.MayHaveGeneratedWater(
                worldOrigin.X - (cell.Width * 0.5f), worldOrigin.Z - (cell.Depth * 0.5f),
                worldOrigin.X + (cell.Width * 0.5f), worldOrigin.Z + (cell.Depth * 0.5f)))
        {
            return null;
        }

        int columns = Mathf.Clamp(Mathf.CeilToInt(cell.Width / RiverSampleStep), 2, 160);
        int rows = Mathf.Clamp(Mathf.CeilToInt(cell.Depth / RiverSampleStep), 2, 160);
        float stepX = cell.Width / columns;
        float stepZ = cell.Depth / rows;

        int count = (columns + 1) * (rows + 1);
        var depths = new float[count];
        var vertices = new Vector3[count];
        bool anyWet = false;
        for (int z = 0; z <= rows; z++)
        {
            float localZ = (-cell.Depth * 0.5f) + (z * stepZ);
            for (int x = 0; x <= columns; x++)
            {
                float localX = (-cell.Width * 0.5f) + (x * stepX);
                int index = (z * (columns + 1)) + x;
                float worldX = worldOrigin.X + localX;
                float worldZ = worldOrigin.Z + localZ;
                float ground = field.Height(worldX, worldZ);
                float? surface = field.GeneratedWaterSurface(worldX, worldZ);
                // A dry vertex still carries the GROUND height, so the fading margin has somewhere
                // to fade to. Giving it the waterline instead lays a flat lip of surface on the bank.
                depths[index] = surface == null ? 0f : surface.Value - ground;
                vertices[index] = new Vector3(localX, surface ?? ground, localZ);
                anyWet |= depths[index] > 0f;
            }
        }

        if (!anyWet)
        {
            return null;
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

        WorldWaterResource? palette = null;
        foreach (WorldWaterResource? water in cell.Water)
        {
            if (water != null)
            {
                palette = water;
                break;
            }
        }

        return BuildInstance(
            "GeneratedWater", vertices, depths, indices,
            palette?.ShallowColor ?? new Color(0.24f, 0.36f, 0.36f),
            palette?.DeepColor ?? new Color(0.06f, 0.14f, 0.18f),
            palette?.OpaqueDepth ?? 2.2f);
    }

    /// <summary>The shared tail of both surface builders: encode depth, assemble the mesh, dress it
    /// with the water shader. Split out so an authored lake and a generated river cannot drift apart
    /// in how they read their own depth - the encoding below is subtle enough that two copies of it
    /// would, and the one that drifted would be the one nobody was looking at.</summary>
    private static MeshInstance3D BuildInstance(
        string name, Vector3[] vertices, float[] depths,
        System.Collections.Generic.List<int> indices,
        Color shallowSrgb, Color deepSrgb, float opaqueDepth)
    {
        var colors = new Color[depths.Length];
        for (int i = 0; i < depths.Length; i++)
        {
            // WARNING: SIGNED AND BIASED TO 0.5, BECAUSE A VERTEX COLOUR IS EIGHT BITS OF UNORM.
            // ArrayMesh stores COLOR as RGBA8, so a raw depth would clamp at 1 m - every basin in
            // the realm would have read as exactly one metre deep - and a depth clamped at 0 would
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
        Color shallow = shallowSrgb.SrgbToLinear();
        Color deep = deepSrgb.SrgbToLinear();
        material.SetShaderParameter("shallow_color", new Vector3(shallow.R, shallow.G, shallow.B));
        material.SetShaderParameter("deep_color", new Vector3(deep.R, deep.G, deep.B));
        material.SetShaderParameter("opaque_depth", opaqueDepth);
        material.SetShaderParameter("depth_range", DepthRange);

        return new MeshInstance3D
        {
            Name = name,
            Mesh = mesh,
            MaterialOverride = material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
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

        return BuildInstance(
            water.Id, vertices, depths, indices, water.ShallowColor, water.DeepColor,
            water.OpaqueDepth);
    }
}
