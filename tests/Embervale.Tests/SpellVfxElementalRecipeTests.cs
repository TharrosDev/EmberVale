using Embervale.Combat;
using Embervale.Magic;
using Embervale.Magic.Vfx;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// The elemental recipes, pinned where a wrong flag would be invisible until someone looked at the
/// screen: an authored recipe is drawn exactly as written, so a beat that is left out draws nothing.
/// </summary>
public class SpellVfxElementalRecipeTests
{
    public static readonly TheoryData<string, DamageType, SpellDelivery> Spells = new()
    {
        { "spell.emberlash", DamageType.Fire, SpellDelivery.Projectile },
        { "spell.flame_lance", DamageType.Fire, SpellDelivery.Projectile },
        { "spell.pyre_wall", DamageType.Fire, SpellDelivery.Barrier },
        { "spell.sunfall", DamageType.Fire, SpellDelivery.Ground },
        { "spell.rime_shard", DamageType.Frost, SpellDelivery.Projectile },
        { "spell.frost_nova", DamageType.Frost, SpellDelivery.Area },
        { "spell.blizzard", DamageType.Frost, SpellDelivery.Area },
        { "spell.glacial_bulwark", DamageType.Frost, SpellDelivery.Barrier },
        { "spell.ball_lightning", DamageType.Lightning, SpellDelivery.Projectile },
        { "spell.storm_conduit", DamageType.Lightning, SpellDelivery.Projectile },
        { "spell.thunder_step", DamageType.Lightning, SpellDelivery.Dash },
        { "spell.stormbrand", DamageType.Lightning, SpellDelivery.Projectile },
    };

    [Theory]
    [MemberData(nameof(Spells))]
    public void EveryElementalSpellIsAuthoredWithACastAndAnImpact(string id, DamageType school, SpellDelivery delivery)
    {
        Assert.True(SpellVfxCatalog.Has(id), $"{id} has no recipe of its own.");
        SpellVfxRecipe recipe = SpellVfxCatalog.For(id, school, delivery);

        // The wind-up glow is always drawn, but the release snap is the Cast stage's flare.
        Assert.True(recipe.Cast.Flare, $"{id} has no release flash.");
        Assert.NotEqual(VfxParticles.None, recipe.Cast.Particles);
        Assert.False(recipe.Impact.IsEmpty, $"{id} has no impact beat.");
    }

    [Theory]
    [MemberData(nameof(Spells))]
    public void ABoltHasATravelBeatAndNothingElseNeedsOne(string id, DamageType school, SpellDelivery delivery)
    {
        SpellVfxRecipe recipe = SpellVfxCatalog.For(id, school, delivery);
        if (delivery is SpellDelivery.Projectile or SpellDelivery.Dash)
        {
            // With no flare in its travel stage a bolt has no core and flies unseen.
            Assert.True(recipe.Travel.Flare, $"{id} would fly with no core.");
        }
        else
        {
            Assert.True(recipe.Travel.IsEmpty, $"{id} has a travel beat nothing reads.");
        }
    }

    [Theory]
    [MemberData(nameof(Spells))]
    public void ASchoolThrowsItsOwnKindOfParticle(string id, DamageType school, SpellDelivery delivery)
    {
        VfxParticles thrown = SpellVfxCatalog.For(id, school, delivery).Impact.Particles;
        VfxParticles expected = school switch
        {
            DamageType.Fire => VfxParticles.Embers,
            DamageType.Frost => VfxParticles.Shards,
            _ => VfxParticles.Sparks,
        };
        Assert.Equal(expected, thrown);
    }

    [Theory]
    [MemberData(nameof(Spells))]
    public void OnlySunfallFallsFromTheSkyAndOnlySunfallFlashesTheScreen(
        string id, DamageType school, SpellDelivery delivery)
    {
        SpellVfxRecipe recipe = SpellVfxCatalog.For(id, school, delivery);
        bool sunfall = id == "spell.sunfall";
        Assert.Equal(sunfall ? VfxGroundStyle.Meteor : VfxGroundStyle.Telegraph, recipe.Ground);
        Assert.Equal(sunfall, recipe.Impact.ScreenFlash);
        Assert.False(recipe.Cast.ScreenFlash || recipe.Travel.ScreenFlash || recipe.Linger.ScreenFlash);
    }

    [Fact]
    public void AFireHitIsBigEnoughForABodyOfFireAndAFrostHitIsNot()
    {
        // A single hit is drawn 1.05 x ImpactScale x Scale metres across, and a blast of
        // FireballRadius or more gets a ball of flowing noise in the colours of fire or of frost.
        // Fire wants it on an ordinary hit; frost must not have it (the ball would be a white dome).
        static float Radius(string id, DamageType school, float weight) =>
            1.05f * VfxRecipeRules.ImpactScale(weight, 0f, false, false, SpellImpactKind.Target) *
            SpellVfxCatalog.For(id, school, SpellDelivery.Projectile).Impact.Scale;

        Assert.True(Radius("spell.emberlash", DamageType.Fire, 0.2f) >= VfxRecipeRules.FireballRadius);
        Assert.True(Radius("spell.flame_lance", DamageType.Fire, 0.55f) >= VfxRecipeRules.FireballRadius);
        Assert.True(Radius("spell.rime_shard", DamageType.Frost, 0.2f) < VfxRecipeRules.FireballRadius);
    }

    [Fact]
    public void WhatStandsIsItsLingerStage()
    {
        // A wall and a zone read their Linger stage as the standing thing itself.
        SpellVfxRecipe pyre = SpellVfxCatalog.For("spell.pyre_wall", DamageType.Fire, SpellDelivery.Barrier);
        Assert.True(pyre.Linger.Flare);
        Assert.Equal(VfxParticles.Embers, pyre.Linger.Particles);
        Assert.Equal(VfxMark.Scorch, pyre.Linger.Mark);

        SpellVfxRecipe bulwark = SpellVfxCatalog.For("spell.glacial_bulwark", DamageType.Frost, SpellDelivery.Barrier);
        Assert.True(bulwark.Linger.Shell);
        Assert.Equal(VfxMark.Frost, bulwark.Linger.Mark);

        SpellVfxRecipe blizzard = SpellVfxCatalog.For("spell.blizzard", DamageType.Frost, SpellDelivery.Area);
        Assert.Equal(VfxParticles.Motes, blizzard.Linger.Particles);
        Assert.False(blizzard.Linger.Flare); // no heart glow: the caster stands in the middle of it
    }

    [Fact]
    public void StormbrandStampsItsBrandAndLeavesItHanging()
    {
        SpellVfxRecipe brand = SpellVfxCatalog.For("spell.stormbrand", DamageType.Lightning, SpellDelivery.Projectile);
        Assert.True(brand.Impact.Sigil);
        Assert.True(brand.Linger.Sigil);
    }
}
