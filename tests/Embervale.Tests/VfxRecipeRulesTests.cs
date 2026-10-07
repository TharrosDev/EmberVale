using System;
using Embervale.Combat;
using Embervale.Magic;
using Embervale.Magic.Vfx;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// The recipe interpreter's decisions: which blocks a beat of an effect is built from, at each tier,
/// each distance and under each comfort setting. The blocks themselves cannot be built here (they
/// are nodes); what is pinned is everything that decides whether one is built at all.
/// </summary>
public class VfxRecipeRulesTests
{
    /// <summary>A beat with every flag set, so a tier can only take things away.</summary>
    private static readonly VfxStage Everything = new()
    {
        Flare = true,
        Ring = true,
        Particles = VfxParticles.Embers,
        Secondary = VfxParticles.Smoke,
        Bolt = true,
        Distortion = true,
        Mark = VfxMark.Scorch,
        ScreenFlash = true,
        Shell = true,
        Sigil = true,
        Tether = true,
        Inward = true,
        Scale = 1.5f,
    };

    public static readonly TheoryData<VfxTier> Tiers = new()
    {
        VfxTier.Performance, VfxTier.Low, VfxTier.Medium, VfxTier.High, VfxTier.Ultra,
    };

    public static readonly TheoryData<DamageType, SpellDelivery> SchoolsAndDeliveries = Matrix();

    private static TheoryData<DamageType, SpellDelivery> Matrix()
    {
        var data = new TheoryData<DamageType, SpellDelivery>();
        foreach (DamageType school in new[]
                 {
                     DamageType.Fire, DamageType.Frost, DamageType.Lightning, DamageType.Arcane, DamageType.Nature,
                     DamageType.Necrotic,
                 })
        {
            foreach (SpellDelivery delivery in Enum.GetValues<SpellDelivery>())
            {
                data.Add(school, delivery);
            }
        }

        return data;
    }

    private static VfxPlan PlanAt(VfxTier tier, VfxStage stage, VfxDetail detail = VfxDetail.Full,
        bool reducedMotion = false, bool byPlayer = true, bool hitsPlayer = false, float radius = 3f) =>
        VfxRecipeRules.Plan(stage, VfxBudgetRules.For(tier), detail, reducedMotion, byPlayer, hitsPlayer, radius);

    // --- the tier ---------------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(Tiers))]
    public void EveryTierDrawsTheFlareTheRingAndTheFirstParticles(VfxTier tier)
    {
        VfxPlan plan = PlanAt(tier, Everything);

        Assert.True(plan.Flare);
        Assert.True(plan.Ring);
        Assert.True(plan.Light);
        Assert.Equal(VfxParticles.Embers, plan.Particles);
        Assert.True(plan.Bolt);
        Assert.True(plan.Shell);
        Assert.True(plan.Sigil);
        Assert.True(plan.Tether);
        Assert.True(plan.Inward);
        Assert.Equal(1.5f, plan.Scale);
    }

    [Theory]
    [MemberData(nameof(Tiers))]
    public void ParticleDensityIsTheTiersMultiplier(VfxTier tier)
    {
        Assert.Equal(VfxBudgetRules.For(tier).ParticleMultiplier, PlanAt(tier, Everything).Density);
    }

    [Theory]
    [InlineData(VfxTier.Performance, false)]
    [InlineData(VfxTier.Low, false)]
    [InlineData(VfxTier.Medium, false)]
    [InlineData(VfxTier.High, true)]
    [InlineData(VfxTier.Ultra, true)]
    public void DistortionIsOnlyOnTheTwoHighestTiers(VfxTier tier, bool expected)
    {
        Assert.Equal(expected, PlanAt(tier, Everything).Distortion);
    }

    [Theory]
    [InlineData(VfxTier.Performance, false)]
    [InlineData(VfxTier.Low, false)]
    [InlineData(VfxTier.Medium, true)]
    [InlineData(VfxTier.High, true)]
    [InlineData(VfxTier.Ultra, true)]
    public void GroundMarksSecondaryDebrisAndTheFireballStartAtMedium(VfxTier tier, bool expected)
    {
        VfxPlan plan = PlanAt(tier, Everything);

        Assert.Equal(expected ? VfxMark.Scorch : VfxMark.None, plan.Mark);
        Assert.Equal(expected ? VfxParticles.Smoke : VfxParticles.None, plan.Secondary);
        Assert.Equal(expected, plan.SecondaryDensity > 0f);
        Assert.Equal(expected, plan.Fireball);
    }

    [Fact]
    public void UltraDoublesTheDebris()
    {
        VfxPlan high = PlanAt(VfxTier.High, Everything);
        VfxPlan ultra = PlanAt(VfxTier.Ultra, Everything);

        Assert.Equal(1f, high.SecondaryDensity);
        Assert.Equal(3f, ultra.SecondaryDensity); // 1.5 particles x 2 debris
    }

    [Fact]
    public void ASmallHitHasNoFireballAndNeitherDoesLightning()
    {
        Assert.False(PlanAt(VfxTier.Ultra, Everything, radius: 0.5f).Fireball);
        Assert.False(VfxRecipeRules.Plan(
            Everything, VfxBudgetRules.For(VfxTier.Ultra), VfxDetail.Full, false, true, false, 4f,
            DamageType.Lightning).Fireball);
        Assert.True(VfxRecipeRules.Plan(
            Everything, VfxBudgetRules.For(VfxTier.Ultra), VfxDetail.Full, false, true, false, 4f,
            DamageType.Fire).Fireball);
    }

    // --- the distance -----------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(Tiers))]
    public void PastFullDetailOnlyTheFlareAndTheBoltDraw(VfxTier tier)
    {
        VfxPlan plan = PlanAt(tier, Everything, VfxDetail.FlareOnly);

        Assert.True(plan.Flare);
        Assert.True(plan.Bolt);
        Assert.False(plan.Ring);
        Assert.False(plan.Light);
        Assert.Equal(VfxParticles.None, plan.Particles);
        Assert.Equal(VfxParticles.None, plan.Secondary);
        Assert.False(plan.Distortion);
        Assert.Equal(VfxMark.None, plan.Mark);
        Assert.False(plan.ScreenFlash);
        Assert.False(plan.Shell);
        Assert.False(plan.Fireball);
    }

    [Fact]
    public void ARingAloneStillFlashesAtADistance()
    {
        // A beat that is only a shock ring would otherwise vanish entirely past full detail.
        VfxPlan plan = PlanAt(VfxTier.High, new VfxStage { Ring = true }, VfxDetail.FlareOnly);

        Assert.True(plan.Flare);
    }

    [Theory]
    [MemberData(nameof(Tiers))]
    public void PastTheCullDistanceNothingDraws(VfxTier tier)
    {
        Assert.True(PlanAt(tier, Everything, VfxDetail.None).IsEmpty);
    }

    [Fact]
    public void AnEmptyStageDrawsNothing()
    {
        Assert.True(PlanAt(VfxTier.Ultra, VfxStage.None).IsEmpty);
        Assert.True(VfxPlan.Nothing.IsEmpty);
    }

    // --- comfort ----------------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(Tiers))]
    public void ReducedMotionNeverDistorts(VfxTier tier)
    {
        Assert.False(PlanAt(tier, Everything, reducedMotion: true).Distortion);
    }

    [Fact]
    public void AnEnemysSpellFlashesTheScreenOnlyWhenItStruckThePlayer()
    {
        Assert.True(PlanAt(VfxTier.High, Everything, byPlayer: true).ScreenFlash);
        Assert.False(PlanAt(VfxTier.High, Everything, byPlayer: false, hitsPlayer: false).ScreenFlash);
        Assert.True(PlanAt(VfxTier.High, Everything, byPlayer: false, hitsPlayer: true).ScreenFlash);
    }

    [Fact]
    public void AStageThatAsksForNoFlashNeverFlashes()
    {
        VfxStage quiet = Everything with { ScreenFlash = false };

        Assert.False(PlanAt(VfxTier.Ultra, quiet, hitsPlayer: true).ScreenFlash);
    }

    // --- filling out a fallback --------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(SchoolsAndDeliveries))]
    public void AnAuthoredStageIsNeverTouched(DamageType school, SpellDelivery delivery)
    {
        var authored = new VfxStage { Particles = VfxParticles.Wisps };
        var traits = new VfxTraits(school, delivery, 1f, 9f, Authored: true);

        foreach (VfxRole role in Enum.GetValues<VfxRole>())
        {
            Assert.Equal(authored, VfxRecipeRules.Enrich(authored, role, traits));
            Assert.Equal(VfxStage.None, VfxRecipeRules.Enrich(VfxStage.None, role, traits));
        }
    }

    [Theory]
    [MemberData(nameof(SchoolsAndDeliveries))]
    public void EveryFallbackImpactIsAFlareWithItsSchoolsParticlesAndDebris(DamageType school, SpellDelivery delivery)
    {
        VfxStage raw = SpellVfxCatalog.Fallback(school, delivery).Impact;
        VfxStage impact = VfxRecipeRules.Enrich(raw, VfxRole.Impact, new VfxTraits(school, delivery, 0.5f, 0f, false));

        Assert.True(impact.Flare);
        Assert.Equal(SpellVfxCatalog.SchoolParticles(school), impact.Particles);
        Assert.Equal(VfxRecipeRules.SchoolSecondary(school), impact.Secondary);
        Assert.True(impact.Ring);                    // weight 0.5 is past the ring's threshold
        Assert.Equal(SpellVfxCatalog.SchoolMark(school), impact.Mark);
        Assert.False(impact.Distortion);             // not a heavy spell
        Assert.False(impact.ScreenFlash);
    }

    [Theory]
    [MemberData(nameof(SchoolsAndDeliveries))]
    public void EveryFallbackBurstHasARingAndBigOnesShakeTheAir(DamageType school, SpellDelivery delivery)
    {
        VfxStage raw = SpellVfxCatalog.Fallback(school, delivery).Impact;
        VfxStage small = VfxRecipeRules.Enrich(raw, VfxRole.Burst, new VfxTraits(school, delivery, 0.3f, 1.5f, false));
        VfxStage large = VfxRecipeRules.Enrich(raw, VfxRole.Burst, new VfxTraits(school, delivery, 0.3f, 6f, false));

        Assert.True(small.Flare && small.Ring);
        Assert.NotEqual(VfxParticles.None, small.Particles);
        Assert.False(small.Distortion);
        Assert.False(small.ScreenFlash);

        Assert.True(large.Distortion);
        Assert.True(large.ScreenFlash);
    }

    [Theory]
    [MemberData(nameof(SchoolsAndDeliveries))]
    public void EveryFallbackCastAndTravelIsVisible(DamageType school, SpellDelivery delivery)
    {
        SpellVfxRecipe recipe = SpellVfxCatalog.Fallback(school, delivery);
        var traits = new VfxTraits(school, delivery, 0.5f, 0f, false);

        VfxStage cast = VfxRecipeRules.Enrich(recipe.Cast, VfxRole.Cast, traits);
        VfxStage travel = VfxRecipeRules.Enrich(recipe.Travel, VfxRole.Travel, traits);

        Assert.True(cast.Flare);
        Assert.NotEqual(VfxParticles.None, cast.Particles);
        Assert.True(travel.Flare);
        Assert.NotEqual(VfxParticles.None, travel.Particles);
        Assert.Equal(school == DamageType.Lightning, travel.Bolt);
        Assert.Equal(school == DamageType.Arcane, cast.Sigil);
    }

    [Fact]
    public void AHeavyFallbackHitEarnsAPressureWaveAndAFlash()
    {
        VfxStage raw = SpellVfxCatalog.Fallback(DamageType.Fire, SpellDelivery.Projectile).Impact;
        VfxStage heavy = VfxRecipeRules.Enrich(
            raw, VfxRole.Impact, new VfxTraits(DamageType.Fire, SpellDelivery.Projectile, 0.9f, 0f, false));
        VfxStage light = VfxRecipeRules.Enrich(
            raw, VfxRole.Impact, new VfxTraits(DamageType.Fire, SpellDelivery.Projectile, 0.2f, 0f, false));

        Assert.True(heavy.Distortion && heavy.ScreenFlash && heavy.Ring);
        Assert.False(light.Distortion || light.ScreenFlash || light.Ring);
    }

    [Theory]
    [MemberData(nameof(Tiers))]
    public void AFallbackBlastIsBuiltFromTheBlocksItsTierAllows(VfxTier tier)
    {
        // End to end, as SpellVfx.Burst does it: a fire nova nobody authored, large enough to ask
        // for a screen flash (whether it is shown is VfxScreenRules' and the player's distance).
        float radius = VfxRecipeRules.ScreenFlashRadius + 0.5f;
        VfxBudget budget = VfxBudgetRules.For(tier);
        VfxStage stage = VfxRecipeRules.Enrich(
            SpellVfxCatalog.Fallback(DamageType.Fire, SpellDelivery.Area).Impact, VfxRole.Burst,
            new VfxTraits(DamageType.Fire, SpellDelivery.Area, 0.6f, radius, false));
        VfxPlan plan = VfxRecipeRules.Plan(stage, budget, VfxDetail.Full, false, true, false, radius, DamageType.Fire);

        Assert.True(plan.Flare && plan.Ring && plan.Light && plan.ScreenFlash);
        Assert.Equal(VfxParticles.Embers, plan.Particles);
        Assert.Equal(budget.Distortion, plan.Distortion);
        Assert.Equal(budget.GroundMarks > 0, plan.Mark == VfxMark.Scorch);
        Assert.Equal(budget.SecondaryDebris, plan.Secondary == VfxParticles.Smoke);
        Assert.Equal(budget.SecondaryDebris, plan.Fireball);
    }

    // --- the small rules --------------------------------------------------------------------------

    [Fact]
    public void TheTrailGrowsWithTheTier()
    {
        Assert.Equal(0f, VfxRecipeRules.TrailSeconds(VfxTrail.CoreAndHalo));
        Assert.True(VfxRecipeRules.TrailSeconds(VfxTrail.Short) > 0f);
        Assert.True(VfxRecipeRules.TrailSeconds(VfxTrail.Normal) > VfxRecipeRules.TrailSeconds(VfxTrail.Short));
        Assert.True(VfxRecipeRules.TrailSeconds(VfxTrail.Long) > VfxRecipeRules.TrailSeconds(VfxTrail.Normal));
        Assert.Equal(VfxRecipeRules.TrailSeconds(VfxTrail.Long), VfxRecipeRules.TrailSeconds(VfxTrail.LongWithSparks));

        Assert.False(VfxRecipeRules.TrailParticles(VfxTrail.Short));
        Assert.True(VfxRecipeRules.TrailParticles(VfxTrail.Normal));
        Assert.False(VfxRecipeRules.TrailSparks(VfxTrail.Long));
        Assert.True(VfxRecipeRules.TrailSparks(VfxTrail.LongWithSparks));
    }

    [Fact]
    public void ABoltsPictureSettlesFromTheHandInAQuarterSecond()
    {
        Assert.Equal(0.25f, VfxRecipeRules.HandSettleSeconds);
        Assert.Equal(1f, VfxRecipeRules.HandOffset(0d));
        Assert.Equal(0f, VfxRecipeRules.HandOffset(0.25d));
        Assert.Equal(0f, VfxRecipeRules.HandOffset(3d));

        float previous = 1f;
        for (double age = 0.01d; age < 0.25d; age += 0.01d)
        {
            float left = VfxRecipeRules.HandOffset(age);
            Assert.InRange(left, 0f, previous);
            previous = left;
        }

        // Eased out: more than half the way there by half time.
        Assert.True(VfxRecipeRules.HandOffset(0.125d) < 0.5f);
    }

    [Fact]
    public void AFallingThingArrivesFastAndExactlyOnTime()
    {
        Assert.Equal(1f, VfxRecipeRules.SettleLeft(0d, 2f, accelerate: true));
        Assert.Equal(0f, VfxRecipeRules.SettleLeft(2d, 2f, accelerate: true));
        Assert.True(VfxRecipeRules.SettleLeft(1d, 2f, accelerate: true) > 0.5f); // still high at half time
        Assert.Equal(0f, VfxRecipeRules.SettleLeft(0.1d, 0f, accelerate: true)); // no time at all: already there
    }

    [Fact]
    public void HeavierFullerDeadlierHitsAreDrawnLarger()
    {
        float plain = VfxRecipeRules.ImpactScale(0.5f, 0f, false, false, SpellImpactKind.Target);

        Assert.True(VfxRecipeRules.ImpactScale(1f, 0f, false, false, SpellImpactKind.Target) > plain);
        Assert.True(VfxRecipeRules.ImpactScale(0.5f, 1f, false, false, SpellImpactKind.Target) > plain);
        Assert.True(VfxRecipeRules.ImpactScale(0.5f, 0f, true, false, SpellImpactKind.Target) > plain);
        Assert.True(VfxRecipeRules.ImpactScale(0.5f, 0f, false, true, SpellImpactKind.Target) > plain);
        Assert.True(VfxRecipeRules.ImpactScale(0.5f, 0f, false, false, SpellImpactKind.Blocked) < plain);
        Assert.True(VfxRecipeRules.ImpactScale(0.5f, 0f, false, false, SpellImpactKind.Expired) <
                    VfxRecipeRules.ImpactScale(0.5f, 0f, false, false, SpellImpactKind.Blocked));
        Assert.Equal(plain, VfxRecipeRules.ImpactScale(0.5f, 0f, false, false, SpellImpactKind.World));
        Assert.Equal(1f, plain, 3);
    }

    [Theory]
    [InlineData(SpellDelivery.Projectile, 0f, false)]
    [InlineData(SpellDelivery.Projectile, 3f, true)]
    [InlineData(SpellDelivery.Self, 0f, false)]
    [InlineData(SpellDelivery.Area, 0f, true)]
    [InlineData(SpellDelivery.Cone, 0f, true)]
    [InlineData(SpellDelivery.Ground, 0f, true)]
    [InlineData(SpellDelivery.Barrier, 0f, true)]
    [InlineData(SpellDelivery.Dash, 0f, true)]
    public void AHitInsideABlastIsASplashNotASecondExplosion(SpellDelivery delivery, float impactRadius, bool splash)
    {
        Assert.Equal(splash, VfxRecipeRules.IsSplash(delivery, impactRadius));
    }

    [Theory]
    [MemberData(nameof(Tiers))]
    public void ABoltsSegmentsAreTheTiersAtLengthAndFewerWhenShort(VfxTier tier)
    {
        VfxBudget budget = VfxBudgetRules.For(tier);

        Assert.Equal(budget.BoltSegments, VfxRecipeRules.BoltSegments(budget, 40f));
        Assert.Equal(3, VfxRecipeRules.BoltSegments(budget, 0f));
        Assert.InRange(VfxRecipeRules.BoltSegments(budget, 2f), 3, budget.BoltSegments);
    }
}
