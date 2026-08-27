using Godot;

namespace Embervale.World;

/// <summary>
/// Builds the visual world surface for one authored cell. Collision and navigation remain on the
/// cell's proven seam-safe slab; this node adds a centimetre-high, edge-neutral material skin and
/// non-playable silhouette ridges outside authored boundaries. It creates no persistent state.
/// </summary>
public sealed partial class WorldCellPresentation : Node3D
{
    private const string ShaderPath = "res://assets/shaders/world/world_surface.gdshader";
    private WorldEnvironmentProfileResource _region = null!;
    private WorldCellPresentationResource _cell = null!;

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
        AddChild(BuildSurface());
    }

    private MeshInstance3D BuildSurface()
    {
        var plane = new PlaneMesh
        {
            Size = new Vector2(_cell.Width, _cell.Depth),
            SubdivideWidth = 48,
            SubdivideDepth = 48,
        };

        Shader? shader = GD.Load<Shader>(ShaderPath);
        var material = new ShaderMaterial { Shader = shader };
        material.SetShaderParameter("surface_color", _region.SurfaceColor);
        material.SetShaderParameter("secondary_color", _region.SecondaryColor);
        material.SetShaderParameter("detail_color", _region.DetailColor);
        material.SetShaderParameter("road_color", _region.RoadColor);
        material.SetShaderParameter("cell_size", new Vector2(_cell.Width, _cell.Depth));
        material.SetShaderParameter("relief", _region.Relief);
        material.SetShaderParameter("detail_scale", _region.DetailScale);
        material.SetShaderParameter("road_axis", _cell.RoadAxis);
        material.SetShaderParameter("road_width", _cell.RoadWidth);
        material.SetShaderParameter("road_offset", _cell.RoadOffset);
        material.SetShaderParameter("seed", (float)_cell.Seed);
        material.SetShaderParameter("tint", _cell.Tint);
        material.SetShaderParameter("tint_strength", _cell.TintStrength);

        return new MeshInstance3D
        {
            Name = "SurfaceSkin",
            Mesh = plane,
            MaterialOverride = material,
            Position = new Vector3(0f, 0.012f, 0f),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
    }

}
