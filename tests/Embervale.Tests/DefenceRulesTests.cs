using Embervale.Combat;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// The defender-side rules of the damage pipeline: what each blow kind does, how a guard is judged
/// and paid for, graded parries, and when a blow is a critical opening. All pure; the live path through
/// a real hurtbox is <c>tools/combat_defence_probe.gd</c>.
/// </summary>
public class DefenceRulesTests
{
    private const float Tolerance = 0.0001f;

    // --- blow kinds ---------------------------------------------------------

    [Fact]
    public void Normal_IsTheIdentity()
    {
        BlowProfile p = DefenceRules.Profile(HitKind.Normal, 0f);
        Assert.Equal(1f, p.DamageMultiplier, Tolerance);
        Assert.Equal(1f, p.PoiseMultiplier, Tolerance);
        Assert.Equal(1f, p.GuardPressure, Tolerance);
        Assert.False(p.CrushesGuard);
        Assert.True(p.Parryable);
    }

    [Fact]
    public void Heavy_WeighsMoreOnPoiseAndGuard_ButNotOnDamage()
    {
        BlowProfile p = DefenceRules.Profile(HitKind.Heavy, 0f);
        Assert.Equal(1f, p.DamageMultiplier, Tolerance);
        Assert.Equal(1.5f, p.PoiseMultiplier, Tolerance);
        Assert.Equal(2f, p.GuardPressure, Tolerance);
        Assert.False(p.CrushesGuard);
    }

    [Theory]
    [InlineData(0f, 1f, 1f, 1f, false)]
    [InlineData(0.5f, 1.375f, 1.5f, 2f, false)]
    [InlineData(0.9f, 1.675f, 1.9f, 2.8f, true)]
    [InlineData(1f, 1.75f, 2f, 3f, true)]
    public void Charged_ScalesWithCharge_AndFullChargeCrushes(
        float charge, float damage, float poise, float pressure, bool crushes)
    {
        BlowProfile p = DefenceRules.Profile(HitKind.Charged, charge);
        Assert.Equal(damage, p.DamageMultiplier, Tolerance);
        Assert.Equal(poise, p.PoiseMultiplier, Tolerance);
        Assert.Equal(pressure, p.GuardPressure, Tolerance);
        Assert.Equal(crushes, p.CrushesGuard);
    }

    [Fact]
    public void Charge_IsClamped()
    {
        Assert.Equal(1.75f, DefenceRules.Profile(HitKind.Charged, 5f).DamageMultiplier, Tolerance);
        Assert.Equal(1f, DefenceRules.Profile(HitKind.Charged, -2f).DamageMultiplier, Tolerance);
    }

    [Fact]
    public void Plunge_AlwaysCrushesAGuard()
    {
        Assert.True(DefenceRules.Profile(HitKind.Plunge, 0f).CrushesGuard);
    }

    [Theory]
    [InlineData(HitKind.Ranged)]
    [InlineData(HitKind.Spell)]
    public void ArrowsAndSpells_CannotBeParried(HitKind kind)
    {
        Assert.False(DefenceRules.Profile(kind, 0f).Parryable);
    }

    [Fact]
    public void ArrowsPressAGuardLessThanASword()
    {
        Assert.True(DefenceRules.Profile(HitKind.Ranged, 0f).GuardPressure
                    < DefenceRules.Profile(HitKind.Normal, 0f).GuardPressure);
    }

    // --- guard zones --------------------------------------------------------

    [Theory]
    [InlineData(0f, GuardZone.Front)]
    [InlineData(70f, GuardZone.Front)]
    [InlineData(71f, GuardZone.Flank)]
    [InlineData(100f, GuardZone.Flank)]
    [InlineData(101f, GuardZone.Outside)]
    [InlineData(180f, GuardZone.Outside)]
    [InlineData(-45f, GuardZone.Front)]
    public void ZoneOf_DefaultArc(float bearing, GuardZone expected)
    {
        Assert.Equal(expected, DefenceRules.ZoneOf(bearing, 100f));
    }

    [Fact]
    public void ZoneOf_ATinyOrHugeArcIsClamped()
    {
        Assert.Equal(GuardZone.Front, DefenceRules.ZoneOf(0.5f, 0f));   // clamps to 1 degree
        Assert.Equal(GuardZone.Front, DefenceRules.ZoneOf(180f, 500f)); // clamps to 360: all round
    }

    [Fact]
    public void FlankBlocks_AreWeaker_AndOutsideBlocksNothing()
    {
        Assert.Equal(0.7f, DefenceRules.ZoneMitigation(0.7f, GuardZone.Front), Tolerance);
        Assert.Equal(0.42f, DefenceRules.ZoneMitigation(0.7f, GuardZone.Flank), Tolerance);
        Assert.Equal(0f, DefenceRules.ZoneMitigation(0.7f, GuardZone.Outside), Tolerance);
    }

    [Fact]
    public void ZoneMitigation_NeverHeals()
    {
        Assert.Equal(1f, DefenceRules.ZoneMitigation(1.4f, GuardZone.Front), Tolerance);
        Assert.Equal(0f, DefenceRules.ZoneMitigation(-0.5f, GuardZone.Front), Tolerance);
    }

    // --- guard cost and tired guard -----------------------------------------

    [Theory]
    [InlineData(10f, 1f, 25f, 10f)]   // a sword-weight blow costs the base
    [InlineData(10f, 1f, 6f, 6f)]     // a dagger costs less, floored at 0.6x
    [InlineData(10f, 1f, 60f, 20f)]   // a maul costs double, capped at 2x
    [InlineData(10f, 1f, 500f, 20f)]
    [InlineData(10f, 2f, 25f, 20f)]   // a heavy blow presses twice as hard
    [InlineData(10f, 3f, 60f, 60f)]   // a full charge with a maul
    public void GuardStaminaCost_ScalesWithWeightAndPressure(
        float baseCost, float pressure, float poise, float expected)
    {
        Assert.Equal(expected, DefenceRules.GuardStaminaCost(baseCost, pressure, poise), Tolerance);
    }

    [Fact]
    public void GuardStaminaCost_NeverNegative()
    {
        Assert.Equal(0f, DefenceRules.GuardStaminaCost(-5f, 1f, 25f), Tolerance);
        Assert.Equal(0f, DefenceRules.GuardStaminaCost(10f, -1f, 25f), Tolerance);
    }

    [Theory]
    [InlineData(1f, 0.5f)]
    [InlineData(0.3f, 0.5f)]      // at the knee, unchanged
    [InlineData(0.15f, 0.75f)]    // halfway to empty
    [InlineData(0f, 1f)]          // empty: double
    public void TiredGuard_BleedsPoiseFaster(float stamina, float expected)
    {
        Assert.Equal(expected, DefenceRules.BlockPoiseFactor(0.5f, stamina), Tolerance);
    }

    [Fact]
    public void TiredGuard_NeverRestoresPoise()
    {
        Assert.Equal(0f, DefenceRules.BlockPoiseFactor(-1f, 0f), Tolerance);
    }

    // --- guard break --------------------------------------------------------

    [Theory]
    [InlineData(ReactionClass.Humanoid, StaggerResponse.Heavy)]
    [InlineData(ReactionClass.Armored, StaggerResponse.Heavy)]
    [InlineData(ReactionClass.Small, StaggerResponse.Knockdown)]
    [InlineData(ReactionClass.Large, StaggerResponse.Stagger)]
    [InlineData(ReactionClass.Boss, StaggerResponse.Stagger)]
    public void GuardBreak_ResponseFollowsTheBody(ReactionClass body, StaggerResponse expected)
    {
        Assert.Equal(expected, DefenceRules.GuardBreakResponse(body));
    }

    [Fact]
    public void GuardBreak_IsLongerThanAPoiseStagger_ButShorterForBigBodies()
    {
        float poiseStagger = PoiseReaction.Duration(StaggerResponse.Stagger, 0.6f);
        Assert.True(DefenceRules.GuardBreakSeconds(ReactionClass.Humanoid, 1.2f) > poiseStagger);
        Assert.True(DefenceRules.GuardBreakSeconds(ReactionClass.Boss, 1.2f)
                    < DefenceRules.GuardBreakSeconds(ReactionClass.Humanoid, 1.2f));
        Assert.True(DefenceRules.GuardBreakSeconds(ReactionClass.Small, 1.2f)
                    > DefenceRules.GuardBreakSeconds(ReactionClass.Humanoid, 1.2f));
    }

    [Fact]
    public void ABossIsNeverKnockedDownByABrokenGuard()
    {
        Assert.NotEqual(StaggerResponse.Knockdown, DefenceRules.GuardBreakResponse(ReactionClass.Boss));
        Assert.NotEqual(StaggerResponse.Knockdown, DefenceRules.GuardBreakResponse(ReactionClass.Large));
    }

    // --- graded parry -------------------------------------------------------

    [Theory]
    [InlineData(0f, ParryGrade.Perfect)]
    [InlineData(0.1f, ParryGrade.Perfect)]
    [InlineData(0.11f, ParryGrade.Good)]
    [InlineData(0.2f, ParryGrade.Good)]
    [InlineData(0.21f, ParryGrade.Late)]
    [InlineData(0.3f, ParryGrade.Late)]
    [InlineData(0.31f, ParryGrade.None)]
    [InlineData(2f, ParryGrade.None)]
    [InlineData(-1f, ParryGrade.None)]
    public void ParryGrade_FollowsTheWindow(float elapsed, ParryGrade expected)
    {
        Assert.Equal(expected, Parry.Grade(elapsed, 0.2f));
    }

    [Fact]
    public void ParryGrade_IsStillAParryInsideTheOriginalWindow()
    {
        // The graded window only ever ADDS to the old one: everything IsParry accepted is still a
        // Good or Perfect parry, so existing timing habits and authored windows are unchanged.
        for (float t = 0f; t <= 0.2f; t += 0.01f)
        {
            Assert.True(Parry.IsParry(t, 0.2f));
            Assert.True(Parry.OpensRiposte(Parry.Grade(t, 0.2f)));
        }
    }

    [Fact]
    public void ParryGrade_ANonPositiveWindowNeverParries()
    {
        Assert.Equal(ParryGrade.None, Parry.Grade(0f, 0f));
        Assert.Equal(ParryGrade.None, Parry.Grade(0f, -1f));
    }

    [Fact]
    public void PerfectParry_IsFree_AndStaggersLongest()
    {
        Assert.Equal(0f, Parry.StaminaFactor(ParryGrade.Perfect), Tolerance);
        Assert.Equal(1f, Parry.StaminaFactor(ParryGrade.Good), Tolerance);
        Assert.True(Parry.AttackerStaggerFactor(ParryGrade.Perfect) > Parry.AttackerStaggerFactor(ParryGrade.Good));
        Assert.True(Parry.AttackerStaggerFactor(ParryGrade.Good) > Parry.AttackerStaggerFactor(ParryGrade.Late));
        Assert.Equal(0f, Parry.AttackerStaggerFactor(ParryGrade.None), Tolerance);
    }

    [Fact]
    public void LateParry_NeverOpensARiposte()
    {
        Assert.False(Parry.OpensRiposte(ParryGrade.Late));
        Assert.False(Parry.OpensRiposte(ParryGrade.None));
        Assert.True(Parry.OpensRiposte(ParryGrade.Good));
        Assert.True(Parry.OpensRiposte(ParryGrade.Perfect));
    }

    [Fact]
    public void LateParry_BeatsAPlainBlock_ButNotAGoodParry()
    {
        Assert.True(Parry.LateMitigation > 0.7f);   // the default BlockMitigation
        Assert.True(Parry.LateMitigation < 1f);
    }

    [Fact]
    public void ParryStagger_ScalesDownForBigBodies()
    {
        Assert.True(DefenceRules.ParryStaggerScale(ReactionClass.Boss) < 1f);
        Assert.True(DefenceRules.ParryStaggerScale(ReactionClass.Large) < 1f);
        Assert.Equal(1f, DefenceRules.ParryStaggerScale(ReactionClass.Humanoid), Tolerance);
    }

    // --- openings -----------------------------------------------------------

    [Fact]
    public void OpenTarget_TakesARiposte()
    {
        Assert.Equal(HitKind.Riposte, DefenceRules.ResolveOpening(HitKind.Normal, true, false, true));
        Assert.Equal(HitKind.Riposte, DefenceRules.ResolveOpening(HitKind.Heavy, true, false, true));
    }

    [Fact]
    public void AStampedRiposte_IsOnlyHonouredAgainstAnOpenTarget()
    {
        Assert.Equal(HitKind.Normal, DefenceRules.ResolveOpening(HitKind.Riposte, false, false, true));
        Assert.Equal(HitKind.Riposte, DefenceRules.ResolveOpening(HitKind.Riposte, true, false, true));
    }

    [Fact]
    public void FromBehind_IsABackstab_AndAStampIsHonoured()
    {
        Assert.Equal(HitKind.Backstab, DefenceRules.ResolveOpening(HitKind.Normal, false, true, true));
        Assert.Equal(HitKind.Backstab, DefenceRules.ResolveOpening(HitKind.Backstab, false, false, true));
    }

    [Fact]
    public void ARiposte_BeatsABackstab()
    {
        Assert.Equal(HitKind.Riposte, DefenceRules.ResolveOpening(HitKind.Normal, true, true, true));
    }

    [Theory]
    [InlineData(HitKind.Ranged)]
    [InlineData(HitKind.Spell)]
    public void ArrowsAndSpellsOpenNothing(HitKind kind)
    {
        Assert.Equal(HitKind.Normal, DefenceRules.ResolveOpening(kind, true, true, true));
    }

    [Fact]
    public void ADefenderThatCannotBeOpened_TakesNoCritical()
    {
        Assert.Equal(HitKind.Normal, DefenceRules.ResolveOpening(HitKind.Normal, true, true, false));
        Assert.Equal(HitKind.Normal, DefenceRules.ResolveOpening(HitKind.Backstab, false, true, false));
    }

    [Theory]
    [InlineData(0f, false)]
    [InlineData(90f, false)]
    [InlineData(119f, false)]
    [InlineData(120f, true)]
    [InlineData(180f, true)]
    public void IsBehind_IsTheRearCone(float bearing, bool expected)
    {
        Assert.Equal(expected, DefenceRules.IsBehind(bearing));
    }

    [Fact]
    public void RiposteBonus_RewardsTheHarderOpening()
    {
        Assert.Equal(1f, DefenceRules.RiposteBonus(OpenCause.None), Tolerance);
        Assert.True(DefenceRules.RiposteBonus(OpenCause.PoiseBreak) > 1f);
        Assert.True(DefenceRules.RiposteBonus(OpenCause.GuardBreak) > DefenceRules.RiposteBonus(OpenCause.PoiseBreak));
        Assert.True(DefenceRules.RiposteBonus(OpenCause.Parry) > DefenceRules.RiposteBonus(OpenCause.GuardBreak));
        Assert.True(DefenceRules.RiposteBonus(OpenCause.PerfectParry) > DefenceRules.RiposteBonus(OpenCause.Parry));
    }

    [Fact]
    public void RiposteBonus_ARecoveryTailIsTheSmallestReward()
    {
        // Punishing a heavy swing's tail is real but never beats breaking a body's poise or guard.
        Assert.True(DefenceRules.RiposteBonus(OpenCause.Recovery) > 1f);
        Assert.True(DefenceRules.RiposteBonus(OpenCause.Recovery) < DefenceRules.RiposteBonus(OpenCause.PoiseBreak));
    }

    [Fact]
    public void OpeningMultiplier_PinsTheNumbers()
    {
        Assert.Equal(2f, DefenceRules.OpeningMultiplier(HitKind.Riposte, OpenCause.Parry, false, ReactionClass.Humanoid), Tolerance);
        Assert.Equal(2.5f, DefenceRules.OpeningMultiplier(HitKind.Riposte, OpenCause.PerfectParry, false, ReactionClass.Humanoid), Tolerance);
        Assert.Equal(1.5f, DefenceRules.OpeningMultiplier(HitKind.Backstab, OpenCause.None, false, ReactionClass.Humanoid), Tolerance);
        Assert.Equal(1f, DefenceRules.OpeningMultiplier(HitKind.Normal, OpenCause.Parry, false, ReactionClass.Humanoid), Tolerance);
    }

    [Fact]
    public void OpeningMultiplier_StacksOnlyHalfOverARolledCrit()
    {
        // A x2 riposte over a rolled crit is x1.5, not x2 on top of the crit's own 1.5.
        Assert.Equal(1.5f, DefenceRules.OpeningMultiplier(HitKind.Riposte, OpenCause.Parry, true, ReactionClass.Humanoid), Tolerance);
    }

    [Fact]
    public void OpeningMultiplier_IsBlunterOnABoss()
    {
        float human = DefenceRules.OpeningMultiplier(HitKind.Riposte, OpenCause.Parry, false, ReactionClass.Humanoid);
        float boss = DefenceRules.OpeningMultiplier(HitKind.Riposte, OpenCause.Parry, false, ReactionClass.Boss);
        Assert.True(boss > 1f && boss < human);
    }

    [Fact]
    public void OpeningWindow_OutlivesTheStaggerBriefly()
    {
        Assert.InRange(DefenceRules.OpeningGraceSeconds, 0.1f, 0.5f);
    }
}
