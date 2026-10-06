using Embervale.Combat;
using Embervale.Entities;
using Godot;

namespace Embervale.Magic.Vfx;

/// <summary>Things in flight: a bolt's core, halo, trail and light, and a channelled beam.</summary>
public static partial class SpellVfx
{
    /// <summary>
    /// A bolt was launched. <paramref name="visualOrigin"/> is where its picture should start (the
    /// casting hand) before it settles onto the bolt's true path. Returns true when the facade drew
    /// the bolt, in which case the projectile hides its own plain sphere and light.
    /// <paramref name="charge"/> is what a held cast reached (0..1).
    /// </summary>
    public static bool AttachProjectile(
        Node3D projectile, SpellResource spell, IEntity? caster, Vector3 visualOrigin, Vector3 direction,
        float charge)
    {
        if (!Active || projectile == null || !GodotObject.IsInstanceValid(projectile) || !projectile.IsInsideTree())
        {
            return false;
        }

        ulong key = KeyOf(projectile);
        StopRig(Projectiles, key);

        // Essential: once the facade has taken a bolt's picture over, the budget must never take it
        // back, or the bolt would fly on unseen. Measured: one launched too far off is not taken
        // over at all, and keeps its plain sphere.
        Vector3 at = projectile.GlobalPosition;
        if (!Begin(spell, caster, at, out VfxCast cast, essential: true, sustained: true, measured: true))
        {
            return false;
        }

        VfxPlan plan = cast.Plan(VfxRole.Travel);
        var rig = new VfxRig { Spell = spell };

        // The bolt flies from the aim origin; its picture starts at the hand and settles onto it.
        Vector3 handOffset = visualOrigin - at;
        if (SpecialOf(spell)?.Projectile is { } special &&
            special(cast, rig, projectile, handOffset, direction, charge))
        {
            return Track(rig, key);
        }

        if (plan.IsEmpty && !rig.AnyLive)
        {
            return false;
        }

        VfxAnchor anchor = VfxAnchor.To(projectile);
        Vector3 velocity = direction.LengthSquared() > 0.0001f
            ? direction.Normalized() * Mathf.Max(0f, spell.ProjectileSpeed)
            : Vector3.Zero;
        float full = Mathf.Clamp(charge, 0f, 1f);
        float size = (0.2f + (0.16f * cast.Weight) + (0.2f * full)) * plan.Scale;
        VfxBudget budget = VfxQuality.Budget;

        // A channel throws a bolt every tick and draws them as one beam (see Beam): each bolt is
        // only a bright pulse running along it, with no trail or debris of its own.
        if (spell.CastMode == CastMode.Channeled)
        {
            VfxFlareSpec pulse = VfxFlareSpec.At(visualOrigin, size * 0.6f, cast.Colors);
            pulse.Sustain = true;
            rig.Flare = rig.Add(cast.Fx.Flare(pulse));
            if (rig.Flare.Get is { } bead)
            {
                bead.Follow(anchor);
                bead.SettleFrom(handOffset);
                bead.Glide(velocity);
            }

            return Track(rig, key);
        }

        if (plan.Flare)
        {
            VfxFlareSpec core = VfxFlareSpec.At(visualOrigin, size, cast.Colors);
            core.Sustain = true;
            core.Light = plan.Light;
            core.LightRange = 4f + (3f * cast.Weight);
            rig.Flare = rig.Add(cast.Fx.Flare(core));
            if (rig.Flare.Get is { } flare)
            {
                flare.Follow(anchor);
                flare.SettleFrom(handOffset);
                flare.Glide(velocity);
            }

            // A heavy bolt has a body of flowing noise inside its glow, not light alone.
            if (budget.SecondaryDebris && (cast.Weight >= 0.6f || full >= 0.6f || plan.Shell))
            {
                VfxShellSpec body = VfxShellSpec.Sphere(visualOrigin, size * 0.8f, cast.Colors);
                body.Sustain = true;
                body.Fresnel = plan.Shell;
                body.Layered = !plan.Shell;
                body.Scroll = new Vector2(0.2f, 1.1f);
                VfxHandle<VfxShell> shell = rig.Add(cast.Fx.Shell(body));
                if (shell.Get is { } flow)
                {
                    flow.Follow(anchor);
                    flow.SettleFrom(handOffset);
                    flow.Glide(velocity);
                }
            }
        }

        float trailSeconds = VfxRecipeRules.TrailSeconds(budget.Trail) * (1f + (0.6f * full));
        if (trailSeconds > 0f)
        {
            VfxHandle<VfxBolt> trail = rig.Add(cast.Fx.Bolt(new VfxBoltSpec
            {
                Mode = VfxBoltMode.Trail,
                From = visualOrigin,
                To = visualOrigin,
                Colors = cast.Colors,
                Width = size * 0.42f,
                TrailSeconds = trailSeconds,
            }));
            if (trail.Get is { } ribbon)
            {
                ribbon.FollowTo(anchor);
                ribbon.SettleTrailFrom(handOffset);
            }
        }

        if (VfxRecipeRules.TrailParticles(budget.Trail) && plan.Particles != VfxParticles.None)
        {
            VfxBurstSpec shed = VfxBurstSpec.At(visualOrigin, cast.Colors, plan.Density * 0.6f);
            shed.Continuous = true;
            shed.Extents = Vector3.One * (size * 0.3f);
            shed.SpeedScale = 0.3f;
            shed.LifeScale = 0.55f;
            shed.SizeScale = 0.8f;
            Shed(rig, cast, plan.Particles, shed, anchor, handOffset, velocity);

            if (VfxRecipeRules.TrailSparks(budget.Trail) && plan.Particles != VfxParticles.Sparks)
            {
                shed.Density = plan.Density * 0.35f;
                shed.SpeedScale = 0.25f;
                Shed(rig, cast, VfxParticles.Sparks, shed, anchor, handOffset, velocity);
            }
        }

        return Track(rig, key);
    }

    /// <summary>The bolt resolved, was cancelled or left the tree. May be called more than once for
    /// one flight, and for a bolt that was never attached.</summary>
    public static void DetachProjectile(Node3D projectile)
    {
        if (projectile == null || Projectiles.Count == 0 || !GodotObject.IsInstanceValid(projectile))
        {
            return;
        }

        // The core fades, the trail drains and the emitters stop where the bolt ended.
        StopRig(Projectiles, KeyOf(projectile));
    }

    /// <summary>One tick of a channelled bolt spell, as the line it travels: from the casting hand to
    /// the end of its range. A sustained beam is redrawn from these. <paramref name="to"/> is the full
    /// range and is not clipped to what the bolt strikes; that arrives later as its <see cref="Impact"/>.</summary>
    public static void Beam(IEntity caster, SpellResource spell, Vector3 from, Vector3 to)
    {
        if (!Active || caster == null || VfxAnchor.BodyOf(caster) == null)
        {
            return;
        }

        ulong key = caster.RuntimeId;
        double now = _director!.Now;

        // Kept alive from tick to tick; let go, it fades a little after the last one.
        float hold = (Mathf.Max(0.05f, spell.ChannelTickInterval) * 1.8f) + 0.08f;
        if (Beams.TryGetValue(key, out VfxRig? live) && live.Bolt.Get is { } beam && ReferenceEquals(live.Spell, spell))
        {
            beam.Refresh(from, Clip(live, from, to, now, hold), hold);
            return;
        }

        Beams.Remove(key);
        if (!Begin(spell, caster, from, out VfxCast cast, sustained: true))
        {
            return;
        }

        var rig = new VfxRig { Spell = spell };
        VfxAnchor hand = VfxAnchor.ToHand(caster, ChestOffset);
        Prune(Beams);
        Beams[key] = rig;
        if (SpecialOf(spell)?.Beam is { } special && special(cast, rig, hand, from, to))
        {
            return;
        }

        VfxBudget budget = VfxQuality.Budget;
        rig.Bolt = rig.Add(cast.Fx.Bolt(new VfxBoltSpec
        {
            Mode = VfxBoltMode.Beam,
            From = from,
            To = to,
            Colors = cast.Colors,
            Width = 0.06f + (0.05f * cast.Weight),
            Jitter = cast.School == DamageType.Lightning ? 0.05f : 0.018f,
            Segments = VfxRecipeRules.BoltSegments(budget, from.DistanceTo(to)),
            Seed = _director.NextSeed(),
        }));
        if (rig.Bolt.Get is { } bolt)
        {
            bolt.FollowFrom(hand);
            bolt.Refresh(from, to, hold);
        }
    }

    /// <summary>A beam's end for this tick: its full range, or where it last struck something if that
    /// was recent enough to still be true.</summary>
    private static Vector3 Clip(VfxRig rig, Vector3 from, Vector3 to, double now, float hold)
    {
        Vector3 line = to - from;
        float length = line.Length();
        if (length < 0.001f || now - rig.ClipAt > hold || rig.ClipDistance >= length)
        {
            return to;
        }

        return from + (line / length * Mathf.Max(0.2f, rig.ClipDistance));
    }

    /// <summary>Tells a caster's live beam that it struck something at <paramref name="position"/>,
    /// so it is drawn ending there instead of running on to the end of its range.</summary>
    private static void ClipBeam(IEntity? caster, SpellResource spell, Vector3 position)
    {
        if (caster == null || Beams.Count == 0 || !Beams.TryGetValue(caster.RuntimeId, out VfxRig? rig) ||
            !ReferenceEquals(rig.Spell, spell) || rig.Bolt.Get is not { } beam)
        {
            return;
        }

        Vector3 hand = CastOrigin(caster, position);
        rig.ClipDistance = hand.DistanceTo(position);
        rig.ClipAt = _director!.Now;
        beam.Refresh(hand, position, (Mathf.Max(0.05f, spell.ChannelTickInterval) * 1.8f) + 0.08f);
    }

    private static void Shed(
        VfxRig rig, in VfxCast cast, VfxParticles kind, in VfxBurstSpec spec, in VfxAnchor anchor, Vector3 handOffset,
        Vector3 velocity)
    {
        VfxHandle<VfxBurst> stream = rig.Add(cast.Fx.Burst(kind, spec));
        if (stream.Get is { } emitter)
        {
            emitter.Follow(anchor);
            emitter.SettleFrom(handOffset);
            emitter.Glide(velocity);
        }
    }

    /// <summary>Remembers a bolt's effect, if anything was drawn for it.</summary>
    private static bool Track(VfxRig rig, ulong key)
    {
        if (!rig.AnyLive)
        {
            return false;
        }

        Prune(Projectiles);
        Projectiles[key] = rig;
        return true;
    }
}
