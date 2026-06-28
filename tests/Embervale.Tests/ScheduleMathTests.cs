using Embervale.Npc;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// Pins the schedule wrap-around lookup behind <see cref="ScheduleResource.EntryForHour"/>: which block
/// covers a given hour, wrapping pre-first-block hours to the last block of the day.
/// </summary>
public class ScheduleMathTests
{
    // A typical day: sleep@22, wake/work@6, market@12, home@18 (authored out of order on purpose).
    private static readonly int[] Day = { 6, 22, 12, 18 };

    [Fact]
    public void ActiveEntryIndex_ExactStartHour_PicksThatBlock()
    {
        Assert.Equal(0, ScheduleMath.ActiveEntryIndex(Day, 6));   // index of start-hour 6
        Assert.Equal(2, ScheduleMath.ActiveEntryIndex(Day, 12));  // index of start-hour 12
    }

    [Fact]
    public void ActiveEntryIndex_MidBlock_PicksTheBlockInProgress()
    {
        Assert.Equal(0, ScheduleMath.ActiveEntryIndex(Day, 9));   // between 6 and 12 → the 6 block
        Assert.Equal(3, ScheduleMath.ActiveEntryIndex(Day, 20));  // between 18 and 22 → the 18 block
    }

    [Fact]
    public void ActiveEntryIndex_BeforeFirstBlock_WrapsToTheLatestBlock()
    {
        // 03:00 is before the earliest start (6); the previous night's block (start 22) still applies.
        Assert.Equal(1, ScheduleMath.ActiveEntryIndex(Day, 3));
    }

    [Fact]
    public void ActiveEntryIndex_SingleEntry_AlwaysThatEntry()
    {
        Assert.Equal(0, ScheduleMath.ActiveEntryIndex(new[] { 8 }, 8));
        Assert.Equal(0, ScheduleMath.ActiveEntryIndex(new[] { 8 }, 0)); // wraps to the only block
    }

    [Fact]
    public void ActiveEntryIndex_EmptySchedule_ReturnsMinusOne()
    {
        Assert.Equal(-1, ScheduleMath.ActiveEntryIndex(new int[0], 10));
    }
}
