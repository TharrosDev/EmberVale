using Embervale.Movement;
using Embervale.World;
using Xunit;

namespace Embervale.Tests;

/// <summary>Fall damage and the landing stumble (<see cref="FallRules"/>).</summary>
public class FallRulesTests
{
    private const float Safe = 7f;
    private const float Lethal = 30f;
    private const float MaxHealth = 100f;

    [Theory]
    [InlineData(0f)]
    [InlineData(1.03f)] // an ordinary jump's apex
    [InlineData(Safe)]
    public void FallsUpToTheSafeHeightAreFree(float height)
    {
        Assert.Equal(0f, FallRules.Damage(height, Safe, Lethal, MaxHealth));
    }

    [Fact]
    public void DamageGrowsLinearlyPastTheSafeHeight()
    {
        float halfway = (Safe + Lethal) / 2f;
        Assert.Equal(MaxHealth / 2f, FallRules.Damage(halfway, Safe, Lethal, MaxHealth), 3);
        Assert.True(FallRules.Damage(12f, Safe, Lethal, MaxHealth) < FallRules.Damage(20f, Safe, Lethal, MaxHealth));
    }

    [Fact]
    public void ATwelveMetreDropCostsAboutAFifth()
    {
        // The number the export's docstring promises; Skyrim-modest, not a survival mechanic.
        float damage = FallRules.Damage(12f, Safe, Lethal, MaxHealth);
        Assert.InRange(damage, 15f, 25f);
    }

    [Theory]
    [InlineData(Lethal)]
    [InlineData(200f)]
    public void TheLethalHeightCostsEverythingAndNoMore(float height)
    {
        Assert.Equal(MaxHealth, FallRules.Damage(height, Safe, Lethal, MaxHealth));
    }

    [Fact]
    public void DamageScalesWithMaxHealth()
    {
        Assert.Equal(
            2f * FallRules.Damage(15f, Safe, Lethal, 100f),
            FallRules.Damage(15f, Safe, Lethal, 200f), 3);
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void ANonFiniteFallDealsNothing(float height)
    {
        Assert.Equal(0f, FallRules.Damage(height, Safe, Lethal, MaxHealth));
    }

    [Fact]
    public void DeepWaterBreaksTheFall()
    {
        Assert.Equal(0f, FallRules.Cushioned(25f, WorldWater.WadeDepth + 0.5f, WorldWater.WadeDepth));
    }

    [Fact]
    public void WadingWaterDoesNot()
    {
        Assert.Equal(25f, FallRules.Cushioned(25f, 0.4f, WorldWater.WadeDepth));
        Assert.Equal(25f, FallRules.Cushioned(25f, 0f, WorldWater.WadeDepth));
    }

    [Fact]
    public void AnOrdinaryJumpNeverStumbles()
    {
        Assert.Equal(0f, FallRules.RecoverySeconds(1.03f, 2.5f, 0.05f, 0.8f));
    }

    [Fact]
    public void TheStumbleGrowsWithTheDropUpToItsCap()
    {
        float short_ = FallRules.RecoverySeconds(3f, 2.5f, 0.05f, 0.8f);
        float long_ = FallRules.RecoverySeconds(10f, 2.5f, 0.05f, 0.8f);
        Assert.True(short_ > 0f);
        Assert.True(long_ > short_);
        Assert.Equal(0.8f, FallRules.RecoverySeconds(100f, 2.5f, 0.05f, 0.8f));
    }

    [Fact]
    public void TheStumbleEasesFromItsSlowestBackToFullSpeed()
    {
        Assert.Equal(0.35f, FallRules.RecoveryScale(0.5f, 0.5f, 0.35f), 4);
        float mid = FallRules.RecoveryScale(0.25f, 0.5f, 0.35f);
        Assert.True(mid > 0.35f && mid < 1f);
        Assert.Equal(1f, FallRules.RecoveryScale(0f, 0.5f, 0.35f));
        Assert.Equal(1f, FallRules.RecoveryScale(0.3f, 0f, 0.35f));
    }
}
