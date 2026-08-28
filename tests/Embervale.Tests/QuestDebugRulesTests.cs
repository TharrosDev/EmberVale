using Embervale.Quests;
using Xunit;

namespace Embervale.Tests;

public sealed class QuestDebugRulesTests
{
    [Fact]
    public void CanAdvance_AllowsOnlyAnActiveLiveIncompleteObjective()
    {
        Assert.Equal(QuestDebugAdvanceResult.Advanced,
            QuestDebugRules.CanAdvance(QuestStatus.Active, validIndex: true, active: true, complete: false, amount: 1));
        Assert.Equal(QuestDebugAdvanceResult.InertObjective,
            QuestDebugRules.CanAdvance(QuestStatus.Active, validIndex: true, active: false, complete: false, amount: 1));
        Assert.Equal(QuestDebugAdvanceResult.CompleteObjective,
            QuestDebugRules.CanAdvance(QuestStatus.Active, validIndex: true, active: true, complete: true, amount: 1));
    }

    [Fact]
    public void CanAdvance_RefusesBadIndexOrAmountBeforeMutatingTheLog()
    {
        Assert.Equal(QuestDebugAdvanceResult.InvalidObjective,
            QuestDebugRules.CanAdvance(QuestStatus.Active, validIndex: false, active: false, complete: false, amount: 1));
        Assert.Equal(QuestDebugAdvanceResult.InvalidAmount,
            QuestDebugRules.CanAdvance(QuestStatus.Active, validIndex: true, active: true, complete: false, amount: 0));
        Assert.Equal(QuestDebugAdvanceResult.NotActive,
            QuestDebugRules.CanAdvance(QuestStatus.Completed, validIndex: true, active: true, complete: false, amount: 1));
    }
}
