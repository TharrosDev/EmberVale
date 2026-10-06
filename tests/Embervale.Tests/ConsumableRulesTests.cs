using Embervale.Items;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// What a consumable restores, when a use is refused, and how its timers advance. The legacy potion
/// (a Heal with only a HealAmount) is the case that must never change.
/// </summary>
public class ConsumableRulesTests
{
    [Fact]
    public void LegacyHeal_UsesHealAmount_AndMagnitudeWinsWhenAuthored()
    {
        Assert.Equal(40f, ConsumableRules.RestoreAmount(ConsumableEffectKind.Heal, 0f, 40f));
        Assert.Equal(110f, ConsumableRules.RestoreAmount(ConsumableEffectKind.Heal, 110f, 40f));
    }

    [Fact]
    public void StaminaAndMana_IgnoreTheLegacyHealAmount()
    {
        Assert.Equal(30f, ConsumableRules.RestoreAmount(ConsumableEffectKind.RestoreStamina, 30f, 20f));
        Assert.Equal(0f, ConsumableRules.RestoreAmount(ConsumableEffectKind.RestoreMana, 0f, 20f));
    }

    [Theory]
    [InlineData(ConsumableEffectKind.Buff)]
    [InlineData(ConsumableEffectKind.Cure)]
    public void BuffAndCure_RestoreNothing(ConsumableEffectKind effect)
    {
        Assert.Equal(0f, ConsumableRules.RestoreAmount(effect, 50f, 50f));
        Assert.False(ConsumableRules.IsRestore(effect));
        Assert.False(ConsumableRules.IsOverTime(effect, 10f));
    }

    [Fact]
    public void CooldownKey_IsTheGroup_OrTheItemAlone()
    {
        Assert.Equal("potion", ConsumableRules.CooldownKey("potion", "item.potion.health"));
        Assert.Equal("item.potion.health", ConsumableRules.CooldownKey("", "item.potion.health"));
        Assert.Equal("item.potion.health", ConsumableRules.CooldownKey(null, "item.potion.health"));
    }

    [Fact]
    public void Check_CooldownRefusesBeforeAnythingElse()
    {
        Assert.Equal(ConsumeRefusal.OnCooldown,
            ConsumableRules.Check(ConsumableEffectKind.Heal, 40f, 3f, 100f, 100f, 0));
    }

    [Fact]
    public void Check_RestoreIsRefusedAtFull_AndAllowedBelow()
    {
        Assert.Equal(ConsumeRefusal.AlreadyFull,
            ConsumableRules.Check(ConsumableEffectKind.RestoreMana, 35f, 0f, 80f, 80f, 0));
        Assert.Equal(ConsumeRefusal.None,
            ConsumableRules.Check(ConsumableEffectKind.RestoreMana, 35f, 0f, 79f, 80f, 0));
    }

    [Fact]
    public void Check_AnInertLegacyConsumableIsStillUsableAtFullHealth()
    {
        Assert.Equal(ConsumeRefusal.None,
            ConsumableRules.Check(ConsumableEffectKind.Heal, 0f, 0f, 100f, 100f, 0));
    }

    [Fact]
    public void Check_CureNeedsSomethingToCure_AndBuffNeverRefuses()
    {
        Assert.Equal(ConsumeRefusal.NothingToCure,
            ConsumableRules.Check(ConsumableEffectKind.Cure, 0f, 0f, 100f, 100f, 0));
        Assert.Equal(ConsumeRefusal.None,
            ConsumableRules.Check(ConsumableEffectKind.Cure, 0f, 0f, 100f, 100f, 2));
        Assert.Equal(ConsumeRefusal.None,
            ConsumableRules.Check(ConsumableEffectKind.Buff, 0f, 0f, 100f, 100f, 0));
    }

    [Fact]
    public void ReasonKey_NamesEveryRefusal_AndNothingForNone()
    {
        Assert.Equal(string.Empty, ConsumableRules.ReasonKey(ConsumeRefusal.None));
        Assert.Equal(ConsumableRules.CooldownReasonKey, ConsumableRules.ReasonKey(ConsumeRefusal.OnCooldown));
        Assert.Equal(ConsumableRules.FullReasonKey, ConsumableRules.ReasonKey(ConsumeRefusal.AlreadyFull));
        Assert.Equal(ConsumableRules.NothingToCureReasonKey, ConsumableRules.ReasonKey(ConsumeRefusal.NothingToCure));
    }

    [Fact]
    public void Cooldown_TicksToZeroAndNeverBelow()
    {
        Assert.Equal(5f, ConsumableRules.TickCooldown(8f, 3f));
        Assert.Equal(0f, ConsumableRules.TickCooldown(1f, 3f));
        Assert.Equal(8f, ConsumableRules.TickCooldown(8f, -1f));
    }

    [Fact]
    public void CooldownFraction_RunsFromOneToZero()
    {
        Assert.Equal(1f, ConsumableRules.CooldownFraction(8f, 8f));
        Assert.Equal(0.25f, ConsumableRules.CooldownFraction(2f, 8f));
        Assert.Equal(0f, ConsumableRules.CooldownFraction(0f, 8f));
        Assert.Equal(0f, ConsumableRules.CooldownFraction(3f, 0f));
    }

    [Fact]
    public void RestoreStep_GivesExactlyTheTotal_HoweverTheFramesFall()
    {
        // 60 over 10 seconds, in uneven frames that overshoot the end.
        float perSecond = 60f / 10f;
        float remaining = 10f;
        float given = 0f;
        foreach (float frame in new[] { 0.016f, 2.5f, 0.3f, 4f, 1f, 9f, 1f })
        {
            given += ConsumableRules.RestoreStep(perSecond, remaining, frame, out remaining);
        }

        Assert.Equal(0f, remaining);
        Assert.Equal(60f, given, 3);
    }
}
