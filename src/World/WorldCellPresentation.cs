using Godot;

namespace Embervale.World;

/// <summary>
/// Builds one cell's ground: the rendered terrain surface and the static collider under it.
///
/// ⚠️ <b>THIS IS THE GROUND NOW, NOT A SKIN OVER IT (the 2026-08-29 geography overhaul).</b> Every
/// cell used to carry a 60×0.5×60 <c>BoxMesh</c> floor with a matching <c>BoxShape3D</c>, and this
/// node laid a 4 cm visual wobble 1.2 cm above it. Those slabs are deleted: the terrain mesh built
/// from the region's <see cref="WorldHeightfield"/> carries the collision, and because the collider
/// is parented into the cell's <c>NavigationRegion3D</c> before <see cref="CellNavBaker"/>'s
/// deferred bake, the navmesh follows the elevation with no extra wiring.
///
/// ⚠️ <b>THE COLLIDER IS BUILT IN <see cref="Attach"/>, NOT IN <c>_Ready</c>.</b> The baker defers one
/// idle turn precisely so runtime geometry is final before it parses colliders; a collider created
/// in this node's <c>_Ready</c> would still make that window, but the streamer attaches this before
/// the cell enters the tree, so building eagerly removes the ordering question entirely.
///
/// It creates no persistent state.
/// </summary>
public sealed partial class WorldCellPresentation : Node3D
{
    private const string ShaderPath = "res://assets/shaders/world/world_surface.gdshader";
    private MeshInstance3D? _surface;
    private StaticBody3D? _collider;

    /// <summary>
    /// Adds the terrain to <paramref name="cellRoot"/>. <paramref name="field"/> is the cell's
    /// clipped view (see <see cref="WorldTerrainMeshBuilder.ViewFor"/>). The rendered surface hangs off this node;
    /// the collider is parented to the cell's <c>Nav</c> region when it has one, because
    /// <c>geometry_parsed_geometry_type = 1</c> means the bake only sees static colliders that are
    /// descendants of the <see cref="NavigationRegion3D"/>.
    /// </summary>
    public static void Attach(
        Node3D cellRoot,
        WorldEnvironmentProfileResource? region,
        WorldCellPresentationResource? cell,
        WorldHeightfield? field,
        Vector3 worldOrigin)
    {
        if (region == null || cell == null || field == null)
        {
            return;
        }

        ArrayMesh topology = WorldTerrainMeshBuilder.Build(field, cell, worldOrigin);

        var presentation = new WorldCellPresentation { Name = "WorldPresentation" };
        presentation._surface = BuildSurface(region, cell, topology, worldOrigin);
        presentation.AddChild(presentation._surface);
        cellRoot.AddChild(presentation);

        var collider = new StaticBody3D { Name = "TerrainCollider" };
        collider.AddChild(new CollisionShape3D
        {
            Name = "Shape",
            Shape = WorldTerrainMeshBuilder.BuildCollision(field, cell, worldOrigin),
        });
        presentation._collider = collider;
        (cellRoot.GetNodeOrNull<NavigationRegion3D>("Nav") ?? (Node)cellRoot).AddChild(collider);
    }

    public override void _ExitTree()
    {
        if (_surface != null)
        {
            Material? material = _surface.MaterialOverride;
            Mesh? mesh = _surface.Mesh;
            _surface.MaterialOverride = null;
            _surface.Mesh = null;
            material?.Dispose();
            mesh?.Dispose();
            _surface = null;
        }

        if (_collider != null)
        {
            if (_collider.GetNodeOrNull<CollisionShape3D>("Shape") is { } shapeNode)
            {
                Shape3D? shape = shapeNode.Shape;
                shapeNode.Shape = null;
                shape?.Dispose();
            }
            _collider = null;
        }
    }

    private static MeshInstance3D BuildSurface(
        WorldEnvironmentProfileResource region, WorldCellPresentationResource cell,
        ArrayMesh topology, Vector3 worldOrigin)
    {
        Shader? shader = GD.Load<Shader>(ShaderPath);
        var material = new ShaderMaterial { Shader = shader };
        material.SetShaderParameter("surface_color", region.SurfaceColor);
        material.SetShaderParameter("secondary_color", region.SecondaryColor);
        material.SetShaderParameter("detail_color", region.DetailColor);
        material.SetShaderParameter("road_color", region.RoadColor);
        material.SetShaderParameter("cell_size", new Vector2(cell.Width, cell.Depth));
        material.SetShaderParameter("detail_scale", region.DetailScale);
        material.SetShaderParameter("tint", cell.Tint);
        material.SetShaderParameter("tint_strength", cell.TintStrength);
        material.SetShaderParameter("world_origin", new Vector2(worldOrigin.X, worldOrigin.Z));
        material.SetShaderParameter("terrain_seed", (float)region.TerrainSeed);
        material.SetShaderParameter("surface_roughness", region.SurfaceRoughness);
        material.SetShaderParameter("detail_roughness", region.DetailRoughness);
        material.SetShaderParameter("road_roughness", region.RoadRoughness);
        material.SetShaderParameter("slope_blend", new Vector2(region.SlopeBlendStart, region.SlopeBlendEnd));
        material.SetShaderParameter("height_blend", new Vector2(region.HeightBlendStart, region.HeightBlendEnd));

        return new MeshInstance3D
        {
            Name = "SurfaceSkin",
            Mesh = topology,
            MaterialOverride = material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.On,
        };
    }
}
