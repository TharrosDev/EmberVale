using Embervale.Entities;
using Godot;

namespace Embervale.Items;

/// <summary>
/// Builds a world pickup for an item: a small rarity-tinted, floating cube with a
/// collider (so the player's interaction raycast can hit it) and an
/// <see cref="ItemPickupComponent"/>. Used to seed the sandbox and to drop loot
/// from defeated enemies.
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
        pickup.AddChild(new MeshInstance3D
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
        });

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
        return pickup;
    }
}
