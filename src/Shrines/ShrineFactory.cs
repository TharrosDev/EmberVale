using Embervale.Entities;
using Embervale.Localization;
using Godot;

namespace Embervale.Shrines;

/// <summary>Builds the 41.5A sandbox witness shrine. It deliberately uses primitives: the core needs
/// a readable in-world caller now, while 41.5B owns the six gods' final authored models and places.</summary>
internal static class ShrineFactory
{
    internal static Entity Create(string shrineId, Vector3 position)
    {
        ShrineResource? shrine = ShrineDatabase.Get(shrineId);
        var entity = new Entity
        {
            Name = "SolarynShrine",
            DisplayName = shrine is null ? shrineId : Loc.T(shrine.NameKey),
            Position = position,
        };

        entity.AddChild(new MeshInstance3D
        {
            Name = "Base",
            Mesh = new CylinderMesh { TopRadius = 0.70f, BottomRadius = 0.90f, Height = 0.35f },
            Position = new Vector3(0f, 0.175f, 0f),
            MaterialOverride = Stone(new Color(0.38f, 0.32f, 0.24f)),
        });
        entity.AddChild(new MeshInstance3D
        {
            Name = "Pillar",
            Mesh = new CylinderMesh { TopRadius = 0.23f, BottomRadius = 0.30f, Height = 1.35f },
            Position = new Vector3(0f, 0.85f, 0f),
            MaterialOverride = Stone(new Color(0.52f, 0.45f, 0.32f)),
        });
        entity.AddChild(new MeshInstance3D
        {
            Name = "Light",
            Mesh = new SphereMesh { Radius = 0.20f, Height = 0.40f },
            Position = new Vector3(0f, 1.68f, 0f),
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = new Color(1.0f, 0.72f, 0.24f),
                EmissionEnabled = true,
                Emission = new Color(1.0f, 0.50f, 0.08f),
                EmissionEnergyMultiplier = 1.4f,
            },
        });

        var collider = new StaticBody3D { Name = "Collider" };
        collider.AddChild(new CollisionShape3D
        {
            Shape = new CylinderShape3D { Radius = 0.9f, Height = 1.7f },
            Position = new Vector3(0f, 0.85f, 0f),
        });
        entity.AddChild(collider);
        entity.AddChild(new ShrineComponent { Name = "Shrine", ShrineId = shrineId });
        return entity;
    }

    private static StandardMaterial3D Stone(Color color) => new() { AlbedoColor = color, Roughness = 0.88f };
}
