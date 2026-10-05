using Embervale.Backgrounds;
using Embervale.Races;
using Xunit;

namespace Embervale.Tests;

public class BackgroundRulesTests
{
    [Theory]
    [InlineData("background.soldier", "background.soldier")]
    [InlineData("  background.hunter  ", "background.hunter")]
    [InlineData("", "")]
    [InlineData(null, "")]
    [InlineData("background.", "")]
    [InlineData("Exile of the deep wood.", "")]
    [InlineData("A traveller from the Reach", "")]
    public void ResolveId_KeepsIdsAndTreatsEverythingElseAsNone(string? raw, string expected)
    {
        Assert.Equal(expected, BackgroundRules.ResolveId(raw));
    }

    [Fact]
    public void LegacyFreeText_StillRoundTripsThroughTheHeaderButResolvesToNone()
    {
        var legacy = new CharacterProfile { Background = "Exile of the deep wood." };

        CharacterProfile restored = CharacterProfile.FromHeaderFields(legacy.ToHeaderFields());

        Assert.Equal("Exile of the deep wood.", restored.Background);
        Assert.Equal(string.Empty, BackgroundRules.ResolveId(restored.Background));
    }

    [Fact]
    public void BackgroundId_RoundTripsThroughTheHeaderAndStaysResolvable()
    {
        var chosen = new CharacterProfile { RaceId = "race.umbral", Background = "background.cutpurse" };

        CharacterProfile restored = CharacterProfile.FromHeaderFields(chosen.ToHeaderFields());

        Assert.Equal("background.cutpurse", BackgroundRules.ResolveId(restored.Background));
    }

    [Fact]
    public void HumanDefault_HasNoBackground()
    {
        Assert.Equal(string.Empty, BackgroundRules.ResolveId(CharacterProfile.Human.Background));
    }

    [Theory]
    [InlineData("item.potion.health", "item.potion.health", 1)]
    [InlineData("item.food.field_ration:2", "item.food.field_ration", 2)]
    [InlineData(" item.ammo.arrows : 10 ", "item.ammo.arrows", 10)]
    public void TryParseItem_ReadsIdAndCount(string entry, string id, int count)
    {
        Assert.True(BackgroundRules.TryParseItem(entry, out string parsedId, out int parsedCount));
        Assert.Equal(id, parsedId);
        Assert.Equal(count, parsedCount);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("item.potion.health:0")]
    [InlineData("item.potion.health:-2")]
    [InlineData("item.potion.health:many")]
    [InlineData(":3")]
    public void TryParseItem_RejectsMalformedOrNonPositiveEntries(string? entry)
    {
        Assert.False(BackgroundRules.TryParseItem(entry, out _, out _));
    }

    [Fact]
    public void KitValue_IsGoldPlusItemValueTimesCount()
    {
        int value = BackgroundRules.KitValue(10, new[] { (35, 1), (2, 10), (7, 2) });

        Assert.Equal(10 + 35 + 20 + 14, value);
    }

    [Fact]
    public void KitValue_IgnoresNegativeInputs()
    {
        Assert.Equal(5, BackgroundRules.KitValue(-20, new[] { (5, 1), (-9, 3), (4, -2) }));
    }

    [Fact]
    public void KitCap_AllowsExactlyTheMaximum()
    {
        Assert.True(BackgroundRules.KitWithinCap(BackgroundRules.MaxKitValue));
        Assert.False(BackgroundRules.KitWithinCap(BackgroundRules.MaxKitValue + 1));
    }

    [Fact]
    public void StatAndReputationCaps_AreSymmetric()
    {
        Assert.True(BackgroundRules.StatDeltaWithinCap(1f));
        Assert.True(BackgroundRules.StatDeltaWithinCap(-1f));
        Assert.False(BackgroundRules.StatDeltaWithinCap(1.5f));
        Assert.True(BackgroundRules.ReputationWithinCap(-BackgroundRules.MaxReputationTweak));
        Assert.False(BackgroundRules.ReputationWithinCap(BackgroundRules.MaxReputationTweak + 1));
    }

    [Theory]
    [InlineData("", true)]
    [InlineData("warrior", true)]
    [InlineData("social", true)]
    [InlineData("paladin", false)]
    [InlineData("Warrior", false)]
    public void LeanBranch_IsAKnownBranchOrEmpty(string branch, bool valid)
    {
        Assert.Equal(valid, BackgroundRules.IsLeanBranch(branch));
    }
}
