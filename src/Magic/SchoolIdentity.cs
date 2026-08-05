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
///   * <b>Arcane</b> — the hit strips one beneficial status from the target (Phase 34E.5). The ward
///     is still the school's self-side identity; this is its offensive half, unblocked once 34E
///     authored <c>spell.arcane_lance</c> (Arcane had only Self casts before, so there was no hit
///     to hang it on).
///   * <b>Fire</b> — handled in <see cref="StatusEffectsComponent"/> (stacking ignite), so it needs
///     no hook here. <b>Nature</b> — heal-over-time, authored as data (a HoT status).
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
            case DamageType.Arcane:
                Dispel(primary);
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

    /// <summary>An Arcane hit tears one buff off the target — the longest-lasting one
    /// (<see cref="StatusMath.PickDispel"/>), never a harmful effect.
    ///
    /// This cannot fire on a self-ward: <c>OnSpellHit</c> is only reached from
    /// <see cref="SpellResolver"/>'s <c>HitOne</c>/<c>Detonate</c> — the Projectile and Area paths —
    /// while a Self cast runs through <c>SpellcastingComponent.CastSelf</c>/<c>ApplySupport</c>. So
    /// casting <c>spell.arcane_shield</c> never dispels the ward it just applied.
    // ponytail: one buff per hit, like Lightning's single jump — a full cleanse would make Arcane a
    // hard counter to every buff at once rather than a trade. Widen only if it plays weak.</summary>
    private static void Dispel(Hurtbox primary)
    {
        StatusEffectsComponent? status = primary.OwnerEntity?.GetComponent<StatusEffectsComponent>();
        if (status == null)
        {
            return;
        }

        // Materialize before consuming: Consume mutates the dictionary ActiveEffects views.
        var candidates = new List<(string Id, bool IsBeneficial, double Remaining)>();
        foreach (StatusEffect effect in status.ActiveEffects)
        {
            candidates.Add((effect.Definition.Id, effect.Definition.IsBeneficial, effect.Remaining));
        }

        if (StatusMath.PickDispel(candidates) is { } stripped)
        {
            status.Consume(stripped);
        }
    }

    /// <summary>Arcs the bolt to the nearest other hostile within <see cref="ChainRadius"/> for a
    /// reduced hit. One jump only — chained arcs don't re-trigger the school hook.
    ///
    /// <b>"Other" is per actor, not per hurtbox.</b> This path predates 35A, when every actor had
    /// exactly one <see cref="Hurtbox"/> and excluding the primary volume was the same as excluding
    /// the primary creature. A dragon has four, all well inside the 6 m chain radius of each other,
    /// so a bolt landing on its head arced straight back into its own wing — half again the damage
    /// and a second application of the spell's status, on the four largest enemies in the game.
    /// <see cref="HitDedupe"/> is the codebase's one answer to that question and is what the other
    /// two damage entry points use; this is now the third rather than a second rule.
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

        // Spend the primary's actor up front, so every zone of the creature just hit is already
        // taken. This also subsumes the duplicate-hurtbox guard the query needed: two rows for one
        // volume resolve to the same owner key.
        //
        // Taking one zone per actor costs nothing in the nearest-wins scan below, because a zone's
        // Area3D sits at the actor's origin — the offset lives on its CollisionShape3D child, which
        // is the same fact SpellResolver.VolumeCentre exists to work around. Every zone of one
        // creature therefore measures the same distance, so which one is kept cannot change the winner.
        var struck = new HitDedupe();
        struck.TryHit(primary.OwnerEntity, primary);

        foreach (Godot.Collections.Dictionary hit in hits)
        {
            if (!hit.TryGetValue("collider", out Variant colliderVar) ||
                colliderVar.AsGodotObject() is not Hurtbox hurtbox ||
                !SpellResolver.IsHostileTarget(hurtbox, caster, casterTeam) ||
                !struck.TryHit(hurtbox.OwnerEntity, hurtbox))
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
