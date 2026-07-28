using Embervale.Combat;
using Embervale.Core;
using Embervale.Factions;
using Embervale.Localization;
using Embervale.Magic;
using Embervale.Movement;
using Embervale.Stats;
using Godot;

namespace Embervale.Companions;

/// <summary>
/// Builds a recruited companion actor in code — the ally counterpart to <see cref="Enemies.EnemyFactory"/>
/// and <see cref="Player.PlayerFactory"/>, assembled from the same building blocks so a companion
/// fights, takes hits and animates exactly the way every other character does. Team 0 puts it on the
/// player's side, which is what makes the shared <see cref="Hitbox"/> friendly-fire rule protect it.
///
/// Phase 32A ships one melee archetype; 32C turns the tuning below into an authored
/// <c>CompanionResource</c> so new companions are content, not code.
/// </summary>
public static class CompanionFactory
{
    internal const string AttributesPath = "res://data/attributes/CompanionAttributes.tres";
    internal const string WeaponPath = "res://data/weapons/IronSword.tres";
    internal const string ModelPath = "res://assets/models/characters/chr_player_base.glb";
    private const float CapsuleRadius = 0.4f;
    private const float CapsuleHeight = 1.8f;

    /// <summary>The player's team — companions share it, so neither can strike the other.</summary>
    public const int PlayerTeam = 0;

    /// <summary>Builds the melee-warrior archetype (Kael in the slice) at <paramref name="position"/>.</summary>
    public static CompanionEntity CreateWarrior(string companionId, string nameKey, Vector3 position)
    {
        var companion = new CompanionEntity
        {
            // Godot node names may not contain '.', so the dotted id becomes the node name with
            // underscores (the id itself stays intact on CompanionId/PersistentId).
            Name = companionId.Replace('.', '_'),
            CompanionId = companionId,
            NameKey = nameKey,
            DisplayName = Loc.T(nameKey),
            TemplateId = companionId,
            // Stable id: a companion's component state (its stats, its wounds) is meant to survive
            // save/load, and the roster respawns it under the same id (see SaveKeyPolicy).
            PersistentId = companionId,
            Position = position,
        };

        companion.AddChild(new CollisionShape3D
        {
            Name = "Collision",
            Shape = new CapsuleShape3D { Radius = CapsuleRadius, Height = CapsuleHeight },
            Position = new Vector3(0f, CapsuleHeight * 0.5f, 0f),
        });

        // Placeholder visual: the player's rigged base body (30B/30C), so the shared animation
        // component drives real idle/run/attack clips. Kael's own model is Phase 32E content.
        if (GD.Load<PackedScene>(ModelPath)?.Instantiate() is Node3D visual)
        {
            visual.Name = "Mesh";
            visual.RotateY(Mathf.Pi); // glTF forward is +Z, Godot's is -Z
            companion.AddChild(visual);
        }
        else
        {
            companion.AddChild(new MeshInstance3D
            {
                Name = "Mesh",
                Mesh = new CapsuleMesh { Radius = CapsuleRadius, Height = CapsuleHeight },
                Position = new Vector3(0f, CapsuleHeight * 0.5f, 0f),
                MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.42f, 0.48f, 0.62f) },
            });
        }

        // Pathfinding agent (Phase 27A): the brain steers toward this agent's path corners where a
        // navmesh is baked, and falls back to straight-line steering where none is.
        companion.AddChild(new NavigationAgent3D
        {
            Name = "NavAgent",
            Radius = CapsuleRadius,
            Height = CapsuleHeight,
            PathDesiredDistance = 0.6f,
            TargetDesiredDistance = 0.6f,
            AvoidanceEnabled = false,
        });

        AttributeSet attributes = GD.Load<AttributeSet>(AttributesPath) ?? AttributeSet.CreateDefault();
        companion.AddChild(new StatsComponent
        {
            Name = "Stats",
            Attributes = attributes,
            HealthRegen = 2f,
            StaminaRegen = 14f,
        });
        companion.AddChild(new CombatComponent { Name = "Combat", Team = PlayerTeam, MaxPoise = 55f });
        companion.AddChild(new LocomotionComponent { Name = "Locomotion" });
        companion.AddChild(new HitReactionComponent { Name = "HitReaction" });
        companion.AddChild(new Embervale.Animation.CharacterAnimationComponent { Name = "Animation", BodyMeshPath = "Mesh" });
        companion.AddChild(new WeaponTrailComponent { Name = "WeaponTrail" });
        companion.AddChild(BuildHurtbox());

        var hitbox = new Hitbox
        {
            Name = "MeleeHitbox",
            Position = new Vector3(0f, 1.0f, -1.1f),
        };
        hitbox.AddChild(new CollisionShape3D
        {
            Shape = new BoxShape3D { Size = new Vector3(1.0f, 1.4f, 1.6f) },
        });
        companion.AddChild(hitbox);

        companion.AddChild(new MeleeWeaponComponent
        {
            Name = "Weapon",
            Weapon = GD.Load<WeaponResource>(WeaponPath),
            Hitbox = hitbox,
        });

        // Spells can burn/chill/ward a companion like any other character.
        companion.AddChild(new StatusEffectsComponent { Name = "StatusEffects" });
        companion.AddChild(new StatusEffectVfxComponent { Name = "StatusVfx" });

        // Companions stand with the villagers, so hostile factions read them as enemies.
        companion.AddChild(new FactionComponent { Name = "Faction", FactionId = GameIds.Factions.Villagers });
        companion.AddChild(new CompanionAIComponent { Name = "AI" });
        return companion;
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
