using Godot;

namespace Embervale.World;

/// <summary>
/// Builds the visual terrain topology and blended surface material for one authored cell. Collision
/// and navigation remain on the cell's proven seam-safe slab; the generated mesh flattens at cell
/// edges and roads so it preserves the authored gameplay seams. It creates no persistent state.
/// </summary>
public sealed partial class WorldCellPresentation : Node3D
{
    private const string ShaderPath = "res://assets/shaders/world/world_surface.gdshader";
    private WorldEnvironmentProfileResource _region = null!;
    private WorldCellPresentationResource _cell = null!;
    private MeshInstance3D? _surface;

    public static void Attach(
        Node3D cellRoot,
        WorldEnvironmentProfileResource? region,
        WorldCellPresentationResource? cell)
    {
        if (region == null || cell == null)
        {
            return;
        }

        var presentation = new WorldCellPresentation
        {
            Name = "WorldPresentation",
            _region = region,
            _cell = cell,
        };
        cellRoot.AddChild(presentation);
    }

    public override void _Ready()
    {
        _surface = BuildSurface();
        AddChild(_surface);
    }

    public override void _ExitTree()
    {
        if (_surface == null)
        {
            return;
        }

        Material? material = _surface.MaterialOverride;
        Mesh? mesh = _surface.Mesh;
        _surface.MaterialOverride = null;
        _surface.Mesh = null;
        material?.Dispose();
        mesh?.Dispose();
        _surface = null;
    }

    private MeshInstance3D BuildSurface()
    {
        ArrayMesh topology = WorldTerrainMeshBuilder.Build(_region, _cell, GlobalPosition);

        Shader? shader = GD.Load<Shader>(ShaderPath);
        var material = new ShaderMaterial { Shader = shader };
        material.SetShaderParameter("surface_color", _region.SurfaceColor);
        material.SetShaderParameter("secondary_color", _region.SecondaryColor);
        material.SetShaderParameter("detail_color", _region.DetailColor);
        material.SetShaderParameter("road_color", _region.RoadColor);
        material.SetShaderParameter("cell_size", new Vector2(_cell.Width, _cell.Depth));
        material.SetShaderParameter("detail_scale", _region.DetailScale);
        material.SetShaderParameter("road_axis", _cell.RoadAxis);
        material.SetShaderParameter("road_width", _cell.RoadWidth);
        material.SetShaderParameter("road_offset", _cell.RoadOffset);
        material.SetShaderParameter("tint", _cell.Tint);
        material.SetShaderParameter("tint_strength", _cell.TintStrength);
        material.SetShaderParameter("world_origin", new Vector2(GlobalPosition.X, GlobalPosition.Z));
        material.SetShaderParameter("terrain_seed", (float)_region.TerrainSeed);
        material.SetShaderParameter("surface_roughness", _region.SurfaceRoughness);
        material.SetShaderParameter("detail_roughness", _region.DetailRoughness);
        material.SetShaderParameter("road_roughness", _region.RoadRoughness);
        material.SetShaderParameter("slope_blend", new Vector2(_region.SlopeBlendStart, _region.SlopeBlendEnd));
        material.SetShaderParameter("height_blend", new Vector2(_region.HeightBlendStart, _region.HeightBlendEnd));

        return new MeshInstance3D
        {
            Name = "SurfaceSkin",
            Mesh = topology,
            MaterialOverride = material,
            Position = new Vector3(0f, 0.012f, 0f),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
    }

}
