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

    // --- 41B: the Defend hold accumulator ------------------------------------

    [Fact]
    public void TickHold_FourQuarterTicks_MakeExactlyOneSecond()
    {
        float held = 0f;
        int earned = 0;
        for (int i = 0; i < 4; i++)
        {
            earned += ObjectiveProgress.TickHold(ref held, 0.25f);
        }

        Assert.Equal(1, earned);
    }

    [Fact]
    public void TickHold_KeepsTheRemainderRatherThanDroppingIt()
    {
        // Three quarters earn nothing yet, but the fourth must not have to start from zero — a hold
        // that dropped the remainder every tick would never complete at all.
        float held = 0f;
        Assert.Equal(0, ObjectiveProgress.TickHold(ref held, 0.25f));
        Assert.Equal(0, ObjectiveProgress.TickHold(ref held, 0.25f));
        Assert.Equal(0, ObjectiveProgress.TickHold(ref held, 0.25f));
        Assert.True(held > 0.7f);
        Assert.Equal(1, ObjectiveProgress.TickHold(ref held, 0.25f));
    }

    [Fact]
    public void TickHold_SixtySecondsOfQuarterTicks_EarnsExactlySixty()
    {
        // The authored hold in HoldTheNorthRoad.tres, ticked the way the 4 Hz poll ticks it. Float
        // drift accumulating to 59 or 61 would be invisible in play and would either hang the quest
        // or complete it early.
        float held = 0f;
        int earned = 0;
        for (int i = 0; i < 240; i++)
        {
            earned += ObjectiveProgress.TickHold(ref held, 0.25f);
        }

        Assert.Equal(60, earned);
    }

    [Fact]
    public void TickHold_ALongTick_EarnsEverySecondInIt()
    {
        // A frame spike (or a resumed poll) hands over more than one second at once; those seconds
        // were still stood through and must not be truncated to one.
        float held = 0f;
        Assert.Equal(3, ObjectiveProgress.TickHold(ref held, 3.5f));
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(-1f)]
    public void TickHold_NonPositiveDelta_EarnsNothingAndKeepsTheClock(float delta)
    {
        float held = 0.75f;
        Assert.Equal(0, ObjectiveProgress.TickHold(ref held, delta));
        Assert.Equal(0.75f, held);
    }
}
