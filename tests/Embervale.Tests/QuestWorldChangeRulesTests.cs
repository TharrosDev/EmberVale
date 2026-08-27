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
}
