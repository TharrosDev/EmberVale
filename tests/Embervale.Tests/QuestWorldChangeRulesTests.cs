using Embervale.Quests;
using Embervale.World;
using Xunit;

namespace Embervale.Tests;

public sealed class QuestWorldChangeRulesTests
{
    [Fact]
    public void CompletionFlag_IsWrittenOnlyOnce()
    {
        Assert.True(QuestCompletionRules.ShouldSetFlag("flag.frostfang.passage_open", alreadySet: false));
        Assert.False(QuestCompletionRules.ShouldSetFlag("flag.frostfang.passage_open", alreadySet: true));
        Assert.False(QuestCompletionRules.ShouldSetFlag(string.Empty, alreadySet: false));
    }

    [Fact]
    public void CompletionFlag_UsesStoryFlagFamily()
    {
        Assert.True(QuestCompletionRules.IsValidFlagId("flag.coyle.departed"));
        Assert.True(QuestCompletionRules.IsValidFlagId(string.Empty));
        Assert.False(QuestCompletionRules.IsValidFlagId("quest.coyle.departed"));
    }

    [Fact]
    public void WorldActor_ReDerivesPresenceFromItsFlag()
    {
        Assert.False(FlagVisibilityRules.ShouldHide(string.Empty, hasFlag: true));
        Assert.False(FlagVisibilityRules.ShouldHide("flag.coyle.departed", hasFlag: false));
        Assert.True(FlagVisibilityRules.ShouldHide("flag.coyle.departed", hasFlag: true));
    }

    [Theory]
    [InlineData("", false, "", false, true)]                      // ungated: present
    [InlineData("flag.gone", true, "", false, false)]             // HiddenWhen set: gone
    [InlineData("", false, "flag.arrived", false, false)]         // VisibleWhen unset: not yet
    [InlineData("", false, "flag.arrived", true, true)]           // VisibleWhen set: present
    [InlineData("flag.gone", true, "flag.arrived", true, false)]  // hidden wins
    [InlineData("flag.gone", false, "flag.arrived", true, true)]
    public void WorldActor_ShouldBePresent_CombinesBothGates(
        string hiddenWhen, bool hiddenSet, string visibleWhen, bool visibleSet, bool expected)
    {
        Assert.Equal(expected, FlagVisibilityRules.ShouldBePresent(hiddenWhen, hiddenSet, visibleWhen, visibleSet));
    }
}
