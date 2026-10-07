using Embervale.Combat;
using Embervale.Entities;
using Godot;

namespace Embervale.Magic.Vfx;

/// <summary>
/// Spells placed on the ground: the flourish over a telegraph, the thing that falls for the delay,
/// and the ambience of a zone that stays.
///
/// <para>Every node handed in here is positioned by its caller just <em>after</em> the call. So
/// nothing here reads where the node is: each block follows it (<see cref="VfxAnchor"/>) and is
/// built through a late spawner (<see cref="VfxSpawner.AsLate"/>), which holds it back, unseen,
/// until its first frame.</para>
/// </summary>
public static partial class SpellVfx
{
    /// <summary>The shortest delay worth dropping something from the sky for.</summary>
    private const float MeteorMinDelay = 0.35f;

    /// <summary>
    /// A ground spell began its delay. <paramref name="ground"/> is the node waiting at the landing
    /// point; it is positioned just after this call, so read its transform on the next frame and
    /// follow its validity. A spell whose recipe says so falls as a <see cref="Meteor"/> instead.
    /// </summary>
    public static void GroundTelegraph(Node3D ground, SpellResource spell, IEntity? caster, float radius, float delay)
    {
        if (!Active || ground == null || spell == null)
        {
            return;
        }

        // A recipe may ask for a meteor outright. A spell nobody authored gets one when it is the
        // kind of spell a meteor suits: it waits, then bursts once (no zone, no pull).
        if (FallsFromSky(spell, delay))
        {
            Meteor(ground, spell, caster, radius, delay);
            return;
        }

        if (OpenGround(ground, spell, caster, out VfxCast cast, out VfxRig rig) &&
            !(SpecialOf(spell)?.GroundTelegraph is { } special && special(cast, rig, ground, radius, delay)))
        {
            Telegraph(cast, rig, ground, radius, delay);
        }
    }

    /// <summary>Whether a ground spell with this <paramref name="delay"/> is drawn as something
    /// falling out of the sky: its recipe says so, or nobody authored it and it is the kind of spell
    /// a meteor suits (it waits, then bursts once: no zone, no pull).</summary>
    private static bool FallsFromSky(SpellResource spell, float delay) =>
        SpellVfxCatalog.For(spell).Ground == VfxGroundStyle.Meteor ||
        (!SpellVfxCatalog.Has(spell.Id) && delay >= MeteorMinDelay && spell.ZoneDuration <= 0f &&
         spell.PullStrength <= 0f && spell.BaseDamage > 0f);

    /// <summary>Something falls onto <paramref name="ground"/> for <paramref name="delay"/> seconds and
    /// lands as the delay ends. The landing itself arrives as a <see cref="Burst"/>.</summary>
    public static void Meteor(Node3D ground, SpellResource spell, IEntity? caster, float radius, float delay)
    {
        if (!Active || ground == null || spell == null ||
            !OpenGround(ground, spell, caster, out VfxCast cast, out VfxRig rig))
        {
            return;
        }

        if (SpecialOf(spell)?.GroundTelegraph is { } special && special(cast, rig, ground, radius, delay))
        {
            return;
        }

        Telegraph(cast, rig, ground, radius, delay);
        if (delay < 0.15f)
        {
            return;
        }

        // It comes in at a slant from high up, slowly at first and fast at the end, and is on the
        // ground exactly as the delay runs out.
        float height = Mathf.Min(34f, 14f + (delay * 9f));
        float slant = height * 0.32f;
        float turn = (_director!.NextSeed() & 0xFFFF) / 65535f * Mathf.Tau;
        var sky = new Vector3(Mathf.Cos(turn) * slant, height, Mathf.Sin(turn) * slant);
        VfxAnchor anchor = VfxAnchor.To(ground, Vector3.Up * 0.3f);
        float size = Mathf.Clamp(0.6f + (radius * 0.18f), 0.6f, 1.9f);
        VfxBudget budget = VfxQuality.Budget;
        VfxRichness rich = VfxQuality.Rich;

        // A burning rock, not a streak: a hot head, a ragged body of fire round it, flame and smoke
        // torn off behind it and a long tail.
        VfxFlareSpec head = VfxFlareSpec.At(Vector3.Zero, size * 0.8f, cast.Colors);
        head.Sustain = true;
        head.Light = budget.MaxLights > 0;
        head.LightRange = Mathf.Max(8f, radius * 3f);
        rig.Flare = rig.Add(cast.Fx.Flare(head));
        if (rig.Flare.Get is { } flare)
        {
            flare.Follow(anchor);
            flare.SettleFrom(sky, delay, accelerate: true);
        }

        if (budget.SecondaryDebris)
        {
            VfxShellSpec rock = VfxShellSpec.Sphere(Vector3.Zero, size * 1.15f, cast.Colors);
            rock.Sustain = true;
            rock.Layered = true;
            rock.Ragged = true;
            rock.Tiling = new Vector2(3f, 1.5f);
            rock.Scroll = new Vector2(0.25f, 1.3f);
            VfxHandle<VfxShell> body = rig.Add(cast.Fx.Shell(rock));
            if (body.Get is { } shell)
            {
                shell.Follow(anchor);
                shell.SettleFrom(sky, delay, accelerate: true);
            }
        }

        float trailSeconds = Mathf.Max(0.25f, VfxRecipeRules.TrailSeconds(budget.Trail) * 2.6f);
        VfxHandle<VfxBolt> tail = rig.Add(cast.Fx.Bolt(new VfxBoltSpec
        {
            Mode = VfxBoltMode.Trail,
            Colors = cast.Colors,
            Width = size * 0.75f,
            TrailSeconds = trailSeconds,
        }));
        if (tail.Get is { } ribbon)
        {
            ribbon.FollowTo(anchor);
            ribbon.SettleTrailFrom(sky, delay, accelerate: true);
        }

        // What it sheds on the way down: the school's particles, and smoke where the tier has debris.
        VfxBurstSpec shed = VfxBurstSpec.At(Vector3.Zero, cast.Colors, budget.ParticleMultiplier);
        shed.Continuous = true;
        shed.Extents = Vector3.One * (size * 0.4f);
        shed.SpeedScale = 0.4f;
        ShedFalling(rig, cast, SpellVfxCatalog.SchoolParticles(cast.School), shed, anchor, sky, delay);
        if (budget.SecondaryDebris)
        {
            shed.Density = budget.ParticleMultiplier * budget.DebrisMultiplier * 0.8f;
            shed.SizeScale = Mathf.Clamp(size * 1.2f, 0.6f, 2f);
            ShedFalling(rig, cast, VfxParticles.Smoke, shed, anchor, sky, delay);
        }

        if (rich.Billow && SchoolSmokes(cast.School))
        {
            // Fire torn off the rock as it falls, cooling to smoke in its wake.
            VfxBurstSpec flame = VfxBurstSpec.At(Vector3.Zero, cast.Colors, budget.ParticleMultiplier * 1.4f);
            flame.Continuous = true;
            flame.Extents = Vector3.One * (size * 0.35f);
            flame.SpeedScale = 0.3f;
            flame.SizeScale = Mathf.Clamp(size * 1.3f, 0.7f, 2.4f);
            flame.LifeScale = 0.8f;
            VfxHandle<VfxBurst> fire = rig.Add(cast.Fx.Burst(VfxEmitter.Flame, flame));
            if (fire.Get is { } emitter)
            {
                emitter.Follow(anchor);
                emitter.SettleFrom(sky, delay, accelerate: true);
            }
        }
    }

    /// <summary>The ground spell's delay is over: <paramref name="landed"/> when it came down (its
    /// <see cref="Burst"/> or <see cref="AttachZone"/> follows at once), false when it was cancelled or
    /// left the tree first. May be called more than once; the first call is the one that counts.</summary>
    public static void GroundEnd(Node3D ground, bool landed)
    {
        if (ground == null || Grounds.Count == 0 || !GodotObject.IsInstanceValid(ground) ||
            !Grounds.Remove(KeyOf(ground), out VfxRig? rig))
        {
            return;
        }

        if (landed)
        {
            // The blast that follows is the picture now: the falling head must not hang in it.
            rig.Flare.Kill();
        }

        rig.Stop();
    }

    /// <summary>A lingering zone began. Each of its pulses arrives as a <see cref="Burst"/>.
    /// <paramref name="zone"/> is positioned just after this call, so read its transform on the next
    /// frame and follow its validity.</summary>
    public static void AttachZone(Node3D zone, SpellResource spell, IEntity? caster, float radius, float duration)
    {
        if (!Active || zone == null || spell == null || !GodotObject.IsInstanceValid(zone))
        {
            return;
        }

        ulong key = KeyOf(zone);
        StopRig(Zones, key);

        // Essential: a zone is a place that hurts, wherever its caster stands, and where it will be
        // is not even known yet (so the distance rule has nothing to measure).
        if (!Begin(spell, caster, Vector3.Zero, out VfxCast opened, essential: true, sustained: true))
        {
            return;
        }

        VfxCast cast = Late(opened);
        var rig = new VfxRig { Spell = spell };
        Prune(Zones);
        Zones[key] = rig;
        if (SpecialOf(spell)?.Zone is { } special && special(cast, rig, zone, radius, duration))
        {
            return;
        }

        radius = Mathf.Max(0.5f, radius);
        VfxPlan plan = cast.Plan(VfxRole.Linger, radius);
        VfxAnchor floor = VfxAnchor.To(zone);
        bool pulls = spell.PullStrength > 0f;
        bool inward = plan.Inward || pulls;
        VfxBudget budget = VfxQuality.Budget;

        // The floor of the zone: its edge is the edge of the danger, on every tier.
        // A rim, the school's pattern spreading out from the centre and wisps: never a lit disc.
        VfxDiscSpec ground = VfxDiscSpec.At(Vector3.Zero, radius, cast.Colors);
        ground.Sustain = true;
        ground.Rune = plan.Sigil || cast.School == DamageType.Arcane;
        ground.Pattern = SchoolPattern(cast.School);
        ground.SpreadSeconds = ground.Rune ? 0f : 0.9f;
        ground.Spin = pulls ? -0.9f : ground.Rune ? 0.35f : 0f;
        ground.Flow = inward ? -0.7f : 0.35f;
        ground.Body = 0.35f;
        rig.Add(cast.Fx.Disc(ground)).Get?.Follow(floor);

        if (cast.School == DamageType.Frost && VfxQuality.Rich.Billow)
        {
            // Cold mist hugging the floor of a frost zone, and glints hanging in it.
            VfxBurstSpec mist = VfxBurstSpec.At(Vector3.Zero, cast.Colors, budget.ParticleMultiplier * Mathf.Clamp(radius * 0.25f, 0.5f, 1.6f));
            mist.Continuous = true;
            mist.Extents = new Vector3(radius * 0.75f, 0.1f, radius * 0.75f);
            mist.Direction = Vector3.Right;
            mist.Flatness = 1f;
            mist.SpeedScale = 0.7f;
            mist.SizeScale = Mathf.Clamp(radius * 0.3f, 0.7f, 2f);
            rig.Add(cast.Fx.Burst(VfxEmitter.Mist, mist)).Get?.Follow(VfxAnchor.To(zone, Vector3.Up * 0.3f));

            VfxBurstSpec glints = VfxBurstSpec.At(Vector3.Zero, cast.Colors, budget.ParticleMultiplier * Mathf.Clamp(radius * 0.2f, 0.4f, 1.4f));
            glints.Continuous = true;
            glints.Extents = new Vector3(radius * 0.8f, 1.1f, radius * 0.8f);
            rig.Add(cast.Fx.Burst(VfxEmitter.Glints, glints)).Get?.Follow(VfxAnchor.To(zone, Vector3.Up * 1.2f));
        }

        // What fills it between pulses.
        VfxParticles filling = plan.Particles != VfxParticles.None
            ? plan.Particles
            : SpellVfxCatalog.SchoolParticles(cast.School);
        float thickness = Mathf.Clamp(radius * 0.3f, 0.5f, 2f);
        VfxBurstSpec air = VfxBurstSpec.At(Vector3.Zero, cast.Colors, Mathf.Max(plan.Density, budget.ParticleMultiplier) * thickness);
        air.Continuous = true;
        air.SpeedScale = 0.6f;
        if (inward)
        {
            air.Inward = true;
            air.Extents = new Vector3(radius, 1.2f, radius);
        }
        else
        {
            air.Extents = new Vector3(radius * 0.75f, 1.1f, radius * 0.75f);
        }

        rig.Stream = rig.Add(cast.Fx.Burst(filling, air));
        rig.Stream.Get?.Follow(VfxAnchor.To(zone, Vector3.Up * 1.2f));

        if (plan.Secondary != VfxParticles.None)
        {
            VfxBurstSpec haze = VfxBurstSpec.At(Vector3.Zero, cast.Colors, plan.SecondaryDensity * thickness * 0.6f);
            haze.Continuous = true;
            haze.Extents = new Vector3(radius * 0.7f, 0.3f, radius * 0.7f);
            haze.SpeedScale = 0.5f;
            haze.SizeScale = plan.Secondary == VfxParticles.Smoke ? Mathf.Clamp(radius * 0.4f, 0.8f, 2.5f) : 1f;
            rig.Add(cast.Fx.Burst(plan.Secondary, haze)).Get?.Follow(VfxAnchor.To(zone, Vector3.Up * 0.4f));
        }

        // A glow and a light that hold between the pulses, so the zone never goes dark.
        if (plan.Flare)
        {
            VfxFlareSpec heart = VfxFlareSpec.At(Vector3.Zero, Mathf.Clamp(radius * 0.22f, 0.3f, 1.2f), cast.Colors.Scaled(0.6f, 1f));
            heart.Sustain = true;
            heart.Level = 0.6f;
            heart.Light = plan.Light;
            heart.LightRange = Mathf.Max(4f, radius * 2.2f);
            rig.Flare = rig.Add(cast.Fx.Flare(heart));
            rig.Flare.Get?.Follow(VfxAnchor.To(zone, Vector3.Up * 0.9f));
        }

        if (plan.Mark != VfxMark.None)
        {
            VfxHandle<VfxGroundMark> mark = rig.Add(cast.Fx.Mark(new VfxGroundMarkSpec
            {
                Mark = plan.Mark,
                Size = radius * 2f,
                Colors = cast.Colors,
                Sustain = true,
                Spin = plan.Mark == VfxMark.Rune ? (pulls ? -0.6f : 0.25f) : 0f,
                Reach = 1.5f,
            }));
            mark.Get?.Follow(floor);
        }

        if (pulls || plan.Distortion)
        {
            VfxHandle<VfxDistortion> warp = rig.Add(cast.Fx.Distortion(new VfxDistortionSpec
            {
                Radius = radius * 0.8f,
                Sustain = true,
                Strength = 0.03f,
            }));
            warp.Get?.Follow(VfxAnchor.To(zone, Vector3.Up * 0.8f));
        }
    }

    /// <summary>The zone ran out, was cancelled or left the tree. May be called more than once.</summary>
    public static void ZoneEnd(Node3D zone)
    {
        if (zone == null || Zones.Count == 0 || !GodotObject.IsInstanceValid(zone))
        {
            return;
        }

        StopRig(Zones, KeyOf(zone));
    }

    /// <summary>Opens the effect of a ground spell's delay and its entry in the table. A telegraph
    /// is a warning, so it is essential: never thinned by distance, never recycled.</summary>
    private static bool OpenGround(Node3D ground, SpellResource spell, IEntity? caster, out VfxCast cast, out VfxRig rig)
    {
        rig = new VfxRig { Spell = spell };
        cast = default;
        if (!GodotObject.IsInstanceValid(ground))
        {
            return false;
        }

        ulong key = KeyOf(ground);
        StopRig(Grounds, key);
        if (!Begin(spell, caster, Vector3.Zero, out VfxCast opened, essential: true, sustained: true))
        {
            return false;
        }

        cast = Late(opened);
        Prune(Grounds);
        Grounds[key] = rig;
        return true;
    }

    /// <summary>The flourish over a ground spell's own telegraph ring: a disc that fills as the
    /// delay runs down, and motes gathering over it.</summary>
    private static void Telegraph(in VfxCast cast, VfxRig rig, Node3D ground, float radius, float delay)
    {
        radius = Mathf.Max(0.5f, radius);
        VfxPlan plan = cast.Plan(VfxRole.Cast);
        VfxAnchor floor = VfxAnchor.To(ground);
        bool inward = plan.Inward || (cast.Spell?.PullStrength ?? 0f) > 0f;

        VfxDiscSpec disc = VfxDiscSpec.At(Vector3.Zero, radius, cast.Colors);
        disc.Life = Mathf.Max(0.1f, delay);
        disc.Fills = true;
        disc.Rune = plan.Sigil || cast.School == DamageType.Arcane;
        disc.Pattern = SchoolPattern(cast.School);
        disc.Spin = disc.Rune ? 0.7f : 0f;
        disc.Flow = inward ? -0.6f : 0.4f;
        disc.Body = 0.3f;
        rig.Add(cast.Fx.Disc(disc)).Get?.Follow(floor);

        if (plan.Particles != VfxParticles.None && delay >= 0.3f)
        {
            VfxBurstSpec gather = VfxBurstSpec.At(Vector3.Zero, cast.Colors, plan.Density * Mathf.Clamp(radius * 0.25f, 0.4f, 1.5f));
            gather.Continuous = true;
            gather.SpeedScale = 0.5f;
            gather.Direction = Vector3.Up;
            gather.Spread = 25f;
            if (inward)
            {
                gather.Inward = true;
                gather.Extents = new Vector3(radius, 0.6f, radius);
            }
            else
            {
                gather.Extents = new Vector3(radius * 0.7f, 0.1f, radius * 0.7f);
            }

            rig.Stream = rig.Add(cast.Fx.Burst(plan.Particles, gather));
            rig.Stream.Get?.Follow(VfxAnchor.To(ground, Vector3.Up * 0.3f));
        }
    }

    private static void ShedFalling(
        VfxRig rig, in VfxCast cast, VfxParticles kind, in VfxBurstSpec spec, in VfxAnchor anchor, Vector3 sky,
        float delay)
    {
        VfxHandle<VfxBurst> stream = rig.Add(cast.Fx.Burst(kind, spec));
        if (stream.Get is { } emitter)
        {
            emitter.Follow(anchor);
            emitter.SettleFrom(sky, delay, accelerate: true);
        }
    }

    /// <summary>The same call, with every block from here on held back until its first frame.</summary>
    private static VfxCast Late(in VfxCast cast) => new(
        cast.Spell, cast.Caster, cast.School, cast.ByPlayer, cast.Colors, cast.Recipe, cast.Authored, cast.Weight,
        cast.Fx.AsLate());
}
