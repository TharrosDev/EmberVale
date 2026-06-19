using Embervale.Combat;
using Embervale.Movement;
using Embervale.Stats;
using Godot;

namespace Embervale.Enemies;

/// <summary>
/// Builds a hostile NPC in code: a visible capsule body with the shared combat,
/// locomotion and AI components. Mirrors <see cref="Player.PlayerFactory"/> so
/// enemies and the player are assembled from the same building blocks. Promote to
/// a packed scene later without touching callers.
/// </summary>
public static class EnemyFactory
{
    private const string AttributesPath = "res://data/attributes/GoblinAttributes.tres";
    private const string WeaponPath = "res://data/weapons/GoblinClaw.tres";
    private const float CapsuleRadius = 0.4f;
    private const float CapsuleHeight = 1.7f;
    private const int HostileTeam = 1;

    public static EnemyEntity Create(Vector3 position)
    {
        var enemy = new EnemyEntity
        {
            Name = "Goblin",
            DisplayName = "Goblin",
            TemplateId = "enemy.goblin",
            Position = position,
        };

        enemy.AddChild(new CollisionShape3D
        {
            Name = "Collision",
            Shape = new CapsuleShape3D { Radius = CapsuleRadius, Height = CapsuleHeight },
            Position = new Vector3(0f, CapsuleHeight * 0.5f, 0f),
        });

        enemy.AddChild(new MeshInstance3D
        {
            Name = "Mesh",
            Mesh = new CapsuleMesh { Radius = CapsuleRadius, Height = CapsuleHeight },
            Position = new Vector3(0f, CapsuleHeight * 0.5f, 0f),
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.30f, 0.55f, 0.32f) },
        });

        AttributeSet attributes = GD.Load<AttributeSet>(AttributesPath) ?? AttributeSet.CreateDefault();
        enemy.AddChild(new StatsComponent { Name = "Stats", Attributes = attributes, StaminaRegen = 12f });
        enemy.AddChild(new CombatComponent { Name = "Combat", Team = HostileTeam, MaxPoise = 40f });
        enemy.AddChild(new LocomotionComponent { Name = "Locomotion" });
        enemy.AddChild(BuildHurtbox());

        var hitbox = new Hitbox
        {
            Name = "MeleeHitbox",
            Position = new Vector3(0f, 1.0f, -1.0f),
        };
        hitbox.AddChild(new CollisionShape3D
        {
            Shape = new BoxShape3D { Size = new Vector3(0.9f, 1.4f, 1.4f) },
        });
        enemy.AddChild(hitbox);

        WeaponResource? weapon = GD.Load<WeaponResource>(WeaponPath);
        enemy.AddChild(new MeleeWeaponComponent
        {
            Name = "Weapon",
            Weapon = weapon,
            Hitbox = hitbox,
        });

        enemy.AddChild(new EnemyAIComponent { Name = "AI" });
        return enemy;
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
