using Embervale.Entities;
using Godot;

namespace Embervale.Items;

/// <summary>
/// Builds a world pickup for an item: the item's own model where it authors one, a rarity-tinted
/// cube where it does not, a rarity-coloured glow on the ground under either, a collider (so the
/// player's interaction raycast can hit it) and an <see cref="ItemPickupComponent"/>. Used to seed
/// the sandbox and to drop loot from defeated enemies.
/// </summary>
public static class ItemPickupFactory
{
    public static Entity Create(ItemResource item, int quantity, Vector3 position)
    {
        return Create(ItemInstance.Plain(item), quantity, position);
    }

    public static Entity Create(ItemInstance instance, int quantity, Vector3 position)
    {
        var pickup = new Entity
        {
            Name = $"Pickup_{instance.TemplateId}",
            DisplayName = instance.DisplayName,
            TemplateId = $"pickup.{instance.TemplateId}",
            Position = position,
        };

        Color tint = ItemRarities.Color(instance.Rarity);
        pickup.AddChild(BuildVisual(instance, tint));
        pickup.AddChild(BuildRarityGlow(tint));

        var body = new StaticBody3D { Name = "Collider" };
        body.AddChild(new CollisionShape3D
        {
            Shape = new BoxShape3D { Size = new Vector3(0.5f, 0.5f, 0.5f) },
            Position = new Vector3(0f, 0.35f, 0f),
        });
        pickup.AddChild(body);

        pickup.AddChild(new ItemPickupComponent
        {
            Name = "Pickup",
            Item = instance.Template,
            Instance = instance,
            Quantity = quantity,
        });
        // Lets the Phase 25F compass / quest markers find this as a Collect-objective target.
        pickup.AddToGroup(Embervale.Quests.ObjectiveLocator.PickupGroup);
        return pickup;
    }

    /// <summary>
    /// The item's own model if it authors one, the rarity-tinted cube otherwise. Named "Mesh" either
    /// way, so anything looking for the visual finds it without caring which it got.
    ///
    /// ⚠️ <b>The cube is a fallback now, not the design.</b> Every dropped item in the game was one
    /// until <see cref="ItemResource.WorldModelPath"/> moved down to the base template — the path
    /// existed, but only on <c>EquippableItemResource</c>, where a pickup could not reach it.
    /// </summary>
    private static MeshInstance3D BuildVisual(ItemInstance instance, Color tint)
    {
        string modelPath = instance.Template.WorldModelPath;
        if (modelPath.Length > 0 &&
            GD.Load<PackedScene>(modelPath)?.Instantiate() is Node3D model)
        {
            // A MeshInstance3D wrapper keeps the node type stable for callers that look for "Mesh",
            // and gives the imported scene a parent to carry the pickup's hover offset.
            var host = new MeshInstance3D { Name = "Mesh", Position = new Vector3(0f, 0.35f, 0f) };
            model.Name = "Model";
            host.AddChild(model);
            return host;
        }

        return new MeshInstance3D
        {
            Name = "Mesh",
            Mesh = new BoxMesh { Size = new Vector3(0.35f, 0.35f, 0.35f) },
            Position = new Vector3(0f, 0.35f, 0f),
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = tint,
                EmissionEnabled = true,
                Emission = tint,
                EmissionEnergyMultiplier = 0.6f,
            },
        };
    }

    /// <summary>
    /// A flat rarity-coloured disc on the ground under the pickup.
    ///
    /// It exists because the model above <i>costs</i> something the cube gave away free: the cube WAS
    /// the rarity read, and a real iron dagger lying in grass is a grey shape you walk past. The glow
    /// keeps "there is loot here, and it is rare" legible at the distance the cube was legible at,
    /// without tinting the model itself.
    /// </summary>
    private static MeshInstance3D BuildRarityGlow(Color tint)
    {
        return new MeshInstance3D
        {
            Name = "RarityGlow",
            Mesh = new QuadMesh { Size = new Vector2(0.7f, 0.7f) },
            // Face up, and sit just clear of the ground so it does not z-fight the terrain.
            Transform = new Transform3D(
                Basis.FromEuler(new Vector3(-Mathf.Pi / 2f, 0f, 0f)),
                new Vector3(0f, 0.02f, 0f)),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = new Color(tint, 0.35f),
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                EmissionEnabled = true,
                Emission = tint,
                EmissionEnergyMultiplier = 0.8f,
            },
        };
    }
}
