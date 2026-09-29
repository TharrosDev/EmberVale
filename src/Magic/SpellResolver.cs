using Embervale.Combat;
using Embervale.Core.Diagnostics;
using Embervale.Core.Events;
using Embervale.Entities;
using Embervale.Stats;
using Godot;

namespace Embervale.Magic;

/// <summary>
/// Shared resolution logic for spell impacts, used by both <see cref="SpellProjectile"/>
/// (on contact) and <see cref="SpellcastingComponent"/> (for instant area casts).
/// It applies a spell's <see cref="DamagePacket"/> and optional status effect to the
/// eligible target(s), honouring the same friendly-fire rules a <see cref="Hitbox"/>
/// uses (never the caster, never an ally on the caster's team).
/// </summary>
/// <summary>What became of a spell aimed at one hurtbox.</summary>
public enum SpellHitResult
{
    /// <summary>Whiffed: a dodge's i-frames, or a target already dead.</summary>
    Missed,

    /// <summary>A guard stopped it (chip damage only, no rider effects).</summary>
    Blocked,

    /// <summary>It landed.</summary>
    Landed,
}

public static class SpellResolver
{
    /// <summary>
    /// Most hurtboxes one burst will examine.
    ///
    /// ⚠️ <b>IT IS A HURTBOX CAP, NOT AN ACTOR CAP, AND IT USED TO BE SILENT.</b> A 35A multi-zone
    /// body spends four of these on its own, so 32 was closer to eight actors than to thirty-two —
    /// and when a blast saturated it, the overflow was simply not hit, with no trace anywhere and no
    /// rule about which ones survived (the physics server's order is arbitrary). Raised, and
    /// <see cref="Resolve"/> now says so when it fills.
    /// </summary>
    private const int MaxHurtboxesPerBurst = 64;

    /// <summary>Delivers a single-target hit (damage + school identity + status) to one hurtbox.
    /// <paramref name="dealDamage"/> false is a status-only touch (a barrier of a spell with no damage).
    /// </summary>
    public static SpellHitResult HitOne(
        Node3D context, Hurtbox hurtbox, DamagePacket packet, SpellResource spell, IEntity? caster, int casterTeam,
        bool dealDamage = true)
    {
        IEntity? target = hurtbox.OwnerEntity;
        CombatComponent? combat = hurtbox.Combat;

        // A dodge's i-frames whiff the whole spell, status included, and a corpse takes nothing.
        if (combat is { IsInvulnerable: true } ||
            target?.GetComponent<StatsComponent>() is { IsAlive: false })
        {
            return SpellHitResult.Missed;
        }

        // A spell that eats a status (a consume-and-bonus spell) strips the stacks as it lands and is
        // stronger for each one.
        if (spell.ConsumesStatusId.Length > 0 &&
            target?.GetComponent<StatusEffectsComponent>() is { } statuses)
        {
            int stacks = statuses.StacksOf(spell.ConsumesStatusId);
            if (stacks > 0)
            {
                packet = packet with
                {
                    Amount = packet.Amount * SpellRules.ConsumeMultiplier(stacks, spell.BonusPerConsumedStack),
                };
                statuses.Consume(spell.ConsumesStatusId);
            }
        }

        DamageResult result = dealDamage ? hurtbox.Receive(packet) : default;

        // A guard stopped it: chip damage only, and none of the rider effects. Never parried (a spell is
        // not parryable), so a blocked result is the whole story.
        if (result.IsBlocked && caster != null && target != null)
        {
            EventBus.Instance?.Publish(new SpellBlockedEvent(caster, spell.Id, target));
            return SpellHitResult.Blocked;
        }

        SchoolIdentity.OnSpellHit(context, spell, packet, caster, casterTeam, hurtbox);
        SpellCombo.OnHit(spell, caster, hurtbox);
        ApplyStatus(target, spell, caster);

        if (caster != null && target != null)
        {
            EventBus.Instance?.Publish(
                new SpellHitEvent(caster, target, spell.Id, result.FinalAmount, result.IsCrit || packet.IsCrit));
        }

        return SpellHitResult.Landed;
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
        Resolve(context, spell, packet, caster, casterTeam, center, radius, coneDirection: null);
        CatchCaster(spell, caster, center, radius);
    }

    /// <summary>A zone or burst that <see cref="SpellResource.AffectsCaster"/> afflicts a caster standing
    /// in it with the spell's status (Blizzard: you can be caught in your own). Status only, never damage.</summary>
    private static void CatchCaster(SpellResource spell, IEntity? caster, Vector3 center, float radius)
    {
        if (!spell.AffectsCaster || caster?.Body is not Node3D body ||
            body.GlobalPosition.DistanceTo(center) > radius + 0.9f)
        {
            return;
        }

        ApplyStatus(caster, spell, caster);
    }

    /// <summary>
    /// Sweeps a wedge out from <paramref name="origin"/> along <paramref name="direction"/> — the
    /// same burst as <see cref="Detonate"/>, narrowed to everything in front (Phase 35C, dragon
    /// breath). The cone's reach is the query radius; <see cref="SpellCone"/> is the only thing that
    /// differs, which is why both shapes share <see cref="Resolve"/> rather than being two resolvers
    /// that must be kept in step.
    /// </summary>
    public static void Sweep(
        Node3D context,
        SpellResource spell,
        DamagePacket packet,
        IEntity? caster,
        int casterTeam,
        Vector3 origin,
        Vector3 direction,
        float length,
        float angleDegrees)
    {
        SpawnConeFlash(context, origin, direction, length, angleDegrees, SpellSchools.Color(spell.School));
        Resolve(context, spell, packet, caster, casterTeam, origin, length, (direction, angleDegrees));
    }

    /// <summary>The shared body: a hurtbox query at a point, each eligible actor hit once. A
    /// <paramref name="cone"/> narrows the candidates to a wedge; null keeps the full sphere.</summary>
    private static void Resolve(
        Node3D context,
        SpellResource spell,
        DamagePacket packet,
        IEntity? caster,
        int casterTeam,
        Vector3 center,
        float radius,
        (Vector3 Direction, float AngleDegrees)? coneDirection)
    {
        PhysicsDirectSpaceState3D space = context.GetWorld3D().DirectSpaceState;
        var query = new PhysicsShapeQueryParameters3D
        {
            Shape = new SphereShape3D { Radius = radius },
            Transform = new Transform3D(Basis.Identity, center),
            CollideWithAreas = true,
            CollideWithBodies = false,
            CollisionMask = CombatLayers.Hurtbox,
        };

        Godot.Collections.Array<Godot.Collections.Dictionary> hits =
            space.IntersectShape(query, MaxHurtboxesPerBurst);
        if (hits.Count >= MaxHurtboxesPerBurst)
        {
            Log.Warn($"Spell '{spell.Id}' filled its {MaxHurtboxesPerBurst}-hurtbox burst budget at " +
                     $"{center}; targets beyond it were not hit.");
        }

        // Per-actor, not per-hurtbox: a blast clipping three zones of one dragon is still one hit (35A).
        var struck = new HitDedupe();
        foreach (Godot.Collections.Dictionary hit in hits)
        {
            if (!hit.TryGetValue("collider", out Variant colliderVar) ||
                colliderVar.AsGodotObject() is not Hurtbox hurtbox)
            {
                continue;
            }

            // Angle before dedupe: a zone outside the wedge must not spend the actor's one hit and
            // shadow a zone that is inside it.
            if (coneDirection is { } cone &&
                !SpellCone.Contains(center, cone.Direction, cone.AngleDegrees, radius, VolumeCentre(hurtbox)))
            {
                continue;
            }

            if (!IsHostileTarget(hurtbox, caster, casterTeam))
            {
                continue;
            }

            // ⚠️ A BLAST DOES NOT GO THROUGH WALLS. The query is a sphere on the hurtbox layer and
            // nothing else, so a fireball bursting against one face of the smithy damaged everyone
            // standing on the other side of it — and the cone version reached through the arena wall
            // for its whole length. Tested before the dedupe, so a zone hidden behind cover cannot
            // spend the actor's one hit and shadow a zone that is exposed.
            if (IsOccluded(space, center, VolumeCentre(hurtbox)))
            {
                continue;
            }

            if (!struck.TryHit(hurtbox.OwnerEntity, hurtbox))
            {
                continue;
            }

            HitOne(context, hurtbox, packet, spell, caster, casterTeam);
        }
    }

    /// <summary>
    /// Is there solid world geometry between the blast's centre and a target?
    ///
    /// World layer only: an actor's own body never shields another (a crowd is not cover), and the
    /// ray does not report a shape it starts inside, so a burst that detonated against a wall is not
    /// blocked by that same wall.
    /// </summary>
    private static bool IsOccluded(PhysicsDirectSpaceState3D space, Vector3 center, Vector3 target)
    {
        if (center.DistanceSquaredTo(target) < 0.0001f)
        {
            return false;
        }

        PhysicsRayQueryParameters3D ray =
            PhysicsRayQueryParameters3D.Create(center, target, CombatLayers.World);
        ray.CollideWithAreas = false;
        return space.IntersectRay(ray).Count > 0;
    }

    /// <summary>
    /// Where a hurtbox actually sits. An <see cref="Area3D"/>'s own origin is the actor's origin —
    /// for a 35A multi-zone body every zone carries its offset on the <c>CollisionShape3D</c> child,
    /// so testing the Area would place a dragon's head, wings and tail at the same point and let a
    /// cone take all of them or none. Falls back to the Area for the ordinary one-shape hurtbox,
    /// where the two are the same anyway.
    /// </summary>
    private static Vector3 VolumeCentre(Hurtbox hurtbox)
    {
        foreach (Node child in hurtbox.GetChildren())
        {
            if (child is CollisionShape3D shape)
            {
                return shape.GlobalPosition;
            }
        }

        return hurtbox.GlobalPosition;
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

    private static void SpawnFlash(Node3D context, Vector3 center, float radius, Color color) =>
        SpawnFlashAt(context, center, radius, color);

    /// <summary>A cast or impact flare at a point, from the spell's school. Presentation only.</summary>
    public static void SpawnFlashAt(Node3D context, Vector3 center, float radius, Color color)
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

    /// <summary>
    /// Greyboxes the cone as a line of widening flashes along its axis — enough to read the shape and
    /// its reach, which an invisible attack does not have. Reuses <see cref="SpellFlash"/> rather than
    /// growing a mesh: a real particle cone is an art pass, and this is the same standard as the
    /// dragon's own placeholder body.
    /// </summary>
    private static void SpawnConeFlash(
        Node3D context, Vector3 origin, Vector3 direction, float length, float angleDegrees, Color color)
    {
        if (direction.LengthSquared() < 0.0001f || length <= 0f)
        {
            return;
        }

        const int Puffs = 4;
        Vector3 axis = direction.Normalized();
        float halfAngle = Mathf.DegToRad(angleDegrees * 0.5f);

        for (int i = 1; i <= Puffs; i++)
        {
            float travelled = length * i / Puffs;
            // The cone's radius at this distance — so the flashes trace the actual damaged volume.
            SpawnFlash(context, origin + (axis * travelled), travelled * Mathf.Tan(halfAngle), color);
        }
    }

    /// <summary>
    /// Draws every hostile body within <paramref name="radius"/> of <paramref name="center"/> toward it
    /// by <paramref name="strength"/> metres a second for this <paramref name="dt"/> (Gravity Well).
    /// Swept with <c>MoveAndCollide</c>, so nobody is pulled through a wall, and never past the centre.
    /// </summary>
    public static void Pull(
        Node3D context, Vector3 center, float radius, float strength, double dt, IEntity? caster, int casterTeam)
    {
        if (strength <= 0f)
        {
            return;
        }

        PhysicsDirectSpaceState3D space = context.GetWorld3D().DirectSpaceState;
        var query = new PhysicsShapeQueryParameters3D
        {
            Shape = new SphereShape3D { Radius = radius },
            Transform = new Transform3D(Basis.Identity, center),
            CollideWithAreas = true,
            CollideWithBodies = false,
            CollisionMask = CombatLayers.Hurtbox,
        };

        var pulled = new HitDedupe();
        foreach (Godot.Collections.Dictionary hit in space.IntersectShape(query, MaxHurtboxesPerBurst))
        {
            if (!hit.TryGetValue("collider", out Variant v) || v.AsGodotObject() is not Hurtbox hurtbox ||
                !IsHostileTarget(hurtbox, caster, casterTeam) ||
                hurtbox.OwnerEntity?.Body is not CharacterBody3D body ||
                !pulled.TryHit(hurtbox.OwnerEntity, hurtbox))
            {
                continue;
            }

            Vector3 toCentre = center - body.GlobalPosition;
            toCentre.Y = 0f;
            float distance = toCentre.Length();
            if (distance < 0.4f)
            {
                continue;
            }

            float step = Mathf.Min(strength * (float)dt, distance - 0.3f);
            if (step > 0f)
            {
                body.MoveAndCollide(toCentre / distance * step);
            }
        }
    }
}
