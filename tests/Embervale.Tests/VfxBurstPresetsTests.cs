using System;
using Embervale.Magic.Vfx;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// Every particle preset a recipe can name must exist and be drawable, and the way a tier's particle
/// multiplier becomes an emitter's <c>AmountRatio</c> must cover the whole table without an emitter
/// ever being asked for more particles than it allocated.
/// </summary>
public class VfxBurstPresetsTests
{
    public static readonly TheoryData<VfxParticles> Kinds = AllKinds();

    private static TheoryData<VfxParticles> AllKinds()
    {
        var data = new TheoryData<VfxParticles>();
        foreach (VfxParticles kind in Enum.GetValues<VfxParticles>())
        {
            if (kind != VfxParticles.None)
            {
                data.Add(kind);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void EveryPresetARecipeCanNameIsSane(VfxParticles kind)
    {
        VfxBurstPreset preset = VfxBurstPresets.For(kind);

        Assert.True(preset.Amount > 0, $"{kind} throws nothing");
        Assert.InRange(preset.Life, 0.2f, 3f);
        Assert.True(preset.SpeedMin >= 0f && preset.SpeedMax >= preset.SpeedMin);
        Assert.True(preset.SizeMin > 0f && preset.SizeMax >= preset.SizeMin);
        Assert.InRange(preset.Explosiveness, 0f, 1f);
        Assert.True(preset.Damping >= 0f);
        Assert.True(preset.Energy > 0f);
    }

    [Fact]
    public void ThereIsNoPresetForNone()
    {
        Assert.Equal(0, VfxBurstPresets.For(VfxParticles.None).Amount);
    }

    [Fact]
    public void OnlySmokeCoversWhatIsBehindItAndOnlyThrownFireStretches()
    {
        foreach (VfxParticles kind in Enum.GetValues<VfxParticles>())
        {
            VfxBurstPreset preset = VfxBurstPresets.For(kind);
            Assert.Equal(kind == VfxParticles.Smoke, preset.Occlude);
            // Sparks, embers and wisps follow their travel (a streak, a comet); nothing else does,
            // and nothing that stretches is the round soft dot.
            bool thrown = kind is VfxParticles.Sparks or VfxParticles.Embers or VfxParticles.Wisps;
            Assert.Equal(thrown, preset.AlignVelocity);
            Assert.False(preset.AlignVelocity && preset.Sprite == VfxSprite.Dot);
        }
    }

    [Fact]
    public void SmokeIsNeverBrightEnoughToBloom()
    {
        // Covering smoke drawn at an HDR energy would be a glowing grey cloud.
        Assert.True(VfxBurstPresets.For(VfxParticles.Smoke).Energy < 1f);
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void AnEmitterAllocatesRoomForTheTopTier(VfxParticles kind)
    {
        VfxBurstPreset preset = VfxBurstPresets.For(kind);
        float ultra = VfxBudgetRules.For(VfxTier.Ultra).ParticleMultiplier;

        Assert.True(VfxBurstPresets.Allocated(preset) >= preset.Amount * ultra);
    }

    [Fact]
    public void TheTiersMultiplierBecomesAGrowingShareOfTheEmitter()
    {
        float previous = 0f;
        foreach (VfxTier tier in Enum.GetValues<VfxTier>())
        {
            float ratio = VfxBurstPresets.AmountRatio(VfxBudgetRules.For(tier).ParticleMultiplier);
            Assert.InRange(ratio, 0f, 1f);
            Assert.True(ratio > previous, $"{tier} draws no more particles than the tier below it");
            previous = ratio;
        }

        // A density of 1 is the preset's own amount.
        Assert.Equal(1f / VfxBurstPresets.Headroom, VfxBurstPresets.AmountRatio(1f), 4);
    }

    [Fact]
    public void TheShareIsClampedAtBothEnds()
    {
        Assert.Equal(1f, VfxBurstPresets.AmountRatio(50f));
        Assert.True(VfxBurstPresets.AmountRatio(0f) > 0f); // never an emitter that draws nothing at all
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void ABurstLastsUntilItsLastParticleHasDied(VfxParticles kind)
    {
        VfxBurstPreset preset = VfxBurstPresets.For(kind);
        float seconds = VfxBurstPresets.BurstSeconds(preset.Life, preset.Explosiveness);

        // The last particle is born (1 - explosiveness) of a lifetime in, and lives a lifetime.
        Assert.Equal(preset.Life + (preset.Life * (1f - preset.Explosiveness)), seconds, 4);
        Assert.True(seconds >= preset.Life);
        Assert.True(seconds <= preset.Life * 2f);
    }

    [Fact]
    public void BurstSecondsToleratesBadInput()
    {
        Assert.Equal(1f, VfxBurstPresets.BurstSeconds(1f, 1f), 4);
        Assert.Equal(1f, VfxBurstPresets.BurstSeconds(1f, 7f), 4);
        Assert.Equal(2f, VfxBurstPresets.BurstSeconds(1f, -3f), 4);
        Assert.Equal(0f, VfxBurstPresets.BurstSeconds(-1f, 0.5f));
    }
}
