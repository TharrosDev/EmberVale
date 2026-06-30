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
/// Builds an <b>Ashen Acolyte</b> (Phase 29.5F): a Fallen fire-caster — the first enemy that casts back.
/// It mirrors <see cref="EnemyFactory"/>'s blocks but swaps the melee weapon for a
/// <see cref="SpellcastingComponent"/> (the very same one the player uses), so the shared
/// <see cref="EnemyAIComponent"/> caster branch holds range, kites when crowded, lobs Firebolts/Fireballs,
/// wards itself, and heals a wounded ally. Squishy and mana-fed; beatable by closing the gap.
///
/// A robed Fire-caster is the slice's one caster archetype; the school-themed caster <em>roster</em>
/// (Phase 34) is more of these — a different `.tres` loadout + tint, no new code.
/// </summary>
public static class AshenAcolyteFactory
{
    private const string AttributesPath = "res://data/attributes/CultistAttributes.tres";
    // ponytail: reuses the goblin loot table until a Fallen/cultist table is authored (content, Phase 35).
    private const string LootTablePath = "res://data/loot/GoblinLoot.tres";
    private const float CapsuleRadius = 0.36f;
    private const float CapsuleHeight = 1.75f;
    private const int HostileTeam = 1;

    public static EnemyEntity Create(Vector3 position)
    {
        var enemy = new EnemyEntity
        {
            Name = "AshenAcolyte",
            DisplayName = "Ashen Acolyte",
            TemplateId = GameIds.Enemies.AshenAcolyte,
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
            // Ashen robes lit by a smouldering ember — a lesser Fallen.
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = new Color(0.30f, 0.10f, 0.12f),
                EmissionEnabled = true,
                Emission = new Color(0.85f, 0.35f, 0.12f),
                EmissionEnergyMultiplier = 0.5f,
            },
        });

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
        enemy.AddChild(new StatsComponent { Name = "Stats", Attributes = attributes, StaminaRegen = 10f, ManaRegen = 6f });
        enemy.AddChild(new CombatComponent { Name = "Combat", Team = HostileTeam, MaxPoise = 26f });
        enemy.AddChild(new LocomotionComponent { Name = "Locomotion" });
        enemy.AddChild(new HitReactionComponent { Name = "HitReaction" });
        enemy.AddChild(BuildHurtbox());

        // The player's spells can burn/chill/freeze it; its own casts also push statuses onto it.
        enemy.AddChild(new StatusEffectsComponent { Name = "StatusEffects" });

        // The cast muzzle/aim source at chest height: a child of the body, so facing the target aims the
        // bolt at them (the AI calls LookAt each tick). Mirrors the player wiring its camera pivot in.
        var castOrigin = new Marker3D { Name = "CastOrigin", Position = new Vector3(0f, 1.3f, 0f) };
        enemy.AddChild(castOrigin);
        enemy.AddChild(new SpellcastingComponent
        {
            Name = "Spellcasting",
            AimNode = castOrigin,
            KnownSpellIds = new Godot.Collections.Array<string>
            {
                GameIds.Spells.Firebolt,   // offensive (single target)
                GameIds.Spells.Fireball,   // offensive (heavier, AoE on impact)
                GameIds.Spells.ArcaneShield, // self-ward
                GameIds.Spells.LesserHeal, // heal a wounded ally (or itself)
            },
        });

        enemy.AddChild(new FactionComponent { Name = "Faction", FactionId = GameIds.Factions.Fallen });
        enemy.AddChild(new EnemyAIComponent { Name = "AI" });
        enemy.AddChild(new LootComponent { Name = "Loot", TablePath = LootTablePath });
        enemy.AddChild(new ExperienceComponent { Name = "Experience", XpValue = 35 });
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
