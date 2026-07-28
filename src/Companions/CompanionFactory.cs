using Embervale.Combat;
using Embervale.Core;
using Embervale.Core.Diagnostics;
using Embervale.Factions;
using Embervale.Localization;
using Embervale.Magic;
using Embervale.Movement;
using Embervale.Stats;
using Godot;

namespace Embervale.Companions;

/// <summary>
/// Builds a recruited companion actor from its authored <see cref="CompanionResource"/> — the ally
/// counterpart to <see cref="Enemies.EnemyFactory"/> and <see cref="Player.PlayerFactory"/>, assembled
/// from the same building blocks so a companion fights, takes hits and animates exactly the way every
/// other character does. Team 0 puts it on the player's side, which is what makes the shared
/// <see cref="Hitbox"/> friendly-fire rule protect it.
///
/// Since Phase 32C every knob (stats, weapon, model, faction, spells, follower envelope) comes from
/// the resource, so a new companion is a <c>.tres</c> — not a new factory.
/// </summary>
public static class CompanionFactory
{
    internal const string DefaultAttributesPath = "res://data/attributes/CompanionAttributes.tres";
    internal const string DefaultWeaponPath = "res://data/weapons/IronSword.tres";
    internal const string DefaultModelPath = "res://assets/models/characters/chr_player_base.glb";
    private const float CapsuleRadius = 0.4f;
    private const float CapsuleHeight = 1.8f;

    /// <summary>The player's team — companions share it, so neither can strike the other.</summary>
    public const int PlayerTeam = 0;

    /// <summary>Builds the companion described by <paramref name="resource"/> at <paramref name="position"/>.</summary>
    public static CompanionEntity Create(CompanionResource resource, Vector3 position)
    {
        var companion = new CompanionEntity
        {
            // Godot node names may not contain '.', so the dotted id becomes the node name with
            // underscores (the id itself stays intact on CompanionId/PersistentId).
            Name = resource.Id.Replace('.', '_'),
            CompanionId = resource.Id,
            NameKey = resource.NameKey,
            DisplayName = Loc.T(resource.NameKey),
            TemplateId = resource.Id,
            // Stable id: a companion's component state (its stats, its wounds) is meant to survive
            // save/load, and the roster respawns it under the same id (see SaveKeyPolicy).
            PersistentId = resource.Id,
            Position = position,
        };

        companion.AddChild(new CollisionShape3D
        {
            Name = "Collision",
            Shape = new CapsuleShape3D { Radius = CapsuleRadius, Height = CapsuleHeight },
            Position = new Vector3(0f, CapsuleHeight * 0.5f, 0f),
        });

        AddVisual(companion, resource.ModelPath);

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

        AttributeSet attributes = GD.Load<AttributeSet>(resource.AttributesPath)
            ?? GD.Load<AttributeSet>(DefaultAttributesPath)
            ?? AttributeSet.CreateDefault();
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

        WeaponResource? weapon = GD.Load<WeaponResource>(resource.WeaponPath);
        if (weapon == null)
        {
            Log.Warn($"Companion '{resource.Id}' weapon '{resource.WeaponPath}' failed to load; using the default.");
            weapon = GD.Load<WeaponResource>(DefaultWeaponPath);
        }

        companion.AddChild(new MeleeWeaponComponent { Name = "Weapon", Weapon = weapon, Hitbox = hitbox });

        // Spells can burn/chill/ward a companion like any other character.
        companion.AddChild(new StatusEffectsComponent { Name = "StatusEffects" });
        companion.AddChild(new StatusEffectVfxComponent { Name = "StatusVfx" });

        // A companion with an authored spell list gets the player's spellcasting component — the
        // seam a caster companion (Nyra, Beta) plugs into with no new code.
        if (resource.KnownSpellIds.Count > 0)
        {
            companion.AddChild(new SpellcastingComponent
            {
                Name = "Spellcasting",
                KnownSpellIds = new Godot.Collections.Array<string>(resource.KnownSpellIds),
            });
        }

        string factionId = string.IsNullOrEmpty(resource.FactionId) ? GameIds.Factions.Villagers : resource.FactionId;
        companion.AddChild(new FactionComponent { Name = "Faction", FactionId = factionId });
        companion.AddChild(new CompanionAIComponent
        {
            Name = "AI",
            FollowDistance = resource.FollowDistance,
            EngageRadius = resource.EngageRadius,
            AttackRange = resource.AttackRange,
            LeashRadius = resource.LeashRadius,
        });

        // Loyalty's mechanical face: applies the tier's combat edge and keeps it current (32C).
        companion.AddChild(new CompanionLoyaltyComponent { Name = "Loyalty" });
        return companion;
    }

    /// <summary>The companion's visible body — the authored model, with the capsule stand-in kept as
    /// a fallback so a missing/unimported asset degrades to "ugly" rather than "invisible".</summary>
    private static void AddVisual(CompanionEntity companion, string modelPath)
    {
        if (GD.Load<PackedScene>(modelPath)?.Instantiate() is Node3D visual)
        {
            visual.Name = "Mesh";
            visual.RotateY(Mathf.Pi); // glTF forward is +Z, Godot's is -Z
            companion.AddChild(visual);
            return;
        }

        companion.AddChild(new MeshInstance3D
        {
            Name = "Mesh",
            Mesh = new CapsuleMesh { Radius = CapsuleRadius, Height = CapsuleHeight },
            Position = new Vector3(0f, CapsuleHeight * 0.5f, 0f),
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.42f, 0.48f, 0.62f) },
        });
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
