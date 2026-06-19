using Embervale.Combat;
using Embervale.Entities;
using Embervale.Items;
using Embervale.Movement;
using Embervale.Stats;
using Godot;

namespace Embervale.Player;

/// <summary>
/// Builds a fully-assembled first-person player actor in code. Constructing it
/// here (rather than a hand-authored <c>.tscn</c>) keeps the node graph, its
/// collision shape and its components in one reviewable place while the project
/// is young; it can be promoted to a packed scene later without changing callers.
/// </summary>
public static class PlayerFactory
{
    private const string PlayerAttributesPath = "res://data/attributes/PlayerAttributes.tres";
    private const string StartingWeaponPath = "res://data/weapons/IronSword.tres";
    private const int PlayerTeam = 0;
    private const float CapsuleRadius = 0.4f;
    private const float CapsuleHeight = 1.8f;
    private const float EyeHeight = 1.62f;

    public static PlayerCharacter Create(Vector3 position)
    {
        var player = new PlayerCharacter
        {
            Name = "Player",
            DisplayName = "Player",
            TemplateId = "player",
            Position = position,
        };

        player.AddChild(new CollisionShape3D
        {
            Name = "Collision",
            Shape = new CapsuleShape3D { Radius = CapsuleRadius, Height = CapsuleHeight },
            Position = new Vector3(0f, CapsuleHeight * 0.5f, 0f),
        });

        AttributeSet attributes = GD.Load<AttributeSet>(PlayerAttributesPath) ?? AttributeSet.CreateDefault();
        player.AddChild(new StatsComponent { Name = "Stats", Attributes = attributes });
        player.AddChild(new LocomotionComponent { Name = "Locomotion" });
        player.AddChild(new CombatComponent { Name = "Combat", Team = PlayerTeam });
        player.AddChild(new InventoryComponent { Name = "Inventory" });
        player.AddChild(BuildHurtbox());

        var cameraPivot = new Node3D
        {
            Name = "CameraPivot",
            Position = new Vector3(0f, EyeHeight, 0f),
        };
        player.AddChild(cameraPivot);
        cameraPivot.AddChild(new Camera3D { Name = "Camera", Current = true });

        // Melee swing volume in front of the body; opened by the weapon component.
        var hitbox = new Hitbox
        {
            Name = "MeleeHitbox",
            Position = new Vector3(0f, 1.0f, -1.1f),
        };
        hitbox.AddChild(new CollisionShape3D
        {
            Shape = new BoxShape3D { Size = new Vector3(1.0f, 1.4f, 1.6f) },
        });
        player.AddChild(hitbox);

        WeaponResource? weapon = GD.Load<WeaponResource>(StartingWeaponPath);
        player.AddChild(new MeleeWeaponComponent
        {
            Name = "Weapon",
            Weapon = weapon,
            Hitbox = hitbox,
        });

        // Equipment sits after inventory + weapon so it can resolve both; the
        // starting weapon above becomes the baseline restored on unequip.
        player.AddChild(new EquipmentComponent { Name = "Equipment" });

        player.AddChild(new PlayerController
        {
            Name = "Controller",
            CameraPivot = cameraPivot,
        });

        return player;
    }

    private static Hurtbox BuildHurtbox()
    {
        var hurtbox = new Hurtbox { Name = "Hurtbox" };
        hurtbox.AddChild(new CollisionShape3D
        {
            Shape = new CapsuleShape3D { Radius = CapsuleRadius, Height = CapsuleHeight },
            Position = new Vector3(0f, CapsuleHeight * 0.5f, 0f),
        });
        return hurtbox;
    }
}
