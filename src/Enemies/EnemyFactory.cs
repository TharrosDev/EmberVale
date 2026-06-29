using Embervale.Combat;
using Embervale.Core;
using Embervale.Factions;
using Embervale.Loot;
using Embervale.Magic;
using Embervale.Movement;
using Embervale.Progression;
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
    private const string LootTablePath = "res://data/loot/GoblinLoot.tres";
    private const float CapsuleRadius = 0.4f;
    private const float CapsuleHeight = 1.7f;
    private const int HostileTeam = 1;

    public static EnemyEntity Create(Vector3 position)
    {
        var enemy = new EnemyEntity
        {
            Name = "Goblin",
            DisplayName = "Goblin",
            TemplateId = GameIds.Enemies.Goblin,
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

        // Pathfinding agent (Phase 27A): the AI steers toward this agent's path corners when a
        // baked navmesh exists under the actor, and falls back to straight-line steering otherwise
        // (the procedural sandbox has no navmesh). Radius/height match the capsule so paths keep the
        // body clear of greybox walls. Avoidance is off — pathing only, for now.
        enemy.AddChild(new NavigationAgent3D
        {
            Name = "NavAgent",
            Radius = CapsuleRadius,
            Height = CapsuleHeight,
            PathDesiredDistance = 0.6f,
            TargetDesiredDistance = 0.6f,
            AvoidanceEnabled = false,
        });

        AttributeSet attributes = GD.Load<AttributeSet>(AttributesPath) ?? AttributeSet.CreateDefault();
        enemy.AddChild(new StatsComponent { Name = "Stats", Attributes = attributes, StaminaRegen = 12f });
        enemy.AddChild(new CombatComponent { Name = "Combat", Team = HostileTeam, MaxPoise = 40f });
        enemy.AddChild(new LocomotionComponent { Name = "Locomotion" });
        enemy.AddChild(new HitReactionComponent { Name = "HitReaction" });
        enemy.AddChild(new WeaponTrailComponent { Name = "WeaponTrail" });
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

        // Lets the player's spells burn/chill the goblin (DoT + slows apply here).
        enemy.AddChild(new StatusEffectsComponent { Name = "StatusEffects" });
        // Faction membership: AI aggression keys off the player's standing with this faction.
        enemy.AddChild(new FactionComponent { Name = "Faction", FactionId = GameIds.Factions.Goblins });
        enemy.AddChild(new EnemyAIComponent { Name = "AI" });
        enemy.AddChild(new LootComponent { Name = "Loot", TablePath = LootTablePath });
        enemy.AddChild(new ExperienceComponent { Name = "Experience", XpValue = 25 });
        // Lets the Phase 25F compass / quest markers find this as a Kill-objective target.
        enemy.AddToGroup(Quests.ObjectiveLocator.EnemyGroup);
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
