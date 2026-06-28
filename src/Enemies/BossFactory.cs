using Embervale.Combat;
using Embervale.Core;
using Embervale.Factions;
using Embervale.Magic;
using Embervale.Movement;
using Embervale.Progression;
using Embervale.Stats;
using Godot;

namespace Embervale.Enemies;

/// <summary>
/// Builds the Iron King — the first boss (a Fallen Flamebearer), Phase 28A. Mirrors
/// <see cref="EnemyFactory"/> exactly (same components, same combat/locomotion/AI seams) but bigger and
/// tuned for a single-phase boss fight: a hulking body, heavy slow weapon, deep HP, high poise, and an
/// AI that never retreats. Multi-phase behaviour (28B), the healthbar (28C) and the reward/corruption
/// loop (28D) graft on top of this; none of them live here.
/// </summary>
public static class BossFactory
{
    private const string AttributesPath = "res://data/attributes/IronKingAttributes.tres";
    private const string WeaponPath = "res://data/weapons/IronKingMaul.tres";
    private const float CapsuleRadius = 0.7f;
    private const float CapsuleHeight = 2.6f;
    private const int HostileTeam = 1;

    public static BossEntity Create(Vector3 position)
    {
        var boss = new BossEntity
        {
            Name = "IronKing",
            DisplayName = "The Iron King",
            TemplateId = GameIds.Enemies.IronKing,
            Position = position,
        };

        boss.AddChild(new CollisionShape3D
        {
            Name = "Collision",
            Shape = new CapsuleShape3D { Radius = CapsuleRadius, Height = CapsuleHeight },
            Position = new Vector3(0f, CapsuleHeight * 0.5f, 0f),
        });

        boss.AddChild(new MeshInstance3D
        {
            Name = "Mesh",
            Mesh = new CapsuleMesh { Radius = CapsuleRadius, Height = CapsuleHeight },
            Position = new Vector3(0f, CapsuleHeight * 0.5f, 0f),
            MaterialOverride = new StandardMaterial3D
            {
                // Dark iron with a smouldering ember glow — a Fallen Flamebearer.
                AlbedoColor = new Color(0.22f, 0.20f, 0.21f),
                EmissionEnabled = true,
                Emission = new Color(0.85f, 0.32f, 0.10f),
                EmissionEnergyMultiplier = 0.5f,
            },
        });

        boss.AddChild(new NavigationAgent3D
        {
            Name = "NavAgent",
            Radius = CapsuleRadius,
            Height = CapsuleHeight,
            PathDesiredDistance = 0.8f,
            TargetDesiredDistance = 0.8f,
            AvoidanceEnabled = false,
        });

        AttributeSet attributes = GD.Load<AttributeSet>(AttributesPath) ?? AttributeSet.CreateDefault();
        boss.AddChild(new StatsComponent { Name = "Stats", Attributes = attributes, StaminaRegen = 16f });
        // Deep poise so light hits don't stagger him — a boss shrugs off chip damage.
        boss.AddChild(new CombatComponent { Name = "Combat", Team = HostileTeam, MaxPoise = 150f });
        boss.AddChild(new LocomotionComponent { Name = "Locomotion" });
        boss.AddChild(BuildHurtbox());

        var hitbox = new Hitbox
        {
            Name = "MeleeHitbox",
            Position = new Vector3(0f, 1.4f, -1.4f),
        };
        hitbox.AddChild(new CollisionShape3D
        {
            Shape = new BoxShape3D { Size = new Vector3(1.4f, 2.0f, 1.8f) },
        });
        boss.AddChild(hitbox);

        WeaponResource? weapon = GD.Load<WeaponResource>(WeaponPath);
        boss.AddChild(new MeleeWeaponComponent
        {
            Name = "Weapon",
            Weapon = weapon,
            Hitbox = hitbox,
        });

        boss.AddChild(new StatusEffectsComponent { Name = "StatusEffects" });
        boss.AddChild(new FactionComponent { Name = "Faction", FactionId = GameIds.Factions.Fallen });
        // Reuse the standard AI, tuned for a boss: long sight, bigger reach, never flees.
        boss.AddChild(new EnemyAIComponent
        {
            Name = "AI",
            VisionRange = 40f,
            FovDegrees = 140f,
            AttackRange = 3.5f,
            RetreatHealthFraction = 0f,
            PatrolRadius = 3f,
            ActiveDistance = 90f,
        });
        boss.AddChild(new ExperienceComponent { Name = "Experience", XpValue = 500 });
        return boss;
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
