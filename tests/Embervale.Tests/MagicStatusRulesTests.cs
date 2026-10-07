using System.Collections.Generic;
using System.Linq;
using Embervale.Combat;
using Embervale.Magic;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// The pure rules behind the magic status group: detonation, wards, marks, dispel, diminishing returns,
/// spread, lifesteal, lightning arcs and the combo table. The component that applies them is
/// Godot-bound and proven by <c>tools/magic_status_probe.gd</c>.
/// </summary>
public class MagicStatusRulesTests
{
    [Theory]
    [InlineData(0f, 1f)]
    [InlineData(-2f, 1f)]
    [InlineData(1f, 1f)]
    [InlineData(2f, 2f)]
    public void ChargeLifetime_NeverShortensAuthoredDuration(float multiplier, float expected) =>
        Assert.Equal(expected, StatusMath.DurationMultiplier(multiplier));

    [Fact]
    public void ChargeLifetime_RejectsNonFiniteInput()
    {
        Assert.Equal(1f, StatusMath.DurationMultiplier(float.NaN));
        Assert.Equal(1f, StatusMath.DurationMultiplier(float.PositiveInfinity));
    }

    // --- detonation ---

    [Theory]
    [InlineData(1, 3, false)]
    [InlineData(2, 3, false)]
    [InlineData(3, 3, true)]
    [InlineData(4, 3, true)]
    [InlineData(5, 0, false)] // 0 = never detonates
    public void Detonation_FiresAtTheAuthoredStackCount(int stacks, int at, bool expected) =>
        Assert.Equal(expected, StatusMath.ShouldDetonate(stacks, at));

    [Fact]
    public void DetonationDamage_IsPerStack_AndNeverNegative()
    {
        Assert.Equal(27f, StatusMath.DetonateDamage(3, 9f), 3);
        Assert.Equal(0f, StatusMath.DetonateDamage(-2, 9f), 3);
        Assert.Equal(0f, StatusMath.DetonateDamage(3, -9f), 3);
    }

    // --- marks and reductions ---

    [Fact]
    public void Amplify_RaisesDamage_AndCapsAtDouble()
    {
        Assert.Equal(125f, StatusMath.Amplify(100f, 0.25f), 3);
        Assert.Equal(200f, StatusMath.Amplify(100f, 5f), 3);
        Assert.Equal(100f, StatusMath.Amplify(100f, -1f), 3); // a negative sum is not an amplifier
    }

    [Fact]
    public void Reduce_TrimsDamage_ButNeverToNothing()
    {
        Assert.Equal(70f, StatusMath.Reduce(100f, 0.3f), 3);
        Assert.Equal(10f, StatusMath.Reduce(100f, 4f), 3);
    }

    // --- wards ---

    [Fact]
    public void Ward_AbsorbsUntilSpent_ThenTheRestPassesThrough()
    {
        (float passed, float left) = StatusMath.Absorb(20f, 1f, 50f);
        Assert.Equal(0f, passed, 3);
        Assert.Equal(30f, left, 3);

        (passed, left) = StatusMath.Absorb(40f, 1f, left);
        Assert.Equal(10f, passed, 3); // 30 absorbed, 10 lands
        Assert.Equal(0f, left, 3);    // and the ward is broken
    }

    [Fact]
    public void Ward_WithAPartialFraction_OnlyTakesThatShareOfEachHit()
    {
        (float passed, float left) = StatusMath.Absorb(100f, 0.4f, 50f);
        Assert.Equal(60f, passed, 3);
        Assert.Equal(10f, left, 3);
    }

    [Fact]
    public void Ward_CapacityGrowsWithSpellPower()
    {
        Assert.Equal(50f, StatusMath.WardCapacity(30f, 10f, 2f), 3);
        Assert.Equal(30f, StatusMath.WardCapacity(30f, -5f, 2f), 3);
    }

    [Fact]
    public void Ward_ExpiryReturnsTheUnspentShareOfItsMana()
    {
        Assert.Equal(8f, StatusMath.WardManaReturn(8f, 50f, 50f), 3);  // untouched
        Assert.Equal(4f, StatusMath.WardManaReturn(8f, 25f, 50f), 3);  // half spent
        Assert.Equal(0f, StatusMath.WardManaReturn(8f, 0f, 50f), 3);   // spent
        Assert.Equal(0f, StatusMath.WardManaReturn(8f, 10f, 0f), 3);   // not a ward
    }

    // --- cleanse ---

    [Fact]
    public void Cleanse_HealsMoreForEachStackRemoved()
    {
        Assert.Equal(1f, StatusMath.StackBonusMultiplier(0, 0.35f), 3);
        Assert.Equal(1.35f, StatusMath.StackBonusMultiplier(1, 0.35f), 3);
        Assert.Equal(2.05f, StatusMath.StackBonusMultiplier(3, 0.35f), 3);
        Assert.Equal(1f, StatusMath.StackBonusMultiplier(-3, 0.35f), 3);
    }

    // --- diminishing returns ---

    [Fact]
    public void HardControls_AreRootSilenceStun_NeverAMark()
    {
        Assert.Equal(StatusControl.Root | StatusControl.Silence | StatusControl.Stun, StatusMath.HardControls);
        Assert.Equal(StatusControl.None, StatusMath.HardOf(StatusControl.Mark));
        Assert.Equal(StatusControl.Root, StatusMath.HardOf(StatusControl.Root | StatusControl.Mark));
    }

    [Fact]
    public void ControlImmunity_RefusesOnlyTheControlsItCovers()
    {
        StatusControl rootImmune = StatusControl.Root;
        Assert.True(StatusMath.IsControlRefused(StatusControl.Root, rootImmune));
        Assert.False(StatusMath.IsControlRefused(StatusControl.Stun, rootImmune));
        Assert.False(StatusMath.IsControlRefused(StatusControl.Mark, StatusControl.Root | StatusControl.Stun));
        Assert.False(StatusMath.IsControlRefused(StatusControl.None, rootImmune));

        // A freeze (Stun and Root) is refused if either lock-down is still immune.
        StatusControl freeze = StatusControl.Stun | StatusControl.Root;
        Assert.True(StatusMath.IsControlRefused(freeze, StatusControl.Stun));
        Assert.True(StatusMath.IsControlRefused(freeze, StatusControl.Root));
        Assert.False(StatusMath.IsControlRefused(freeze, StatusControl.Silence));
    }

    // --- spread on death ---

    [Fact]
    public void Spread_IsBounded()
    {
        Assert.True(StatusMath.CanSpread(0, StatusEffectsComponent.MaxSpreadJumps));
        Assert.True(StatusMath.CanSpread(StatusEffectsComponent.MaxSpreadJumps - 1, StatusEffectsComponent.MaxSpreadJumps));
        Assert.False(StatusMath.CanSpread(StatusEffectsComponent.MaxSpreadJumps, StatusEffectsComponent.MaxSpreadJumps));
    }

    [Fact]
    public void SpreadTarget_IsTheNearestOneNotAlreadyCarrying()
    {
        var candidates = new List<(float, bool)> { (4f, true), (9f, false), (16f, false) };
        Assert.Equal(1, StatusMath.PickSpreadTarget(candidates)); // the nearer bare one beats the nearest carrier

        var allCarrying = new List<(float, bool)> { (9f, true), (4f, true) };
        Assert.Equal(1, StatusMath.PickSpreadTarget(allCarrying)); // all carry it: just the nearest

        Assert.Equal(-1, StatusMath.PickSpreadTarget(new List<(float, bool)>()));
    }

    // --- school identities ---

    [Fact]
    public void Lifesteal_HealsMoreTheLowerTheTargetIs()
    {
        float full = SchoolIdentity.LifestealAmount(100f, 1f);
        float half = SchoolIdentity.LifestealAmount(100f, 0.5f);
        float nearDead = SchoolIdentity.LifestealAmount(100f, 0f);
        Assert.Equal(35f, full, 3);
        Assert.True(half > full && nearDead > half);
        Assert.Equal(70f, nearDead, 3);
    }

    [Fact]
    public void Lifesteal_DoublesOffAGraveMark()
    {
        Assert.Equal(
            2f * SchoolIdentity.LifestealAmount(80f, 0.6f, false),
            SchoolIdentity.LifestealAmount(80f, 0.6f, true), 3);
    }

    [Fact]
    public void Lifesteal_IsLimitedToHealthActuallyRemoved()
    {
        Assert.Equal(0f, SchoolIdentity.LifestealAmount(0f, 0f, true));
        Assert.Equal(3.5f, SchoolIdentity.LifestealAmount(5f, 0f), 3);
        Assert.Equal(0f, SchoolIdentity.LifestealAmount(-5f, 0f));
    }

    [Fact]
    public void LightningArc_PrefersABrandOverANearerFoe()
    {
        var near = (Distance: 2f, Branded: false);
        var branded = (Distance: 10f, Branded: true);
        Assert.Equal(1, SchoolIdentity.PickChainTarget(new[] { near, branded }));
    }

    [Fact]
    public void LightningArc_IgnoresABrandOutOfRange_AndAFoeBeyondTheChainRadius()
    {
        Assert.Equal(-1, SchoolIdentity.PickChainTarget(new[] { (SchoolIdentity.BrandRange + 1f, true) }));
        Assert.Equal(-1, SchoolIdentity.PickChainTarget(new[] { (7f, false) }));
        Assert.Equal(0, SchoolIdentity.PickChainTarget(new[] { (5f, false) }));
    }

    [Fact]
    public void LightningArc_ToABrandCarriesMore()
    {
        Assert.True(SchoolIdentity.ChainDamage(100f, true) > SchoolIdentity.ChainDamage(100f, false));
        Assert.Equal(50f, SchoolIdentity.ChainDamage(100f), 3); // the plain arc is unchanged
    }

    // --- combos ---

    [Theory]
    [InlineData(DamageType.Frost, "status.kindled", "combo.steam_burst")]
    [InlineData(DamageType.Fire, "status.frozen", "combo.meltdown")]
    [InlineData(DamageType.Frost, "status.stormbrand", "combo.superconduct")]
    [InlineData(DamageType.Fire, "status.swarmed", "combo.smoke_out")]
    [InlineData(DamageType.Lightning, "status.chill", "combo.shatter")]
    [InlineData(DamageType.Fire, "status.chill", "combo.thermal_shock")]
    public void Combo_MatchesItsSchoolAndStatus_WithAStableId(DamageType school, string status, string id)
    {
        ComboRule? rule = SpellCombo.Match(school, s => s == status);
        Assert.NotNull(rule);
        Assert.Equal(id, rule!.Value.Id);
        Assert.True(rule.Value.ConsumeStatus);
    }

    [Fact]
    public void ComboIds_AreUnique_AndNamespaced()
    {
        List<string> ids = SpellCombo.All.Select(r => r.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
        Assert.All(ids, id => Assert.StartsWith("combo.", id));
        Assert.True(SpellCombo.All.Count >= 5); // Shatter and Thermal Shock plus at least three new
    }

    [Fact]
    public void ComboRule_NeverNeedsAStatusItsOwnSchoolApplies_ByAccident()
    {
        // Frost's freeze consumes chill before the combo reads, so no Frost rule may key on chill.
        Assert.DoesNotContain(SpellCombo.All, r => r.TriggerSchool == DamageType.Frost && r.RequiredStatusId == "status.chill");
    }

    // --- shapes ---

    [Theory]
    [InlineData(StatusControl.None, false, StatusVfxShape.Swirl)]
    [InlineData(StatusControl.Mark, false, StatusVfxShape.MarkRing)]
    [InlineData(StatusControl.Root, false, StatusVfxShape.Thorns)]
    [InlineData(StatusControl.Silence, false, StatusVfxShape.BrokenGlyph)]
    [InlineData(StatusControl.Stun, false, StatusVfxShape.Stars)]
    [InlineData(StatusControl.Stun | StatusControl.Root, false, StatusVfxShape.IceShell)]
    [InlineData(StatusControl.None, true, StatusVfxShape.WardShell)]
    public void StatusShape_ReadsFromWhatTheStatusDoes(StatusControl controls, bool ward, StatusVfxShape expected) =>
        Assert.Equal(expected, StatusVfxShapes.Pick(controls, ward));

    [Theory]
    [InlineData(1.8f, 1f)]   // the body the marks were authored for
    [InlineData(1.7f, 1f)]   // a goblin: unchanged
    [InlineData(0.9f, 1f)]   // a wolf: never shrunk
    [InlineData(3.6f, 2f)]
    [InlineData(5.2f, 5.2f / 1.8f)]
    public void StatusMarks_GrowOnlyForABodyTallerThanAPerson(float bodyHeight, float expected) =>
        Assert.Equal(expected, StatusVfxShapes.BodyFit(bodyHeight, 1.8f), 4);

    [Fact]
    public void StatusMarks_KeepTheirClearanceOverATallerHead()
    {
        // 2.25 over a 1.8 m body is 0.45 m of air; over a 5.2 m body it is still 0.45 m.
        float fit = StatusVfxShapes.BodyFit(5.2f, 1.8f);

        Assert.Equal(5.65f, StatusVfxShapes.HeadHeight(2.25f, 1.8f, fit), 3);
        Assert.Equal(2.25f, StatusVfxShapes.HeadHeight(2.25f, 1.8f, 1f), 4);
    }
}
