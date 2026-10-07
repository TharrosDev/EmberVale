using Embervale.Combat;
using Embervale.Entities;
using Godot;

namespace Embervale.Magic.Vfx;

/// <summary>
/// Things that land: a hit, a blast, a breath, an arc between two bodies, a combo going off.
/// <see cref="Blast"/> is the heart of the interpreter: it is the one routine that turns a
/// <see cref="VfxPlan"/> into blocks at a point, and every beat that "goes off" somewhere is drawn
/// through it.
/// </summary>
public static partial class SpellVfx
{
    /// <summary>A spell struck something: a target, a guard, a wall, a barrier, or nothing at the end
    /// of its range.</summary>
    public static void Impact(SpellResource spell, IEntity? caster, in SpellImpactInfo hit)
    {
        if (!Begin(spell, caster, hit.Position, out VfxCast cast))
        {
            return;
        }

        if (hit.Kind == SpellImpactKind.Target)
        {
            RecentImpacts[_recentImpact] = (hit.Position, cast.Colors.Mid, _director!.Now);
            _recentImpact = (_recentImpact + 1) % RecentImpacts.Length;
        }

        bool channelled = spell.CastMode == CastMode.Channeled;
        if (channelled)
        {
            ClipBeam(caster, spell, hit.Position);
        }

        if (SpecialOf(spell)?.Impact is { } special && special(cast, hit))
        {
            return;
        }

        bool hitsPlayer = hit.Target != null && IsPlayer(hit.Target);
        float scale = VfxRecipeRules.ImpactScale(cast.Weight, hit.Charge, hit.Crit, hit.Killed, hit.Kind);
        bool onBody = hit.Kind is SpellImpactKind.Target or SpellImpactKind.Blocked;

        // One of many: a blast, a breath or a beam has drawn (or is drawing) the big picture, and
        // this is one body inside it lighting up.
        if (onBody && (channelled || VfxRecipeRules.IsSplash(spell.Delivery, spell.ImpactRadius)))
        {
            VfxFlareSpec spark = VfxFlareSpec.At(hit.Position, 0.36f * scale, cast.Colors);
            spark.Life = 0.2f;
            cast.Fx.Flare(spark);
            VfxParticles thrown = hit.Kind == SpellImpactKind.Blocked
                ? VfxParticles.Sparks
                : SpellVfxCatalog.SchoolParticles(cast.School);
            VfxBurstSpec few = VfxBurstSpec.At(hit.Position, cast.Colors, VfxQuality.Budget.ParticleMultiplier * 0.35f);
            few.SpeedScale = 0.8f;
            cast.Fx.Burst(thrown, few);
            return;
        }

        VfxPlan plan = cast.Plan(VfxRole.Impact, 0f, hitsPlayer);
        switch (hit.Kind)
        {
            case SpellImpactKind.Blocked:
                // Stopped on a guard: sparks off the steel, and none of what a landed hit leaves.
                plan = plan with
                {
                    Particles = plan.Particles == VfxParticles.None ? VfxParticles.None : VfxParticles.Sparks,
                    Secondary = VfxParticles.None,
                    Mark = VfxMark.None,
                    Distortion = false,
                    ScreenFlash = false,
                    Fireball = false,
                    Shell = false,
                    Sigil = false,
                    Tether = false,
                };
                break;

            case SpellImpactKind.Expired:
                // Ran out of range in the air: it gutters.
                plan = plan with
                {
                    Ring = false,
                    Secondary = VfxParticles.None,
                    Mark = VfxMark.None,
                    Distortion = false,
                    ScreenFlash = false,
                    Fireball = false,
                    Shell = false,
                    Sigil = false,
                    Tether = false,
                    Density = plan.Density * 0.5f,
                };
                break;

            case SpellImpactKind.World:
            case SpellImpactKind.Barrier:
                plan = plan with { Shell = false, Sigil = false, Tether = false };
                break;
        }

        float radius = 1.05f * scale * plan.Scale;
        Blast(cast, plan, hit.Position, radius, hit.Normal, float.NaN, hitsPlayer);
        if (hit.Kind != SpellImpactKind.Target)
        {
            return;
        }

        Node3D? body = VfxAnchor.BodyOf(hit.Target);
        if (plan.Shell && body != null)
        {
            // On a frost spell the shell is ice closing over the struck; on any other, a rim of light.
            // Fitted to the body and standing on its feet, wherever the body's origin is.
            BodyFit(body, out Vector3 middle, out Vector3 fit);
            VfxShellSpec shell = cast.School == DamageType.Frost
                ? VfxShellSpec.IceShell(body.GlobalPosition + middle, 0.95f, cast.Colors)
                : VfxShellSpec.Sphere(body.GlobalPosition + middle, 0.95f, cast.Colors);
            shell.Size = new Vector3(fit.X + 0.6f, fit.Y + 0.2f, fit.X + 0.6f);
            shell.Fresnel = true;
            shell.Life = 0.7f;
            shell.BurnsAway = true;
            cast.Fx.Shell(shell).Get?.Follow(VfxAnchor.To(body, middle));
        }

        if (plan.Sigil)
        {
            VfxDiscSpec sigil = VfxDiscSpec.At(hit.Position, 0.55f * scale, cast.Colors);
            sigil.Life = 0.8f;
            sigil.Rune = true;
            sigil.FaceCamera = true;
            sigil.Body = 0.1f;
            sigil.Rim = 0f;
            sigil.Spin = 2.5f;
            cast.Fx.Disc(sigil);
        }

        if (plan.Tether && caster != null && VfxAnchor.BodyOf(caster) != null)
        {
            // Drawn from the struck back to the caster's hand: life being taken.
            VfxHandle<VfxBolt> tether = cast.Fx.Bolt(new VfxBoltSpec
            {
                Mode = VfxBoltMode.Tether,
                From = hit.Position,
                To = CastOrigin(caster, hit.Position),
                Colors = cast.Colors,
                Width = 0.07f,
                Segments = VfxRecipeRules.BoltSegments(VfxQuality.Budget, 6f),
                Life = 0.45f,
                Seed = _director!.NextSeed(),
            });
            if (tether.Get is { } line)
            {
                if (body != null)
                {
                    line.FollowFrom(VfxAnchor.To(body, hit.Position - body.GlobalPosition));
                }

                line.FollowTo(VfxAnchor.ToHand(caster, ChestOffset));
            }
        }

        Linger(cast, hit.Position, body, radius);
    }

    /// <summary>A spell burst over <paramref name="radius"/> at <paramref name="position"/>, which is
    /// the centre of the damaged sphere and not a point on the floor. <paramref name="charge"/> is
    /// what a held cast reached (0..1).</summary>
    public static void Burst(
        SpellResource spell, IEntity? caster, Vector3 position, float radius, SpellBurstSource source,
        float charge = 0f)
    {
        if (!Begin(spell, caster, position, out VfxCast cast))
        {
            return;
        }

        if (SpecialOf(spell)?.Burst is { } special && special(cast, position, radius, source, charge))
        {
            return;
        }

        radius = Mathf.Max(0.3f, radius);
        float groundY = source switch
        {
            // A nova's centre is a metre up the caster; a ground spell's a hand above the floor.
            SpellBurstSource.Caster => VfxAnchor.BodyOf(caster)?.GlobalPosition.Y ?? position.Y - 1f,
            SpellBurstSource.Ground => position.Y - 0.3f,
            SpellBurstSource.Zone => position.Y - 0.1f,
            _ => float.NaN,
        };

        if (source == SpellBurstSource.Zone)
        {
            // One pulse of a standing zone. The zone's own ambience is the picture; this is its beat.
            var floor = new Vector3(position.X, groundY + 0.12f, position.Z);
            VfxFlareSpec pulse = VfxFlareSpec.At(floor, radius * 0.22f, cast.Colors);
            pulse.Life = 0.45f;
            pulse.Ring = true;
            pulse.RingRadius = radius;
            pulse.Inward = spell.PullStrength > 0f;
            cast.Fx.Flare(pulse);
            VfxBurstSpec beat = VfxBurstSpec.At(
                floor + (Vector3.Up * 0.3f), cast.Colors, VfxQuality.Budget.ParticleMultiplier * 0.4f);
            beat.Extents = new Vector3(radius * 0.6f, 0.2f, radius * 0.6f);
            beat.Direction = Vector3.Up;
            beat.Spread = 60f;
            cast.Fx.Burst(SpellVfxCatalog.SchoolParticles(cast.School), beat);
            return;
        }

        bool hitsPlayer = !cast.ByPlayer && PlayerWithin(position, radius + 0.5f);
        VfxPlan plan = cast.Plan(VfxRole.Burst, radius, hitsPlayer);

        // Something that fell out of the sky lands harder than a blast of the same reach: the fire
        // is drawn larger than the damage (the ring still marks the true radius).
        bool fell = source == SpellBurstSource.Ground && FallsFromSky(spell, 1f);
        Blast(cast, plan, position, radius, Vector3.Zero, groundY, hitsPlayer, streak: fell, bodyScale: fell ? 1.4f : 1f);
        Linger(cast, position, null, radius);
    }

    /// <summary>A wedge swept out from <paramref name="origin"/>: a breath, a word of power.</summary>
    public static void Cone(
        SpellResource spell, IEntity? caster, Vector3 origin, Vector3 direction, float range, float angleDegrees)
    {
        if (direction.LengthSquared() < 0.0001f || range <= 0f)
        {
            return;
        }

        Vector3 axis = direction.Normalized();
        if (!Begin(spell, caster, origin + (axis * (range * 0.5f)), out VfxCast cast))
        {
            return;
        }

        if (SpecialOf(spell)?.Cone is { } special && special(cast, origin, axis, range, angleDegrees))
        {
            return;
        }

        VfxPlan plan = cast.Plan(VfxRole.Travel);
        float half = Mathf.Clamp(angleDegrees * 0.5f, 4f, 85f);
        float widthAtEnd = range * Mathf.Tan(Mathf.DegToRad(half));
        VfxBudget budget = VfxQuality.Budget;

        // The cone itself: particles thrown down the wedge fast enough to reach its end.
        if (plan.Particles != VfxParticles.None)
        {
            VfxBurstPreset preset = VfxBurstPresets.For(plan.Particles);
            VfxBurstSpec spray = VfxBurstSpec.At(origin, cast.Colors, plan.Density * 1.3f);
            spray.Direction = axis;
            spray.Spread = half;
            spray.LifeScale = Mathf.Clamp(0.55f / Mathf.Max(0.1f, preset.Life), 0.3f, 1f);
            spray.Speed = range / (Mathf.Max(0.1f, preset.Life * spray.LifeScale) * 0.8f);
            spray.Damping = 0.6f;
            spray.GravityScale = 0.15f;
            spray.SizeScale = 1.3f;
            spray.Extents = Vector3.One * 0.12f;
            cast.Fx.Burst(plan.Particles, spray);

            VfxParticles second = plan.Secondary != VfxParticles.None
                ? plan.Secondary
                : budget.SecondaryDebris && !cast.Authored ? VfxRecipeRules.SchoolSecondary(cast.School) : VfxParticles.None;
            if (second != VfxParticles.None && second != plan.Particles)
            {
                VfxBurstPreset drift = VfxBurstPresets.For(second);
                VfxBurstSpec haze = VfxBurstSpec.At(origin + (axis * (range * 0.25f)), cast.Colors, budget.ParticleMultiplier * 0.6f);
                haze.Direction = axis;
                haze.Spread = half;
                haze.Speed = range * 0.55f / Mathf.Max(0.1f, drift.Life);
                haze.Extents = Vector3.One * Mathf.Max(0.15f, widthAtEnd * 0.2f);
                cast.Fx.Burst(second, haze);
            }
        }

        // A body of flowing noise filling the wedge, so a breath is a volume and not only its sparks.
        if (plan.Flare && budget.SecondaryDebris && VfxRecipeRules.SchoolFireball(cast.School))
        {
            VfxShellSpec gout = VfxShellSpec.Ball(
                origin + (axis * (range * 0.55f)), 0.5f, cast.Colors, SchoolSmokes(cast.School));
            gout.Size = new Vector3(widthAtEnd * 1.3f, widthAtEnd * 1.3f, range * 0.95f);
            gout.Forward = axis;
            gout.Life = 0.38f;
            gout.StartScale = 0.45f;
            gout.Scroll = new Vector2(0.1f, 1.4f);
            gout.Tiling = new Vector2(3f, 2f);
            gout.Opacity = 0.7f;
            cast.Fx.Shell(gout);
        }

        // What the breath is made of: flame that cools to smoke down the wedge, or cold mist.
        VfxEmitter breath = SchoolSmokes(cast.School)
            ? VfxEmitter.Flame
            : cast.School == DamageType.Frost ? VfxEmitter.Mist : VfxEmitter.None;
        if (plan.Flare && breath != VfxEmitter.None && VfxQuality.Rich.Billow)
        {
            VfxBurstPreset puffs = VfxBurstPresets.For(breath);
            VfxBurstSpec fire = VfxBurstSpec.At(origin + (axis * 0.3f), cast.Colors, budget.ParticleMultiplier * 1.2f);
            fire.Direction = axis;
            fire.Spread = half * 0.8f;
            fire.LifeScale = Mathf.Clamp(0.7f / Mathf.Max(0.1f, puffs.Life), 0.25f, 1f);
            fire.Speed = range / (Mathf.Max(0.1f, puffs.Life * fire.LifeScale) * 0.75f);
            fire.Damping = 1.2f;
            fire.GravityScale = 0.4f;
            fire.SizeScale = Mathf.Clamp(widthAtEnd * 0.45f / Mathf.Max(0.1f, puffs.SizeMax), 0.5f, 2.4f);
            fire.Extents = Vector3.One * 0.15f;
            cast.Fx.Burst(breath, fire);
        }

        // Widening flashes down the axis: the reach and the shape, readable at a glance.
        if (plan.Flare)
        {
            int puffs = cast.Fx.Full && VfxQuality.Rich.Rays ? 3 : 1;
            for (int i = 1; i <= puffs; i++)
            {
                float travelled = range * i / (puffs + 0.5f);
                Vector3 at = origin + (axis * travelled);

                // A breath in the player's face is not a wall of light: the nearest puffs are left out.
                if (!cast.ByPlayer && _director!.DistanceToCamera(at) < 2f)
                {
                    continue;
                }

                // Glints of heat down the wedge, not a wall of discs: the body and the particles
                // are the breath, these only light it.
                VfxFlareSpec puff = VfxFlareSpec.At(
                    at, Mathf.Clamp(travelled * Mathf.Tan(Mathf.DegToRad(half)) * 0.35f, 0.25f, 1.3f),
                    cast.Colors.Scaled(0.6f, 0.7f));
                puff.Life = 0.26f + (0.05f * i);
                puff.Light = plan.Light && i == 1;
                puff.LightRange = Mathf.Max(4f, range * 0.8f);
                cast.Fx.Flare(puff);
            }
        }

        if (plan.Bolt)
        {
            cast.Fx.Bolt(new VfxBoltSpec
            {
                Mode = VfxBoltMode.Lightning,
                From = origin,
                To = origin + (axis * range),
                Colors = cast.Colors,
                Width = 0.06f,
                Segments = VfxRecipeRules.BoltSegments(budget, range),
                Branches = budget.BoltBranches,
                Seed = _director!.NextSeed(),
            });
        }

        if (plan.Distortion)
        {
            cast.Fx.Distortion(new VfxDistortionSpec
            {
                Position = origin + (axis * (range * 0.5f)),
                Radius = range * 0.45f,
                Life = 0.4f,
                Strength = 0.025f,
            });
        }

        if (plan.Ring)
        {
            // A word of power: a ring thrown down the cone, face on.
            VfxFlareSpec ring = VfxFlareSpec.At(origin + (axis * (range * 0.6f)), 0.2f, cast.Colors);
            ring.NoCore = true;
            ring.Ring = true;
            ring.RingRadius = Mathf.Max(0.5f, widthAtEnd * 0.8f);
            ring.RingNormal = axis;
            ring.Life = 0.4f;
            cast.Fx.Flare(ring);
        }
    }

    /// <summary>A line of <paramref name="school"/> between two points: a chained bolt, a life tether,
    /// a status jumping bearers. <paramref name="source"/> is whose effect it is, and
    /// <paramref name="spell"/> the spell whose hit drew it (null for a status spreading by itself).</summary>
    public static void Arc(
        DamageType school, IEntity? source, Vector3 from, Vector3 to, SpellArcKind kind, SpellResource? spell = null)
    {
        if (!BeginSchool(school, source, (from + to) * 0.5f, spell, out VfxCast cast))
        {
            return;
        }

        if (SpecialOf(spell)?.Arc is { } special && special(cast, from, to, kind))
        {
            return;
        }

        VfxBudget budget = VfxQuality.Budget;
        float length = from.DistanceTo(to);
        bool strike = kind is SpellArcKind.Chain or SpellArcKind.Brand;
        cast.Fx.Bolt(new VfxBoltSpec
        {
            Mode = strike ? VfxBoltMode.Lightning : VfxBoltMode.Tether,
            From = from,
            To = to,
            Colors = cast.Colors,
            Width = kind == SpellArcKind.Brand ? 0.085f : 0.06f,
            Electric = school == DamageType.Lightning,
            Jitter = kind == SpellArcKind.Spread ? 0.13f : 0f,
            Segments = VfxRecipeRules.BoltSegments(budget, length),
            Branches = kind == SpellArcKind.Chain ? budget.BoltBranches : 0,
            Life = kind == SpellArcKind.Spread ? 0.5f : 0f,
            Seed = _director!.NextSeed(),
        });

        // Where it lands.
        VfxFlareSpec end = VfxFlareSpec.At(to, strike ? 0.45f : 0.3f, cast.Colors);
        end.Life = strike ? 0.2f : 0.35f;
        end.Rays = strike;
        end.Light = strike && budget.MaxLights > 0;
        end.LightRange = 4f;
        cast.Fx.Flare(end);
        VfxBurstSpec scatter = VfxBurstSpec.At(to, cast.Colors, budget.ParticleMultiplier * 0.35f);
        cast.Fx.Burst(strike ? VfxParticles.Sparks : SpellVfxCatalog.SchoolParticles(school), scatter);
    }

    /// <summary>A spell combo went off on <paramref name="target"/>: <paramref name="spell"/> struck a
    /// bearer of the status the combo <paramref name="comboId"/> (<c>combo.*</c>) needs.
    /// <paramref name="position"/> is the struck volume, read before the bonus damage landed.</summary>
    public static void Combo(string comboId, SpellResource spell, IEntity? caster, IEntity target, Vector3 position)
    {
        if (!Begin(spell, caster, position, out VfxCast cast))
        {
            return;
        }

        if (comboId != null && Specials.Combos.TryGetValue(comboId, out SpellVfxSpecialTable.ComboHook? special) &&
            special(cast, target, position))
        {
            return;
        }

        // Two schools meeting: louder than either hit, and thrown wide.
        var stage = new VfxStage
        {
            Flare = true,
            Ring = true,
            Particles = VfxParticles.Sparks,
            Secondary = SpellVfxCatalog.SchoolParticles(cast.School),
            Distortion = true,
        };
        bool hitsPlayer = target != null && IsPlayer(target);
        VfxPlan plan = cast.PlanOf(stage, 1.8f, hitsPlayer) with { Fireball = false };
        Blast(cast, plan, position, 1.8f, Vector3.Zero, float.NaN, hitsPlayer, streak: true);
    }

    /// <summary>
    /// Builds a plan's blocks at a point: the flare and its light, the shock ring, the ball of fire,
    /// both particle layers, the pressure wave, the mark on the floor and the screen flash, each
    /// sized from <paramref name="radius"/>. <paramref name="groundY"/> is the height of the floor
    /// under the blast when the caller knows it (NaN when it does not): with a floor, the ring runs
    /// along it and the debris is thrown upward instead of into it.
    /// </summary>
    private static void Blast(
        in VfxCast cast, in VfxPlan plan, Vector3 centre, float radius, Vector3 ringNormal, float groundY,
        bool hitsPlayer, bool streak = false, float bodyScale = 1f)
    {
        if (plan.IsEmpty)
        {
            return;
        }

        VfxSpawner fx = cast.Fx;
        VfxSchoolColors colors = cast.Colors;
        VfxRichness rich = VfxQuality.Rich;
        radius = Mathf.Max(0.15f, radius);

        // The size of the fire, as opposed to the reach of the damage: a meteor's is larger.
        float body = radius * Mathf.Max(0.5f, bodyScale);
        bool grounded = !float.IsNaN(groundY) && Mathf.Abs(centre.Y - groundY) <= Mathf.Max(1.5f, radius);
        var floor = new Vector3(centre.X, grounded ? groundY : centre.Y, centre.Z);
        float life = Mathf.Clamp(0.3f + (radius * 0.05f), 0.3f, 0.7f) * rich.LifeScale;

        if (plan.Flare)
        {
            // A flash whose white heart is capped and brief (VfxFlare and the coverage governor see
            // to that); what makes it large is the rays, the ring and everything built below.
            VfxFlareSpec flare = VfxFlareSpec.At(centre, body * 0.55f, colors);
            flare.Life = life;
            flare.Light = plan.Light;
            flare.LightRange = Mathf.Max(3.5f, radius * 2.6f);
            flare.Rays = radius >= 0.9f || cast.Weight >= 0.5f || streak;
            flare.Streak = streak && radius >= 1.2f;
            flare.Inward = plan.Inward;
            if (plan.Ring && !grounded)
            {
                flare.Ring = true;
                flare.RingRadius = radius;
                flare.RingNormal = ringNormal;
                flare.RingStreaks = 0.8f;
            }

            fx.Flare(flare);
        }

        if (plan.Ring && (grounded || !plan.Flare))
        {
            // The shock front, along the floor.
            VfxFlareSpec ring = VfxFlareSpec.At(floor + (Vector3.Up * 0.12f), radius * 0.3f, colors);
            ring.NoCore = true;
            ring.Ring = true;
            ring.RingRadius = radius;
            ring.RingNormal = grounded ? Vector3.Zero : ringNormal;
            ring.RingStreaks = 0.8f;
            ring.Life = life + 0.15f;
            ring.Inward = plan.Inward;
            fx.Flare(ring);

            if (rich.DebrisLayers >= 2 && radius >= 2f && !plan.Inward)
            {
                // The top tier: a second, slower, fainter front rolling out past the first.
                ring.Colors = colors.Scaled(0.45f, 1f);
                ring.RingRadius = radius * 1.3f;
                ring.RingStreaks = 0f;
                ring.Life = (life + 0.15f) * 1.7f;
                fx.Flare(ring);
            }
        }

        if (plan.Fireball)
        {
            VfxShellSpec ball = VfxShellSpec.Ball(centre, body * 0.6f, colors, SchoolSmokes(cast.School));
            ball.Life = life + 0.3f;
            fx.Shell(ball);
        }

        if (plan.Flare)
        {
            Signature(cast, plan, centre, floor, radius, body, grounded);
        }

        float throwScale = Mathf.Clamp(0.6f + (radius * 0.32f), 0.6f, 3f);
        float amount = Mathf.Clamp(0.6f + (radius * 0.25f), 0.6f, 2f);
        if (plan.Particles != VfxParticles.None)
        {
            VfxBurstSpec debris = VfxBurstSpec.At(centre, colors, plan.Density * amount);
            debris.SizeScale = Mathf.Clamp(0.85f + (radius * 0.1f), 0.85f, 1.8f);
            debris.LifeScale = rich.LifeScale;
            if (plan.Inward)
            {
                debris.Inward = true;
                debris.Extents = Vector3.One * (radius * 0.9f);
            }
            else
            {
                debris.Extents = Vector3.One * (radius * 0.15f);
                debris.SpeedScale = throwScale;
                if (grounded)
                {
                    debris.Direction = Vector3.Up;
                    debris.Spread = 78f;
                }
            }

            fx.Burst(plan.Particles, debris);
        }

        if (plan.Secondary != VfxParticles.None)
        {
            VfxBurstSpec second = VfxBurstSpec.At(centre, colors, plan.SecondaryDensity * amount);
            second.Extents = Vector3.One * (radius * 0.3f);
            second.SpeedScale = Mathf.Clamp(0.5f + (radius * 0.2f), 0.5f, 2f);
            second.SizeScale = plan.Secondary == VfxParticles.Smoke
                ? Mathf.Clamp(radius * 0.55f, 0.7f, 3f)
                : Mathf.Clamp(0.9f + (radius * 0.1f), 0.9f, 1.8f);
            if (grounded)
            {
                second.Direction = Vector3.Up;
                second.Spread = 70f;
            }

            fx.Burst(plan.Secondary, second);
        }

        if (plan.Distortion)
        {
            fx.Distortion(new VfxDistortionSpec
            {
                Position = centre,
                Radius = radius * 1.25f,
                Life = 0.42f,
                Strength = Mathf.Clamp(0.028f + (radius * 0.004f), 0.028f, 0.06f),
                Inward = plan.Inward,
            });
        }

        if (plan.Mark != VfxMark.None)
        {
            fx.Mark(new VfxGroundMarkSpec
            {
                Mark = plan.Mark,
                Position = floor,
                Size = Mathf.Max(0.8f, radius * 1.9f),
                Colors = colors,
                Life = 7f + radius,
                Reach = grounded ? Mathf.Max(1f, radius * 0.5f) : Mathf.Max(1.7f, radius),
            });
        }

        // Only a blast the player is standing in flashes the screen, and then only slightly.
        if (plan.ScreenFlash && VfxScreenRules.PlayerCentred(PlayerDistance(centre), radius))
        {
            float strength = Mathf.Clamp(0.4f + (cast.Weight * 0.4f) + (radius * 0.05f), 0f, 1f);
            fx.Screen(SpellSchools.Color(cast.School), strength, hitsPlayer);
        }
    }

    /// <summary>
    /// What a hit or a blast leaves behind for a moment, read from the recipe's linger stage exactly
    /// as written (a fallback has none here: its smoke is already the blast's second particle layer).
    /// A spell whose linger stage is a standing thing (a zone, a wall) does not use this: that stage
    /// is drawn by the zone or the wall itself.
    /// </summary>
    private static void Linger(in VfxCast cast, Vector3 centre, Node3D? body, float radius)
    {
        VfxStage stage = cast.Recipe.Linger;
        if (stage.IsEmpty || cast.Spell is not { } spell || spell.Delivery == SpellDelivery.Barrier ||
            spell.ZoneDuration > 0f || spell.SummonDuration > 0f)
        {
            return;
        }

        VfxPlan plan = cast.PlanOf(stage);
        if (plan.IsEmpty)
        {
            return;
        }

        VfxSpawner fx = cast.Fx;
        float size = Mathf.Max(0.3f, radius) * plan.Scale;
        if (plan.Flare)
        {
            // A soft glow that hangs for a second.
            VfxFlareSpec glow = VfxFlareSpec.At(centre, size * 0.45f, cast.Colors.Scaled(0.55f, 1f));
            glow.Life = 1f;
            glow.Ring = plan.Ring;
            glow.RingRadius = size;
            VfxHandle<VfxFlare> flare = fx.Flare(glow);
            if (body != null)
            {
                flare.Get?.Follow(VfxAnchor.To(body, centre - body.GlobalPosition));
            }
        }

        if (plan.Particles != VfxParticles.None)
        {
            VfxBurstSpec hang = VfxBurstSpec.At(centre, cast.Colors, plan.Density * 0.6f);
            hang.Extents = Vector3.One * (size * 0.4f);
            hang.SpeedScale = 0.5f;
            hang.LifeScale = 1.5f;
            hang.SizeScale = plan.Particles == VfxParticles.Smoke ? Mathf.Clamp(size * 0.6f, 0.7f, 3f) : 1f;
            fx.Burst(plan.Particles, hang);
        }

        if (plan.Secondary != VfxParticles.None)
        {
            VfxBurstSpec second = VfxBurstSpec.At(centre, cast.Colors, plan.SecondaryDensity * 0.5f);
            second.Extents = Vector3.One * (size * 0.4f);
            second.SpeedScale = 0.5f;
            second.LifeScale = 1.5f;
            fx.Burst(plan.Secondary, second);
        }

        if (plan.Mark != VfxMark.None)
        {
            fx.Mark(new VfxGroundMarkSpec
            {
                Mark = plan.Mark,
                Position = centre,
                Size = Mathf.Max(0.8f, size * 1.9f),
                Colors = cast.Colors,
                Life = 7f + size,
                Reach = Mathf.Max(1.7f, size),
            });
        }

        if (plan.Sigil)
        {
            // Hangs over the struck for a couple of seconds.
            Vector3 above = body != null ? body.GlobalPosition + (Vector3.Up * 2.2f) : centre + (Vector3.Up * 1.2f);
            VfxDiscSpec sigil = VfxDiscSpec.At(above, 0.4f, cast.Colors);
            sigil.Life = 2.2f;
            sigil.Rune = true;
            sigil.FaceCamera = true;
            sigil.Body = 0.1f;
            sigil.Rim = 0f;
            sigil.Spin = 0.8f;
            VfxHandle<VfxDisc> disc = fx.Disc(sigil);
            if (body != null)
            {
                disc.Get?.Follow(VfxAnchor.To(body, Vector3.Up * 2.2f));
            }
        }

        if (plan.Shell && body != null)
        {
            VfxShellSpec shell = VfxShellSpec.Sphere(body.GlobalPosition + Vector3.Up, 0.95f, cast.Colors);
            shell.Fresnel = true;
            shell.Life = 1.4f;
            shell.Opacity = 0.7f;
            fx.Shell(shell).Get?.Follow(VfxAnchor.To(body, Vector3.Up));
        }
    }
}
