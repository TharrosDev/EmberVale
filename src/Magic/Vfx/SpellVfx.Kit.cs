using Embervale.Combat;
using Godot;

namespace Embervale.Magic.Vfx;

/// <summary>
/// The kit: what each school's blast is made of beyond the flags of its recipe, and the richer
/// building pieces a special case can call for. This is the layer that makes a fireball fire and
/// explosions and a lightning strike lightning and explosions, and that makes the tiers differ.
///
/// <para><b>HOW A RECIPE GETS THIS FOR FREE.</b> Nothing here needs a new <see cref="VfxStage"/> flag.
/// The generic interpreter calls <see cref="Signature"/> from <c>Blast</c> for every impact, burst
/// and self-cast, authored or not, and it adds by school and by tier
/// (<see cref="VfxRichness"/>, read through <c>VfxQuality.Rich</c>):</para>
/// <list type="bullet">
/// <item><b>Fire</b>: a billow of flame puffs that cool to rising smoke (<see cref="VfxEmitter.Flame"/>),
/// debris off the ground (<see cref="VfxEmitter.Chunks"/>), long-lived flying embers, and for a large
/// blast a column of smoke.</item>
/// <item><b>Frost</b>: thrown ice crystals (<see cref="VfxEmitter.Crystals"/>), cold mist rolling out
/// along the floor (<see cref="VfxEmitter.Mist"/>), glints.</item>
/// <item><b>Lightning</b>: forked bolts thrown from the centre to the ground around it
/// (<see cref="Forks"/>), debris, a wisp of smoke.</item>
/// <item><b>Arcane</b>: glints and, on the top tier, a second scatter of sparks.</item>
/// <item><b>Nature</b>: glints of pollen, and earth thrown up.</item>
/// <item><b>Necrotic</b>: soul-fire that cools to dark smoke.</item>
/// </list>
/// On the Performance tier none of it is drawn: a blast there is its flare, its ring and a few
/// particles.
///
/// <para><b>PIECES A SPECIAL CAN CALL</b> (all take the <see cref="VfxCast"/> a hook is handed, are
/// safe at any tier and distance, and are one-shots that end themselves):</para>
/// <list type="bullet">
/// <item><see cref="Explosion"/>: the whole school blast at a point (flare, rays, ring, ball,
/// signature, particles). The generic <c>Blast</c> with a plan built for you.</item>
/// <item><see cref="FireBillow"/>: flame puffs that turn to smoke.</item>
/// <item><see cref="SmokeColumn"/>: smoke rising from a point for a while, then stopping by itself.</item>
/// <item><see cref="Debris"/>: dark chunks thrown up off the ground.</item>
/// <item><see cref="ShardBurst"/>: ice crystals thrown point first, with glints.</item>
/// <item><see cref="GroundMist"/>: cold mist rolling out along the floor.</item>
/// <item><see cref="Forks"/>: forked lightning from a point to the ground around it.</item>
/// <item><see cref="FrostSpread"/>: frost creeping outward across the floor from a point.</item>
/// </list>
/// New block capabilities these rest on, all with defaults that leave older code as it was:
/// <c>VfxFlareSpec.Rays</c> and <c>RingStreaks</c>; <c>VfxShellSpec.Ball</c>, <c>IceWall</c>,
/// <c>IceShell</c> (and <c>Ragged</c>, <c>Cools</c>, <c>Ice</c>); <c>VfxDiscSpec.Pattern</c> and
/// <c>SpreadSeconds</c>; <c>VfxBoltSpec.Electric</c>; <c>VfxBurstSpec.Flatness</c> and
/// <c>StreamSeconds</c>; and <c>cast.Fx.Burst(VfxEmitter, spec)</c>.
/// </summary>
public static partial class SpellVfx
{
    /// <summary>The smallest blast that gets its school's extras: below this it is a spark.</summary>
    private const float SignatureRadius = 0.6f;

    /// <summary>
    /// The whole school blast at <paramref name="centre"/> over <paramref name="radius"/> metres,
    /// for a special case that wants an explosion where the generic interpreter draws none.
    /// <paramref name="groundY"/> is the floor's height when known (NaN when not).
    /// </summary>
    internal static void Explosion(in VfxCast cast, Vector3 centre, float radius, float groundY = float.NaN)
    {
        var stage = new VfxStage
        {
            Flare = true,
            Ring = true,
            Particles = SpellVfxCatalog.SchoolParticles(cast.School),
            Secondary = VfxRecipeRules.SchoolSecondary(cast.School),
            Mark = SpellVfxCatalog.SchoolMark(cast.School),
            Distortion = radius >= VfxRecipeRules.DistortionRadius,
        };
        Blast(cast, cast.PlanOf(stage, radius), centre, radius, Vector3.Zero, groundY, hitsPlayer: false);
    }

    /// <summary>Flame puffs that swell, cool and end as smoke. <paramref name="size"/> is the
    /// radius of the fire, metres; <paramref name="amount"/> scales how many.</summary>
    internal static VfxHandle<VfxBurst> FireBillow(
        in VfxCast cast, Vector3 centre, float size, float amount = 1f, bool rising = true)
    {
        if (!VfxQuality.Rich.Billow)
        {
            return default;
        }

        VfxBurstSpec billow = VfxBurstSpec.At(
            centre, cast.Colors, VfxQuality.Budget.ParticleMultiplier * amount * Mathf.Clamp(0.5f + (size * 0.35f), 0.5f, 2.2f));
        billow.SizeScale = Mathf.Clamp(size * 0.6f, 0.4f, 2.6f);
        billow.Extents = Vector3.One * (size * 0.25f);
        billow.SpeedScale = Mathf.Clamp(0.5f + (size * 0.3f), 0.5f, 2.2f);
        billow.LifeScale = Mathf.Clamp(0.8f + (size * 0.1f), 0.8f, 1.5f) * VfxQuality.Rich.LifeScale;
        if (rising)
        {
            billow.Direction = Vector3.Up;
            billow.Spread = 75f;
        }

        return cast.Fx.Burst(VfxEmitter.Flame, billow);
    }

    /// <summary>Smoke rising from <paramref name="foot"/> for <paramref name="seconds"/>, then
    /// stopping by itself. Drawn where the tier has smoke columns.</summary>
    internal static VfxHandle<VfxBurst> SmokeColumn(in VfxCast cast, Vector3 foot, float size, float seconds = 1.1f)
    {
        VfxBudget budget = VfxQuality.Budget;
        if (!VfxQuality.Rich.SmokeColumn || !budget.SecondaryDebris)
        {
            return default;
        }

        VfxBurstSpec column = VfxBurstSpec.At(foot, cast.Colors, budget.ParticleMultiplier * budget.DebrisMultiplier * 0.7f);
        column.Continuous = true;
        column.StreamSeconds = seconds;
        column.Extents = new Vector3(size * 0.3f, 0.1f, size * 0.3f);
        column.Direction = Vector3.Up;
        column.Spread = 16f;
        column.SpeedScale = 1.6f;
        column.SizeScale = Mathf.Clamp(size * 0.5f, 0.7f, 2.4f);
        return cast.Fx.Burst(VfxParticles.Smoke, column);
    }

    /// <summary>Dark chunks thrown up off the ground at <paramref name="foot"/>.</summary>
    internal static VfxHandle<VfxBurst> Debris(in VfxCast cast, Vector3 foot, float size)
    {
        VfxRichness rich = VfxQuality.Rich;
        if (rich.DebrisLayers <= 0)
        {
            return default;
        }

        VfxBurstSpec chunks = VfxBurstSpec.At(
            foot + (Vector3.Up * 0.15f), cast.Colors,
            VfxQuality.Budget.ParticleMultiplier * rich.DebrisLayers * Mathf.Clamp(0.5f + (size * 0.25f), 0.5f, 1.8f));
        chunks.Extents = new Vector3(size * 0.3f, 0.05f, size * 0.3f);
        chunks.Direction = Vector3.Up;
        chunks.Spread = 55f;
        chunks.SpeedScale = Mathf.Clamp(0.6f + (size * 0.2f), 0.6f, 1.6f);
        chunks.SizeScale = Mathf.Clamp(0.8f + (size * 0.15f), 0.8f, 1.8f);
        return cast.Fx.Burst(VfxEmitter.Chunks, chunks);
    }

    /// <summary>Ice crystals thrown point first from <paramref name="centre"/>, with glints hanging
    /// where they were. <paramref name="up"/> throws them off a floor instead of in every direction.</summary>
    internal static void ShardBurst(in VfxCast cast, Vector3 centre, float size, bool up)
    {
        VfxRichness rich = VfxQuality.Rich;
        float multiplier = VfxQuality.Budget.ParticleMultiplier;
        if (rich.Billow)
        {
            VfxBurstSpec crystals = VfxBurstSpec.At(centre, cast.Colors, multiplier * Mathf.Clamp(0.5f + (size * 0.3f), 0.5f, 2f));
            crystals.Extents = Vector3.One * (size * 0.15f);
            crystals.SpeedScale = Mathf.Clamp(0.6f + (size * 0.3f), 0.6f, 2.4f);
            crystals.SizeScale = Mathf.Clamp(0.8f + (size * 0.12f), 0.8f, 1.7f);
            if (up)
            {
                crystals.Direction = Vector3.Up;
                crystals.Spread = 80f;
            }

            cast.Fx.Burst(VfxEmitter.Crystals, crystals);
        }

        if (rich.Glints)
        {
            VfxBurstSpec glints = VfxBurstSpec.At(centre, cast.Colors, multiplier * Mathf.Clamp(0.4f + (size * 0.25f), 0.4f, 1.6f));
            glints.Extents = Vector3.One * Mathf.Max(0.2f, size * 0.55f);
            glints.LifeScale = rich.LifeScale;
            cast.Fx.Burst(VfxEmitter.Glints, glints);
        }
    }

    /// <summary>Cold mist rolling out along the floor from <paramref name="foot"/>.</summary>
    internal static VfxHandle<VfxBurst> GroundMist(in VfxCast cast, Vector3 foot, float size, float amount = 1f)
    {
        if (!VfxQuality.Rich.Billow)
        {
            return default;
        }

        VfxBurstSpec mist = VfxBurstSpec.At(
            foot + (Vector3.Up * 0.25f), cast.Colors,
            VfxQuality.Budget.ParticleMultiplier * amount * Mathf.Clamp(0.4f + (size * 0.3f), 0.4f, 1.8f));
        mist.Extents = new Vector3(size * 0.45f, 0.08f, size * 0.45f);
        mist.Direction = Vector3.Right;
        mist.Spread = 180f;
        mist.Flatness = 1f;
        mist.SpeedScale = Mathf.Clamp(0.6f + (size * 0.25f), 0.6f, 2f);
        mist.SizeScale = Mathf.Clamp(size * 0.4f, 0.6f, 2.2f);
        mist.LifeScale = VfxQuality.Rich.LifeScale;
        return cast.Fx.Burst(VfxEmitter.Mist, mist);
    }

    /// <summary>
    /// Forked lightning from <paramref name="centre"/> to points around it, <paramref name="reach"/>
    /// metres out: on the floor at <paramref name="groundY"/> when it is known, in every direction
    /// when it is NaN. As many forks as the tier draws strands, plus <paramref name="extra"/>.
    /// </summary>
    internal static void Forks(in VfxCast cast, Vector3 centre, float reach, float groundY, int extra = 0)
    {
        if (!cast.Fx.Full || _director is not { } director)
        {
            return;
        }

        VfxBudget budget = VfxQuality.Budget;
        int forks = Mathf.Clamp(VfxQuality.Rich.BoltStrands + extra, 1, 6);
        uint state = unchecked((uint)director.NextSeed());
        float turn = VfxBoltPath.Unit(ref state) * Mathf.Tau;
        bool grounded = !float.IsNaN(groundY);
        for (int i = 0; i < forks; i++)
        {
            float angle = turn + (Mathf.Tau * i / forks) + (VfxBoltPath.Signed(ref state) * 0.5f);
            float far = reach * (0.55f + (0.45f * VfxBoltPath.Unit(ref state)));
            Vector3 to = grounded
                ? new Vector3(centre.X + (Mathf.Cos(angle) * far), groundY + 0.05f, centre.Z + (Mathf.Sin(angle) * far))
                : centre + (new Vector3(Mathf.Cos(angle), VfxBoltPath.Signed(ref state) * 0.7f, Mathf.Sin(angle)).Normalized() * far);
            cast.Fx.Bolt(new VfxBoltSpec
            {
                Mode = VfxBoltMode.Lightning,
                From = centre,
                To = to,
                Colors = cast.Colors,
                Width = 0.07f,
                Jitter = 0.16f,
                Segments = VfxRecipeRules.BoltSegments(budget, centre.DistanceTo(to)),
                Branches = budget.BoltBranches,
                Life = 0.16f + (0.05f * i),
                Seed = director.NextSeed(),
            });
        }
    }

    /// <summary>Frost creeping outward across the floor from <paramref name="foot"/> to
    /// <paramref name="radius"/> metres over <paramref name="seconds"/>, then fading.</summary>
    internal static VfxHandle<VfxDisc> FrostSpread(in VfxCast cast, Vector3 foot, float radius, float seconds = 0.7f)
    {
        VfxDiscSpec frost = VfxDiscSpec.At(foot, Mathf.Max(0.5f, radius), cast.Colors);
        frost.Pattern = VfxDiscPattern.Frost;
        frost.SpreadSeconds = Mathf.Max(0.2f, seconds);
        frost.Life = frost.SpreadSeconds * 2.4f;
        frost.Body = 0.25f;
        frost.Rim = 0f;
        frost.Flow = 0.15f;
        return cast.Fx.Disc(frost);
    }

    /// <summary>
    /// A fireball going off: the layered explosion a fire spell lands with. Every tier gets the
    /// short white-hot core with its pulse of light, the shock ring, a burst of large flame puffs,
    /// thrown sparks and a patch of glowing cracks left on the floor, which is a handful of draws.
    /// Medium and up add the eroding ball of fire that cools to smoke, a billow that rises, smoke
    /// rolling up after it, flying embers, debris and a scorch; High and up leave small flames
    /// burning on the ground and bend the air. <paramref name="radius"/> is the size of the fire
    /// (not a damage radius: nothing here is read back). <paramref name="groundY"/> is the floor
    /// under it, NaN when it went off in the air.
    /// </summary>
    internal static void FireBlast(in VfxCast cast, Vector3 centre, float radius, float groundY = float.NaN)
    {
        VfxSpawner fx = cast.Fx;
        VfxBudget budget = VfxQuality.Budget;
        VfxRichness rich = VfxQuality.Rich;
        VfxTier tier = VfxQuality.Tier;
        radius = Mathf.Clamp(radius, 0.6f, 6f);
        bool grounded = !float.IsNaN(groundY) && centre.Y - groundY <= Mathf.Max(2f, radius);
        var floor = new Vector3(centre.X, grounded ? groundY : centre.Y - (radius * 0.5f), centre.Z);
        VfxSchoolColors flame = Flame(cast.Colors);
        float density = budget.ParticleMultiplier;
        float amount = Mathf.Clamp(0.6f + (radius * 0.3f), 0.6f, 2f);

        BlastCore(cast, centre, radius * 0.4f, Mathf.Max(5f, radius * 3.5f));
        Vector3 facing = grounded || _director is not { HasCamera: true } eye ? default : eye.CameraPosition - centre;
        ThinRing(cast, grounded ? floor + (Vector3.Up * 0.12f) : centre, radius * 1.25f, 0.36f, facing, energy: 1.7f);

        // The body of the blast on every tier: a few large puffs of flame, at once. Below Medium
        // this is all the fire there is, so it is never thinned under about ten puffs.
        VfxBurstSpec puffs = VfxBurstSpec.At(centre, flame, Mathf.Max(0.6f, density * amount));
        puffs.Extents = Vector3.One * (radius * 0.22f);
        puffs.SizeScale = Mathf.Clamp(radius * 0.62f, 0.6f, 2.8f);
        puffs.SpeedScale = Mathf.Clamp(0.6f + (radius * 0.35f), 0.6f, 2.4f);
        puffs.LifeScale = rich.Billow ? 1.25f * rich.LifeScale : 0.6f;
        fx.Burst(VfxEmitter.Flame, puffs);

        VfxBurstSpec sparks = VfxBurstSpec.At(centre, cast.Colors, density * amount);
        sparks.SpeedScale = Mathf.Clamp(0.9f + (radius * 0.3f), 0.9f, 2.4f);
        if (grounded)
        {
            sparks.Direction = Vector3.Up;
            sparks.Spread = 80f;
        }

        fx.Burst(VfxParticles.Sparks, sparks);

        if (grounded)
        {
            // What is left when the fire has gone: the ground cracked and glowing. Drawn on every
            // tier (the two leanest have no scorch decals), and it outlasts everything else here.
            VfxDiscSpec cracks = VfxDiscSpec.At(floor, radius * 0.85f, cast.Colors);
            cracks.Pattern = VfxDiscPattern.Cracks;
            cracks.Life = rich.Billow ? 3.2f : 1.5f;
            cracks.Body = 0.12f;
            cracks.Rim = 0f;
            cracks.Flow = 0.1f;
            cracks.Energy = 0.9f;
            fx.Disc(cracks);
        }

        if (!rich.Billow || !fx.Full)
        {
            return;
        }

        VfxShellSpec ball = VfxShellSpec.Ball(centre, radius * 0.62f, flame, cools: true);
        ball.Life = Mathf.Clamp(0.5f + (radius * 0.06f), 0.5f, 0.8f) * rich.LifeScale;
        fx.Shell(ball);

        FireBillow(cast, centre + (Vector3.Up * (radius * 0.25f)), radius, 1.2f, rising: true);
        if (budget.SecondaryDebris)
        {
            // Smoke rolling up out of it, for a second and a half after the flame is gone.
            VfxBurstSpec smoke = VfxBurstSpec.At(
                centre + (Vector3.Up * (radius * 0.3f)), cast.Colors,
                density * budget.DebrisMultiplier * Mathf.Clamp(0.5f + (radius * 0.2f), 0.5f, 1.2f));
            smoke.Extents = Vector3.One * (radius * 0.3f);
            smoke.Direction = Vector3.Up;
            smoke.Spread = 40f;
            smoke.SpeedScale = 1.3f;
            smoke.SizeScale = Mathf.Clamp(radius * 0.5f, 0.6f, 2.2f);
            fx.Burst(VfxParticles.Smoke, smoke);

            VfxBurstSpec embers = VfxBurstSpec.At(centre, cast.Colors, density * amount);
            embers.SpeedScale = Mathf.Clamp(1.4f + (radius * 0.4f), 1.4f, 3.4f);
            embers.LifeScale = 1.7f;
            embers.GravityScale = 0.3f;
            embers.Damping = 0.8f;
            fx.Burst(VfxParticles.Embers, embers);
        }

        if (grounded)
        {
            Debris(cast, floor, radius);
            if (radius >= 2f)
            {
                SmokeColumn(cast, floor, radius * 0.8f, 1.6f);
            }

            if (budget.GroundMarks > 0)
            {
                fx.Mark(new VfxGroundMarkSpec
                {
                    Mark = VfxMark.Scorch,
                    Position = floor,
                    Size = radius * 1.9f,
                    Colors = cast.Colors,
                    Life = 8f + radius,
                    Reach = Mathf.Clamp(radius * 0.15f, 0.5f, 0.9f),
                });
            }

            if (tier >= VfxTier.High)
            {
                // Small flames left burning on the scorch.
                VfxBurstSpec burning = VfxBurstSpec.At(floor + (Vector3.Up * 0.15f), flame, density * 0.35f);
                burning.Continuous = true;
                burning.StreamSeconds = 2.2f;
                burning.Extents = new Vector3(radius * 0.4f, 0.05f, radius * 0.4f);
                burning.Direction = Vector3.Up;
                burning.Spread = 20f;
                burning.SizeScale = 0.45f;
                burning.SpeedScale = 0.6f;
                burning.LifeScale = 0.7f;
                fx.Burst(VfxEmitter.Flame, burning);
            }
        }

        if (budget.Distortion && !VfxQuality.ReducedMotion && radius >= 1.5f)
        {
            fx.Distortion(new VfxDistortionSpec
            {
                Position = centre,
                Radius = radius * 1.3f,
                Life = 0.4f,
                Strength = Mathf.Clamp(0.026f + (radius * 0.004f), 0.026f, 0.05f),
            });
        }
    }

    /// <summary>
    /// A lightning strike going off: the flash and its light, a thunderclap ring along the ground,
    /// thick forked bolts grounding into the floor around it and a shower of sparks, on every tier
    /// (two or three plain forks on the leanest). Medium and up add more forks with branches,
    /// debris, a wisp of smoke, cracks left glowing on the floor and a scorch.
    /// </summary>
    internal static void LightningBlast(in VfxCast cast, Vector3 centre, float radius, float groundY = float.NaN)
    {
        VfxSpawner fx = cast.Fx;
        VfxBudget budget = VfxQuality.Budget;
        VfxTier tier = VfxQuality.Tier;
        radius = Mathf.Clamp(radius, 0.8f, 5f);
        bool grounded = !float.IsNaN(groundY) && centre.Y - groundY <= Mathf.Max(2.5f, radius);
        float drop = grounded ? Mathf.Max(0.3f, centre.Y - groundY - 0.05f) : radius * 0.6f;
        var floor = new Vector3(centre.X, centre.Y - drop, centre.Z);

        BlastCore(cast, centre, radius * 0.4f, Mathf.Max(6f, radius * 4f));
        ThinRing(cast, floor + (Vector3.Up * 0.12f), radius * 1.2f, 0.3f, energy: 1.6f);

        int forks = tier switch
        {
            VfxTier.Performance => 2,
            VfxTier.Low => 3,
            VfxTier.Medium => 5,
            VfxTier.High => 7,
            _ => 9,
        };
        GroundForks(cast, centre, forks, radius, drop, 0.055f, budget.BoltBranches);

        VfxBurstSpec sparks = VfxBurstSpec.At(
            centre, cast.Colors, budget.ParticleMultiplier * Mathf.Clamp(0.7f + (radius * 0.3f), 0.7f, 2f));
        sparks.SpeedScale = Mathf.Clamp(1f + (radius * 0.25f), 1f, 2f);
        fx.Burst(VfxParticles.Sparks, sparks);

        if (grounded)
        {
            VfxDiscSpec cracks = VfxDiscSpec.At(floor, radius * 0.8f, cast.Colors);
            cracks.Pattern = VfxDiscPattern.Cracks;
            cracks.Life = budget.SecondaryDebris ? 1.8f : 1f;
            cracks.Body = 0.1f;
            cracks.Rim = 0f;
            cracks.Energy = 1.2f;
            fx.Disc(cracks);
        }

        if (!budget.SecondaryDebris || !fx.Full)
        {
            return;
        }

        if (grounded)
        {
            Debris(cast, floor, radius * 0.8f);
            if (budget.GroundMarks > 0)
            {
                fx.Mark(new VfxGroundMarkSpec
                {
                    Mark = VfxMark.Scorch,
                    Position = floor,
                    Size = radius * 1.5f,
                    Colors = cast.Colors,
                    Life = 7f,
                    Reach = 0.6f,
                });
            }
        }

        VfxBurstSpec smoke = VfxBurstSpec.At(
            floor + (Vector3.Up * 0.3f), cast.Colors, budget.ParticleMultiplier * budget.DebrisMultiplier * 0.35f);
        smoke.SizeScale = Mathf.Clamp(radius * 0.4f, 0.5f, 1.2f);
        smoke.SpeedScale = 0.6f;
        fx.Burst(VfxParticles.Smoke, smoke);

        if (tier >= VfxTier.High)
        {
            // A second, slower clap rolling out past the first.
            ThinRing(cast, floor + (Vector3.Up * 0.12f), radius * 1.7f, 0.55f, energy: 0.7f);
        }
    }

    /// <summary>The pattern a school draws on the ground under a telegraph or a zone.</summary>
    private static VfxDiscPattern SchoolPattern(DamageType school) => school switch
    {
        DamageType.Fire or DamageType.Lightning => VfxDiscPattern.Cracks,
        DamageType.Frost => VfxDiscPattern.Frost,
        DamageType.Arcane => VfxDiscPattern.Rune,
        DamageType.Nature => VfxDiscPattern.Roots,
        _ => VfxDiscPattern.None,
    };

    /// <summary>Whether a school's fire turns to smoke as it burns out.</summary>
    private static bool SchoolSmokes(DamageType school) => school is DamageType.Fire or DamageType.Necrotic;

    /// <summary>
    /// What a school's blast is made of beyond its recipe's flags. Called by <c>Blast</c> for every
    /// blast of <see cref="SignatureRadius"/> or more that has a flare; draws nothing on the leanest
    /// tier and more with each tier above it. <paramref name="body"/> is the size of the fire
    /// (already scaled for a meteor), <paramref name="radius"/> the true reach.
    /// </summary>
    private static void Signature(
        in VfxCast cast, in VfxPlan plan, Vector3 centre, Vector3 floor, float radius, float body, bool grounded)
    {
        if (!cast.Fx.Full || radius < SignatureRadius || plan.Inward)
        {
            return;
        }

        VfxRichness rich = VfxQuality.Rich;
        VfxBudget budget = VfxQuality.Budget;
        bool large = radius >= 2f;
        switch (cast.School)
        {
            case DamageType.Fire:
                FireBillow(cast, centre, body, 1f, rising: true);
                if (grounded && radius >= 1.2f)
                {
                    Debris(cast, floor, radius);
                }

                if (large && grounded)
                {
                    SmokeColumn(cast, floor, radius);
                }

                if (rich.DebrisLayers >= 2)
                {
                    // Embers that fly far and hang: the last thing left of a fireball.
                    VfxBurstSpec embers = VfxBurstSpec.At(centre, cast.Colors, budget.ParticleMultiplier * Mathf.Clamp(0.4f + (radius * 0.2f), 0.4f, 1.4f));
                    embers.SpeedScale = Mathf.Clamp(1.2f + (radius * 0.35f), 1.2f, 3.2f);
                    embers.LifeScale = 1.8f;
                    embers.GravityScale = 0.4f;
                    embers.Damping = 0.7f;
                    cast.Fx.Burst(VfxParticles.Embers, embers);
                }

                break;

            case DamageType.Frost:
                ShardBurst(cast, centre, radius, grounded);
                if (grounded)
                {
                    GroundMist(cast, floor, radius);
                    if (rich.Billow && radius >= 1.5f)
                    {
                        FrostSpread(cast, floor, radius, 0.5f + (radius * 0.08f));
                    }
                }

                break;

            case DamageType.Lightning:
                if (rich.Rays || large)
                {
                    Forks(cast, centre, Mathf.Max(1.2f, radius), grounded ? floor.Y : float.NaN, large ? 1 : 0);
                }

                if (grounded && radius >= 1.5f)
                {
                    Debris(cast, floor, radius * 0.7f);
                }

                if (large && grounded)
                {
                    SmokeColumn(cast, floor, radius * 0.6f, 0.6f);
                }

                break;

            case DamageType.Arcane:
                if (rich.Glints)
                {
                    VfxBurstSpec glints = VfxBurstSpec.At(centre, cast.Colors, budget.ParticleMultiplier * Mathf.Clamp(0.5f + (radius * 0.3f), 0.5f, 1.8f));
                    glints.Extents = Vector3.One * (radius * 0.6f);
                    glints.LifeScale = rich.LifeScale;
                    cast.Fx.Burst(VfxEmitter.Glints, glints);
                }

                if (rich.DebrisLayers >= 2 && plan.Particles != VfxParticles.Sparks && plan.Secondary != VfxParticles.Sparks)
                {
                    VfxBurstSpec sparks = VfxBurstSpec.At(centre, cast.Colors, budget.ParticleMultiplier * 0.6f);
                    sparks.SpeedScale = Mathf.Clamp(0.7f + (radius * 0.3f), 0.7f, 2.4f);
                    cast.Fx.Burst(VfxParticles.Sparks, sparks);
                }

                break;

            case DamageType.Nature:
                if (rich.Glints)
                {
                    VfxBurstSpec pollen = VfxBurstSpec.At(centre, cast.Colors, budget.ParticleMultiplier * 0.6f);
                    pollen.Extents = Vector3.One * (radius * 0.6f);
                    pollen.GravityScale = -0.6f;
                    pollen.LifeScale = rich.LifeScale;
                    cast.Fx.Burst(VfxEmitter.Glints, pollen);
                }

                if (grounded && radius >= 1.5f)
                {
                    Debris(cast, floor, radius * 0.6f);
                }

                break;

            case DamageType.Necrotic:
                FireBillow(cast, centre, body * 0.8f, 0.7f, rising: true);
                if (large && grounded)
                {
                    SmokeColumn(cast, floor, radius * 0.7f, 0.8f);
                }

                break;
        }
    }
}
