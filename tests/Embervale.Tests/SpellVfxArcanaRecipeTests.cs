using System.Collections.Generic;
using Embervale.Combat;
using Embervale.Magic;
using Embervale.Magic.Vfx;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// The rules the arcane, nature, necrotic and enemy-spell recipes are written to, pinned so a later
/// edit cannot quietly bring back a tinted screen or put smoke on the leanest tier.
/// </summary>
public class SpellVfxArcanaRecipeTests
{
    public static readonly TheoryData<string, DamageType, SpellDelivery> Owned = new()
    {
        { "spell.null_lance", DamageType.Arcane, SpellDelivery.Projectile },
        { "spell.arcane_shield", DamageType.Arcane, SpellDelivery.Self },
        { "spell.blink", DamageType.Arcane, SpellDelivery.Self },
        { "spell.gravity_well", DamageType.Arcane, SpellDelivery.Ground },
        { "spell.mending_bloom", DamageType.Nature, SpellDelivery.Self },
        { "spell.lifebloom_totem", DamageType.Nature, SpellDelivery.Self },
        { "spell.thornsnare", DamageType.Nature, SpellDelivery.Ground },
        { "spell.stinging_swarm", DamageType.Nature, SpellDelivery.Projectile },
        { "spell.barkskin", DamageType.Nature, SpellDelivery.Self },
        { "spell.ember_siphon", DamageType.Necrotic, SpellDelivery.Projectile },
        { "spell.soul_tithe", DamageType.Necrotic, SpellDelivery.Projectile },
        { "spell.knit_bone", DamageType.Necrotic, SpellDelivery.Self },
        { "spell.grave_mark", DamageType.Necrotic, SpellDelivery.Projectile },
        { "spell.ash_breath", DamageType.Necrotic, SpellDelivery.Cone },
        { "spell.dragon_breath", DamageType.Fire, SpellDelivery.Cone },
        { "spell.drake_breath", DamageType.Frost, SpellDelivery.Cone },
        { "spell.elder_word", DamageType.Arcane, SpellDelivery.Cone },
        { "spell.wither", DamageType.Necrotic, SpellDelivery.Projectile },
    };

    private static IEnumerable<VfxStage> Stages(SpellVfxRecipe recipe)
    {
        yield return recipe.Cast;
        yield return recipe.Travel;
        yield return recipe.Impact;
        yield return recipe.Linger;
    }

    [Theory]
    [MemberData(nameof(Owned))]
    public void EveryOwnedSpellHasItsOwnRecipeWithACastAndAnImpact(string id, DamageType school, SpellDelivery delivery)
    {
        Assert.True(SpellVfxCatalog.Has(id), $"{id} has no recipe of its own.");
        SpellVfxRecipe recipe = SpellVfxCatalog.For(id, school, delivery);

        // The wind-up glow is the warning an enemy cast is dodged on, and the release snap is the
        // cast itself: both read the cast stage's flare.
        Assert.True(recipe.Cast.Flare, $"{id} has no cast flare.");
        Assert.NotEqual(VfxParticles.None, recipe.Cast.Particles);
        Assert.False(recipe.Impact.IsEmpty, $"{id} has no impact beat.");
        Assert.Equal(VfxGroundStyle.Telegraph, recipe.Ground);
    }

    [Theory]
    [MemberData(nameof(Owned))]
    public void NoneOfThemTintsTheScreen(string id, DamageType school, SpellDelivery delivery)
    {
        foreach (VfxStage stage in Stages(SpellVfxCatalog.For(id, school, delivery)))
        {
            Assert.False(stage.ScreenFlash, $"{id} asks for a screen flash.");
        }
    }

    [Theory]
    [MemberData(nameof(Owned))]
    public void ThePerformanceTierDrawsNoSmokeMarksOrDistortionForThem(string id, DamageType school, SpellDelivery delivery)
    {
        VfxBudget lean = VfxBudgetRules.For(VfxTier.Performance);
        foreach (VfxStage stage in Stages(SpellVfxCatalog.For(id, school, delivery)))
        {
            // Smoke is only ever a second layer, so the tier that draws no second layer draws none.
            Assert.NotEqual(VfxParticles.Smoke, stage.Particles);

            VfxPlan plan = VfxRecipeRules.Plan(stage, lean, VfxDetail.Full, false, true, false, 3f, school);
            Assert.Equal(VfxParticles.None, plan.Secondary);
            Assert.Equal(VfxMark.None, plan.Mark);
            Assert.False(plan.Distortion);
            Assert.False(plan.Fireball);
            Assert.False(plan.ScreenFlash);
        }
    }

    [Theory]
    [MemberData(nameof(Owned))]
    public void TheTopTierDrawsAtLeastAsMuchAsTheLeanOne(string id, DamageType school, SpellDelivery delivery)
    {
        VfxBudget lean = VfxBudgetRules.For(VfxTier.Performance);
        VfxBudget rich = VfxBudgetRules.For(VfxTier.Ultra);
        foreach (VfxStage stage in Stages(SpellVfxCatalog.For(id, school, delivery)))
        {
            VfxPlan low = VfxRecipeRules.Plan(stage, lean, VfxDetail.Full, false, true, false, 3f, school);
            VfxPlan high = VfxRecipeRules.Plan(stage, rich, VfxDetail.Full, false, true, false, 3f, school);
            Assert.True(high.Density >= low.Density);
            Assert.Equal(stage.Secondary, high.Secondary);
            Assert.Equal(stage.Mark, high.Mark);
            Assert.Equal(stage.Distortion, high.Distortion);
        }
    }

    [Theory]
    [InlineData("spell.arcane_shield", DamageType.Arcane)]
    [InlineData("spell.barkskin", DamageType.Nature)]
    [InlineData("spell.mending_bloom", DamageType.Nature)]
    [InlineData("spell.knit_bone", DamageType.Necrotic)]
    public void ASpellOnOneselfHasAFlareARingAndParticlesOnTheCaster(string id, DamageType school)
    {
        // Its impact beat is drawn on the caster at release: with none, the buff would be invisible.
        VfxStage impact = SpellVfxCatalog.For(id, school, SpellDelivery.Self).Impact;
        Assert.True(impact.Flare);
        Assert.True(impact.Ring);
        Assert.NotEqual(VfxParticles.None, impact.Particles);
    }

    [Fact]
    public void TheLifestealSpellsLeaveTheTetherToTheLifestealArc()
    {
        // Every necrotic hit that heals already draws a tether through SpellVfx.Arc; a recipe that
        // also flagged one would draw two lines for one drain.
        foreach (string id in new[] { "spell.ember_siphon", "spell.soul_tithe", "spell.grave_mark", "spell.wither" })
        {
            Assert.False(SpellVfxCatalog.For(id, DamageType.Necrotic, SpellDelivery.Projectile).Impact.Tether, id);
        }
    }

    [Fact]
    public void TheRecipesThatDefineASpellsIdentityAreWhatTheDesignAsksFor()
    {
        SpellVfxRecipe lance = SpellVfxCatalog.For("spell.null_lance", DamageType.Arcane, SpellDelivery.Projectile);
        Assert.True(lance.Cast.Sigil);
        Assert.True(lance.Travel.Shell);
        Assert.True(lance.Impact.Inward);

        SpellVfxRecipe well = SpellVfxCatalog.For("spell.gravity_well", DamageType.Arcane, SpellDelivery.Ground);
        Assert.True(well.Impact.Inward);
        Assert.Equal(VfxMark.Rune, well.Impact.Mark);

        SpellVfxRecipe snare = SpellVfxCatalog.For("spell.thornsnare", DamageType.Nature, SpellDelivery.Ground);
        Assert.Equal(VfxParticles.Shards, snare.Impact.Particles);
        Assert.Equal(VfxMark.Roots, snare.Impact.Mark);

        SpellVfxRecipe mark = SpellVfxCatalog.For("spell.grave_mark", DamageType.Necrotic, SpellDelivery.Projectile);
        Assert.True(mark.Cast.Sigil && mark.Impact.Sigil);

        // What the mark leaves is the sigil under the marked, drawn with the status for as long as
        // it lasts: the linger stage hangs no second one over the head.
        Assert.True(mark.Linger.IsEmpty);

        SpellVfxRecipe word = SpellVfxCatalog.For("spell.elder_word", DamageType.Arcane, SpellDelivery.Cone);
        Assert.True(word.Travel.Ring);
        Assert.True(word.Cast.Sigil);
    }
}
