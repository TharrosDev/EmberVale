using Embervale.Entities;
using Godot;

namespace Embervale.Magic.Vfx;

/// <summary>The cast itself: the aura in the hand while a spell winds up, charges or channels, and
/// the flash as it leaves.</summary>
public static partial class SpellVfx
{
    /// <summary>Where an aura sits on a body with no casting hand, above its origin.</summary>
    private static readonly Vector3 ChestOffset = new(0f, 1.3f, 0f);

    /// <summary>
    /// A caster began winding up, charging or sustaining <paramref name="spell"/>. One caster has one
    /// such aura at a time: a new call replaces the last (a charge handing over to its cast wind-up, a
    /// wind-up handing over to its channel). <paramref name="seconds"/> is how long the wind-up or
    /// the full charge takes (0 for a channel); <paramref name="charge"/> is what a held cast reached.
    /// </summary>
    public static void Windup(IEntity caster, SpellResource spell, SpellWindupKind kind, float seconds, float charge = 0f)
    {
        if (!Active || caster == null || VfxAnchor.BodyOf(caster) is not { } body)
        {
            return;
        }

        ulong key = caster.RuntimeId;
        StopRig(Auras, key);

        // Never culled and never recycled: on an enemy this aura is the warning the player dodges on.
        VfxAnchor hand = VfxAnchor.ToHand(caster, ChestOffset);
        Vector3 at = hand.TryResolve(out Vector3 resolved) ? resolved : body.GlobalPosition + ChestOffset;
        if (!Begin(spell, caster, at, out VfxCast cast, essential: true, sustained: true))
        {
            return;
        }

        var rig = new VfxRig { Spell = spell };
        Prune(Auras);
        Auras[key] = rig;
        if (SpecialOf(spell)?.Windup is { } special && special(cast, rig, hand, kind, seconds, charge))
        {
            return;
        }

        VfxPlan plan = cast.Plan(VfxRole.Cast);

        // An enemy's aura is drawn larger (its colours are dimmer): it has to be read across a fight.
        float size = (0.16f + (0.2f * cast.Weight)) * (cast.ByPlayer ? 1f : 1.35f) * plan.Scale;
        VfxFlareSpec glow = VfxFlareSpec.At(at, size, cast.Colors);
        glow.Sustain = true;
        glow.Light = plan.Light;
        glow.LightRange = 3.5f;
        switch (kind)
        {
            case SpellWindupKind.Charge:
                glow.Level = 0.15f; // WindupProgress fills it
                break;

            case SpellWindupKind.Channel:
                glow.Level = 0.85f;
                break;

            default:
                // Builds to full exactly as the wind-up ends, from wherever a held charge left it.
                glow.Level = Mathf.Clamp(0.35f + (0.5f * charge), 0f, 1f);
                glow.RampSeconds = Mathf.Max(0.05f, seconds);
                break;
        }

        rig.Flare = rig.Add(cast.Fx.Flare(glow));
        rig.Flare.Get?.Follow(hand);

        if (plan.Particles != VfxParticles.None)
        {
            rig.Density = plan.Density * (kind == SpellWindupKind.Channel ? 0.7f : 0.5f);
            VfxBurstSpec stream = VfxBurstSpec.At(at, cast.Colors, rig.Density);
            stream.Continuous = true;
            stream.SpeedScale = 0.45f;
            stream.SizeScale = 0.75f;
            stream.LifeScale = 0.6f;
            stream.GravityScale = 0.4f;
            if (plan.Inward)
            {
                // Motes drawn in to the hand from all around it.
                stream.Inward = true;
                stream.Extents = Vector3.One * 0.55f;
            }
            else
            {
                stream.Extents = Vector3.One * 0.08f;
            }

            rig.Stream = rig.Add(cast.Fx.Burst(plan.Particles, stream));
            rig.Stream.Get?.Follow(hand);
        }

        if (plan.Sigil)
        {
            VfxDiscSpec sigil = VfxDiscSpec.At(at, size * 2.2f, cast.Colors);
            sigil.Sustain = true;
            sigil.Rune = true;
            sigil.FaceCamera = true;
            sigil.Spin = 1.6f;
            sigil.Body = 0.15f;
            sigil.Rim = 0f;
            VfxHandle<VfxDisc> disc = rig.Add(cast.Fx.Disc(sigil));
            disc.Get?.Follow(hand);
        }
    }

    /// <summary>How full the caster's held charge is now (0..1). Called every frame it is held.</summary>
    public static void WindupProgress(IEntity caster, float progress)
    {
        if (!Active || caster == null || !Auras.TryGetValue(caster.RuntimeId, out VfxRig? rig))
        {
            return;
        }

        float filled = Mathf.Clamp(progress, 0f, 1f);
        rig.Flare.Get?.SetLevel(0.15f + (0.85f * filled));
        rig.Stream.Get?.SetDensity(rig.Density * (0.6f + (1.4f * filled)));
    }

    /// <summary>The caster's wind-up, charge or channel is over. Safe to call with none running.</summary>
    public static void WindupEnd(IEntity caster, SpellWindupEnd how)
    {
        if (caster == null || !Auras.Remove(caster.RuntimeId, out VfxRig? rig))
        {
            return;
        }

        if (!Active)
        {
            return;
        }

        rig.Stop();

        // Cut off: the gathered power gutters out as a puff rather than simply vanishing.
        if (how == SpellWindupEnd.Interrupted && rig.Spell is { } spell &&
            VfxAnchor.BodyOf(caster) is { } body)
        {
            VfxAnchor hand = VfxAnchor.ToHand(caster, ChestOffset);
            Vector3 at = hand.TryResolve(out Vector3 resolved) ? resolved : body.GlobalPosition + ChestOffset;
            if (Begin(spell, caster, at, out VfxCast cast))
            {
                VfxFlareSpec fizzle = VfxFlareSpec.At(at, 0.22f, cast.Colors.Scaled(0.5f, 0.6f));
                fizzle.Life = 0.25f;
                cast.Fx.Flare(fizzle);
                VfxBurstSpec puff = VfxBurstSpec.At(at, cast.Colors, VfxQuality.Budget.ParticleMultiplier * 0.4f);
                puff.SizeScale = 0.4f;
                puff.SpeedScale = 0.5f;
                puff.LifeScale = 0.5f;
                cast.Fx.Burst(VfxParticles.Smoke, puff);
            }
        }
    }

    /// <summary>The spell left the caster: the release frame, for every delivery and every channel
    /// tick. <paramref name="origin"/> and <paramref name="direction"/> are the true aim.</summary>
    public static void Release(IEntity caster, SpellResource spell, Vector3 origin, Vector3 direction, float charge)
    {
        if (!Active || caster == null || VfxAnchor.BodyOf(caster) is not { } body)
        {
            return;
        }

        Vector3 hand = CastOrigin(caster, origin);
        if (!Begin(spell, caster, hand, out VfxCast cast))
        {
            return;
        }

        // A channel releases several times a second: a flicker at the hand, not a cast flash each.
        if (spell.CastMode == CastMode.Channeled)
        {
            VfxFlareSpec flicker = VfxFlareSpec.At(hand, 0.14f + (0.08f * cast.Weight), cast.Colors);
            flicker.Life = 0.12f;
            cast.Fx.Flare(flicker);
            return;
        }

        if (SpecialOf(spell)?.Release is { } special && special(cast, hand, direction, charge))
        {
            return;
        }

        VfxPlan plan = cast.Plan(VfxRole.Cast);
        float full = Mathf.Clamp(charge, 0f, 1f);
        if (plan.Flare)
        {
            VfxFlareSpec snap = VfxFlareSpec.At(hand, (0.3f + (0.3f * cast.Weight) + (0.3f * full)) * plan.Scale, cast.Colors);
            snap.Life = 0.2f;
            snap.Light = plan.Light;
            snap.LightRange = 4.5f;

            // A heavy or fully charged cast leaves the hand with a burst of rays, not a wider disc.
            snap.Rays = cast.Weight >= 0.6f || full >= 0.6f;
            cast.Fx.Flare(snap);
        }

        if (plan.Particles != VfxParticles.None && direction.LengthSquared() > 0.0001f)
        {
            // A spit of the school's particles along the aim.
            VfxBurstSpec spit = VfxBurstSpec.At(hand, cast.Colors, plan.Density * (0.45f + (0.4f * full)));
            spit.Direction = direction;
            spit.Spread = 28f;
            spit.SpeedScale = 1.2f;
            spit.LifeScale = 0.6f;
            cast.Fx.Burst(plan.Particles, spit);
        }

        // A spell on oneself has no impact call of its own (nothing was struck), so its impact beat
        // is drawn here, on the caster. A blink draws its own two ends.
        if (spell.Delivery == SpellDelivery.Self && spell.BlinkDistance <= 0f)
        {
            Vector3 centre = body.GlobalPosition + Vector3.Up;
            VfxPlan impact = cast.Plan(VfxRole.Impact);
            Blast(cast, impact, centre, 1.25f * impact.Scale, Vector3.Zero, body.GlobalPosition.Y, false);
            Linger(cast, centre, body, 1.1f);
        }
    }

    /// <summary>The cast beat at the casting hand, when the cast animation starts.</summary>
    public static void HandFlash(IEntity caster, SpellResource spell, Vector3 hand)
    {
        if (caster != null && IsPlayer(caster) && VfxAnchor.BodyOf(caster) is { } own &&
            TryFirstPersonHand(own, out Vector3 inView))
        {
            hand = inView;
        }

        if (!Begin(spell, caster, hand, out VfxCast cast))
        {
            return;
        }

        VfxFlareSpec flash = VfxFlareSpec.At(hand, 0.2f + (0.12f * cast.Weight), cast.Colors);
        flash.Life = 0.16f;
        cast.Fx.Flare(flash);
    }
}
