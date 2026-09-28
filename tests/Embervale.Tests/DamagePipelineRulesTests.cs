using Embervale.Combat;
using Xunit;

namespace Embervale.Tests;

/// <summary>The mitigation, floor and crit-clamp rules added to <see cref="CombatMath"/> by the combat upgrade.</summary>
public class DamagePipelineRulesTests
{
    private const float Tolerance = 0.0001f;

    [Theory]
    [InlineData(0f, 1f)]
    [InlineData(100f, 0.5f)]
    [InlineData(300f, 0.25f)]
    public void MitigationMultiplier_MatchesTheArmourCurveAtOrAboveZero(float value, float expected)
    {
        Assert.Equal(expected, CombatMath.MitigationMultiplier(value), Tolerance);
    }

    [Theory]
    [InlineData(-50f, 1.3333f)]     // 2 - 100/150
    [InlineData(-100f, 1.5f)]       // 2 - 100/200
    [InlineData(-900f, 1.9f)]       // 2 - 100/1000
    public void MitigationMultiplier_AVulnerabilityAmplifies(float value, float expected)
    {
        Assert.Equal(expected, CombatMath.MitigationMultiplier(value), 0.001f);
    }

    [Fact]
    public void MitigationMultiplier_IsContinuousAtZero_AndBoundedBelowDouble()
    {
        Assert.Equal(CombatMath.MitigationMultiplier(0f), CombatMath.MitigationMultiplier(-0.0001f), 0.001f);
        for (float v = -10f; v >= -100000f; v *= 3f)
        {
            float m = CombatMath.MitigationMultiplier(v);
            Assert.True(m > 1f && m < 2f, $"vulnerability {v} gave {m}");
        }
    }

    [Fact]
    public void MitigationMultiplier_IsMonotonicAcrossZero()
    {
        float previous = CombatMath.MitigationMultiplier(-500f);
        for (float v = -490f; v <= 500f; v += 10f)
        {
            float m = CombatMath.MitigationMultiplier(v);
            Assert.True(m < previous, $"not decreasing at {v}");
            previous = m;
        }
    }

    [Fact]
    public void ArmourStaysAWeakness_OnlyWhenNegative_ArmorMultiplierIsUnchanged()
    {
        // The old, pinned behaviour of the armour helper itself.
        Assert.Equal(1f, CombatMath.ArmorMultiplier(-50f), Tolerance);
    }

    [Fact]
    public void FloorHit_AnUnblockedHitDoesAtLeastAPoint()
    {
        Assert.Equal(1f, CombatMath.FloorHit(0.2f, 5f, false), Tolerance);
        Assert.Equal(3f, CombatMath.FloorHit(3f, 5f, false), Tolerance);
    }

    [Fact]
    public void FloorHit_ASmallerHitThanTheFloorIsNotInflated()
    {
        Assert.Equal(0.4f, CombatMath.FloorHit(0.1f, 0.4f, false), Tolerance);
    }

    [Fact]
    public void FloorHit_ABlockedOrEmptyHitHasNoFloor()
    {
        Assert.Equal(0.2f, CombatMath.FloorHit(0.2f, 5f, true), Tolerance);
        Assert.Equal(0f, CombatMath.FloorHit(0f, 0f, false), Tolerance);
    }

    [Theory]
    [InlineData(-0.5f, 0f)]
    [InlineData(0.05f, 0.05f)]
    [InlineData(0.75f, 0.75f)]
    [InlineData(1.4f, 0.75f)]
    public void CritChance_IsCapped(float chance, float expected)
    {
        Assert.Equal(expected, CombatMath.ClampCritChance(chance), Tolerance);
    }

    [Theory]
    [InlineData(0f, 1.25f)]
    [InlineData(1f, 1.25f)]
    [InlineData(1.5f, 1.5f)]
    [InlineData(9f, 4f)]
    public void CritMultiplier_ReadsAsACrit_AndCannotRunAway(float multiplier, float expected)
    {
        Assert.Equal(expected, CombatMath.ClampCritMultiplier(multiplier), Tolerance);
    }

    [Fact]
    public void ADamageResult_DefaultsToNoParryNoBreakNoOpening()
    {
        var result = new DamageResult(5f, false, false, DamageType.Physical);
        Assert.Equal(ParryGrade.None, result.Parry);
        Assert.False(result.GuardBroken);
        Assert.Equal(HitKind.Normal, result.Opening);
    }

    [Fact]
    public void ADamagePacket_StillConstructsTheOldWay()
    {
        var packet = new DamagePacket(10f, DamageType.Fire, null, false, 5f);
        Assert.Equal(HitKind.Normal, packet.Kind);
        Assert.Equal(0f, packet.Charge, Tolerance);
    }
}
