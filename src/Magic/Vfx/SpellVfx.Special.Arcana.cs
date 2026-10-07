using Embervale.Combat;
using Embervale.Entities;
using Godot;

namespace Embervale.Magic.Vfx;

public static partial class SpellVfx
{
    // Colours a school does not have: bone for a mending that is not green, blood for a price paid,
    // and the tints that turn a leaf into a chip of bark, a mote into a stinging speck, smoke into ash.
    private static readonly Color ArcanaBark = new(0.62f, 0.44f, 0.24f);
    private static readonly Color ArcanaThorn = new(0.42f, 0.56f, 0.22f);
    private static readonly Color ArcanaSoil = new(0.5f, 0.42f, 0.3f);
    private static readonly Color ArcanaSting = new(1f, 0.86f, 0.3f);
    private static readonly Color ArcanaAsh = new(0.36f, 0.31f, 0.42f);

    /// <summary>How far from the camera a first-person body's head is at most (metres).</summary>
    private const float ArcanaInsideViewDistance = 0.8f;

    /// <summary>
    /// The special cases of the arcane, nature and necrotic spells and of the enemy-only spells (the
    /// spells whose recipes live in <c>SpellVfxCatalog.Arcana.cs</c>).
    ///
    /// <para>Three rules run through all of them. <b>Nothing is one flare scaled up to a radius</b>:
    /// a flare's halo is several times its core, so a big beat is rings, particles, a floor disc and
    /// one small hot core. <b>Each tier adds a layer</b>: Performance gets the core, the rings and the
    /// first particles; Medium adds the second particle layer and floor marks; High adds the
    /// pressure wave and extra rings; Ultra doubles the debris. <b>A spell on oneself is laid out so
    /// first person sees it</b>: the floor ring and the rising particles reach past the bottom of the
    /// frame (<see cref="ArcanaInside"/>), since the body-sized shell is hidden from inside.</para>
    ///
    /// <para>The sizes these are held to (how far a floor ring runs, how big a breath's body, smoke
    /// and glints may be, what the lean tiers leave out) are in <see cref="VfxArcanaRules"/>.</para>
    /// </summary>
    private static void RegisterArcanaSpecials(SpellVfxSpecialTable table)
    {
        SpellVfxSpecial nullLance = table.For("spell.null_lance");
        nullLance.Projectile = NullLanceProjectile;
        nullLance.Impact = NullLanceImpact;

        table.For("spell.arcane_shield").Release = ArcaneShieldRelease;
        table.For("spell.blink").Blink = BlinkMove;

        SpellVfxSpecial gravityWell = table.For("spell.gravity_well");
        gravityWell.GroundTelegraph = GravityWellTelegraph;
        gravityWell.Burst = GravityWellBurst;

        table.For("spell.mending_bloom").Release = MendingBloomRelease;

        SpellVfxSpecial lifebloom = table.For("spell.lifebloom_totem");
        lifebloom.Release = LifebloomRelease;
        lifebloom.Totem = LifebloomTotem;
        lifebloom.TotemPulse = LifebloomPulse;

        SpellVfxSpecial thornsnare = table.For("spell.thornsnare");
        thornsnare.GroundTelegraph = ThornsnareTelegraph;
        thornsnare.Burst = ThornsnareBurst;

        SpellVfxSpecial swarm = table.For("spell.stinging_swarm");
        swarm.Projectile = StingingSwarmProjectile;
        swarm.Impact = StingingSwarmImpact;

        table.For("spell.barkskin").Release = BarkskinRelease;

        // Every necrotic hit heals its caster, so every one of these draws a line home. Only the
        // siphon, whose whole point it is, draws the drain in full; the others get a thread.
        table.For("spell.ember_siphon").Arc = NecroticDrain;
        table.For("spell.grave_mark").Arc = NecroticThread;
        SpellVfxSpecial wither = table.For("spell.wither");
        wither.Arc = NecroticThread;
        wither.Impact = WitherImpact;
        wither.Windup = WitherWindup;
        wither.Projectile = WitherProjectile;
        table.For("spell.ash_breath").Arc = NecroticThread;

        // One body lighting up inside a breath, every tick it stands there.
        table.For("spell.dragon_breath").Impact = static (in VfxCast cast, in SpellImpactInfo hit) =>
            ArcanaBreathHit(cast, hit, VfxParticles.Embers);
        table.For("spell.drake_breath").Impact = static (in VfxCast cast, in SpellImpactInfo hit) =>
            ArcanaBreathHit(cast, hit, VfxParticles.Shards);
        table.For("spell.ash_breath").Impact = static (in VfxCast cast, in SpellImpactInfo hit) =>
            ArcanaBreathHit(cast, hit, VfxParticles.Embers, ashen: true);
        table.For("spell.elder_word").Impact = static (in VfxCast cast, in SpellImpactInfo hit) =>
            ArcanaBreathHit(cast, hit, VfxParticles.Motes);

        SpellVfxSpecial soulTithe = table.For("spell.soul_tithe");
        soulTithe.Release = SoulTitheRelease;
        soulTithe.Impact = SoulTitheImpact;
        soulTithe.Projectile = SoulTitheProjectile;
        soulTithe.Arc = NecroticThread;

        SpellVfxSpecial knitBone = table.For("spell.knit_bone");
        knitBone.Release = KnitBoneRelease;
        knitBone.Impact = KnitBoneImpact;

        foreach (string breath in new[] { "spell.dragon_breath", "spell.drake_breath", "spell.ash_breath", "spell.elder_word" })
        {
            SpellVfxSpecial special = table.For(breath);
            special.Windup = BreathWindup;
            special.Cone = ArcanaBreath;
        }
    }

    // --- what the hooks share --------------------------------------------------------------------

    /// <summary>The second particle layer and the floor marks are drawn (Medium and up).</summary>
    private static bool ArcanaRich => VfxQuality.Budget.SecondaryDebris;

    /// <summary>The pressure wave and the extra rings are drawn (High and up).</summary>
    private static bool ArcanaLavish => VfxQuality.Tier >= VfxTier.High;

    /// <summary>The short list only: cores, one ring, the first particles (Performance and Low).</summary>
    private static bool ArcanaLean => VfxArcanaRules.IsLean(VfxQuality.Tier);

    /// <summary>Everything, twice (Ultra).</summary>
    private static bool ArcanaWild => VfxQuality.Tier >= VfxTier.Ultra;

    /// <summary>A first-layer particle amount: <paramref name="amount"/> of a preset's own, at the tier.</summary>
    private static float ArcanaAmount(float amount) => VfxQuality.Budget.ParticleMultiplier * amount;

    /// <summary>A second-layer particle amount: nothing below Medium, doubled on Ultra.</summary>
    private static float ArcanaDebris(float amount)
    {
        VfxBudget budget = VfxQuality.Budget;
        return budget.SecondaryDebris ? budget.ParticleMultiplier * budget.DebrisMultiplier * amount : 0f;
    }

    /// <summary>The school's energies with other colours (bone, blood).</summary>
    private static VfxSchoolColors ArcanaTinted(in VfxSchoolColors like, Color core, Color mid, Color edge) =>
        like with { Core = core, Mid = mid, Edge = edge };

    private static VfxSchoolColors ArcanaBone(in VfxSchoolColors like) => ArcanaTinted(
        like, new Color(1f, 0.98f, 0.92f), new Color(0.92f, 0.88f, 0.8f), new Color(0.62f, 0.54f, 0.64f));

    private static VfxSchoolColors ArcanaBlood(in VfxSchoolColors like) => ArcanaTinted(
        like, new Color(1f, 0.55f, 0.45f), new Color(0.86f, 0.1f, 0.12f), new Color(0.4f, 0.02f, 0.06f));

    /// <summary>An ash breath's colours: dark grey-violet smoke with a dull magenta heart, at half
    /// the school's energy. The school's own pink at full strength read as confetti.</summary>
    private static VfxSchoolColors ArcanaAshen(in VfxSchoolColors like) => ArcanaTinted(
        like, new Color(0.66f, 0.32f, 0.58f), new Color(0.44f, 0.36f, 0.52f), new Color(0.2f, 0.17f, 0.25f))
        .Scaled(0.55f, 0.5f);

    /// <summary>
    /// Whether the camera sits inside <paramref name="body"/>'s head: the player's own body in first
    /// person. An effect on that body is then laid out wider and lower (the floor past the bottom of
    /// the frame, particles rising around rather than on the caster), because everything within arm's
    /// reach of the eye is faded out and a body-sized shell is not drawn from inside at all.
    /// </summary>
    private static bool ArcanaInside(Node3D body) =>
        _director is { HasCamera: true } director &&
        director.DistanceToCamera(body.GlobalPosition + (Vector3.Up * 1.6f)) < ArcanaInsideViewDistance;

    /// <summary>The flash at the casting hand as a spell on oneself lets go: small, with a light.</summary>
    private static void ArcanaSnap(in VfxCast cast, Vector3 hand, in VfxSchoolColors colors, float radius = 0.24f)
    {
        VfxFlareSpec snap = VfxFlareSpec.At(hand, radius, colors);
        snap.Life = 0.2f;
        snap.Light = VfxQuality.Budget.MaxLights > 0;
        snap.LightRange = 4f;
        cast.Fx.Flare(snap);
    }

    /// <summary>A thin shock ring and nothing else: no core, no halo, so it can be any size without
    /// washing the frame out. Flat on the floor unless <paramref name="normal"/> says otherwise.</summary>
    private static VfxHandle<VfxFlare> ArcanaRing(
        in VfxCast cast, Vector3 at, float radius, in VfxSchoolColors colors, float life, bool inward = false,
        Vector3 normal = default)
    {
        VfxFlareSpec ring = VfxFlareSpec.At(at, 0.2f, colors);
        ring.NoCore = true;
        ring.Ring = true;
        ring.RingRadius = Mathf.Max(0.1f, radius);
        ring.RingNormal = normal;
        ring.Life = life;
        ring.Inward = inward;
        return cast.Fx.Flare(ring);
    }

    /// <summary>How long a floor ring takes to run its reach: longer from inside the caster, where it
    /// has further to go and is most of the cue.</summary>
    private static float ArcanaFloorSeconds(bool inside, float seconds) => inside ? seconds + 0.35f : seconds;

    /// <summary>A soft glow that rides a body for a moment: the "it worked" of a spell on oneself.
    /// Left out from <paramref name="inside"/> that body: a halo at the chest is a wash over the
    /// whole view to an eye half a metre above it.</summary>
    private static void ArcanaAfterglow(
        in VfxCast cast, Node3D body, in VfxSchoolColors colors, float seconds, bool inside)
    {
        if (inside)
        {
            return;
        }

        VfxFlareSpec glow = VfxFlareSpec.At(body.GlobalPosition + Vector3.Up, 0.28f, colors.Scaled(0.5f, 0.9f));
        glow.Life = seconds;

        // The hand's snap already took a light; a second one held for a second is for the tiers
        // that have lights to spare.
        glow.Light = !ArcanaLean;
        glow.LightRange = 4f;
        cast.Fx.Flare(glow).Get?.Follow(VfxAnchor.To(body, Vector3.Up));
    }

    // --- arcane ----------------------------------------------------------------------------------

    /// <summary>The lance itself: a thin, long line behind the bolt, so it reads as a needle of light
    /// and not a ball, on every tier (Performance draws no other trail).</summary>
    private static bool NullLanceProjectile(
        in VfxCast cast, VfxRig rig, Node3D projectile, Vector3 handOffset, Vector3 direction, float charge)
    {
        Vector3 start = projectile.GlobalPosition + handOffset;
        VfxHandle<VfxBolt> lance = rig.Add(cast.Fx.Bolt(new VfxBoltSpec
        {
            Mode = VfxBoltMode.Trail,
            From = start,
            To = start,
            Colors = cast.Colors,
            Width = 0.034f,
            TrailSeconds = ArcanaRich ? 0.3f : 0.16f,
        }));
        if (lance.Get is { } line)
        {
            line.FollowTo(VfxAnchor.To(projectile));
            line.SettleTrailFrom(handOffset);
        }

        return false;
    }

    /// <summary>
    /// The lance lands as an implosion, replacing the generic hit (which was a white bubble and a
    /// ring): a front closing on the point with streaks drawn in after it, a heart that is dark
    /// inside a lit rim, and then the snap back out as a wider, slower ring. Medium and up break a
    /// glyph over it and throw sparks; High bends the air inward; Ultra adds a second ring.
    /// A hit on a guard, or a bolt that ran out, keeps the generic gutter.
    /// </summary>
    private static bool NullLanceImpact(in VfxCast cast, in SpellImpactInfo hit)
    {
        if (hit.Kind is SpellImpactKind.Blocked or SpellImpactKind.Expired ||
            (VfxAnchor.BodyOf(hit.Target) is { } struck && ArcanaInside(struck)))
        {
            return false;
        }

        VfxSchoolColors colors = cast.Colors;
        VfxTier tier = VfxQuality.Tier;
        Vector3 at = hit.Position;
        float scale = VfxRecipeRules.ImpactScale(cast.Weight, hit.Charge, hit.Crit, hit.Killed, hit.Kind);

        // The collapse: one block for the spark, its light and the front closing on it.
        VfxFlareSpec fold = VfxFlareSpec.At(at, 0.2f * scale, colors);
        fold.Life = 0.2f;
        fold.Ring = true;
        fold.RingRadius = 1.5f * scale;
        fold.RingNormal = hit.Normal;
        fold.Inward = true;
        fold.Light = VfxQuality.Budget.MaxLights > 0;
        fold.LightRange = 5f;
        cast.Fx.Flare(fold);

        // What is drawn in: thin pale streaks falling to the point from all around it. One draw.
        VfxMotifSpec drawn = VfxMotifSpec.At(
            at, colors, VfxSprite.Streak, VfxMotion.Inward, VfxArcanaRules.ImplosionStreaks(tier), 1.35f * scale,
            0.55f * scale);
        drawn.Aspect = 0.14f;
        drawn.Speed = 3f;
        drawn.Life = 0.36f;
        drawn.Energy = 1.3f;
        drawn.Pale = true;
        drawn.TrueSize = true;
        cast.Fx.Motif(drawn);

        // The snap back out: wider and slower than the collapse, so it is seen after it.
        ArcanaRing(cast, at, 2.1f * scale, colors, 0.44f, normal: hit.Normal);
        if (tier == VfxTier.Performance)
        {
            return true;
        }

        // The heart: nothing, with a lit rim. It covers what is behind it for a quarter of a second.
        VfxShellSpec heart = VfxShellSpec.Sphere(
            at, 0.3f * scale, colors with { Core = colors.Edge.Darkened(0.85f), Mid = colors.Edge.Darkened(0.7f) });
        heart.Fresnel = true;
        heart.RimPower = 2.5f;
        heart.Occlude = 0.9f;
        heart.Energy = 0.5f;
        heart.Life = 0.3f;
        heart.StartScale = 0.5f;
        heart.BurnsAway = true;
        cast.Fx.Shell(heart);

        if (ArcanaRich)
        {
            // The glyph that was on the lance, breaking.
            VfxDiscSpec glyph = VfxDiscSpec.At(at, 0.6f * scale, colors);
            glyph.Life = 0.7f;
            glyph.Rune = true;
            glyph.FaceCamera = true;
            glyph.Body = 0.1f;
            glyph.Rim = 0f;
            glyph.Spin = -3f;
            cast.Fx.Disc(glyph);

            VfxBurstSpec sparks = VfxBurstSpec.At(at, colors, ArcanaDebris(0.6f));
            sparks.SpeedScale = 1.3f;
            cast.Fx.Burst(VfxParticles.Sparks, sparks);
        }

        if (ArcanaLavish)
        {
            cast.Fx.Distortion(new VfxDistortionSpec
            {
                Position = at,
                Radius = 1.5f * scale,
                Life = 0.32f,
                Strength = 0.035f,
                Inward = true,
            });
        }

        if (ArcanaWild)
        {
            ArcanaRing(cast, at, 3f * scale, colors.Scaled(0.6f, 1f), 0.7f, normal: hit.Normal);
            VfxBurstSpec settling = VfxBurstSpec.At(at, colors, ArcanaAmount(0.5f));
            settling.Inward = true;
            settling.Extents = Vector3.One * (1.2f * scale);
            settling.LifeScale = 0.7f;
            cast.Fx.Burst(VfxParticles.Motes, settling);
        }

        return true;
    }

    /// <summary>The ward closes over the caster: a shell that forms from inside out, a rune circle
    /// underfoot, motes drawn in from all around, and a front along the floor.</summary>
    private static bool ArcaneShieldRelease(in VfxCast cast, Vector3 hand, Vector3 direction, float charge)
    {
        if (VfxAnchor.BodyOf(cast.Caster) is not { } body)
        {
            return false;
        }

        VfxSchoolColors colors = cast.Colors;
        Vector3 feet = body.GlobalPosition;
        Vector3 chest = feet + Vector3.Up;
        bool inside = cast.ByPlayer && ArcanaInside(body);
        float reach = VfxArcanaRules.FloorReach(inside);
        ArcanaSnap(cast, hand, colors);

        VfxShellSpec ward = VfxShellSpec.Sphere(chest, 1.02f, colors);
        ward.Fresnel = true;
        ward.Life = 1.1f;
        ward.StartScale = 0.55f;
        ward.Opacity = 0.85f;
        cast.Fx.Shell(ward).Get?.Follow(VfxAnchor.To(body, Vector3.Up));

        ArcanaRing(cast, feet + (Vector3.Up * 0.12f), reach, colors, ArcanaFloorSeconds(inside, 0.55f));
        VfxDiscSpec circle = VfxDiscSpec.At(feet, VfxArcanaRules.SelfDisc(inside, 1.5f), colors);
        circle.Life = 1.4f;
        circle.Rune = true;
        circle.Spin = 1.2f;
        circle.Flow = -0.4f;
        circle.Body = 0.2f;
        cast.Fx.Disc(circle).Get?.Follow(VfxAnchor.To(body));

        float spread = inside ? 2.4f : 1.3f;
        VfxBurstSpec gather = VfxBurstSpec.At(chest, colors, ArcanaAmount(inside ? 1f : 0.8f));
        gather.Inward = true;
        gather.Extents = new Vector3(spread, 1f, spread);
        gather.LifeScale = 0.6f;
        cast.Fx.Burst(VfxParticles.Motes, gather);

        VfxBurstSpec glints = VfxBurstSpec.At(chest, colors, ArcanaDebris(0.4f));
        glints.Extents = new Vector3(spread * 0.6f, 0.7f, spread * 0.6f);
        glints.SpeedScale = 0.45f;
        cast.Fx.Burst(VfxParticles.Sparks, glints);

        if (ArcanaLavish)
        {
            // A second, slower front, and a hoop about the waist as the shell settles.
            ArcanaRing(cast, feet + (Vector3.Up * 0.12f), reach * 0.6f, colors, 0.85f);
            if (!inside)
            {
                ArcanaRing(cast, chest, 1.05f, colors, 0.7f, inward: true);
            }
        }

        cast.Fx.Mark(new VfxGroundMarkSpec
        {
            Mark = VfxMark.Rune,
            Position = feet,
            Size = 2.6f,
            Colors = colors,
            Life = 5f,
            Reach = 1f,
        });
        return true;
    }

    /// <summary>The air folds shut where the caster was (rings closing, motes drawn in, a hoop
    /// collapsing across the path), a line runs between, and it bursts open where they are.</summary>
    private static bool BlinkMove(in VfxCast cast, Vector3 from, Vector3 to)
    {
        VfxSchoolColors colors = cast.Colors;
        VfxBudget budget = VfxQuality.Budget;
        Vector3 gone = from + Vector3.Up;
        Vector3 here = to + Vector3.Up;
        Vector3 line = to - from;
        float length = line.Length();
        Vector3 along = length > 0.01f ? line / length : Vector3.Zero;
        bool inside = cast.ByPlayer && _director!.HasCamera &&
                      _director.DistanceToCamera(to + (Vector3.Up * 1.6f)) < ArcanaInsideViewDistance;

        // Where they were. (The lean tiers keep the hoop that folds shut and drop the floor's.)
        bool lean = ArcanaLean;
        if (!lean)
        {
            ArcanaRing(cast, from + (Vector3.Up * 0.12f), 1.5f, colors, 0.4f, inward: true);
        }

        VfxFlareSpec fold = VfxFlareSpec.At(gone, 0.26f, colors);
        fold.Life = 0.22f;
        fold.Ring = along != Vector3.Zero;
        fold.RingRadius = 1.1f;
        fold.RingNormal = along;
        fold.Inward = true;
        cast.Fx.Flare(fold);
        VfxBurstSpec drawn = VfxBurstSpec.At(gone, colors, ArcanaAmount(0.9f));
        drawn.Inward = true;
        drawn.Extents = Vector3.One * 1.1f;
        drawn.LifeScale = 0.45f;
        cast.Fx.Burst(VfxParticles.Motes, drawn);
        if (!lean)
        {
            VfxDiscSpec left = VfxDiscSpec.At(from, 1.1f, colors);
            left.Life = 0.7f;
            left.Rune = true;
            left.Spin = -3f;
            left.Flow = -0.8f;
            left.Body = 0.15f;
            cast.Fx.Disc(left);
        }

        cast.Fx.Distortion(new VfxDistortionSpec { Position = gone, Radius = 1.3f, Life = 0.35f, Strength = 0.03f, Inward = true });

        // The line between: a smooth streak, a crackle along it, and what it shakes loose.
        if (length > 0.5f)
        {
            cast.Fx.Bolt(new VfxBoltSpec
            {
                Mode = VfxBoltMode.Tether,
                From = gone,
                To = here,
                Colors = colors,
                Width = 0.09f,
                Jitter = 0.01f,
                Segments = 4,
                Life = 0.28f,
                Seed = _director!.NextSeed(),
            });
            if (ArcanaRich)
            {
                cast.Fx.Bolt(new VfxBoltSpec
                {
                    Mode = VfxBoltMode.Lightning,
                    From = gone,
                    To = here,
                    Colors = colors,
                    Width = 0.03f,
                    Jitter = 0.04f,
                    Segments = VfxRecipeRules.BoltSegments(budget, length),
                    Life = 0.22f,
                    Seed = _director!.NextSeed(),
                });
            }

            VfxBurstSpec wake = VfxBurstSpec.At(
                (gone + here) * 0.5f, colors, lean ? 0f : ArcanaAmount(Mathf.Clamp(length * 0.14f, 0.4f, 1.5f)));
            wake.Extents = new Vector3(
                Mathf.Max(0.2f, Mathf.Abs(line.X) * 0.5f), 0.4f, Mathf.Max(0.2f, Mathf.Abs(line.Z) * 0.5f));
            wake.SpeedScale = 0.4f;
            wake.LifeScale = 0.6f;
            cast.Fx.Burst(VfxParticles.Motes, wake);
        }

        // Where they are. From inside, the core is kept small and the wave left out: both would be
        // wrapped around the eye.
        VfxFlareSpec appear = VfxFlareSpec.At(here, inside ? 0.2f : 0.32f, colors);
        appear.Life = 0.26f;
        appear.Light = budget.MaxLights > 0;
        appear.LightRange = 3f;
        cast.Fx.Flare(appear);
        if (inside && along != Vector3.Zero && _director is { HasCamera: true } view)
        {
            // From inside, everything above is at the feet, under the frame. The arrival shown at
            // eye level: a hoop opening ahead along the way they came, and streaks falling into it.
            Vector3 ahead = view.CameraPosition + (along * 2.6f);
            VfxFlareSpec open = VfxFlareSpec.At(ahead, 0.2f, colors.Scaled(0.6f, 1f));
            open.NoCore = true;
            open.Ring = true;
            open.RingRadius = 1.5f;
            open.RingNormal = along;
            open.Life = 0.3f;
            cast.Fx.Flare(open);
            if (!lean)
            {
                VfxBurstSpec fall = VfxBurstSpec.At(ahead, colors, ArcanaDebris(0.6f));
                fall.Inward = true;
                fall.Extents = Vector3.One * 1.8f;
                fall.LifeScale = 0.45f;
                cast.Fx.Burst(VfxParticles.Sparks, fall);
            }
        }
        ArcanaRing(
            cast, to + (Vector3.Up * 0.12f), VfxArcanaRules.FloorReach(inside), colors, ArcanaFloorSeconds(inside, 0.5f));
        VfxBurstSpec thrown = VfxBurstSpec.At(here, colors, ArcanaAmount(0.9f));
        thrown.SpeedScale = 1.3f;
        cast.Fx.Burst(VfxParticles.Motes, thrown);
        VfxBurstSpec sparks = VfxBurstSpec.At(here, colors, ArcanaDebris(0.5f));
        sparks.SpeedScale = 0.8f;
        cast.Fx.Burst(VfxParticles.Sparks, sparks);
        VfxDiscSpec arrived = VfxDiscSpec.At(to, VfxArcanaRules.SelfDisc(inside, 1.3f), colors);
        arrived.Life = 0.9f;
        arrived.Rune = true;
        arrived.Spin = 3f;
        arrived.Flow = 0.8f;
        arrived.Body = 0.15f;
        cast.Fx.Disc(arrived);
        if (!inside)
        {
            if (along != Vector3.Zero)
            {
                ArcanaRing(cast, here, 1.2f, colors, 0.35f, normal: along);
            }

            cast.Fx.Distortion(new VfxDistortionSpec { Position = here, Radius = 1.5f, Life = 0.4f, Strength = 0.03f });
        }

        cast.Fx.Mark(new VfxGroundMarkSpec
        {
            Mark = VfxMark.Rune,
            Position = to,
            Size = 2.2f,
            Colors = colors,
            Life = 4f,
            Reach = 1f,
        });
        return true;
    }

    /// <summary>While the well waits: a heart that brightens over the delay, streaks already falling
    /// toward it, and the air starting to bend. Added to the generic telegraph disc.</summary>
    private static bool GravityWellTelegraph(in VfxCast cast, VfxRig rig, Node3D ground, float radius, float delay)
    {
        VfxAnchor heart = VfxAnchor.To(ground, Vector3.Up);
        VfxFlareSpec core = VfxFlareSpec.At(Vector3.Zero, 0.2f, cast.Colors);
        core.Sustain = true;
        core.Level = 0.2f;
        core.RampSeconds = Mathf.Max(0.1f, delay);
        core.Light = VfxQuality.Budget.MaxLights > 0;
        core.LightRange = Mathf.Max(4f, radius * 1.5f);

        // The rig's flare is the one killed outright as the spell lands, so the heart does not hang
        // in the implosion that replaces it.
        rig.Flare = rig.Add(cast.Fx.Flare(core));
        rig.Flare.Get?.Follow(heart);

        VfxBurstSpec streaks = VfxBurstSpec.At(Vector3.Zero, cast.Colors, ArcanaDebris(0.6f));
        streaks.Continuous = true;
        streaks.Inward = true;
        streaks.Extents = new Vector3(radius, 0.8f, radius);
        rig.Add(cast.Fx.Burst(VfxParticles.Sparks, streaks)).Get?.Follow(heart);

        rig.Add(cast.Fx.Distortion(new VfxDistortionSpec
        {
            Radius = radius * 0.5f,
            Sustain = true,
            Strength = 0.018f,
            Inward = true,
        })).Get?.Follow(heart);
        return false;
    }

    /// <summary>
    /// The well closes. No dome of light: two fronts running in along the floor at different speeds,
    /// dust and streaks falling to one point, a small hot heart inside a shell that burns away, and
    /// the air pulled in after them. What stays is a rune circle turning on the floor with dust still
    /// settling into it.
    /// </summary>
    private static bool GravityWellBurst(in VfxCast cast, Vector3 position, float radius, SpellBurstSource source, float charge)
    {
        if (source == SpellBurstSource.Zone)
        {
            return false;
        }

        radius = Mathf.Max(0.5f, radius);
        VfxSchoolColors colors = cast.Colors;
        VfxBudget budget = VfxQuality.Budget;
        float groundY = ArcanaGroundY(cast, position, source);
        var floor = new Vector3(position.X, groundY, position.Z);
        Vector3 skim = floor + (Vector3.Up * 0.12f);
        Vector3 heart = floor + Vector3.Up;

        bool lean = ArcanaLean;
        ArcanaRing(cast, skim, radius, colors, 0.38f, inward: true);
        if (!lean)
        {
            ArcanaRing(cast, skim, radius * 0.62f, colors, 0.6f, inward: true);
        }

        VfxFlareSpec core = VfxFlareSpec.At(heart, Mathf.Clamp(radius * 0.11f, 0.3f, 0.5f), colors);
        core.Life = 0.3f;
        core.Light = budget.MaxLights > 0;
        core.LightRange = Mathf.Max(4f, radius * 2f);
        cast.Fx.Flare(core);

        if (!lean)
        {
            VfxShellSpec orb = VfxShellSpec.Sphere(heart, Mathf.Clamp(radius * 0.18f, 0.45f, 0.8f), colors);
            orb.Fresnel = true;
            orb.Life = 0.55f;
            orb.BurnsAway = true;
            orb.StartScale = 0.35f;
            cast.Fx.Shell(orb);
        }

        var across = new Vector3(radius * 0.9f, 1.2f, radius * 0.9f);
        VfxBurstSpec dust = VfxBurstSpec.At(heart, colors, ArcanaAmount(Mathf.Clamp(0.6f + (radius * 0.25f), 0.6f, 2f)));
        dust.Inward = true;
        dust.Extents = across;
        dust.LifeScale = 0.55f;
        cast.Fx.Burst(VfxParticles.Motes, dust);

        VfxBurstSpec streaks = VfxBurstSpec.At(heart, colors, ArcanaDebris(0.9f));
        streaks.Inward = true;
        streaks.Extents = across;
        cast.Fx.Burst(VfxParticles.Sparks, streaks);

        cast.Fx.Distortion(new VfxDistortionSpec
        {
            Position = heart,
            Radius = radius * 1.1f,
            Life = 0.5f,
            Strength = Mathf.Clamp(0.03f + (radius * 0.004f), 0.03f, 0.05f),
            Inward = true,
        });

        if (ArcanaLavish)
        {
            // The rebound: a thin front kicked back out of the heart, slower than the collapse.
            ArcanaRing(cast, skim, radius * 0.45f, colors, 0.8f);
            ArcanaRing(cast, heart, 0.9f, colors, 0.45f, inward: true, normal: Vector3.Right);
        }

        // What stays.
        VfxDiscSpec rune = VfxDiscSpec.At(floor, radius, colors);
        rune.Life = 2.2f;
        rune.Rune = true;
        rune.Spin = -1.1f;
        rune.Flow = -0.7f;
        rune.Body = 0.22f;
        rune.Rim = 0.8f;
        cast.Fx.Disc(rune);
        VfxBurstSpec settling = VfxBurstSpec.At(heart, colors, lean ? 0f : ArcanaAmount(0.6f));
        settling.Inward = true;
        settling.Extents = new Vector3(radius * 0.8f, 1f, radius * 0.8f);
        settling.LifeScale = 1.5f;
        cast.Fx.Burst(VfxParticles.Motes, settling);
        cast.Fx.Mark(new VfxGroundMarkSpec
        {
            Mark = VfxMark.Rune,
            Position = floor,
            Size = radius * 2f,
            Colors = colors,
            Life = 6f,

            // Against the disc over it: two circles turning opposite ways while both are there.
            Spin = 0.4f,
            Reach = 1f,
        });
        return true;
    }

    /// <summary>The floor under a burst, as <see cref="Burst"/> works it out for its own blocks.</summary>
    private static float ArcanaGroundY(in VfxCast cast, Vector3 position, SpellBurstSource source) => source switch
    {
        SpellBurstSource.Caster => VfxAnchor.BodyOf(cast.Caster)?.GlobalPosition.Y ?? position.Y - 1f,
        SpellBurstSource.Ground => position.Y - 0.3f,
        SpellBurstSource.Zone => position.Y - 0.1f,
        _ => position.Y - 1f,
    };

    // --- nature ----------------------------------------------------------------------------------

    /// <summary>Leaves and motes climb the caster, a bloom of light opens on the ground under them,
    /// and a soft glow rides them for a second.</summary>
    private static bool MendingBloomRelease(in VfxCast cast, Vector3 hand, Vector3 direction, float charge)
    {
        if (VfxAnchor.BodyOf(cast.Caster) is not { } body)
        {
            return false;
        }

        VfxSchoolColors colors = cast.Colors;
        Vector3 feet = body.GlobalPosition;
        Vector3 chest = feet + Vector3.Up;
        bool inside = cast.ByPlayer && ArcanaInside(body);
        float spread = inside ? 2.2f : 0.55f;
        ArcanaSnap(cast, hand, colors);

        VfxBurstSpec climb = VfxBurstSpec.At(feet + (Vector3.Up * 0.3f), colors, ArcanaAmount(inside ? 1.3f : 1f));
        climb.Direction = Vector3.Up;
        climb.Spread = 22f;
        climb.Extents = new Vector3(spread, 0.2f, spread);
        climb.Speed = 3.2f;
        climb.GravityScale = 0.25f;
        cast.Fx.Burst(VfxParticles.Leaves, climb);

        VfxBurstSpec light = VfxBurstSpec.At(chest, colors, ArcanaAmount(inside ? 1.2f : 0.9f));
        light.Extents = new Vector3(spread + 0.1f, 0.9f, spread + 0.1f);
        light.Direction = Vector3.Up;
        light.Spread = 40f;
        light.SpeedScale = 0.9f;
        cast.Fx.Burst(VfxParticles.Motes, light);

        ArcanaRing(
            cast, feet + (Vector3.Up * 0.12f), VfxArcanaRules.FloorReach(inside), colors, ArcanaFloorSeconds(inside, 0.6f));
        VfxDiscSpec bloom = VfxDiscSpec.At(feet, VfxArcanaRules.SelfDisc(inside, 1.4f), colors);
        bloom.Life = 1.2f;
        bloom.Flow = 0.6f;
        bloom.Body = 0.55f;
        bloom.Rim = 0.5f;
        cast.Fx.Disc(bloom).Get?.Follow(VfxAnchor.To(body));

        // Petals let go outward, where the tier has a second layer.
        VfxBurstSpec petals = VfxBurstSpec.At(chest, colors, ArcanaDebris(0.5f));
        petals.SpeedScale = 0.7f;
        petals.GravityScale = 0.5f;
        cast.Fx.Burst(VfxParticles.Leaves, petals);
        if (ArcanaLavish)
        {
            ArcanaRing(cast, feet + (Vector3.Up * 0.12f), inside ? VfxArcanaRules.FloorReachInside * 0.6f : 1.1f, colors, 0.95f);
        }

        ArcanaAfterglow(cast, body, colors, 1f, inside);
        return true;
    }

    /// <summary>The cast of a totem is only its snap: what the spell looks like is the totem.</summary>
    private static bool LifebloomRelease(in VfxCast cast, Vector3 hand, Vector3 direction, float charge)
    {
        ArcanaSnap(cast, hand, cast.Colors, 0.28f);
        VfxBurstSpec spit = VfxBurstSpec.At(hand, cast.Colors, ArcanaAmount(0.5f));
        spit.Direction = Vector3.Down;
        spit.Spread = 35f;
        spit.SpeedScale = 0.8f;
        cast.Fx.Burst(VfxParticles.Motes, spit);
        return true;
    }

    /// <summary>The totem sprouts: leaves thrown up from its foot, a front along the ground, and
    /// roots spreading under it for as long as it stands. Added to the generic post and crown.</summary>
    private static bool LifebloomTotem(in VfxCast cast, VfxRig rig, Node3D totem, float size, float seconds)
    {
        VfxSchoolColors colors = cast.Colors;
        VfxBurstSpec sprout = VfxBurstSpec.At(Vector3.Zero, colors, ArcanaAmount(1.1f));
        sprout.Direction = Vector3.Up;
        sprout.Spread = 32f;
        sprout.Extents = new Vector3(0.3f, 0.1f, 0.3f);
        sprout.Speed = 4.2f;
        sprout.GravityScale = 0.6f;
        cast.Fx.Burst(VfxParticles.Leaves, sprout).Get?.Follow(VfxAnchor.To(totem, Vector3.Up * 0.2f));

        VfxBurstSpec soil = VfxBurstSpec.At(Vector3.Zero, colors, ArcanaDebris(0.4f));
        soil.Tint = ArcanaSoil;
        soil.Extents = new Vector3(0.4f, 0.1f, 0.4f);
        soil.SpeedScale = 0.8f;
        cast.Fx.Burst(VfxParticles.Motes, soil).Get?.Follow(VfxAnchor.To(totem, Vector3.Up * 0.2f));

        ArcanaRing(cast, Vector3.Zero, 1.7f, colors, 0.6f).Get?.Follow(VfxAnchor.To(totem, Vector3.Up * 0.12f));

        // For as long as it stands: leaves lifting off the ground a couple of metres about it. From
        // first person, with the totem at the caster's feet and under the frame, these are what
        // says it is there. Not on the leanest tier.
        if (VfxQuality.Tier != VfxTier.Performance)
        {
            VfxBurstSpec garden = VfxBurstSpec.At(Vector3.Zero, colors, ArcanaAmount(0.3f));
            garden.Continuous = true;
            garden.Direction = Vector3.Up;
            garden.Spread = 14f;
            garden.Extents = new Vector3(2.2f, 0.25f, 2.2f);
            garden.Speed = 1.1f;
            garden.GravityScale = 0.05f;
            garden.LifeScale = 1.3f;
            rig.Add(cast.Fx.Burst(VfxParticles.Leaves, garden)).Get?.Follow(VfxAnchor.To(totem, Vector3.Up * 0.3f));
        }

        rig.Add(cast.Fx.Mark(new VfxGroundMarkSpec
        {
            Mark = VfxMark.Roots,
            Size = 2.6f,
            Colors = colors,
            Sustain = true,
            Reach = 1.2f,
        })).Get?.Follow(VfxAnchor.To(totem));
        return false;
    }

    /// <summary>
    /// One heal from the totem, replacing the generic pulse. The totem mends its owner wherever
    /// they stand (there is no radius in the rule, so no radius is drawn): the beat is a ring out of
    /// its foot, its crown flaring, a vine of light to the healed with leaves let go along it and
    /// green drawn in where it lands. When the healed is the player in first person, where the
    /// totem at their feet is under the frame, the beat is a breath of green at the edges of the
    /// view instead of a line into the lens.
    /// </summary>
    private static bool LifebloomPulse(in VfxCast cast, Node3D totem, Node3D? target)
    {
        VfxSchoolColors colors = cast.Colors;
        Vector3 foot = totem.GlobalPosition;
        Vector3 crown = foot + Vector3.Up;
        bool lens = target != null && ArcanaInside(target);

        ArcanaRing(cast, foot + (Vector3.Up * 0.12f), 1.8f, colors, 0.6f);
        if (!lens)
        {
            VfxFlareSpec beat = VfxFlareSpec.At(crown, 0.34f, colors.Scaled(0.8f, 0.7f));
            beat.Life = 0.3f;
            cast.Fx.Flare(beat);
        }

        if (target == null)
        {
            return true;
        }

        if (lens)
        {
            cast.Fx.ScreenEdge(SpellSchools.Color(cast.School), 0.3f, 0.6f);
            return true;
        }

        Vector3 chest = target.GlobalPosition + (Vector3.Up * 1.1f);
        float length = crown.DistanceTo(chest);
        if (length > 0.6f)
        {
            VfxHandle<VfxBolt> vine = cast.Fx.Bolt(new VfxBoltSpec
            {
                Mode = VfxBoltMode.Tether,
                From = crown,
                To = chest,
                Colors = colors,
                Width = 0.055f,
                Segments = VfxRecipeRules.BoltSegments(VfxQuality.Budget, length),
                Seed = _director!.NextSeed(),
            });
            vine.Get?.FollowTo(VfxAnchor.To(target, Vector3.Up * 1.1f));
        }

        // Leaves climbing the healed, on every tier: the heal is seen on who it lands on.
        VfxBurstSpec mend = VfxBurstSpec.At(target.GlobalPosition + (Vector3.Up * 0.4f), colors, ArcanaAmount(0.4f));
        mend.Direction = Vector3.Up;
        mend.Spread = 24f;
        mend.Extents = new Vector3(0.35f, 0.2f, 0.35f);
        mend.Speed = 2.6f;
        mend.GravityScale = 0.2f;
        cast.Fx.Burst(VfxParticles.Leaves, mend);
        if (ArcanaRich && length > 0.6f)
        {
            TetherScatter(cast, crown, chest, 0.7f);
        }

        return true;
    }

    /// <summary>Roots crack the circle while the snare waits. Added to the generic telegraph disc.</summary>
    private static bool ThornsnareTelegraph(in VfxCast cast, VfxRig rig, Node3D ground, float radius, float delay)
    {
        rig.Add(cast.Fx.Mark(new VfxGroundMarkSpec
        {
            Mark = VfxMark.Roots,
            Size = Mathf.Max(1f, radius * 2f),
            Colors = cast.Colors,
            Sustain = true,
            Reach = 1.2f,
        })).Get?.Follow(VfxAnchor.To(ground));
        return false;
    }

    /// <summary>
    /// The snare springs: thorns driven straight up out of the whole circle, leaves and soil thrown
    /// after them, a front along the ground and roots left across it. One small core, not a dome.
    /// </summary>
    private static bool ThornsnareBurst(in VfxCast cast, Vector3 position, float radius, SpellBurstSource source, float charge)
    {
        if (source == SpellBurstSource.Zone)
        {
            return false;
        }

        radius = Mathf.Max(0.5f, radius);
        VfxSchoolColors colors = cast.Colors;
        VfxBudget budget = VfxQuality.Budget;
        var floor = new Vector3(position.X, ArcanaGroundY(cast, position, source), position.Z);
        Vector3 skim = floor + (Vector3.Up * 0.12f);
        var bed = new Vector3(radius * 0.75f, 0.05f, radius * 0.75f);

        VfxBurstSpec thorns = VfxBurstSpec.At(skim, colors, ArcanaAmount(Mathf.Clamp(0.8f + (radius * 0.3f), 0.8f, 2f)));
        thorns.Tint = ArcanaThorn;
        thorns.Direction = Vector3.Up;
        thorns.Spread = 16f;
        thorns.Extents = bed;
        thorns.Speed = 8.5f;
        thorns.SizeScale = 1.5f;
        cast.Fx.Burst(VfxParticles.Shards, thorns);

        VfxBurstSpec leaves = VfxBurstSpec.At(floor + (Vector3.Up * 0.3f), colors, ArcanaAmount(Mathf.Clamp(0.6f + (radius * 0.2f), 0.6f, 1.6f)));
        leaves.Direction = Vector3.Up;
        leaves.Spread = 65f;
        leaves.Extents = new Vector3(radius * 0.6f, 0.1f, radius * 0.6f);
        leaves.SpeedScale = 1.2f;
        cast.Fx.Burst(VfxParticles.Leaves, leaves);

        VfxBurstSpec soil = VfxBurstSpec.At(floor + (Vector3.Up * 0.3f), colors, ArcanaDebris(0.6f));
        soil.Tint = ArcanaSoil;
        soil.Direction = Vector3.Up;
        soil.Spread = 70f;
        soil.Extents = new Vector3(radius * 0.7f, 0.2f, radius * 0.7f);
        soil.SpeedScale = 1.6f;
        cast.Fx.Burst(VfxParticles.Motes, soil);

        ArcanaRing(cast, skim, radius, colors, 0.45f);
        VfxFlareSpec core = VfxFlareSpec.At(floor + (Vector3.Up * 0.5f), Mathf.Clamp(radius * 0.12f, 0.3f, 0.5f), colors);
        core.Life = 0.28f;
        core.Light = budget.MaxLights > 0;
        core.LightRange = Mathf.Max(4f, radius * 2f);
        cast.Fx.Flare(core);

        // The circle itself lights for a beat, so the reach reads on tiers with no floor marks.
        VfxDiscSpec pulse = VfxDiscSpec.At(floor, radius, colors);
        pulse.Life = 0.9f;
        pulse.Flow = 0.8f;
        pulse.Body = 0.35f;
        pulse.Rim = 0.4f;
        cast.Fx.Disc(pulse);

        if (ArcanaLavish)
        {
            ArcanaRing(cast, skim, radius * 0.55f, colors, 0.62f);
        }

        if (ArcanaWild)
        {
            // A second, slower row of thorns leaning outward, and splinters off the first.
            VfxBurstSpec second = thorns;
            second.Density = ArcanaAmount(0.9f);
            second.Spread = 38f;
            second.Speed = 5.5f;
            second.SizeScale = 1.1f;
            cast.Fx.Burst(VfxParticles.Shards, second);
            VfxBurstSpec splinters = VfxBurstSpec.At(floor + (Vector3.Up * 0.4f), colors, ArcanaDebris(0.4f));
            splinters.Direction = Vector3.Up;
            splinters.Spread = 50f;
            splinters.Extents = bed;
            cast.Fx.Burst(VfxParticles.Sparks, splinters);
        }

        cast.Fx.Mark(new VfxGroundMarkSpec
        {
            Mark = VfxMark.Roots,
            Position = floor,
            Size = radius * 2.1f,
            Colors = colors,
            Life = 8f,
            Reach = 1f,
        });

        // Leaves still coming down after it.
        VfxBurstSpec falling = VfxBurstSpec.At(floor + (Vector3.Up * 1.7f), colors, ArcanaLean ? 0f : ArcanaAmount(0.5f));
        falling.Extents = new Vector3(radius * 0.6f, 0.4f, radius * 0.6f);
        falling.SpeedScale = 0.3f;
        falling.LifeScale = 1.6f;
        cast.Fx.Burst(VfxParticles.Leaves, falling);
        return true;
    }

    /// <summary>
    /// The swarm in flight. It replaces the bolt outright: no bright ball and no ribbon, but a tight,
    /// short-lived cloud of stinging specks that the bolt drags along, with a faint heart so it is
    /// still a readable thing with glow off, and stragglers left behind on the richer tiers.
    /// </summary>
    private static bool StingingSwarmProjectile(
        in VfxCast cast, VfxRig rig, Node3D projectile, Vector3 handOffset, Vector3 direction, float charge)
    {
        VfxAnchor anchor = VfxAnchor.To(projectile);
        Vector3 start = projectile.GlobalPosition + handOffset;
        Vector3 velocity = direction.LengthSquared() > 0.0001f && cast.Spell is { } spell
            ? direction.Normalized() * Mathf.Max(0f, spell.ProjectileSpeed)
            : Vector3.Zero;

        VfxFlareSpec heart = VfxFlareSpec.At(start, 0.13f, cast.Colors.Scaled(0.6f, 0.7f));
        heart.Sustain = true;
        heart.Level = 0.7f;
        heart.Light = VfxQuality.Budget.MaxLights > 0;
        heart.LightRange = 3f;
        rig.Flare = rig.Add(cast.Fx.Flare(heart));
        if (rig.Flare.Get is { } flare)
        {
            flare.Follow(anchor);
            flare.SettleFrom(handOffset);
            flare.Glide(velocity);
        }

        VfxBurstSpec swarm = VfxBurstSpec.At(start, cast.Colors, ArcanaAmount(2.2f));
        swarm.Tint = ArcanaSting;
        swarm.Continuous = true;
        swarm.Extents = Vector3.One * (0.34f * ProjectileViewScale(cast));
        swarm.LifeScale = 0.25f;
        swarm.SizeScale = 1.2f;
        swarm.GravityScale = 0f;
        if (velocity != Vector3.Zero)
        {
            // Particles live in the world, not on the emitter: left to themselves they would be a
            // dotted line metres long behind a bolt this fast. Thrown forward at up to the bolt's own
            // speed they keep pace with it, the quickest at its head and the rest strung out a
            // couple of metres behind, which is a swarm.
            swarm.Direction = velocity;
            swarm.Spread = 4f;
            swarm.Speed = velocity.Length();
            swarm.Damping = 0f;
        }
        else
        {
            swarm.SpeedScale = 1.6f;
        }

        Shed(rig, cast, VfxParticles.Motes, swarm, anchor, handOffset, velocity);

        if (ArcanaRich)
        {
            VfxBurstSpec stragglers = VfxBurstSpec.At(start, cast.Colors, ArcanaDebris(0.8f));
            stragglers.Continuous = true;
            stragglers.Extents = Vector3.One * 0.45f;
            stragglers.SpeedScale = 0.8f;
            stragglers.LifeScale = 0.5f;
            stragglers.GravityScale = 0f;
            Shed(rig, cast, VfxParticles.Motes, stragglers, anchor, handOffset, velocity);
        }

        return true;
    }

    /// <summary>The swarm breaks over what it hit: a cloud of the same stinging specks, spreading
    /// fast. Added to the small generic hit.</summary>
    private static bool StingingSwarmImpact(in VfxCast cast, in SpellImpactInfo hit)
    {
        if (hit.Kind is SpellImpactKind.Blocked or SpellImpactKind.Expired)
        {
            return false;
        }

        VfxBurstSpec cloud = VfxBurstSpec.At(hit.Position, cast.Colors, ArcanaAmount(1.3f));
        cloud.Tint = ArcanaSting;
        cloud.Extents = Vector3.One * 0.6f;
        cloud.SpeedScale = 1.8f;
        cloud.LifeScale = 0.9f;
        cloud.GravityScale = 0f;
        cast.Fx.Burst(VfxParticles.Motes, cloud);
        return false;
    }

    /// <summary>Bark closes up the body: three hoops tightening at the shins, the waist and the
    /// chest, each a beat after the last, in a shower of chips, with roots left under the feet.</summary>
    private static bool BarkskinRelease(in VfxCast cast, Vector3 hand, Vector3 direction, float charge)
    {
        if (VfxAnchor.BodyOf(cast.Caster) is not { } body)
        {
            return false;
        }

        VfxSchoolColors colors = cast.Colors;
        Vector3 feet = body.GlobalPosition;
        Vector3 chest = feet + Vector3.Up;
        bool inside = cast.ByPlayer && ArcanaInside(body);
        float spread = inside ? 2.2f : 0.5f;
        ArcanaSnap(cast, hand, colors);

        // The top hoop is at the eye from inside, so first person keeps the two below it; so do the
        // lean tiers.
        int hoops = inside || ArcanaLean ? 2 : 3;
        for (int i = 0; i < hoops; i++)
        {
            VfxHandle<VfxFlare> hoop = ArcanaRing(
                cast, feet + (Vector3.Up * (0.25f + (0.6f * i))), 0.85f - (0.1f * i), colors, 0.4f + (0.14f * i), inward: true);
            hoop.Get?.Follow(VfxAnchor.To(body, Vector3.Up * (0.25f + (0.6f * i))));
        }

        VfxBurstSpec chips = VfxBurstSpec.At(feet + (Vector3.Up * 0.2f), colors, ArcanaAmount(inside ? 1.2f : 0.9f));
        chips.Tint = ArcanaBark;
        chips.Direction = Vector3.Up;
        chips.Spread = 28f;
        chips.Extents = new Vector3(spread, 0.15f, spread);
        chips.Speed = 3.6f;
        chips.GravityScale = 0.5f;
        cast.Fx.Burst(VfxParticles.Leaves, chips);

        VfxBurstSpec green = VfxBurstSpec.At(chest, colors, ArcanaLean && !inside ? 0f : ArcanaAmount(inside ? 1f : 0.7f));
        green.Extents = new Vector3(spread + 0.1f, 0.8f, spread + 0.1f);
        green.SpeedScale = 0.7f;
        cast.Fx.Burst(VfxParticles.Motes, green);

        VfxBurstSpec splinters = VfxBurstSpec.At(feet + (Vector3.Up * 0.3f), colors, ArcanaDebris(0.4f));
        splinters.Tint = ArcanaBark;
        splinters.Direction = Vector3.Up;
        splinters.Spread = 40f;
        splinters.Extents = new Vector3(spread, 0.15f, spread);
        splinters.SpeedScale = 0.6f;
        splinters.SizeScale = 0.6f;
        cast.Fx.Burst(VfxParticles.Shards, splinters);

        ArcanaRing(
            cast, feet + (Vector3.Up * 0.12f), VfxArcanaRules.FloorReach(inside), colors, ArcanaFloorSeconds(inside, 0.55f));
        if (inside)
        {
            // No hoops to see from in here: the ground itself greens for a beat instead.
            VfxDiscSpec ground = VfxDiscSpec.At(feet, VfxArcanaRules.SelfDiscInside, colors);
            ground.Life = 1.1f;
            ground.Flow = -0.5f;
            ground.Body = 0.4f;
            ground.Rim = 0.5f;
            cast.Fx.Disc(ground).Get?.Follow(VfxAnchor.To(body));
        }

        cast.Fx.Mark(new VfxGroundMarkSpec
        {
            Mark = VfxMark.Roots,
            Position = feet,
            Size = 2.4f,
            Colors = colors,
            Life = 5f,
            Reach = 1f,
        });
        ArcanaAfterglow(cast, body, colors, 0.8f, inside);
        return true;
    }

    // --- necrotic --------------------------------------------------------------------------------

    /// <summary>
    /// Life drawn from a struck body back to the caster (the lifesteal's own arc), as the siphon
    /// draws it. Replaces the generic tether: a line, embers running home along it, and where they
    /// arrive a small glow with motes drawn into the caster. Medium and up add a second, crackling
    /// strand and light climbing the caster; a first-person caster, whose own body is under the
    /// frame, gets a breath of the school's colour at the edges of the view.
    /// </summary>
    private static bool NecroticDrain(in VfxCast cast, Vector3 from, Vector3 to, SpellArcKind kind)
    {
        if (kind != SpellArcKind.Tether)
        {
            return false;
        }

        VfxSchoolColors colors = cast.Colors;
        Vector3 line = to - from;
        float length = line.Length();
        if (length < 0.2f)
        {
            return true;
        }

        cast.Fx.Bolt(new VfxBoltSpec
        {
            Mode = VfxBoltMode.Tether,
            From = from,
            To = to,
            Colors = colors,
            Width = 0.06f,
            Segments = VfxRecipeRules.BoltSegments(VfxQuality.Budget, length),
            Life = 0.6f,
            Seed = _director!.NextSeed(),
        });

        // Embers that cover the line in their short life: thrown hard, never slowed, never falling.
        VfxBurstPreset embers = VfxBurstPresets.For(VfxParticles.Embers);
        VfxBurstSpec home = VfxBurstSpec.At(from, colors, ArcanaAmount(0.8f));
        home.Direction = line / length;
        home.Spread = 6f;
        home.LifeScale = 0.45f;
        home.Speed = length / Mathf.Max(0.1f, embers.Life * home.LifeScale);
        home.Damping = 0f;
        home.GravityScale = 0f;
        home.SizeScale = 0.8f;
        cast.Fx.Burst(VfxParticles.Embers, home);

        if (!ArcanaLean)
        {
            VfxFlareSpec taken = VfxFlareSpec.At(to, 0.18f, colors.Scaled(0.7f, 0.8f));
            taken.Life = 0.4f;
            cast.Fx.Flare(taken);
        }

        VfxBurstSpec mend = VfxBurstSpec.At(to, colors, ArcanaAmount(0.6f));
        mend.Inward = true;
        mend.Extents = Vector3.One * 0.65f;
        mend.LifeScale = 0.5f;
        cast.Fx.Burst(VfxParticles.Motes, mend);

        bool lens = cast.ByPlayer && ViewIsFirstPerson(cast.Caster);
        if (lens)
        {
            cast.Fx.ScreenEdge(SpellSchools.Color(cast.School), 0.25f, 0.5f);
        }

        if (ArcanaRich)
        {
            cast.Fx.Bolt(new VfxBoltSpec
            {
                Mode = VfxBoltMode.Lightning,
                From = from,
                To = to,
                Colors = colors.Scaled(0.8f, 0.6f),
                Width = 0.024f,
                Jitter = 0.07f,
                Segments = VfxRecipeRules.BoltSegments(VfxQuality.Budget, length),
                Life = 0.3f,
                Seed = _director!.NextSeed(),
            });
            if (!lens)
            {
                // What was taken, climbing the one who took it.
                VfxBurstSpec climbing = VfxBurstSpec.At(to + (Vector3.Down * 0.5f), colors, ArcanaDebris(0.5f));
                climbing.Direction = Vector3.Up;
                climbing.Spread = 18f;
                climbing.Extents = new Vector3(0.3f, 0.4f, 0.3f);
                climbing.Speed = 1.8f;
                climbing.GravityScale = 0f;
                cast.Fx.Burst(VfxParticles.Motes, climbing);
            }
        }

        return true;
    }

    /// <summary>
    /// The same heal on a necrotic spell that is not about draining (a mark, a tithe, a rot, a
    /// breath of ash): one thin thread from the struck to the caster that is gone in a third of a
    /// second, and above the lean tiers a few motes drawn in where it lands. Drawn in full it made
    /// every necrotic bolt the same spell; kept this small, each keeps its own picture.
    /// </summary>
    private static bool NecroticThread(in VfxCast cast, Vector3 from, Vector3 to, SpellArcKind kind)
    {
        if (kind != SpellArcKind.Tether)
        {
            return false;
        }

        float length = from.DistanceTo(to);
        if (length < 0.2f)
        {
            return true;
        }

        cast.Fx.Bolt(new VfxBoltSpec
        {
            Mode = VfxBoltMode.Tether,
            From = from,
            To = to,
            Colors = cast.Colors.Scaled(0.6f, 0.5f),
            Width = 0.022f,
            Segments = VfxRecipeRules.BoltSegments(VfxQuality.Budget, length),
            Life = 0.3f,
            Seed = _director!.NextSeed(),
        });
        if (!ArcanaLean)
        {
            VfxBurstSpec mend = VfxBurstSpec.At(to, cast.Colors, ArcanaAmount(0.3f));
            mend.Inward = true;
            mend.Extents = Vector3.One * 0.5f;
            mend.LifeScale = 0.45f;
            cast.Fx.Burst(VfxParticles.Motes, mend);
        }

        return true;
    }

    /// <summary>
    /// The tithe in flight, added to the generic bolt: a broad blood-dark wake behind it and, where
    /// the tier has a second layer, drops of the price still falling off it. It is the heavy one of
    /// the necrotic bolts. Not on the leanest tier, where the bolt keeps its head and its glow.
    /// </summary>
    private static bool SoulTitheProjectile(
        in VfxCast cast, VfxRig rig, Node3D projectile, Vector3 handOffset, Vector3 direction, float charge)
    {
        if (VfxQuality.Tier == VfxTier.Performance)
        {
            return false;
        }

        VfxSchoolColors blood = ArcanaBlood(cast.Colors);
        VfxAnchor anchor = VfxAnchor.To(projectile);
        Vector3 start = projectile.GlobalPosition + handOffset;
        float view = ProjectileViewScale(cast);
        VfxHandle<VfxBolt> wake = rig.Add(cast.Fx.Bolt(new VfxBoltSpec
        {
            Mode = VfxBoltMode.Trail,
            From = start,
            To = start,
            Colors = blood.Scaled(0.7f, 0.5f),
            Width = 0.12f * view,
            TrailSeconds = ArcanaRich ? 0.42f : 0.24f,
        }));
        if (wake.Get is { } ribbon)
        {
            ribbon.FollowTo(anchor);
            ribbon.SettleTrailFrom(handOffset);
        }

        if (ArcanaRich)
        {
            Vector3 velocity = direction.LengthSquared() > 0.0001f && cast.Spell is { } spell
                ? direction.Normalized() * Mathf.Max(0f, spell.ProjectileSpeed)
                : Vector3.Zero;
            VfxBurstSpec drops = VfxBurstSpec.At(start, blood, ArcanaDebris(0.5f));
            drops.Continuous = true;
            drops.Extents = Vector3.One * (0.12f * view);
            drops.SpeedScale = 0.3f;
            drops.LifeScale = 0.6f;
            drops.SizeScale = 0.8f;
            drops.GravityScale = -2.5f;
            Shed(rig, cast, VfxParticles.Embers, drops, anchor, handOffset, velocity);
        }

        return false;
    }

    /// <summary>The price: blood-red embers and a small hoop leave the caster's chest along the aim
    /// as the bolt goes. Added to the generic cast.</summary>
    private static bool SoulTitheRelease(in VfxCast cast, Vector3 hand, Vector3 direction, float charge)
    {
        if (VfxAnchor.BodyOf(cast.Caster) is not { } body)
        {
            return false;
        }

        // From inside, the chest is under the eye: the price is paid from the hand instead.
        bool inside = cast.ByPlayer && ArcanaInside(body);
        Vector3 from = inside ? hand : body.GlobalPosition + (Vector3.Up * 1.25f);
        VfxSchoolColors blood = ArcanaBlood(cast.Colors);
        Vector3 aim = direction.LengthSquared() > 0.0001f ? direction.Normalized() : Vector3.Zero;

        VfxBurstSpec paid = VfxBurstSpec.At(from, blood, ArcanaAmount(0.8f));
        paid.Direction = aim == Vector3.Zero ? Vector3.Up : aim;
        paid.Spread = 32f;
        paid.Extents = new Vector3(0.18f, 0.28f, 0.18f);
        paid.SpeedScale = 1.2f;
        paid.GravityScale = -1.5f;
        cast.Fx.Burst(VfxParticles.Embers, paid);
        if (aim != Vector3.Zero && !inside)
        {
            ArcanaRing(cast, from + (aim * 0.3f), 0.6f, blood, 0.32f, normal: aim);
        }

        return false;
    }

    /// <summary>A kill pays back: a wisp of mana runs from the fallen to the caster's hand. Added to
    /// the generic hit.</summary>
    private static bool SoulTitheImpact(in VfxCast cast, in SpellImpactInfo hit)
    {
        // It lands hard: a blood-dark front thrown back off the struck and, where the tier has a
        // second layer, what it knocked out of them falling. Not on the leanest tier.
        if (hit.Kind == SpellImpactKind.Target && VfxQuality.Tier != VfxTier.Performance &&
            !(VfxAnchor.BodyOf(hit.Target) is { } struck && ArcanaInside(struck)))
        {
            VfxSchoolColors blood = ArcanaBlood(cast.Colors);
            ArcanaRing(cast, hit.Position, 1.1f, blood, 0.34f, normal: hit.Normal);
            VfxBurstSpec spilt = VfxBurstSpec.At(hit.Position, blood, ArcanaDebris(0.6f));
            spilt.Direction = hit.Normal.LengthSquared() > 0.0001f ? hit.Normal : Vector3.Up;
            spilt.Spread = 50f;
            spilt.SpeedScale = 1.3f;
            spilt.GravityScale = -3f;
            cast.Fx.Burst(VfxParticles.Embers, spilt);
        }

        if (!hit.Killed || hit.Kind != SpellImpactKind.Target || cast.Caster is not { } caster ||
            VfxAnchor.BodyOf(caster) == null)
        {
            return false;
        }

        VfxSchoolColors mana = VfxPalette.For(DamageType.Arcane, cast.ByPlayer);
        Vector3 home = CastOrigin(caster, hit.Position);
        VfxHandle<VfxBolt> wisp = cast.Fx.Bolt(new VfxBoltSpec
        {
            Mode = VfxBoltMode.Tether,
            From = hit.Position,
            To = home,
            Colors = mana,
            Width = 0.06f,
            Segments = VfxRecipeRules.BoltSegments(VfxQuality.Budget, hit.Position.DistanceTo(home)),
            Life = 0.65f,
            Seed = _director!.NextSeed(),
        });
        wisp.Get?.FollowTo(VfxAnchor.ToHand(caster, ChestOffset));

        VfxFlareSpec freed = VfxFlareSpec.At(hit.Position + (Vector3.Up * 0.3f), 0.26f, mana);
        freed.Life = 0.35f;
        cast.Fx.Flare(freed);
        VfxBurstSpec soul = VfxBurstSpec.At(hit.Position, mana, ArcanaAmount(0.7f));
        soul.Direction = Vector3.Up;
        soul.Spread = 30f;
        soul.Extents = new Vector3(0.3f, 0.5f, 0.3f);
        soul.SpeedScale = 1.4f;
        cast.Fx.Burst(VfxParticles.Motes, soul);
        if (ArcanaRich)
        {
            ArcanaRing(cast, hit.Position, 1f, mana, 0.5f, normal: hit.Normal);
        }

        return false;
    }

    /// <summary>Bone-white light drawn in and stitched shut: fronts closing along the floor and about
    /// the chest, pale motes and thin streaks converging, and the rot falling off in dark wisps.</summary>
    private static bool KnitBoneRelease(in VfxCast cast, Vector3 hand, Vector3 direction, float charge)
    {
        if (VfxAnchor.BodyOf(cast.Caster) is not { } body)
        {
            return false;
        }

        VfxSchoolColors bone = ArcanaBone(cast.Colors);
        Vector3 feet = body.GlobalPosition;
        Vector3 chest = feet + Vector3.Up;
        bool inside = cast.ByPlayer && ArcanaInside(body);
        float spread = inside ? 2.4f : 1.3f;
        ArcanaSnap(cast, hand, bone);

        // The fronts are bone at half its light, tinged with the school's violet: at full they were
        // two thick white bands, and the spell read as a generic burst.
        VfxSchoolColors front = bone.Scaled(0.5f, 0.5f) with { Edge = cast.Colors.Mid };
        ArcanaRing(
            cast, feet + (Vector3.Up * 0.12f), inside ? VfxArcanaRules.FloorReachInside : 1.6f, front,
            ArcanaFloorSeconds(inside, 0.5f), inward: true);
        if (inside)
        {
            // A pale floor drawing in under the caster, in place of the hoop about a chest they cannot see.
            VfxDiscSpec ground = VfxDiscSpec.At(feet, VfxArcanaRules.SelfDiscInside, front);
            ground.Life = 1.1f;
            ground.Flow = -0.8f;
            ground.Body = 0.35f;
            ground.Rim = 0.5f;
            cast.Fx.Disc(ground).Get?.Follow(VfxAnchor.To(body));
        }

        if (!inside)
        {
            ArcanaRing(cast, chest, 0.9f, front, 0.65f, inward: true).Get?.Follow(VfxAnchor.To(body, Vector3.Up));

            // Splinters of bone drawn in to the body and knitting there: the picture of the spell,
            // on every tier, in one draw.
            VfxMotifSpec splinters = VfxMotifSpec.At(
                chest, bone, VfxSprite.Shard, VfxMotion.Inward, VfxArcanaRules.ImplosionStreaks(VfxQuality.Tier), 1.25f, 0.3f);
            splinters.Aspect = 0.3f;
            splinters.Speed = 1.7f;
            splinters.Life = 0.85f;
            splinters.Pale = true;
            splinters.TrueSize = true;
            cast.Fx.Motif(splinters).Get?.Follow(VfxAnchor.To(body, Vector3.Up));
        }

        // (From outside, the leanest tier has the splinters in place of these motes: one draw for one.)
        VfxBurstSpec drawn = VfxBurstSpec.At(
            chest, bone, !inside && VfxQuality.Tier == VfxTier.Performance ? 0f : ArcanaAmount(inside ? 1.2f : 1f));
        drawn.Inward = true;
        drawn.Extents = new Vector3(spread, 1f, spread);
        drawn.LifeScale = 0.6f;
        cast.Fx.Burst(VfxParticles.Motes, drawn);

        // The stitches are the school's own violet: bone-white alone read as a generic burst.
        VfxBurstSpec stitches = VfxBurstSpec.At(chest, cast.Colors, ArcanaDebris(0.5f));
        stitches.Inward = true;
        stitches.Extents = new Vector3(spread * 0.8f, 0.9f, spread * 0.8f);
        cast.Fx.Burst(VfxParticles.Sparks, stitches);

        // The rot leaving, in the school's own colour: small, slow, and falling.
        VfxBurstSpec rot = VfxBurstSpec.At(feet + (Vector3.Up * 0.8f), cast.Colors, ArcanaLean ? 0f : ArcanaAmount(0.35f));
        rot.Extents = new Vector3(inside ? 1.8f : 0.4f, 0.5f, inside ? 1.8f : 0.4f);
        rot.SpeedScale = 0.4f;
        rot.SizeScale = 0.5f;
        rot.GravityScale = -3f;
        cast.Fx.Burst(VfxParticles.Wisps, rot);

        // One soft pulse, violet at its edge, as it closes.
        ArcanaAfterglow(cast, body, bone with { Mid = cast.Colors.Mid.Lerp(bone.Mid, 0.5f) }, 0.9f, inside);
        return true;
    }

    /// <summary>Knit Bone cast on an ally: one pale flash up the body for each stack of Decay it
    /// ate (three at most). Added to the generic hit.</summary>
    private static bool KnitBoneImpact(in VfxCast cast, in SpellImpactInfo hit)
    {
        if (hit.Consumed <= 0 || VfxAnchor.BodyOf(hit.Target) is not { } body)
        {
            return false;
        }

        VfxSchoolColors bone = ArcanaBone(cast.Colors);
        int flashes = Mathf.Min(hit.Consumed, 3);
        for (int i = 0; i < flashes; i++)
        {
            Vector3 offset = Vector3.Up * (0.5f + (0.45f * i));
            VfxFlareSpec knit = VfxFlareSpec.At(body.GlobalPosition + offset, 0.2f, bone);
            knit.Life = 0.25f + (0.1f * i);
            cast.Fx.Flare(knit).Get?.Follow(VfxAnchor.To(body, offset));
        }

        return false;
    }

    /// <summary>
    /// Wither landing on the first-person player. The generic hit is a flare at the struck body's
    /// middle, which for this body is half a metre under the eye: a flash across the whole view.
    /// From inside, the rot is drawn where it can be looked at instead: a front closing along the
    /// floor and dark wisps falling about the feet. Anyone else struck gets the generic hit.
    /// </summary>
    private static bool WitherImpact(in VfxCast cast, in SpellImpactInfo hit)
    {
        if (hit.Kind != SpellImpactKind.Target || VfxAnchor.BodyOf(hit.Target) is not { } body || !ArcanaInside(body))
        {
            return false;
        }

        Vector3 feet = body.GlobalPosition;
        ArcanaRing(cast, feet + (Vector3.Up * 0.12f), VfxArcanaRules.SelfDiscInside, cast.Colors.Scaled(0.6f, 0.5f), 0.7f, inward: true);
        cast.Fx.ScreenEdge(SpellSchools.Color(cast.School), 0.5f);
        VfxBurstSpec rot = VfxBurstSpec.At(feet + (Vector3.Up * 0.5f), cast.Colors, ArcanaAmount(0.7f));
        rot.Extents = new Vector3(2f, 0.4f, 2f);
        rot.SpeedScale = 0.4f;
        rot.SizeScale = 0.5f;
        rot.GravityScale = -3f;
        cast.Fx.Burst(VfxParticles.Wisps, rot);
        return true;
    }

    /// <summary>
    /// Wither gathering in an enemy's hand, replacing the generic aura: the warning the player
    /// dodges on, and at a caster's usual range the generic one was a glint. The glow is twice the
    /// size, the school's wisps are drawn in around it, and above the leanest tier a ring a metre
    /// across stands about the hand, facing whoever is looking.
    /// </summary>
    private static bool WitherWindup(
        in VfxCast cast, VfxRig rig, VfxAnchor hand, SpellWindupKind kind, float seconds, float charge)
    {
        if (cast.ByPlayer || !hand.TryResolve(out Vector3 at))
        {
            return false;
        }

        const float Size = 0.5f;
        VfxFlareSpec glow = VfxFlareSpec.At(at, Size, cast.Colors);
        glow.Sustain = true;
        glow.Level = 0.35f;
        glow.RampSeconds = Mathf.Max(0.05f, seconds);
        glow.Light = VfxQuality.Budget.MaxLights > 0;
        glow.LightRange = 5f;
        rig.Flare = rig.Add(cast.Fx.Flare(glow));
        rig.Flare.Get?.Follow(hand);
        WindupMotif(cast, rig, hand, at, Size);
        if (VfxQuality.Tier == VfxTier.Performance)
        {
            return true;
        }

        VfxDiscSpec tell = VfxDiscSpec.At(at, Size * 1.1f, cast.Colors);
        tell.Sustain = true;
        tell.FaceCamera = true;
        tell.Body = 0.04f;
        tell.Rim = 1f;
        tell.Flow = -0.6f;
        rig.Add(cast.Fx.Disc(tell)).Get?.Follow(hand);

        if (ArcanaRich)
        {
            rig.Density = ArcanaAmount(0.5f);
            VfxBurstSpec drawn = VfxBurstSpec.At(at, cast.Colors, rig.Density);
            drawn.Continuous = true;
            drawn.Inward = true;
            drawn.Extents = Vector3.One * 0.9f;
            drawn.SizeScale = 0.6f;
            drawn.LifeScale = 0.5f;
            drawn.GravityScale = 0f;
            rig.Stream = rig.Add(cast.Fx.Burst(VfxParticles.Wisps, drawn));
            rig.Stream.Get?.Follow(hand);
        }

        return true;
    }

    /// <summary>The rot in flight, added to the generic bolt: a long thin tendril left hanging
    /// along the way it came, so a slow bolt reads as a reaching thing and its line can be seen
    /// from the side. Not on the leanest tier.</summary>
    private static bool WitherProjectile(
        in VfxCast cast, VfxRig rig, Node3D projectile, Vector3 handOffset, Vector3 direction, float charge)
    {
        if (VfxQuality.Tier == VfxTier.Performance)
        {
            return false;
        }

        Vector3 start = projectile.GlobalPosition + handOffset;
        VfxHandle<VfxBolt> tendril = rig.Add(cast.Fx.Bolt(new VfxBoltSpec
        {
            Mode = VfxBoltMode.Trail,
            From = start,
            To = start,
            Colors = cast.Colors.Scaled(0.8f, 0.5f),
            Width = 0.05f,
            TrailSeconds = ArcanaRich ? 0.9f : 0.5f,
        }));
        if (tendril.Get is { } line)
        {
            line.FollowTo(VfxAnchor.To(projectile));
            line.SettleTrailFrom(handOffset);
        }

        return false;
    }

    // --- what a status wears -----------------------------------------------------------------------

    /// <summary>
    /// What this lane's statuses wear on top of their standing effect, for exactly as long as the
    /// status lasts: <see cref="StatusEffectVfxComponent"/> calls this with the rig it already
    /// holds for the status (the one <c>StatusAura</c> returned), and whatever is added to that rig
    /// ends when the status does.
    /// <list type="bullet">
    /// <item><b>Swarmed</b>: the swarm itself, circling the body (stinging darts on an orbit, a
    /// second, tighter and quicker ring of them from High up). Not on the leanest tier, where the
    /// status keeps its drift of specks.</item>
    /// <item><b>Grave Mark</b>: a sigil turning on the ground under the marked, the width of the
    /// body and a little more, with wisps lifting off it from Medium up.</item>
    /// </list>
    /// Nothing is drawn for a body the camera is inside (the standing effect is not either).
    /// </summary>
    internal static void StatusExtras(IEntity bearer, string statusId, DamageType school, VfxRig rig)
    {
        if (rig == null || statusId is not (StatusIds.Swarmed or StatusIds.GraveMark) || !Active ||
            VfxAnchor.BodyOf(bearer) is not { } body || StatusAuraHidden(bearer))
        {
            return;
        }

        VfxSpawner fx = _director!.Open(
            body.GlobalPosition + Vector3.Up, IsPlayer(bearer), essential: true, sustained: true, measured: true)
            .ForSchool(school);
        if (!fx.Full)
        {
            return;
        }

        VfxSchoolColors colors = VfxPalette.For(school);
        BodyFit(body, out Vector3 middle, out Vector3 fit);
        if (statusId == StatusIds.Swarmed)
        {
            int insects = VfxArcanaRules.SwarmInsects(VfxQuality.Tier);
            if (insects <= 0)
            {
                return;
            }

            float reach = Mathf.Clamp((fit.X * 0.5f) + 0.3f, 0.5f, 1.4f);
            VfxMotifSpec swarm = VfxMotifSpec.At(
                body.GlobalPosition + middle, colors, VfxSprite.Comet, VfxMotion.Orbit, insects, reach, 0.17f);
            swarm.Tint = ArcanaSting;
            swarm.Aspect = 0.45f;
            swarm.Speed = 2.1f;
            swarm.Energy = 1.3f;
            swarm.Sustain = true;
            swarm.TrueSize = true;
            rig.Add(fx.Motif(swarm)).Get?.Follow(VfxAnchor.To(body, middle));
            if (ArcanaLavish)
            {
                Vector3 higher = middle + new Vector3(0f, fit.Y * 0.22f, 0f);
                VfxMotifSpec inner = swarm;
                inner.Position = body.GlobalPosition + higher;
                inner.Count = Mathf.Max(3, insects / 2);
                inner.Radius = reach * 0.62f;
                inner.Speed = 3.3f;
                rig.Add(fx.Motif(inner)).Get?.Follow(VfxAnchor.To(body, higher));
            }

            return;
        }

        var feet = new Vector3(0f, middle.Y - (fit.Y * 0.5f) + 0.06f, 0f);
        VfxDiscSpec sigil = VfxDiscSpec.At(body.GlobalPosition + feet, VfxArcanaRules.MarkSigilRadius(fit.X), colors);
        sigil.Sustain = true;
        sigil.Rune = true;
        sigil.Spin = -0.5f;
        sigil.Flow = -0.3f;
        sigil.Body = 0.16f;
        sigil.Rim = 0.5f;
        rig.Add(fx.Disc(sigil)).Get?.Follow(VfxAnchor.To(body, feet));
        if (ArcanaRich)
        {
            Vector3 low = feet + new Vector3(0f, 0.15f, 0f);
            VfxBurstSpec lifting = VfxBurstSpec.At(body.GlobalPosition + low, colors, ArcanaDebris(0.18f));
            lifting.Continuous = true;
            lifting.Direction = Vector3.Up;
            lifting.Spread = 14f;
            lifting.Extents = new Vector3(sigil.Radius * 0.7f, 0.05f, sigil.Radius * 0.7f);
            lifting.SpeedScale = 0.5f;
            lifting.SizeScale = 0.5f;
            rig.Add(fx.Burst(VfxParticles.Wisps, lifting)).Get?.Follow(VfxAnchor.To(body, low));
        }
    }

    // --- breaths ---------------------------------------------------------------------------------

    /// <summary>
    /// A breath gathering, and then held, at the mouth, replacing the generic aura in the hand (a
    /// dragon has no casting hand: the glow sat in its chest). While it winds up the mouth glows
    /// brighter, the school's particles are drawn in to it and its motif stands about it
    /// (<see cref="MouthGlow"/>); a word of power also turns a rune ring there and lays one on the
    /// ground under the speaker. For as long as the breath then lasts the mouth stays lit. A body
    /// with no jaw, mouth or head bone keeps the generic aura, and so does the player's own cast.
    /// </summary>
    private static bool BreathWindup(
        in VfxCast cast, VfxRig rig, VfxAnchor hand, SpellWindupKind kind, float seconds, float charge)
    {
        if (cast.ByPlayer || cast.Caster is not { } caster)
        {
            return false;
        }

        VfxAnchor mouth = MouthAnchor(caster);
        if (!mouth.HasBone || !mouth.TryResolve(out Vector3 at))
        {
            return false;
        }

        bool word = cast.School == DamageType.Arcane;
        float size = word ? 0.42f : 0.5f;
        if (kind == SpellWindupKind.Channel)
        {
            VfxFlareSpec lit = VfxFlareSpec.At(at, size * 0.8f, cast.Colors);
            lit.Sustain = true;
            lit.Level = 0.9f;
            lit.Light = VfxQuality.Budget.MaxLights > 0;
            lit.LightRange = 7f;
            rig.Flare = rig.Add(cast.Fx.Flare(lit));
            rig.Flare.Get?.Follow(mouth);
            WindupMotif(cast, rig, mouth, at, size);
        }
        else if (VfxQuality.Tier == VfxTier.Performance)
        {
            // The leanest tier: the glow that builds and the motif, and no stream drawn in (what
            // the generic aura costs there, moved to the mouth).
            VfxFlareSpec gathers = VfxFlareSpec.At(at, size, cast.Colors);
            gathers.Sustain = true;
            gathers.Level = 0.25f;
            gathers.RampSeconds = Mathf.Max(0.05f, seconds);
            gathers.Light = VfxQuality.Budget.MaxLights > 0;
            gathers.LightRange = 5f;
            rig.Flare = rig.Add(cast.Fx.Flare(gathers));
            rig.Flare.Get?.Follow(mouth);
            WindupMotif(cast, rig, mouth, at, size);
        }
        else
        {
            MouthGlow(cast, rig, mouth, size, seconds);
        }

        if (!word || VfxQuality.Tier == VfxTier.Performance)
        {
            return true;
        }

        VfxDiscSpec spoken = VfxDiscSpec.At(at, size * 2.4f, cast.Colors);
        spoken.Sustain = true;
        spoken.Rune = true;
        spoken.FaceCamera = true;
        spoken.Spin = 1.6f;
        spoken.Body = 0.12f;
        spoken.Rim = 0f;
        rig.Add(cast.Fx.Disc(spoken)).Get?.Follow(mouth);
        if (ArcanaRich && VfxAnchor.BodyOf(caster) is { } body)
        {
            VfxDiscSpec under = VfxDiscSpec.At(body.GlobalPosition, 2.6f, cast.Colors.Scaled(0.7f, 1f));
            under.Sustain = true;
            under.Rune = true;
            under.Spin = -0.7f;
            under.Flow = 0.4f;
            under.Body = 0.14f;
            under.Rim = 0.6f;
            rig.Add(cast.Fx.Disc(under)).Get?.Follow(VfxAnchor.To(body));
        }

        return true;
    }

    /// <summary>
    /// One body struck by one tick of a breath, replacing the generic splash. That one is a flare at
    /// the body's middle every tick, with the school's own particles (pale wisps the size of a fist
    /// for the ash breath): three times a second, on the player, with the camera a few metres
    /// behind or half a metre above. Here it is a small spark with its halo turned down and a few of
    /// the breath's own particles, and on the first-person player no spark at all: only the
    /// particles, low, where they do not cross the lens. <paramref name="ashen"/> draws both in an
    /// ash breath's dull colours.
    /// </summary>
    private static bool ArcanaBreathHit(in VfxCast cast, in SpellImpactInfo hit, VfxParticles thrown, bool ashen = false)
    {
        if (hit.Kind is not (SpellImpactKind.Target or SpellImpactKind.Blocked))
        {
            return false;
        }

        VfxSchoolColors colors = ashen ? ArcanaAshen(cast.Colors) : cast.Colors;
        Vector3 at = hit.Position;
        bool lens = VfxAnchor.BodyOf(hit.Target) is { } body && ArcanaInside(body);
        if (lens)
        {
            at = new Vector3(at.X, at.Y - 0.6f, at.Z);
        }
        else
        {
            float scale = VfxRecipeRules.ImpactScale(cast.Weight, hit.Charge, hit.Crit, hit.Killed, hit.Kind);
            VfxFlareSpec spark = VfxFlareSpec.At(at, 0.26f * scale, colors.Scaled(0.8f, 0.5f));
            spark.Life = 0.2f;
            cast.Fx.Flare(spark);
        }

        VfxBurstSpec few = VfxBurstSpec.At(at, colors, ArcanaAmount(0.35f));
        few.SpeedScale = 0.8f;
        cast.Fx.Burst(hit.Kind == SpellImpactKind.Blocked ? VfxParticles.Sparks : thrown, few);
        return true;
    }

    /// <summary>
    /// Particles of <paramref name="preset"/> driven from <paramref name="origin"/> down a breath:
    /// thrown along <paramref name="axis"/> fast enough to cover <paramref name="reach"/> of the
    /// <paramref name="range"/> in <paramref name="seconds"/>, fanned over <paramref name="fan"/> of
    /// the wedge's half angle, and sized so the largest is <paramref name="width"/> metres across.
    /// </summary>
    private static VfxBurstSpec BreathDriven(
        in VfxSchoolColors colors, Vector3 origin, Vector3 axis, float range, float half, in VfxBurstPreset preset,
        float density, float seconds, float reach, float fan, float width)
    {
        VfxBurstSpec driven = VfxBurstSpec.At(origin + (axis * 0.3f), colors, density);
        driven.Direction = axis;
        driven.Spread = half * fan;
        driven.LifeScale = Mathf.Clamp(seconds / Mathf.Max(0.1f, preset.Life), 0.25f, 1f);
        driven.Speed = range * reach / (Mathf.Max(0.1f, preset.Life * driven.LifeScale) * 0.75f);
        driven.Damping = 0.6f;
        driven.GravityScale = 0.15f;
        driven.SizeScale = Mathf.Clamp(width / Mathf.Max(0.05f, preset.SizeMax), 0.4f, 2.4f);
        driven.Extents = Vector3.One * 0.15f;
        return driven;
    }

    /// <summary>
    /// One tick of a breath, replacing the generic cone.
    ///
    /// <para><b>It is a stream from the mouth.</b> Where the breather has a jaw, mouth or head bone
    /// the picture starts there and is aimed at the far end of the true wedge (the rule's own origin
    /// and reach are never read back or moved). Its body is shaped particles driven down the axis and
    /// widening as they go, on every tier; light is spent only where it reads as heat: at the mouth,
    /// and in a few glints down the axis, each small and left out near the camera.</para>
    /// <list type="bullet">
    /// <item><b>Fire</b>: tongues of flame that cool to smoke, with a narrow, quicker, brighter core
    /// of licks along the first half; from Medium up smoke rolls off the top, a body of fire stands
    /// at the mouth and patches of the ground it crossed are left burning for a couple of seconds.</item>
    /// <item><b>Ash</b>: grey-violet smoke billowing down the wedge, flakes of ash let go over it
    /// that hang three or four seconds, and above the leanest tier a few dull embers. Nothing pink.</item>
    /// <item><b>Frost</b>: a rolling body of cold mist with crystals thrown point first through it,
    /// and from Medium up driven snow.</item>
    /// <item><b>A word</b>: motes and thin rings thrown down the wedge, and rune rings among them.</item>
    /// </list>
    /// <para>The ground under the wedge is lit for the whole breath: every tick lays a patch of the
    /// school's glow (cracks, frost) a little further along than the last, sized to the wedge there.
    /// When the camera stands in the breath (the player it is aimed at) everything thrown is thinned.</para>
    /// </summary>
    private static bool ArcanaBreath(in VfxCast cast, Vector3 origin, Vector3 axis, float range, float angleDegrees)
    {
        VfxSchoolColors colors = cast.Colors;
        VfxBudget budget = VfxQuality.Budget;
        VfxTier tier = VfxQuality.Tier;
        SpellVfxDirector director = _director!;
        float half = Mathf.Clamp(angleDegrees * 0.5f, 4f, 85f);
        float slope = Mathf.Tan(Mathf.DegToRad(half));

        // From the mouth, toward the far end of the wedge the rule swept.
        bool mouthed = false;
        if (cast.Caster is { } caster && !cast.ByPlayer)
        {
            VfxAnchor jaw = MouthAnchor(caster);
            if (jaw.HasBone && jaw.TryResolve(out Vector3 mouth))
            {
                Vector3 end = origin + (axis * range);
                Vector3 line = end - mouth;
                float length = line.Length();
                if (VfxArcanaRules.MouthIsUsable(mouth.DistanceTo(origin), length, range))
                {
                    origin = mouth;
                    axis = line / length;
                    range = length;
                    mouthed = true;
                }
            }
        }

        float widthAtEnd = range * slope;
        Vector3 toCamera = director.HasCamera ? director.CameraPosition - origin : Vector3.Zero;
        float along = toCamera.Dot(axis);
        bool lens = director.HasCamera &&
                    VfxArcanaRules.InBreath(along, (toCamera - (axis * along)).Length(), range, slope);
        float thin = lens ? 0.6f : 1f;
        float density = budget.ParticleMultiplier;
        int roll = director.NextSeed();
        VfxMark mark = VfxMark.None;
        VfxDiscPattern pattern = VfxDiscPattern.None;
        VfxSchoolColors lit = colors;

        switch (cast.School)
        {
            case DamageType.Fire:
            {
                mark = VfxMark.Scorch;
                pattern = VfxDiscPattern.Cracks;
                BreathPuffs(cast, origin, axis, range, angleDegrees, VfxEmitter.Flame, thin);

                // The core: quick bright licks down the first half, added light only.
                VfxBurstPreset licks = VfxBurstPresets.For(VfxEmitter.FlameLick);
                VfxBurstSpec core = BreathDriven(
                    colors, origin, axis, range, half, licks, Mathf.Max(0.4f, density * 0.9f) * thin, 0.36f, 0.55f, 0.35f, 0.55f);
                cast.Fx.Burst(VfxEmitter.FlameLick, core);

                if (tier != VfxTier.Performance)
                {
                    VfxBurstSpec embers = BreathDriven(
                        colors, origin, axis, range, half, VfxBurstPresets.For(VfxParticles.Embers), ArcanaAmount(0.7f) * thin,
                        0.6f, 1f, 0.9f, 0.3f);
                    cast.Fx.Burst(VfxParticles.Embers, embers);
                }

                // Dark smoke rolling up off the top of it.
                VfxBurstPreset puff = VfxBurstPresets.For(VfxParticles.Smoke);
                VfxBurstSpec smoke = VfxBurstSpec.At(
                    origin + (axis * (range * 0.4f)) + (Vector3.Up * 0.4f), colors,
                    VfxArcanaRules.HazeDensity(budget, thin, smoke: true));
                smoke.Direction = (axis + (Vector3.Up * 0.7f)).Normalized();
                smoke.Spread = 26f;
                smoke.LifeScale = 0.7f;
                smoke.Speed = range * 0.3f / Mathf.Max(0.1f, puff.Life * smoke.LifeScale);
                smoke.Extents = new Vector3(0.3f, 0.15f, 0.3f) * Mathf.Max(1f, widthAtEnd * 0.3f);
                smoke.SizeScale = Mathf.Clamp(widthAtEnd * 0.2f, 0.7f, 1.5f);
                cast.Fx.Burst(VfxParticles.Smoke, smoke);

                if (ArcanaWild)
                {
                    VfxBurstSpec streaks = BreathDriven(
                        colors, origin, axis, range, half, VfxBurstPresets.For(VfxParticles.Sparks), ArcanaDebris(0.35f) * thin,
                        0.4f, 1f, 0.9f, 0.5f);
                    cast.Fx.Burst(VfxParticles.Sparks, streaks);
                }

                break;
            }

            case DamageType.Frost:
            {
                mark = VfxMark.Frost;
                pattern = VfxDiscPattern.Frost;
                BreathPuffs(cast, origin, axis, range, angleDegrees, VfxEmitter.Mist, thin);

                // Crystals thrown point first through the mist. In the face of whoever it is aimed
                // at they are fewer and smaller: at arm's length each was a pane across the view.
                VfxBurstSpec shards = BreathDriven(
                    colors, origin, axis, range, half, VfxBurstPresets.For(VfxEmitter.Crystals),
                    Mathf.Max(0.35f, density * 1.1f) * (lens ? 0.5f : 1f), 0.6f, 1f, 0.85f, lens ? 0.4f : 0.6f);
                cast.Fx.Burst(VfxEmitter.Crystals, shards);

                VfxBurstSpec snow = BreathDriven(
                    colors, origin, axis, range, half, VfxBurstPresets.For(VfxEmitter.Snow),
                    VfxArcanaRules.HazeDensity(budget, 0.8f * thin, smoke: false), 0.55f, 1f, 1f, 0.3f);
                snow.Extents = Vector3.One * 0.3f;
                cast.Fx.Burst(VfxEmitter.Snow, snow);
                break;
            }

            case DamageType.Necrotic:
            {
                VfxSchoolColors ashen = ArcanaAshen(colors);
                lit = ashen;

                // The breath: smoke that covers what is behind it, so a handful of puffs a tick on
                // any tier (ticks overlap), each thinned by how wide it stands on screen.
                VfxBurstPreset puff = VfxBurstPresets.For(VfxParticles.Smoke);
                VfxBurstSpec billow = BreathDriven(
                    ashen, origin, axis, range, half, puff, Mathf.Clamp(density * 0.55f, 0.36f, 0.6f) * thin, 0.9f, 1f, 0.7f,
                    Mathf.Clamp(widthAtEnd * 0.45f, 0.9f, 2.6f));
                billow.Tint = ArcanaAsh;
                billow.Damping = 1.1f;
                billow.GravityScale = 0.5f;
                cast.Fx.Burst(VfxParticles.Smoke, billow);

                // Ash let go over the wedge, hanging long after the breath has moved on. On the lean
                // tiers one tick in two or three lets some go, so no more emitters are alive at once.
                int every = VfxArcanaRules.AshEvery(tier);
                if (VfxArcanaRules.Falls(every, roll))
                {
                    float far = 0.35f + (0.5f * (((roll >> 8) & 0xFF) / 255f));
                    AshFall(
                        cast, origin + (axis * (range * far)) + (Vector3.Up * 0.5f),
                        Mathf.Clamp(range * far * slope * 0.8f, 0.8f, 3.5f), 0f, ArcanaAsh, every * thin);
                }

                if (tier != VfxTier.Performance)
                {
                    VfxBurstSpec embers = BreathDriven(
                        ashen, origin, axis, range, half, VfxBurstPresets.For(VfxParticles.Embers), ArcanaAmount(0.3f) * thin,
                        0.7f, 0.9f, 0.9f, 0.24f);
                    cast.Fx.Burst(VfxParticles.Embers, embers);
                }

                break;
            }

            default:
            {
                VfxBurstSpec motes = BreathDriven(
                    colors, origin, axis, range, half, VfxBurstPresets.For(VfxParticles.Motes), ArcanaAmount(1.3f) * thin, 0.6f,
                    1f, 0.9f, 0.2f);
                cast.Fx.Burst(VfxParticles.Motes, motes);

                // Streaks, where the tier has a second layer; far fewer into a camera that is in
                // the word's path, where each one crossed the whole view.
                VfxBurstSpec streaks = BreathDriven(
                    colors, origin, axis, range, half, VfxBurstPresets.For(VfxParticles.Sparks),
                    VfxArcanaRules.HazeDensity(budget, lens ? 0.3f : 1f, smoke: false), 0.45f, 1f, 1f, 0.45f);
                cast.Fx.Burst(VfxParticles.Sparks, streaks);

                // Rings thrown down the wedge, face on, and a rune ring among them (two from Medium up).
                int rings = ArcanaRich ? 3 : 2;
                for (int i = 0; i < rings; i++)
                {
                    float travelled = range * (0.25f + (0.25f * i));
                    Vector3 at = origin + (axis * travelled);
                    float radius = Mathf.Max(0.4f, travelled * slope * 0.8f);
                    bool rune = i == 1 || (i == 2 && !ArcanaLean);
                    if (!rune)
                    {
                        ArcanaRing(cast, at, radius, colors, 0.34f + (0.08f * i), normal: axis);
                        continue;
                    }

                    radius = Mathf.Min(radius, 2.2f);
                    if (director.DistanceToCamera(at) < radius + 2f)
                    {
                        continue;
                    }

                    VfxDiscSpec word = VfxDiscSpec.At(at, radius, colors);
                    word.Life = 0.5f + (0.08f * i);
                    word.Rune = true;
                    word.FaceCamera = true;
                    word.Spin = (i & 1) == 0 ? 2.4f : -2.4f;
                    word.Body = 0.08f;
                    word.Rim = 0f;
                    cast.Fx.Disc(word);
                }

                break;
            }
        }

        // The mouth: the one place a breath is white-hot. A mouth that has a bone is already held
        // lit for the whole breath by its wind-up, and needs no flash a tick.
        Vector3 hot = origin + (axis * 0.5f);
        if (!mouthed && director.DistanceToCamera(hot) >= 1.5f)
        {
            VfxFlareSpec heat = VfxFlareSpec.At(hot, 0.34f, lit);
            heat.Life = 0.3f;
            heat.Light = budget.MaxLights > 0;
            heat.LightRange = Mathf.Max(5f, range * 0.7f);
            cast.Fx.Flare(heat);
        }

        // A body at the mouth: a tongue of flame (or ice) a few metres long, not the wedge filled.
        // A body the size of the wedge is metres across, and whoever it is aimed at looks straight
        // down its length: that was the blank frame. It is left out when the camera is near its
        // far end, and a sphere shell is not drawn at all for a camera inside it.
        if (cast.School is DamageType.Fire or DamageType.Frost && budget.SecondaryDebris)
        {
            float length = VfxArcanaRules.GoutLength(range);
            float girth = VfxArcanaRules.GoutGirth(length, slope);
            Vector3 centre = origin + (axis * (0.3f + (length * 0.5f)));
            if (director.DistanceToCamera(centre) > (length * 0.5f) + VfxArcanaRules.GoutClearance)
            {
                VfxShellSpec gout = VfxShellSpec.Sphere(centre, 0.5f, colors);
                gout.Size = new Vector3(girth, girth, length);
                gout.Forward = axis;
                gout.Life = 0.36f;
                gout.BurnsAway = true;
                gout.StartScale = 0.4f;
                gout.Scroll = new Vector2(0.1f, 1.6f);
                gout.Opacity = 0.5f;
                gout.Energy = 0.8f;
                gout.Layered = ArcanaWild;
                cast.Fx.Shell(gout);
            }
        }

        // Glints down the axis: more of them on the richer tiers, none on the leanest, none of them
        // large and none near the camera. Their halos are turned well down: seen from the far end
        // of a breath they stack on one point of the screen.
        int glints = cast.Fx.Full ? VfxArcanaRules.Glints(tier) : 0;
        for (int i = 1; i <= glints; i++)
        {
            float travelled = range * i / (glints + 1f);
            Vector3 at = origin + (axis * travelled);
            if (director.DistanceToCamera(at) < VfxArcanaRules.GlintMinCameraDistance)
            {
                continue;
            }

            VfxFlareSpec glint = VfxFlareSpec.At(at, VfxArcanaRules.GlintRadius(travelled, slope), lit.Scaled(0.7f, 0.35f));
            glint.Life = 0.22f + (0.05f * i);
            cast.Fx.Flare(glint);
        }

        if (cast.School == DamageType.Fire)
        {
            Vector3 centre = origin + (axis * (range * 0.3f));
            float reach = range * 0.22f;
            if (director.DistanceToCamera(centre) > reach + 1f)
            {
                cast.Fx.Distortion(new VfxDistortionSpec { Position = centre, Radius = reach, Life = 0.4f, Strength = 0.02f });
            }
        }

        if (cast.School == DamageType.Arcane || VfxAnchor.BodyOf(cast.Caster) is not { } body)
        {
            return true;
        }

        var flat = new Vector3(axis.X, 0f, axis.Z);
        if (flat.LengthSquared() <= 0.01f)
        {
            return true;
        }

        // The ground under the wedge, lit for the whole breath: one patch a tick, at a new place
        // along the centre line each time and as wide as the wedge is there, living long enough
        // that three or four overlap. (The caster's own feet give the floor's height.) An ash
        // breath, which never left a patch, leaves none on the leanest tier.
        if (pattern == VfxDiscPattern.None && tier == VfxTier.Performance)
        {
            return true;
        }

        flat = flat.Normalized();
        float step = VfxArcanaRules.PatchAlong(roll);
        var foot = new Vector3(origin.X, body.GlobalPosition.Y, origin.Z);
        Vector3 under = foot + (flat * (range * step));
        float wide = Mathf.Clamp(range * step * slope * 0.85f, 0.9f, 3.2f);
        VfxDiscSpec patch = VfxDiscSpec.At(under, wide, lit);
        patch.Pattern = pattern;
        patch.Life = 1.5f;
        patch.Body = pattern == VfxDiscPattern.None ? 0.2f : 0.12f;
        patch.Rim = 0f;
        patch.Flow = 0.2f;
        cast.Fx.Disc(patch);

        if (mark != VfxMark.None && budget.GroundMarks > 0)
        {
            // What it leaves: somewhere new in the wedge each tick; the mark budget keeps the
            // newest, so a long breath leaves a scorched (or frosted) fan and not a single spot.
            float far = 0.25f + (0.65f * ((roll & 0xFFFF) / 65535f));
            float across = ((((roll >> 16) & 0xFFFF) / 65535f) - 0.5f) * far * widthAtEnd;
            Vector3 at = foot + (flat * (range * far)) + (flat.Cross(Vector3.Up) * across);
            cast.Fx.Mark(new VfxGroundMarkSpec
            {
                Mark = mark,
                Position = at,
                Size = Mathf.Clamp(widthAtEnd * 0.5f, 1.5f, 3.5f),
                Colors = colors,
                Life = 6f,
                Reach = 1.5f,
            });

            // Fire left burning where it scorched, for two seconds and a half.
            if (cast.School == DamageType.Fire && VfxArcanaRules.Falls(VfxArcanaRules.BurnEvery(tier), roll >> 4))
            {
                VfxBurstSpec burning = VfxBurstSpec.At(at + (Vector3.Up * 0.15f), Flame(colors), density * 0.3f);
                burning.Continuous = true;
                burning.StreamSeconds = 2.5f;
                burning.Extents = new Vector3(0.7f, 0.05f, 0.7f);
                burning.Direction = Vector3.Up;
                burning.Spread = 20f;
                burning.SizeScale = 0.5f;
                burning.SpeedScale = 0.6f;
                burning.LifeScale = 0.7f;
                cast.Fx.Burst(VfxEmitter.Flame, burning);
            }
        }

        return true;
    }
}
