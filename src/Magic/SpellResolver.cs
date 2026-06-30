using System.Collections.Generic;
using Embervale.Combat;
using Embervale.Entities;
using Godot;

namespace Embervale.Magic;

/// <summary>
/// Shared resolution logic for spell impacts, used by both <see cref="SpellProjectile"/>
/// (on contact) and <see cref="SpellcastingComponent"/> (for instant area casts).
/// It applies a spell's <see cref="DamagePacket"/> and optional status effect to the
/// eligible target(s), honouring the same friendly-fire rules a <see cref="Hitbox"/>
/// uses (never the caster, never an ally on the caster's team).
/// </summary>
public static class SpellResolver
{
    /// <summary>Delivers a single-target hit (damage + school identity + status) to one hurtbox.</summary>
    public static void HitOne(
        Node3D context, Hurtbox hurtbox, DamagePacket packet, SpellResource spell, IEntity? caster, int casterTeam)
    {
        hurtbox.Receive(packet);
        SchoolIdentity.OnSpellHit(context, spell, packet, caster, casterTeam, hurtbox);
        SpellCombo.OnHit(spell, caster, hurtbox);
        ApplyStatus(hurtbox.OwnerEntity, spell, caster);
    }

    /// <summary>
    /// Bursts at <paramref name="center"/>, hitting every eligible hurtbox within
    /// <paramref name="radius"/> with the spell's damage and status, and spawns a
    /// brief visual flash. Uses a physics shape query so it needs no persistent area.
    /// </summary>
    public static void Detonate(
        Node3D context,
        SpellResource spell,
        DamagePacket packet,
        IEntity? caster,
        int casterTeam,
        Vector3 center,
        float radius)
    {
        SpawnFlash(context, center, radius, SpellSchools.Color(spell.School));

        PhysicsDirectSpaceState3D space = context.GetWorld3D().DirectSpaceState;
        var query = new PhysicsShapeQueryParameters3D
        {
            Shape = new SphereShape3D { Radius = radius },
            Transform = new Transform3D(Basis.Identity, center),
            CollideWithAreas = true,
            CollideWithBodies = false,
            CollisionMask = CombatLayers.Hurtbox,
        };

        Godot.Collections.Array<Godot.Collections.Dictionary> hits = space.IntersectShape(query, 32);
        var struck = new HashSet<Hurtbox>();
        foreach (Godot.Collections.Dictionary hit in hits)
        {
            if (!hit.TryGetValue("collider", out Variant colliderVar) ||
                colliderVar.AsGodotObject() is not Hurtbox hurtbox)
            {
                continue;
            }

            if (!struck.Add(hurtbox) || !IsHostileTarget(hurtbox, caster, casterTeam))
            {
                continue;
            }

            hurtbox.Receive(packet);
            SchoolIdentity.OnSpellHit(context, spell, packet, caster, casterTeam, hurtbox);
            SpellCombo.OnHit(spell, caster, hurtbox);
            ApplyStatus(hurtbox.OwnerEntity, spell, caster);
        }
    }

    /// <summary>True if a hurtbox is a valid spell target (not the caster, not an ally).</summary>
    public static bool IsHostileTarget(Hurtbox hurtbox, IEntity? caster, int casterTeam)
    {
        if (hurtbox.OwnerEntity != null && ReferenceEquals(hurtbox.OwnerEntity, caster))
        {
            return false;
        }

        return hurtbox.Combat == null || hurtbox.Combat.Team != casterTeam;
    }

    /// <summary>Applies a spell's status effect (if any) to a target entity.</summary>
    public static void ApplyStatus(IEntity? target, SpellResource spell, IEntity? caster)
    {
        if (target == null || !spell.HasStatusEffect)
        {
            return;
        }

        StatusEffectResource? definition = StatusEffectDatabase.Get(spell.StatusEffectId);
        target.GetComponent<StatusEffectsComponent>()?.Apply(definition, caster);
    }

    private static void SpawnFlash(Node3D context, Vector3 center, float radius, Color color)
    {
        SceneTree? tree = context.GetTree();
        Node? parent = tree?.CurrentScene;
        if (parent == null)
        {
            return;
        }

        var flash = new SpellFlash { Radius = radius, FlashColor = color };
        parent.AddChild(flash);
        flash.GlobalPosition = center;
    }
}
