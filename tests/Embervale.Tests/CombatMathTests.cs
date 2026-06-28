using Embervale.Combat;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// Covers the armor-mitigation curve behind every physical hit. The full <c>Mitigate</c>/<c>RollAttack</c>
/// paths read a live <c>StatsComponent</c> and roll via Godot RNG (exercised in-engine), but the
/// load-bearing defence formula — <c>100 / (100 + armor)</c> — is pure and pinned here.
/// </summary>
public class CombatMathTests
{
    private const float Tolerance = 0.0001f;

    [Theory]
    [InlineData(0f, 1.0f)]      // no armor → no reduction
    [InlineData(100f, 0.5f)]    // armor == 100 → half damage
    [InlineData(300f, 0.25f)]   // diminishing returns
    [InlineData(900f, 0.1f)]
    public void ArmorMultiplier_FollowsTheCurve(float armor, float expected)
    {
        Assert.Equal(expected, CombatMath.ArmorMultiplier(armor), Tolerance);
    }

    [Fact]
    public void ArmorMultiplier_NegativeArmor_ClampsToNoReduction()
    {
        Assert.Equal(1.0f, CombatMath.ArmorMultiplier(-50f), Tolerance);
    }

    [Fact]
    public void ArmorMultiplier_AlwaysInZeroToOne_AndMonotonicallyDecreasing()
    {
        float previous = CombatMath.ArmorMultiplier(0f);
        Assert.Equal(1.0f, previous, Tolerance);

        for (float armor = 10f; armor <= 2000f; armor += 10f)
        {
            float m = CombatMath.ArmorMultiplier(armor);
            Assert.True(m > 0f && m <= 1f, $"multiplier out of range at armor {armor}: {m}");
            Assert.True(m < previous, $"multiplier should strictly decrease as armor rises (armor {armor})");
            previous = m;
        }
    }
}
