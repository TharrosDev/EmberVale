using System.Collections.Generic;
using Embervale.Combat;
using Embervale.Entities;
using Embervale.Stats;
using Godot;

namespace Embervale.Magic;

/// <summary>
/// The signature on-hit behaviour that makes each magic <see cref="DamageType"/> school play
/// differently (Phase 29.5B), beyond a tint and a status effect:
///   * <b>Frost</b> — chill escalates to a freeze on a target that was already chilled.
///   * <b>Lightning</b> — the bolt chains to one nearby foe for a fraction of its damage.
///   * <b>Necrotic</b> — the caster lifesteals a fraction of the damage dealt (the corrupted line,
///     gated by the spell's <see cref="SpellResource.MinCorruptionTier"/> per Phase 23H).
///   * <b>Fire</b> — handled in <see cref="StatusEffectsComponent"/> (stacking ignite), so it needs
///     no hook here. <b>Nature</b> — heal-over-time, authored as data (a HoT status). <b>Arcane</b> —
///     the ward (a self buff) is its identity; on-hit dispel waits for an offensive Arcane spell.
///
/// Invoked by <see cref="SpellResolver"/> once per struck target, <em>after</em> damage lands but
/// <em>before</em> the spell's own status is applied (so Frost can read the pre-hit chill).
/// </summary>
public static class SchoolIdentity
{
    private const float ChainRadius = 6f;
    private const float ChainDamageFraction = 0.5f;
    private const float NecroticLifestealFraction = 0.35f;

    private const string ChillId = "status.chill";
    private const string FrozenId = "status.frozen";

    /// <summary>Health the caster recovers from a Necrotic hit dealing <paramref name="damage"/>.</summary>
    public static float LifestealAmount(float damage) => Mathf.Max(0f, damage) * NecroticLifestealFraction;

    /// <summary>Damage a chained Lightning arc deals, as a fraction of the primary hit.</summary>
    public static float ChainDamage(float damage) => Mathf.Max(0f, damage) * ChainDamageFraction;

    public static void OnSpellHit(
        Node3D context,
        SpellResource spell,
        DamagePacket packet,
        IEntity? caster,
        int casterTeam,
        Hurtbox primary)
    {
        switch (spell.School)
        {
            case DamageType.Frost:
                EscalateFreeze(primary, caster);
                break;
            case DamageType.Lightning:
                ChainToNearby(context, spell, packet, caster, casterTeam, primary);
                break;
            case DamageType.Necrotic:
                Lifesteal(caster, packet.Amount);
                break;
        }
    }

    /// <summary>A Frost hit on an already-chilled target freezes it solid (a hard root).</summary>
    private static void EscalateFreeze(Hurtbox primary, IEntity? caster)
    {
        StatusEffectsComponent? status = primary.OwnerEntity?.GetComponent<StatusEffectsComponent>();
        if (status != null && status.Has(ChillId))
        {
            status.Apply(StatusEffectDatabase.Get(FrozenId), caster);
        }
    }

    /// <summary>The caster heals for a share of the Necrotic damage it just dealt.</summary>
    private static void Lifesteal(IEntity? caster, float damage)
    {
        caster?.GetComponent<StatsComponent>()?.Heal(LifestealAmount(damage));
    }

    /// <summary>Arcs the bolt to the nearest other hostile within <see cref="ChainRadius"/> for a
    /// reduced hit. One jump only — chained arcs don't re-trigger the school hook.
    // ponytail: single jump, widen to multi-jump if Lightning needs more reach.</summary>
    private static void ChainToNearby(
        Node3D context,
        SpellResource spell,
        DamagePacket packet,
        IEntity? caster,
        int casterTeam,
        Hurtbox primary)
    {
        Vector3 center = primary.GlobalPosition;
        PhysicsDirectSpaceState3D space = context.GetWorld3D().DirectSpaceState;
        var query = new PhysicsShapeQueryParameters3D
        {
            Shape = new SphereShape3D { Radius = ChainRadius },
            Transform = new Transform3D(Basis.Identity, center),
            CollideWithAreas = true,
            CollideWithBodies = false,
            CollisionMask = CombatLayers.Hurtbox,
        };

        Godot.Collections.Array<Godot.Collections.Dictionary> hits = space.IntersectShape(query, 16);
        Hurtbox? best = null;
        float bestDist = float.MaxValue;
        var seen = new HashSet<Hurtbox>();
        foreach (Godot.Collections.Dictionary hit in hits)
        {
            if (!hit.TryGetValue("collider", out Variant colliderVar) ||
                colliderVar.AsGodotObject() is not Hurtbox hurtbox ||
                ReferenceEquals(hurtbox, primary) ||
                !seen.Add(hurtbox) ||
                !SpellResolver.IsHostileTarget(hurtbox, caster, casterTeam))
            {
                continue;
            }

            float dist = hurtbox.GlobalPosition.DistanceSquaredTo(center);
            if (dist < bestDist)
            {
                bestDist = dist;
                best = hurtbox;
            }
        }

        if (best == null)
        {
            return;
        }

        var arc = packet with { Amount = ChainDamage(packet.Amount) };
        best.Receive(arc);
        SpellResolver.ApplyStatus(best.OwnerEntity, spell, caster);
    }
}
