using Embervale.Entities;
using Godot;

namespace Embervale.Crafting;

/// <summary>
/// Builds a world crafting station: a model (or a coloured block) with a collider — the player's
/// interaction raycast needs one — and a <see cref="CraftingStationComponent"/>. Mirrors the other
/// code-built actors (e.g. <see cref="Items.ItemPickupFactory"/>).
///
/// It had no callers at all until Phase 37C: both settlements author their stations directly in
/// their cell <c>.tscn</c>, so this existed for two dozen phases building nothing. 37C is what gives
/// it a job — every station the player places is built here.
/// </summary>
public static class CraftingStationFactory
{
    /// <summary>
    /// Builds a station. <paramref name="modelPath"/> is an optional <c>.glb</c>; when it is absent
    /// or fails to load the emissive box is used instead, the same first-a-model-then-a-box order
    /// <c>GameBootstrap.BuildPersistentCache</c> uses. <paramref name="position"/> is <b>local</b> to
    /// the parent it will be added to.
    /// </summary>
    public static Entity Create(
        CraftingStationType station, string name, Vector3 position, Color color, string modelPath = "")
    {
        var entity = new Entity
        {
            Name = $"Station_{station}",
            DisplayName = name,
            TemplateId = $"station.{station.ToString().ToLowerInvariant()}",
            Position = position,
        };

        entity.AddChild(BuildVisual(modelPath, color));

        var collider = new StaticBody3D { Name = "Collider" };
        collider.AddChild(new CollisionShape3D
        {
            Shape = new BoxShape3D { Size = new Vector3(0.9f, 1.0f, 0.9f) },
            Position = new Vector3(0f, 0.5f, 0f),
        });
        entity.AddChild(collider);

        entity.AddChild(new CraftingStationComponent
        {
            Name = "Station",
            Station = station,
            StationName = name,
        });

        return entity;
    }

    /// <summary>The model if there is one, the greybox block otherwise. Named "Mesh" either way, so
    /// anything looking for the visual finds it without caring which it got.</summary>
    private static Node3D BuildVisual(string modelPath, Color color)
    {
        if (!string.IsNullOrEmpty(modelPath) &&
            GD.Load<PackedScene>(modelPath)?.Instantiate() is Node3D model)
        {
            model.Name = "Mesh";
            return model;
        }

        return new MeshInstance3D
        {
            Name = "Mesh",
            Mesh = new BoxMesh { Size = new Vector3(0.9f, 1.0f, 0.9f) },
            Position = new Vector3(0f, 0.5f, 0f),
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = color,
                EmissionEnabled = true,
                Emission = color,
                EmissionEnergyMultiplier = 0.25f,
            },
        };
    }
}
