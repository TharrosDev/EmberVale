using Embervale.Combat;
using Embervale.Factions;
using Embervale.Localization;
using Embervale.Loot;
using Embervale.Magic;
using Embervale.Movement;
using Embervale.Progression;
using Embervale.Stats;
using Godot;

namespace Embervale.Enemies;

/// <summary>
/// Builds any enemy from an <see cref="EnemyArchetypeResource"/> (Phase 34B; generalized past
/// humanoids in 34C). Mirrors <see cref="EnemyFactory"/>'s assembly — collision, mesh, stats, combat,
/// locomotion, hurt/hitbox, weapon, status effects, faction, AI, loot, XP — but takes every number
/// from the resource, so bandits, cultists, soldiers, Syndicate enforcers and the beast roster are
/// content rather than nine copies of this file.
/// </summary>
public static class EnemyArchetypeFactory
{
    private const int HostileTeam = 1;

    /// <summary>Body height the melee hitbox offsets below were authored against.</summary>
    private const float HumanoidReferenceHeight = 1.8f;

    public static EnemyEntity Create(EnemyArchetypeResource archetype, Vector3 position)
    {
        float radius = archetype.CapsuleRadius;
        float height = archetype.CapsuleHeight;

        var enemy = new EnemyEntity
        {
            Name = archetype.Id,
            DisplayName = archetype.NameKey.Length > 0 ? Loc.T(archetype.NameKey) : archetype.Id,
            TemplateId = archetype.Id,
            Position = position,
        };

        enemy.AddChild(new CollisionShape3D
        {
            Name = "Collision",
            Shape = new CapsuleShape3D { Radius = radius, Height = height },
            Position = new Vector3(0f, height * 0.5f, 0f),
        });

        AddVisual(enemy, archetype, radius, height);

        enemy.AddChild(new NavigationAgent3D
        {
            Name = "NavAgent",
            Radius = radius,
            Height = height,
            PathDesiredDistance = 0.6f,
            TargetDesiredDistance = 0.6f,
            AvoidanceEnabled = false,
        });

        AttributeSet attributes = GD.Load<AttributeSet>(archetype.AttributesPath) ?? AttributeSet.CreateDefault();
        enemy.AddChild(new StatsComponent
        {
            Name = "Stats",
            Attributes = attributes,
            StaminaRegen = archetype.StaminaRegen,
        });
        enemy.AddChild(new CombatComponent { Name = "Combat", Team = HostileTeam, MaxPoise = archetype.MaxPoise });
        enemy.AddChild(new LocomotionComponent { Name = "Locomotion" });
        enemy.AddChild(new HitReactionComponent { Name = "HitReaction" });
        enemy.AddChild(new Animation.CharacterAnimationComponent { Name = "Animation", BodyMeshPath = "Mesh" });
        enemy.AddChild(new WeaponTrailComponent { Name = "WeaponTrail" });
        enemy.AddChild(BuildHurtbox(radius, height));

        // The reach and the box scale with the body: these numbers were authored against a 1.8 m
        // humanoid's sword arc, and bolting that arc onto a 0.9 m wolf would have it biting a metre
        // past its own nose (Phase 34C).
        float bodyScale = height / HumanoidReferenceHeight;
        var hitbox = new Hitbox
        {
            Name = "MeleeHitbox",
            Position = new Vector3(0f, height * 0.6f, -1.0f * bodyScale),
        };
        hitbox.AddChild(new CollisionShape3D
        {
            Shape = new BoxShape3D { Size = new Vector3(0.9f, 1.4f, 1.4f) * bodyScale },
        });
        enemy.AddChild(hitbox);

        enemy.AddChild(new MeleeWeaponComponent
        {
            Name = "Weapon",
            Weapon = GD.Load<WeaponResource>(archetype.WeaponPath),
            Hitbox = hitbox,
        });

        enemy.AddChild(new StatusEffectsComponent { Name = "StatusEffects" });
        enemy.AddChild(new StatusEffectVfxComponent { Name = "StatusVfx" });

        // A populated spell list turns the archetype into a caster; paired with a standoff AI
        // profile it kites and casts exactly like the Ashen Acolyte does.
        if (archetype.KnownSpellIds.Count > 0)
        {
            var castOrigin = new Node3D { Name = "CastOrigin", Position = new Vector3(0f, height * 0.75f, -0.4f) };
            enemy.AddChild(castOrigin);
            enemy.AddChild(new SpellcastingComponent
            {
                Name = "Spellcasting",
                AimNode = castOrigin,
                KnownSpellIds = archetype.KnownSpellIds,
            });
        }

        if (archetype.FactionId.Length > 0)
        {
            enemy.AddChild(new FactionComponent { Name = "Faction", FactionId = archetype.FactionId });
        }

        enemy.AddChild(new EnemyAIComponent { Name = "AI", ProfileId = archetype.AiProfileId });

        if (archetype.LootTablePath.Length > 0)
        {
            enemy.AddChild(new LootComponent { Name = "Loot", TablePath = archetype.LootTablePath });
        }

        enemy.AddChild(new ExperienceComponent { Name = "Experience", XpValue = archetype.XpValue });
        enemy.AddToGroup(Quests.ObjectiveLocator.EnemyGroup);
        return enemy;
    }

    /// <summary>The authored model (origin at feet, turned for glTF→Godot forward), with a tinted
    /// capsule kept as the fallback so an archetype without art yet is still playable and still
    /// reads apart from its siblings.</summary>
    private static void AddVisual(EnemyEntity enemy, EnemyArchetypeResource archetype, float radius, float height)
    {
        if (archetype.ModelPath.Length > 0 &&
            GD.Load<PackedScene>(archetype.ModelPath)?.Instantiate() is Node3D visual)
        {
            visual.Name = "Mesh";
            visual.RotateY(Mathf.Pi);
            enemy.AddChild(visual);
            return;
        }

        enemy.AddChild(new MeshInstance3D
        {
            Name = "Mesh",
            Mesh = new CapsuleMesh { Radius = radius, Height = height },
            Position = new Vector3(0f, height * 0.5f, 0f),
            MaterialOverride = new StandardMaterial3D { AlbedoColor = archetype.PlaceholderTint },
        });
    }

    private static Hurtbox BuildHurtbox(float radius, float height)
    {
        var hurtbox = new Hurtbox { Name = "Hurtbox" };
        hurtbox.AddChild(new CollisionShape3D
        {
            Shape = new CapsuleShape3D { Radius = radius, Height = height },
            Position = new Vector3(0f, height * 0.5f, 0f),
        });
        return hurtbox;
    }
}
