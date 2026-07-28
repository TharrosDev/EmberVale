using Embervale.Companions;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// Covers the loyalty standing (Phase 32C): clamping, the value→tier bands, and the combat edge each
/// band buys. Loyalty gates banter, abilities and (Phase 44) ending flags, so a silent drift in the
/// thresholds would quietly re-gate authored content — pin them.
/// </summary>
public class CompanionLoyaltyTests
{
    [Theory]
    [InlineData(-40, CompanionLoyalty.Min)]
    [InlineData(0, 0)]
    [InlineData(50, 50)]
    [InlineData(100, 100)]
    [InlineData(9999, CompanionLoyalty.Max)]
    public void ClampKeepsLoyaltyInRange(int raw, int expected)
    {
        Assert.Equal(expected, CompanionLoyalty.Clamp(raw));
    }

    [Theory]
    [InlineData(0, LoyaltyTier.Wary)]
    [InlineData(34, LoyaltyTier.Wary)]
    [InlineData(CompanionLoyalty.SteadyThreshold, LoyaltyTier.Steady)]
    [InlineData(64, LoyaltyTier.Steady)]
    [InlineData(CompanionLoyalty.TrustedThreshold, LoyaltyTier.Trusted)]
    [InlineData(89, LoyaltyTier.Trusted)]
    [InlineData(CompanionLoyalty.SwornThreshold, LoyaltyTier.Sworn)]
    [InlineData(100, LoyaltyTier.Sworn)]
    public void TiersFallOnTheAuthoredThresholds(int value, LoyaltyTier expected)
    {
        Assert.Equal(expected, CompanionLoyalty.Of(value));
    }

    [Fact]
    public void OutOfRangeValuesStillTier()
    {
        // Of() clamps first, so a bad caller can't produce a tierless companion.
        Assert.Equal(LoyaltyTier.Wary, CompanionLoyalty.Of(-500));
        Assert.Equal(LoyaltyTier.Sworn, CompanionLoyalty.Of(500));
    }

    [Fact]
    public void CombatBonusRisesWithEachTier()
    {
        float wary = CompanionLoyalty.CombatBonus(LoyaltyTier.Wary);
        float steady = CompanionLoyalty.CombatBonus(LoyaltyTier.Steady);
        float trusted = CompanionLoyalty.CombatBonus(LoyaltyTier.Trusted);
        float sworn = CompanionLoyalty.CombatBonus(LoyaltyTier.Sworn);

        Assert.Equal(0f, wary);
        Assert.True(steady > wary);
        Assert.True(trusted > steady);
        Assert.True(sworn > trusted);
    }

    [Fact]
    public void EveryTierHasItsOwnNameKey()
    {
        Assert.Equal("companion.loyalty.wary", CompanionLoyalty.NameKey(LoyaltyTier.Wary));
        Assert.Equal("companion.loyalty.steady", CompanionLoyalty.NameKey(LoyaltyTier.Steady));
        Assert.Equal("companion.loyalty.trusted", CompanionLoyalty.NameKey(LoyaltyTier.Trusted));
        Assert.Equal("companion.loyalty.sworn", CompanionLoyalty.NameKey(LoyaltyTier.Sworn));
    }

    [Theory]
    [InlineData("companion.kael:10", "companion.kael", 10)]
    [InlineData("companion.kael:-15", "companion.kael", -15)]
    [InlineData("  companion.kael : 65 ", "companion.kael", 65)]
    [InlineData("companion.kael", "companion.kael", 0)]
    public void DialogueArgumentsSplitOnTheLastColon(string arg, string expectedId, int expectedAmount)
    {
        Assert.True(CompanionArg.TryParse(arg, out string id, out int amount));
        Assert.Equal(expectedId, id);
        Assert.Equal(expectedAmount, amount);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void EmptyDialogueArgumentsAreRejected(string? arg)
    {
        Assert.False(CompanionArg.TryParse(arg, out _, out _));
    }

    [Fact]
    public void MalformedAmountDegradesToNoChange()
    {
        // A content typo must not throw mid-conversation; it becomes a no-op the validator reports.
        Assert.True(CompanionArg.TryParse("companion.kael:soon", out string id, out int amount));
        Assert.Equal("companion.kael", id);
        Assert.Equal(0, amount);
    }
}
