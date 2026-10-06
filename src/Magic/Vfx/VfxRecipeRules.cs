using System;
using Embervale.Combat;

namespace Embervale.Magic.Vfx;

/// <summary>Which beat of a spell a stage is being read for.</summary>
public enum VfxRole
{
    /// <summary>The wind-up aura and the release flash at the hand.</summary>
    Cast,

    /// <summary>A bolt in flight, a cone in the air.</summary>
    Travel,

    /// <summary>A hit on one target, or on the world.</summary>
    Impact,

    /// <summary>A blast over a radius.</summary>
    Burst,

    /// <summary>What stays: a zone, a wall, the smoke after a blast.</summary>
    Linger,
}

/// <summary>What the interpreter knows about a spell besides its recipe.</summary>
/// <param name="School">The spell's school.</param>
/// <param name="Delivery">How it is delivered.</param>
/// <param name="Weight">Its <c>ImpactWeight</c> (0..1): how heavy a hit should feel.</param>
/// <param name="Radius">The radius of the burst being drawn, 0 for a single-target hit.</param>
/// <param name="Authored">Whether the recipe is the spell's own. An authored recipe is drawn exactly
/// as written; only a fallback is filled out by <see cref="VfxRecipeRules.Enrich"/>.</param>
public readonly record struct VfxTraits(
    DamageType School, SpellDelivery Delivery, float Weight, float Radius, bool Authored);

/// <summary>
/// One beat of an effect, after the tier, the distance and the comfort settings have had their say:
/// exactly the blocks to build. Everything that decides <em>whether</em> a block is drawn is in
/// <see cref="VfxRecipeRules.Plan"/>, so <c>SpellVfx</c> only has to build what a plan lists.
/// </summary>
/// <param name="Flare">A core flash and its halo.</param>
/// <param name="Light">The flare may light the scene, if the light budget has one free.</param>
/// <param name="Ring">An expanding shock ring.</param>
/// <param name="Inward">The ring and the particles converge instead of leaving.</param>
/// <param name="Particles">The first particle layer.</param>
/// <param name="Density">Its amount against the preset's (the tier's particle multiplier).</param>
/// <param name="Secondary">The second layer (smoke, debris), where the tier draws one.</param>
/// <param name="SecondaryDensity">Its amount against the preset's.</param>
/// <param name="Bolt">A lightning ribbon.</param>
/// <param name="Distortion">A refraction shell.</param>
/// <param name="Mark">A mark left on the ground.</param>
/// <param name="ScreenFlash">A screen flash (still capped by <see cref="VfxScreenRules"/>).</param>
/// <param name="Shell">A fresnel shell.</param>
/// <param name="Sigil">A flat sigil.</param>
/// <param name="Tether">Something returns to the caster.</param>
/// <param name="Fireball">A body of flowing noise inside the flare: the ball of a blast.</param>
/// <param name="Scale">Size against the school's ordinary beat.</param>
public readonly record struct VfxPlan(
    bool Flare,
    bool Light,
    bool Ring,
    bool Inward,
    VfxParticles Particles,
    float Density,
    VfxParticles Secondary,
    float SecondaryDensity,
    bool Bolt,
    bool Distortion,
    VfxMark Mark,
    bool ScreenFlash,
    bool Shell,
    bool Sigil,
    bool Tether,
    bool Fireball,
    float Scale)
{
    /// <summary>A beat that draws nothing.</summary>
    public static readonly VfxPlan Nothing = new(
        false, false, false, false, VfxParticles.None, 0f, VfxParticles.None, 0f, false, false, VfxMark.None,
        false, false, false, false, false, 1f);

    public bool IsEmpty =>
        !Flare && !Ring && Particles == VfxParticles.None && Secondary == VfxParticles.None && !Bolt &&
        !Distortion && Mark == VfxMark.None && !ScreenFlash && !Shell && !Sigil && !Tether && !Fireball;
}

/// <summary>
/// The recipe interpreter's decisions. Two steps, both pure:
///
/// <para><see cref="Enrich"/> fills out a <em>fallback</em> stage. The fallback table in
/// <see cref="SpellVfxCatalog"/> is deliberately spare (it only promises that no spell is invisible);
/// this is where a spell nobody authored still gets a shock ring, smoke under its embers, a mark on
/// the floor and, when it is heavy enough, a pressure wave and a screen flash. An authored recipe is
/// never touched: what its author flagged is what is drawn.</para>
///
/// <para><see cref="Plan"/> then applies the budget. The tier removes distortion, ground marks and
/// secondary debris; the distance removes everything but the flare (and the bolt, which is the whole
/// of a lightning effect); Reduced Motion removes distortion; and a screen flash survives only for
/// the player's own spell or one that struck the player.</para>
/// </summary>
public static class VfxRecipeRules
{
    /// <summary>Seconds a bolt's picture takes to settle from the casting hand onto its true path.</summary>
    public const float HandSettleSeconds = 0.25f;

    /// <summary>The weight at which a single-target hit earns a shock ring.</summary>
    public const float RingWeight = 0.45f;

    /// <summary>The weight at which a hit earns a pressure wave.</summary>
    public const float DistortionWeight = 0.7f;

    /// <summary>The weight at which a hit earns a screen flash.</summary>
    public const float ScreenFlashWeight = 0.9f;

    /// <summary>The burst radius (metres) at which a blast earns a pressure wave.</summary>
    public const float DistortionRadius = 2.5f;

    /// <summary>The burst radius (metres) at which a blast earns a screen flash.</summary>
    public const float ScreenFlashRadius = 6f;

    /// <summary>The smallest blast that gets a fireball body.</summary>
    public const float FireballRadius = 1.2f;

    /// <summary>What a school leaves hanging in the air after its first particles.</summary>
    public static VfxParticles SchoolSecondary(DamageType school) => school switch
    {
        DamageType.Fire => VfxParticles.Smoke,
        DamageType.Frost => VfxParticles.Motes,
        DamageType.Lightning => VfxParticles.Smoke,
        DamageType.Arcane => VfxParticles.Sparks,
        DamageType.Nature => VfxParticles.Motes,
        DamageType.Necrotic => VfxParticles.Smoke,
        _ => VfxParticles.Smoke,
    };

    /// <summary>Whether a school's blast has a body of fire in it (rather than light alone).</summary>
    public static bool SchoolFireball(DamageType school) =>
        school is DamageType.Fire or DamageType.Necrotic or DamageType.Arcane or DamageType.Frost;

    /// <summary>A fallback stage, filled out for its role. An authored stage is returned unchanged.</summary>
    public static VfxStage Enrich(VfxStage stage, VfxRole role, in VfxTraits traits)
    {
        if (traits.Authored)
        {
            return stage;
        }

        VfxParticles school = SpellVfxCatalog.SchoolParticles(traits.School);
        VfxParticles primary = stage.Particles == VfxParticles.None ? school : stage.Particles;
        VfxParticles secondary = stage.Secondary == VfxParticles.None ? SchoolSecondary(traits.School) : stage.Secondary;
        VfxMark mark = stage.Mark == VfxMark.None ? SpellVfxCatalog.SchoolMark(traits.School) : stage.Mark;
        bool heavy = traits.Weight >= DistortionWeight;

        switch (role)
        {
            case VfxRole.Cast:
                return stage with
                {
                    Flare = true,
                    Particles = primary,
                    Sigil = stage.Sigil || traits.School == DamageType.Arcane,
                    Inward = stage.Inward || traits.Delivery is SpellDelivery.Self,
                };

            case VfxRole.Travel:
                return stage with
                {
                    Flare = true,
                    Particles = primary,
                    Bolt = stage.Bolt || traits.School == DamageType.Lightning,
                };

            case VfxRole.Impact:
                return stage with
                {
                    Flare = true,
                    Particles = primary,
                    Secondary = secondary,
                    Ring = stage.Ring || traits.Weight >= RingWeight || traits.Delivery == SpellDelivery.Self,
                    Mark = mark,
                    Distortion = stage.Distortion || heavy,
                    ScreenFlash = stage.ScreenFlash || traits.Weight >= ScreenFlashWeight,
                };

            case VfxRole.Burst:
                return stage with
                {
                    Flare = true,
                    Ring = true,
                    Particles = primary,
                    Secondary = secondary,
                    Mark = mark,
                    Distortion = stage.Distortion || traits.Radius >= DistortionRadius || heavy,
                    ScreenFlash = stage.ScreenFlash || traits.Radius >= ScreenFlashRadius ||
                                  traits.Weight >= ScreenFlashWeight,
                };

            default:
                return stage with
                {
                    Flare = true,
                    Ring = true,
                    Particles = primary,
                    Secondary = stage.Secondary,
                    Mark = mark,
                };
        }
    }

    /// <summary>
    /// The blocks one beat is built from, at this tier, this far away and under these comfort
    /// settings. <paramref name="radius"/> is the burst radius (0 for a single hit) and
    /// <paramref name="school"/> decides whether a blast gets a fireball body.
    /// </summary>
    public static VfxPlan Plan(
        VfxStage stage, in VfxBudget budget, VfxDetail detail, bool reducedMotion, bool byPlayer, bool hitsPlayer,
        float radius = 0f, DamageType school = DamageType.Fire)
    {
        if (detail == VfxDetail.None || stage.IsEmpty)
        {
            return VfxPlan.Nothing;
        }

        float scale = stage.Scale <= 0f ? 1f : stage.Scale;
        if (detail == VfxDetail.FlareOnly)
        {
            // Too far for the detail to read: the flash and the bolt are the whole effect.
            return VfxPlan.Nothing with { Flare = stage.Flare || stage.Ring, Bolt = stage.Bolt, Scale = scale };
        }

        bool debris = budget.SecondaryDebris && stage.Secondary != VfxParticles.None;
        return new VfxPlan(
            Flare: stage.Flare,
            Light: stage.Flare && budget.MaxLights > 0,
            Ring: stage.Ring,
            Inward: stage.Inward,
            Particles: stage.Particles,
            Density: stage.Particles == VfxParticles.None ? 0f : budget.ParticleMultiplier,
            Secondary: debris ? stage.Secondary : VfxParticles.None,
            SecondaryDensity: debris ? budget.ParticleMultiplier * budget.DebrisMultiplier : 0f,
            Bolt: stage.Bolt,
            Distortion: stage.Distortion && budget.Distortion && !reducedMotion,
            Mark: budget.GroundMarks > 0 ? stage.Mark : VfxMark.None,
            ScreenFlash: stage.ScreenFlash && (byPlayer || hitsPlayer),
            Shell: stage.Shell,
            Sigil: stage.Sigil,
            Tether: stage.Tether,
            Fireball: stage.Flare && budget.SecondaryDebris && radius >= FireballRadius && SchoolFireball(school),
            Scale: scale);
    }

    /// <summary>Seconds of trail a projectile draws at a tier. 0 = the core and halo alone.</summary>
    public static float TrailSeconds(VfxTrail trail) => trail switch
    {
        VfxTrail.CoreAndHalo => 0f,
        VfxTrail.Short => 0.12f,
        VfxTrail.Normal => 0.22f,
        VfxTrail.Long => 0.36f,
        VfxTrail.LongWithSparks => 0.36f,
        _ => 0f,
    };

    /// <summary>Whether a projectile sheds particles behind it at a tier.</summary>
    public static bool TrailParticles(VfxTrail trail) => trail >= VfxTrail.Normal;

    /// <summary>Whether a projectile also sheds sparks (the top tier).</summary>
    public static bool TrailSparks(VfxTrail trail) => trail == VfxTrail.LongWithSparks;

    /// <summary>
    /// How far the bolt's picture still sits from its true path: 1 at launch (at the hand), 0 once
    /// <see cref="HandSettleSeconds"/> have passed. Eased out, so it leaves the hand at speed and
    /// settles gently.
    /// </summary>
    public static float HandOffset(double age) => SettleLeft(age, HandSettleSeconds, accelerate: false);

    /// <summary>
    /// How much of a starting offset is still left <paramref name="age"/> seconds into a settle of
    /// <paramref name="seconds"/>: 1 at the start, 0 at the end and after. Eased out by default (it
    /// leaves fast and arrives gently, a bolt leaving the hand); <paramref name="accelerate"/> eases
    /// in instead (it starts slow and arrives fast, a meteor falling).
    /// </summary>
    public static float SettleLeft(double age, float seconds, bool accelerate)
    {
        if (age <= 0d)
        {
            return 1f;
        }

        if (seconds <= 0f || age >= seconds)
        {
            return 0f;
        }

        float t = (float)(age / seconds);
        return accelerate ? 1f - (t * t) : (1f - t) * (1f - t);
    }

    /// <summary>
    /// How big one hit is drawn against the school's ordinary one: heavier spells, fuller charges,
    /// crits and kills are larger; a guarded hit and a bolt that ran out of range are smaller.
    /// </summary>
    public static float ImpactScale(float weight, float charge, bool crit, bool killed, SpellImpactKind kind)
    {
        float scale = 0.7f + (0.6f * Math.Clamp(weight, 0f, 1f)) + (0.5f * Math.Clamp(charge, 0f, 1f));
        if (crit)
        {
            scale *= 1.2f;
        }

        if (killed)
        {
            scale *= 1.25f;
        }

        return kind switch
        {
            SpellImpactKind.Blocked => scale * 0.6f,
            SpellImpactKind.Expired => scale * 0.45f,
            _ => scale,
        };
    }

    /// <summary>
    /// Whether a landed hit is the spell's own impact or one splash of a blast that already drew
    /// its own. A cone, a nova, a ground spell and a bolt that bursts all draw one
    /// <c>SpellVfx.Burst</c> or <c>Cone</c> and then hit everyone in it, and a wall burns whoever stands
    /// in it again and again: those per-target hits are small flashes, not a second explosion each.
    /// </summary>
    public static bool IsSplash(SpellDelivery delivery, float impactRadius) =>
        delivery is SpellDelivery.Area or SpellDelivery.Cone or SpellDelivery.Ground or SpellDelivery.Dash
            or SpellDelivery.Barrier ||
        (delivery == SpellDelivery.Projectile && impactRadius > 0f);

    /// <summary>Segments a bolt of <paramref name="length"/> metres is drawn with: the tier's count,
    /// thinned for a short arc (a 2 m chain does not need 24 kinks) and never fewer than 3.</summary>
    public static int BoltSegments(in VfxBudget budget, float length)
    {
        int byLength = (int)MathF.Ceiling(MathF.Max(0f, length) * 2.5f);
        return Math.Clamp(Math.Min(budget.BoltSegments, byLength), 3, Math.Max(3, budget.BoltSegments));
    }
}
