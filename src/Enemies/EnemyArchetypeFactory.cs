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

        // A boss-flagged archetype is a BossEntity so the Phase 28C healthbar and the 28D
        // corruption-on-kill loop can resolve it by type through the ServiceLocator. Everything
        // below is identical either way — BossEntity is an EnemyEntity.
        EnemyEntity enemy = archetype.IsBoss ? new BossEntity() : new EnemyEntity();
        enemy.Name = archetype.Id;
        enemy.DisplayName = archetype.NameKey.Length > 0 ? Loc.T(archetype.NameKey) : archetype.Id;
        enemy.TemplateId = archetype.Id;
        enemy.Position = position;

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
            ManaRegen = archetype.ManaRegen,
        });
        enemy.AddChild(new CombatComponent { Name = "Combat", Team = HostileTeam, MaxPoise = archetype.MaxPoise });
        enemy.AddChild(new LocomotionComponent { Name = "Locomotion" });
        enemy.AddChild(new HitReactionComponent { Name = "HitReaction" });
        enemy.AddChild(new Animation.CharacterAnimationComponent { Name = "Animation", BodyMeshPath = "Mesh" });
        enemy.AddChild(new WeaponTrailComponent { Name = "WeaponTrail" });
        AddHurtboxes(enemy, archetype, radius, height);

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

        // 35A: a body this size is dangerous on every side. The component swaps the weapon's arc
        // between jaws/wing/tail by bearing, so the single swing above becomes three attacks.
        if (archetype.DirectionalMelee)
        {
            enemy.AddChild(DragonMeleeComponent.BuildArcs(enemy, height, radius));
        }

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

        // 35F: a creature that talks. The player's interact raycast is unmasked and resolves the
        // owning entity from whatever collider it hits, so the body it already has is the target —
        // a conversational creature needs no extra collision, only this component.
        if (archetype.DialogueId.Length > 0)
        {
            enemy.AddChild(new Dialogue.DialogueComponent { Name = "Dialogue", DialogueId = archetype.DialogueId });
        }

        var ai = new EnemyAIComponent { Name = "AI", ProfileId = archetype.AiProfileId };
        enemy.AddChild(ai);

        // Phase 36A: a boss archetype gets the phase/ability/enrage/telegraph controller. Before
        // this only the Iron King's bespoke factory attached one, so the three dragons were
        // BossEntities with a healthbar and no fight structure at all behind it.
        if (archetype.IsBoss)
        {
            // 36C: the ground ring is sized to the reach it is warning about — the hitbox offset plus
            // half its depth, from the same bodyScale the hitbox above used. A ring that does not
            // match the blow teaches the wrong spacing, which is worse than no ring.
            enemy.AddChild(new TelegraphComponent
            {
                Name = "Telegraph",
                RingRadius = ((1.0f + (1.4f * 0.5f)) * bodyScale) + radius,
            });

            // After the telegraph, so the controller resolves it as a sibling and can push each
            // phase's colour in. It only sets a property, never touches the ring the telegraph
            // builds in its own OnInitialize, so the two are order-independent either way.
            enemy.AddChild(new BossController { Name = "BossController", BossId = archetype.BossId });
        }

        // 35B: flight is a property of the AI profile, not of the archetype — a profile with a
        // takeoff range gets the vertical axis, everything else is untouched. Asked through the
        // brain's own resolver rather than the database, so the two cannot answer differently for an
        // actor that inlines its profile. ⚠️ The component is attached HERE, at build time, so a
        // profile swapped in later (a boss phase writing ProfileId) cannot grant flight — flight is
        // structural, not a knob a phase can turn.
        if (EnemyAIComponent.Resolve(ai.ProfileId, ai.Profile) is { TakeoffRange: > 0f })
        {
            enemy.AddChild(new FlightComponent { Name = "Flight" });
        }

        // 35C: a breath weapon. Needs the spellcasting component above, so it only attaches when the
        // archetype actually knows spells — a breath id with an empty loadout is caught by the validator.
        if (archetype.BreathSpellId.Length > 0 && archetype.KnownSpellIds.Count > 0)
        {
            enemy.AddChild(new BreathComponent
            {
                Name = "Breath",
                BreathSpellId = archetype.BreathSpellId,
                BreathDuration = archetype.BreathDuration,
            });
        }

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

        // A multi-zone body greyboxes as its zones (35A): one blob per hurtbox, the weak points
        // brighter. Built from the same numbers as the hurtboxes, so the silhouette can never drift
        // out of alignment with what is actually damageable — the trap a hand-placed greybox sets.
        if (archetype.HitZones.Count > 0)
        {
            var body = new Node3D { Name = "Mesh" };
            foreach (HitZoneResource zone in archetype.HitZones)
            {
                if (zone == null)
                {
                    continue;
                }

                body.AddChild(new MeshInstance3D
                {
                    Name = zone.Id,
                    Mesh = zone.Height > zone.Radius * 2f
                        ? new CapsuleMesh { Radius = zone.Radius, Height = zone.Height }
                        : new SphereMesh { Radius = zone.Radius, Height = zone.Radius * 2f },
                    Position = zone.Offset,
                    MaterialOverride = new StandardMaterial3D
                    {
                        AlbedoColor = zone.DamageMultiplier >= 1f
                            ? archetype.PlaceholderTint.Lightened((zone.DamageMultiplier - 1f) * 0.3f)
                            : archetype.PlaceholderTint.Darkened((1f - zone.DamageMultiplier) * 0.3f),
                    },
                });
            }

            enemy.AddChild(body);
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

    /// <summary>
    /// One whole-body hurtbox, or — when the archetype authors <c>HitZones</c> (Phase 35A) — one per
    /// zone, so a dragon's head can take double what its tail does. The zones replace the single
    /// capsule rather than sitting alongside it: overlapping both would be two hurtboxes over the same
    /// flesh, and while <see cref="HitDedupe"/> would stop the double damage, whichever one the
    /// physics query happened to return first would decide the multiplier.
    /// </summary>
    private static void AddHurtboxes(EnemyEntity enemy, EnemyArchetypeResource archetype, float radius, float height)
    {
        if (archetype.HitZones.Count == 0)
        {
            enemy.AddChild(BuildHurtbox("Hurtbox", string.Empty, 1f, new Vector3(0f, height * 0.5f, 0f), radius, height));
            return;
        }

        foreach (HitZoneResource zone in archetype.HitZones)
        {
            if (zone == null)
            {
                continue;
            }

            enemy.AddChild(BuildHurtbox(
                $"Hurtbox_{zone.Id}", zone.Id, zone.DamageMultiplier, zone.Offset, zone.Radius, zone.Height));
        }
    }

    /// <summary>A capsule hurtbox, or a sphere when the height cannot contain one (a head, a wing
    /// knuckle) — Godot's CapsuleShape3D silently clamps a height below 2r, which would quietly
    /// inflate a small zone's volume.</summary>
    private static Hurtbox BuildHurtbox(
        string name, string zoneId, float multiplier, Vector3 offset, float radius, float height)
    {
        var hurtbox = new Hurtbox { Name = name, ZoneId = zoneId, DamageMultiplier = multiplier };
        Shape3D shape = height > radius * 2f
            ? new CapsuleShape3D { Radius = radius, Height = height }
            : new SphereShape3D { Radius = radius };
        hurtbox.AddChild(new CollisionShape3D { Shape = shape, Position = offset });
        return hurtbox;
    }
}
