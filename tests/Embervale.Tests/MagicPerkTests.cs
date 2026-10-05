using Embervale.Combat;
using Embervale.Magic;
using Embervale.Progression;
using Xunit;

namespace Embervale.Tests;

/// <summary>Pins the pure side of the magic perk hooks: the mana price a perk factor and the Weave give
/// together, the school qualifier a school-power perk reads through, and the caps that keep a stack of
/// perks from making magic free or a crit certain.</summary>
public class MagicPerkTests
{
    [Fact]
    public void ManaCost_WithNoPerks_IsTheSheetCostTimesTheWeave()
    {
        Assert.Equal(20f, SpellRules.ManaCost(20f, 1f, 1f), 4);
        Assert.Equal(28f, SpellRules.ManaCost(20f, 1.4f, 1f), 4);
        Assert.Equal(0f, SpellRules.ManaCost(-5f, 1f, 1f), 4);
    }

    [Fact]
    public void ManaCost_AppliesThePerkFactor()
    {
        float factor = PerkEffectMath.Factor(PerkEffectKind.ManaCostMult, -0.15f);
        Assert.Equal(0.85f, factor, 4);
        Assert.Equal(17f, SpellRules.ManaCost(20f, 1f, factor), 4);
    }

    [Fact]
    public void ManaCost_StackedPerksStopAtTheFloor()
    {
        float factor = PerkEffectMath.Factor(PerkEffectKind.ManaCostMult, -5f);
        Assert.Equal(PerkEffectMath.ManaFactorFloor, factor, 4);
        Assert.True(SpellRules.ManaCost(20f, 1f, factor) > 0f);
    }

    [Fact]
    public void SchoolPower_QualifiedPerkOnlyTouchesItsSchool()
    {
        var totals = new PerkEffectTotals();
        totals.Add(PerkEffectKind.SchoolPowerBonus, "Fire", 0.12f);
        Assert.Equal(0.12f, totals.Get(PerkEffectKind.SchoolPowerBonus, "Fire"), 4);
        Assert.Equal(0f, totals.Get(PerkEffectKind.SchoolPowerBonus, "Frost"), 4);
        Assert.Equal(0f, totals.Get(PerkEffectKind.SchoolPowerBonus), 4);
    }

    [Fact]
    public void SchoolPower_UnqualifiedPerkStacksOntoEverySchool()
    {
        var totals = new PerkEffectTotals();
        totals.Add(PerkEffectKind.SchoolPowerBonus, "Fire", 0.12f);
        totals.Add(PerkEffectKind.SchoolPowerBonus, null, 0.10f);
        Assert.Equal(0.22f, totals.Get(PerkEffectKind.SchoolPowerBonus, DamageType.Fire.ToString()), 4);
        Assert.Equal(0.10f, totals.Get(PerkEffectKind.SchoolPowerBonus, DamageType.Arcane.ToString()), 4);
    }

    [Fact]
    public void SchoolPower_IsCapped()
    {
        Assert.Equal(1.5f, PerkEffectMath.Factor(PerkEffectKind.SchoolPowerBonus, 4f), 4);
    }

    [Fact]
    public void SpellCrit_BonusAddsToTheStatAndStopsAtTheCritCap()
    {
        float bonus = PerkEffectMath.Clamp(PerkEffectKind.SpellCritBonus, 9f);
        Assert.Equal(0.25f, bonus, 4);
        Assert.Equal(0.35f, CombatMath.ClampCritChance(0.10f + bonus), 4);
        Assert.Equal(CombatMath.MaxCritChance, CombatMath.ClampCritChance(0.70f + bonus), 4);
    }
}
