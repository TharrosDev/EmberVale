using Godot;

namespace Embervale.World;

/// <summary>
/// A dragon lair cell (Phase 35F), promoted out of the two hand-authored roosts 35D and 35E shipped
/// as copies of each other. Those two differed only in floor size, floor colour, props and which
/// dragon slept there — everything structural (the <see cref="NavigationRegion3D"/>, the
/// <see cref="CellNavBaker"/>, the floor mesh + collider, the lair marker) was the same scene twice,
/// which is why both phases' notes said a third roost should promote it rather than become a third
/// copy.
///
/// <c>scenes/regions/roost.tscn</c> owns that structure; each roost is an inherited scene that
/// overrides the four knobs here, the marker's <c>PersistentId</c> and the lair's <c>TemplateId</c>,
/// and adds its own props under <c>Nav</c> (they must be children of the navigation region or the
/// bake will not carve them out).
///
/// The floor is sized here rather than in each scene's own <c>BoxMesh</c> because mesh, shape and
/// material are sub-resources of the *base* scene and are therefore shared by every roost that
/// inherits it: a third roost setting a size would have moved the other two. Every one of them is
/// <see cref="Resource.Duplicate"/>d before it is touched — the same rule 34F's affliction learned
/// about tinting a shared material.
/// </summary>
[GlobalClass]
public partial class RoostCell : Node3D
{
    /// <summary>Side length of the square floor, in metres. Size it to the occupant's territory
    /// radius (35D) and butt it against the neighbouring cell's floor rather than overlapping —
    /// co-planar floors z-fight.</summary>
    [Export] public float FloorSize { get; set; } = 90f;

    /// <summary>Base ground colour.</summary>
    [Export] public Color FloorColor { get; set; } = new(0.46f, 0.48f, 0.53f);

    /// <summary>Ember glow worked into the ground — the ash/ember language the corruption systems
    /// use, so a corrupted place reads like a corrupted creature (ART_STYLE §2.2).</summary>
    [Export] public Color EmberColor { get; set; } = new(0.82f, 0.34f, 0.1f);

    /// <summary>Strength of that glow. Effectively off at 0.</summary>
    [Export] public float EmberEnergy { get; set; }

    private const float FloorThickness = 0.5f;

    public override void _Ready()
    {
        var size = new Vector3(FloorSize, FloorThickness, FloorSize);

        if (GetNodeOrNull<MeshInstance3D>("Nav/Floor") is { Mesh: BoxMesh } floor)
        {
            var mesh = (BoxMesh)floor.Mesh.Duplicate();
            mesh.Size = size;
            floor.Mesh = mesh;
            floor.SetSurfaceOverrideMaterial(0, BuildMaterial());
        }

        if (GetNodeOrNull<CollisionShape3D>("Nav/FloorCol/Shape") is { Shape: BoxShape3D } collider)
        {
            var shape = (BoxShape3D)collider.Shape.Duplicate();
            shape.Size = size;
            collider.Shape = shape;
        }
    }

    private StandardMaterial3D BuildMaterial() => new()
    {
        AlbedoColor = FloorColor,
        Roughness = 0.95f,
        EmissionEnabled = EmberEnergy > 0f,
        Emission = EmberColor,
        EmissionEnergyMultiplier = EmberEnergy,
    };
}
