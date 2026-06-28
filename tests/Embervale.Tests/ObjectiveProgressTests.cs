using Embervale.Quests;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// Pins the objective-completion predicate behind <see cref="QuestProgress"/> — the "no stuck
/// objectives" boundary. The full quest flow (advance, reward grant) runs in-engine over Godot
/// resources; the count-vs-required comparison is pure and lives in <see cref="ObjectiveProgress"/>.
/// </summary>
public class ObjectiveProgressTests
{
    [Theory]
    [InlineData(0, 1, false)]  // no progress yet
    [InlineData(1, 1, true)]   // exact
    [InlineData(3, 2, true)]   // over-count still complete
    [InlineData(0, 0, true)]   // zero requirement is met immediately — can never stick
    [InlineData(0, -1, true)]  // negative requirement also met immediately
    public void IsComplete_MatchesBoundary(int count, int required, bool expected)
    {
        Assert.Equal(expected, ObjectiveProgress.IsComplete(count, required));
    }

    [Fact]
    public void AllMet_EmptyObjectiveList_IsTriviallyComplete()
    {
        Assert.True(ObjectiveProgress.AllMet(new int[0], new int[0]));
    }

    [Fact]
    public void AllMet_EveryObjectiveSatisfied_IsTrue()
    {
        Assert.True(ObjectiveProgress.AllMet(new[] { 3, 1, 5 }, new[] { 3, 1, 2 }));
    }

    [Fact]
    public void AllMet_OneObjectiveShort_IsFalse()
    {
        Assert.False(ObjectiveProgress.AllMet(new[] { 3, 0, 5 }, new[] { 3, 1, 2 }));
    }

    [Fact]
    public void AllMet_AllZeroRequirements_IsComplete()
    {
        Assert.True(ObjectiveProgress.AllMet(new[] { 0, 0 }, new[] { 0, 0 }));
    }
}
