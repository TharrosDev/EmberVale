using System.Collections.Generic;
using Embervale.Combat;
using Embervale.Entities;
using Embervale.Magic.Vfx;
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
///   * <b>Fire</b> — stacking ignite (<see cref="StatusEffectsComponent"/>), and a Fire hit on a
///     Kindled foe feeds it a Burning stack; the Kindle itself detonates at three stacks.
///   * <b>Lightning</b> also answers a Stormbrand: every lightning hit within
///     <see cref="BrandRange"/> arcs to the branded foe first.
///   * <b>Necrotic</b> lifesteal heals more the lower the target's health, doubled off a Grave Mark.
///   * <b>Nature</b> — regrowth and roots, authored as data (a HoT status, a Root control).
///
/// Invoked by <see cref="SpellResolver"/> once per struck target, <em>after</em> damage lands but
/// <em>before</em> the spell's own status is applied (so Frost can read the pre-hit chill).
/// </summary>
public static class SchoolIdentity
{
    private const float ChainRadius = 6f;
    private const float ChainDamageFraction = 0.5f;
    private const float NecroticLifestealFraction = 0.35f;

    /// <summary>How far from the struck foe a Stormbrand still pulls a lightning arc.</summary>
    public const float BrandRange = 12f;

    private const float BrandArcFraction = 0.75f;

    /// <summary>Health the caster recovers from a Necrotic hit dealing <paramref name="damage"/>. It grows
    /// as the target's health (<paramref name="targetHealthFraction"/>, after the hit) falls, up to double
    /// at the last sliver, and doubles again off a Grave Marked foe.</summary>
    public static float LifestealAmount(float damage, float targetHealthFraction = 1f, bool targetMarked = false)
    {
        float lowHealth = 1f + (1f - Mathf.Clamp(targetHealthFraction, 0f, 1f));
        return Mathf.Max(0f, damage) * NecroticLifestealFraction * lowHealth * (targetMarked ? 2f : 1f);
    }

    /// <summary>Damage a chained Lightning arc deals, as a fraction of the primary hit; an arc drawn to a
    /// Stormbrand carries more.</summary>
    public static float ChainDamage(float damage, bool toBrand = false) =>
        Mathf.Max(0f, damage) * (toBrand ? BrandArcFraction : ChainDamageFraction);

    /// <summary>Which candidate a lightning arc jumps to: the nearest Stormbranded foe within
    /// <see cref="BrandRange"/>, else the nearest foe within the plain chain radius. -1 for none.</summary>
    public static int PickChainTarget(IReadOnlyList<(float Distance, bool Branded)> candidates)
    {
        int best = -1;
        for (int i = 0; i < candidates.Count; i++)
        {
            (float distance, bool branded) = candidates[i];
            if (branded ? distance > BrandRange : distance > ChainRadius)
            {
                continue;
            }

            if (best < 0 || Prefer(candidates[i], candidates[best]))
            {
                best = i;
            }
        }

        return best;

        static bool Prefer((float D, bool B) a, (float D, bool B) b) => a.B != b.B ? a.B : a.D < b.D;
    }

    public static void OnSpellHit(
        Node3D context,
        SpellResource spell,
        DamagePacket packet,
        IEntity? caster,
        int casterTeam,
        Hurtbox primary,
        float resolvedDamage = -1f,
        bool targetWasMarked = false,
        bool targetKilled = false,
        float targetHealthFraction = -1f,
        Vector3? hitPosition = null,
        SpellLifetime? lifetime = null,
        Vector3? impactPoint = null)
    {
        switch (spell.School)
        {
            case DamageType.Fire when !targetKilled:
                FeedKindle(spell, primary, caster);
                break;
            case DamageType.Frost when !targetKilled:
                EscalateFreeze(spell, primary, caster, lifetime);
                break;
            case DamageType.Lightning:
                // A brand is a mark, not a bolt: it must not arc to (and re-brand) another foe.
                if (spell.StatusEffectId != StatusIds.Stormbrand)
                {
                    ChainToNearby(context, spell, packet, caster, casterTeam, primary, hitPosition, lifetime, impactPoint);
                }

                break;
            case DamageType.Necrotic:
                Lifesteal(spell, caster, resolvedDamage >= 0f ? resolvedDamage : packet.Amount, primary, targetWasMarked, targetHealthFraction);
                break;
            case DamageType.Arcane when !targetKilled:
                // A bolt tears a buff off; a ground or barrier pulse does not.
                if (spell.Delivery == SpellDelivery.Projectile)
                {
                    Dispel(spell, primary, caster);
                }

                break;
        }
    }

    /// <summary>A Fire hit on a Kindled foe feeds the fire: one more Burning stack.</summary>
    private static void FeedKindle(SpellResource spell, Hurtbox primary, IEntity? caster)
    {
        StatusEffectsComponent? status = primary.OwnerEntity?.GetComponent<StatusEffectsComponent>();
        if (status != null && status.Has(StatusIds.Kindled))
        {
            Vector3 at = primary.GlobalPosition;
            status.Apply(StatusEffectDatabase.Get(StatusIds.Burning), caster);
            SpellVfx.StatusProc(SpellProcKind.KindleFed, DamageType.Fire, primary.OwnerEntity, at, 0f, spell);
        }
    }

    /// <summary>A Frost hit on an already-chilled target freezes it: a short Stun and Root, then it is
    /// immune to being frozen again (the status's <c>ControlImmunitySeconds</c>). The chill is spent.</summary>
    private static void EscalateFreeze(SpellResource spell, Hurtbox primary, IEntity? caster, SpellLifetime? lifetime)
    {
        StatusEffectsComponent? status = primary.OwnerEntity?.GetComponent<StatusEffectsComponent>();
        if (status != null && status.Has(StatusIds.Chill))
        {
            Vector3 at = primary.GlobalPosition;
            status.Consume(StatusIds.Chill);
            if (lifetime?.Check() != false && status.Apply(StatusEffectDatabase.Get(StatusIds.Frozen), caster))
            {
                SpellVfx.StatusProc(SpellProcKind.Freeze, DamageType.Frost, primary.OwnerEntity, at, 0f, spell);
            }
        }
    }

    /// <summary>The caster heals for a share of the Necrotic damage it just dealt.</summary>
    private static void Lifesteal(
        SpellResource spell, IEntity? caster, float damage, Hurtbox primary, bool targetWasMarked, float targetHealthFraction)
    {
        float fraction = targetHealthFraction >= 0f ? targetHealthFraction
            : primary.OwnerEntity?.GetComponent<StatsComponent>()?.GetNormalized(StatType.Health) ?? 1f;
        bool marked = targetWasMarked || primary.OwnerEntity?.GetComponent<StatusEffectsComponent>()?.Has(StatusIds.GraveMark) == true;
        float healed = LifestealAmount(damage, fraction, marked);
        caster?.GetComponent<StatsComponent>()?.Heal(healed);
        if (healed > 0f && caster?.Body is { } body && GodotObject.IsInstanceValid(body) && body.IsInsideTree() &&
            GodotObject.IsInstanceValid(primary) && primary.IsInsideTree())
        {
            SpellVfx.Arc(DamageType.Necrotic, caster, SpellResolver.VolumeCentre(primary),
                body.GlobalPosition + Vector3.Up, SpellArcKind.Tether, spell);
        }
    }

    /// <summary>An Arcane bolt tears one buff off the target — the longest-lasting dispellable one
    /// (<see cref="StatusEffectsComponent.Dispel"/>), never a harmful effect.
    ///
    /// This cannot fire on a self-ward: <c>OnSpellHit</c> is only reached from
    /// <see cref="SpellResolver"/>'s <c>HitOne</c>/<c>Detonate</c> — the Projectile and Area paths —
    /// while a Self cast runs through <c>SpellcastingComponent.CastSelf</c>/<c>ApplySupport</c>. So
    /// casting <c>spell.arcane_shield</c> never dispels the ward it just applied.
    // ponytail: one buff per hit, like Lightning's single jump — a full cleanse would make Arcane a
    // hard counter to every buff at once rather than a trade. Widen only if it plays weak.</summary>
    private static void Dispel(SpellResource spell, Hurtbox primary, IEntity? caster)
    {
        Vector3 at = primary.GlobalPosition;
        if (primary.OwnerEntity?.GetComponent<StatusEffectsComponent>()?.Dispel(caster) != null)
        {
            SpellVfx.StatusProc(SpellProcKind.Dispel, DamageType.Arcane, primary.OwnerEntity, at, 0f, spell);
        }
    }

    /// <summary>Arcs the bolt to a Stormbranded foe within <see cref="BrandRange"/> if there is one,
    /// else to the nearest other hostile within <see cref="ChainRadius"/>, for a reduced hit. One jump only — chained arcs don't re-trigger the school hook.
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
        Hurtbox primary,
        Vector3? hitPosition = null,
        SpellLifetime? lifetime = null,
        Vector3? impactPoint = null)
    {
        Vector3 center = hitPosition ?? primary.GlobalPosition;
        PhysicsDirectSpaceState3D space = context.GetWorld3D().DirectSpaceState;
        var query = new PhysicsShapeQueryParameters3D
        {
            Shape = new SphereShape3D { Radius = BrandRange },
            Transform = new Transform3D(Basis.Identity, center),
            CollideWithAreas = true,
            CollideWithBodies = false,
            CollisionMask = CombatLayers.Hurtbox,
        };

        Godot.Collections.Array<Godot.Collections.Dictionary> hits = space.IntersectShape(query, 32);
        var hurtboxes = new List<Hurtbox>();
        var candidates = new List<(float Distance, bool Branded)>();

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
                hurtbox.OwnerEntity?.GetComponent<StatsComponent>() is { IsAlive: false } ||
                !struck.TryHit(hurtbox.OwnerEntity, hurtbox))
            {
                continue;
            }

            hurtboxes.Add(hurtbox);
            candidates.Add((
                hurtbox.GlobalPosition.DistanceTo(center),
                hurtbox.OwnerEntity?.GetComponent<StatusEffectsComponent>()?.Has(StatusIds.Stormbrand) == true));
        }

        int pick = PickChainTarget(candidates);
        if (pick < 0 || lifetime?.Check() == false)
        {
            return;
        }

        Hurtbox best = hurtboxes[pick];
        bool toBrand = candidates[pick].Branded;
        // Drawn from where the bolt struck the body; the search above keeps its own centre.
        SpellVfx.Arc(spell.School, caster, impactPoint ?? center, SpellResolver.VolumeCentre(best),
            toBrand ? SpellArcKind.Brand : SpellArcKind.Chain, spell);
        var arc = packet with { Amount = ChainDamage(packet.Amount, toBrand) };
        if (!best.Receive(arc).Killed && lifetime?.Check() != false)
        {
            SpellResolver.ApplyStatus(best.OwnerEntity, spell, caster);
        }
    }
}
