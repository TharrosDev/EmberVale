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
        Resolve(context, spell, packet, caster, casterTeam, center, radius, coneDirection: null);
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

        Godot.Collections.Array<Godot.Collections.Dictionary> hits = space.IntersectShape(query, 32);
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

            if (!IsHostileTarget(hurtbox, caster, casterTeam) ||
                !struck.TryHit(hurtbox.OwnerEntity, hurtbox))
            {
                continue;
            }

            hurtbox.Receive(packet);
            SchoolIdentity.OnSpellHit(context, spell, packet, caster, casterTeam, hurtbox);
            SpellCombo.OnHit(spell, caster, hurtbox);
            ApplyStatus(hurtbox.OwnerEntity, spell, caster);
        }
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
}
