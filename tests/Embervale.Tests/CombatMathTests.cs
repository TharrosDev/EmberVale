using Embervale.Combat;
using Embervale.Stats;
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

    // --- ScaleDamage (offensive base + power scaling) -----------------------

    [Theory]
    [InlineData(10f, 0f, 0.5f, 10f)]    // no power → just the base
    [InlineData(10f, 40f, 0.5f, 30f)]   // melee: 10 + 40×0.5
    [InlineData(10f, 40f, 0.6f, 34f)]   // spell: 10 + 40×0.6
    [InlineData(0f, 20f, 0.5f, 10f)]    // zero base, power only
    public void ScaleDamage_AddsPowerShareToBase(float baseDamage, float power, float scaling, float expected)
    {
        Assert.Equal(expected, CombatMath.ScaleDamage(baseDamage, power, scaling), Tolerance);
    }

    [Fact]
    public void DamagePipeline_ScaleThenMitigate_PinsTheNumber()
    {
        // 10 base + 30 power × 0.5 = 25 raw physical; through 100 armor (×0.5) = 12.5 mitigated.
        float raw = CombatMath.ScaleDamage(10f, 30f, 0.5f);
        Assert.Equal(25f, raw, Tolerance);
        Assert.Equal(12.5f, raw * CombatMath.ArmorMultiplier(100f), Tolerance);
    }

    // --- Per-school resistance mapping (Phase 34E) --------------------------
    // Mitigate itself reads a live StatsComponent (a GodotObject, so out of this project by the
    // csproj rule), but the part that can silently break is the school → stat lookup: a school that
    // fell through to Armor would be mitigated by the wrong stat, and before 34E every non-Physical
    // school was mitigated by nothing at all. The curve is already pinned above.

    [Theory]
    [InlineData(DamageType.Fire, StatType.FireResist)]
    [InlineData(DamageType.Frost, StatType.FrostResist)]
    [InlineData(DamageType.Lightning, StatType.LightningResist)]
    [InlineData(DamageType.Arcane, StatType.ArcaneResist)]
    [InlineData(DamageType.Nature, StatType.NatureResist)]
    [InlineData(DamageType.Necrotic, StatType.NecroticResist)]
    public void ResistanceStat_MapsEachSchoolToItsOwnResistance(DamageType school, StatType expected)
    {
        Assert.Equal(expected, CombatMath.ResistanceStat(school));
    }

    [Fact]
    public void ResistanceStat_PhysicalAnswersToArmor()
    {
        Assert.Equal(StatType.Armor, CombatMath.ResistanceStat(DamageType.Physical));
    }

    /// <summary>The regression guard: no magic school may share Armor's stat. Written over the enum
    /// rather than a fixed list so a school added later fails here instead of silently inheriting
    /// armour mitigation. (True is excluded — Mitigate returns before the lookup.)</summary>
    [Fact]
    public void ResistanceStat_NoMagicSchoolFallsThroughToArmor()
    {
        foreach (DamageType school in System.Enum.GetValues<DamageType>())
        {
            if (school is DamageType.Physical or DamageType.True)
            {
                continue;
            }

            Assert.NotEqual(StatType.Armor, CombatMath.ResistanceStat(school));
        }
    }

    /// <summary>Resistance is never immunity: whatever the value, some damage lands. DESIGN's
    /// "none a trap" rule depends on this — a fully immune enemy would make a school unplayable.</summary>
    [Theory]
    [InlineData(0f)]
    [InlineData(100f)]
    [InlineData(10000f)]
    [InlineData(-50f)]
    public void Resistance_NeverReachesImmunity(float resist)
    {
        float m = CombatMath.ArmorMultiplier(resist);
        Assert.True(m > 0f && m <= 1f, $"resist {resist} produced multiplier {m}");
    }
    // --- Poise (Phase 36C) --------------------------------------------------

    [Fact]
    public void PoiseDamage_UnblockedAndNotWindingUp_IsTheRawAmount()
    {
        // The pre-36C behaviour, pinned: a multiplier of 1 must change nothing.
        Assert.Equal(20f, CombatMath.PoiseDamage(20f, blocked: false, blockFactor: 0.5f, windupMultiplier: 1f), 4);
    }

    [Fact]
    public void PoiseDamage_ABlockedHitStillChipsPoise()
    {
        // A held guard has to be breakable into a stagger, or blocking is a win button.
        Assert.Equal(10f, CombatMath.PoiseDamage(20f, blocked: true, blockFactor: 0.5f, windupMultiplier: 1f), 4);
    }

    [Fact]
    public void PoiseDamage_CaughtInAWindupTakesMore()
    {
        // The knob that makes a big telegraphed swing worth attacking into.
        Assert.Equal(30f, CombatMath.PoiseDamage(20f, blocked: false, blockFactor: 0.5f, windupMultiplier: 1.5f), 4);
    }

    [Fact]
    public void PoiseDamage_TheWindupMultiplierAppliesOnTopOfABlock()
    {
        Assert.Equal(15f, CombatMath.PoiseDamage(20f, blocked: true, blockFactor: 0.5f, windupMultiplier: 1.5f), 4);
    }

    [Fact]
    public void PoiseDamage_AHardenedWindupTakesLess()
    {
        // Below 1 is legal: a phase meant to be survived rather than interrupted.
        Assert.Equal(10f, CombatMath.PoiseDamage(20f, blocked: false, blockFactor: 0.5f, windupMultiplier: 0.5f), 4);
    }

    [Fact]
    public void PoiseDamage_ANegativeMultiplierCannotHealPoise()
    {
        // Content is validated against this, but the maths must not restore poise on a hit even if
        // a hand-edited .tres gets through.
        Assert.Equal(0f, CombatMath.PoiseDamage(20f, blocked: false, blockFactor: 0.5f, windupMultiplier: -2f), 4);
    }

}
