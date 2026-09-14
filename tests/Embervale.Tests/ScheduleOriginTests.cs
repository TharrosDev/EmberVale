using Embervale.Npc;
using Godot;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// Pins <see cref="ScheduleMath.Destination"/>: a routine's destinations are CELL-LOCAL and become a
/// place only once the world position of the NPC's own streamed cell is added (2026-09 world rebuild,
/// which deleted the hand-copied per-file Origin that pinned 26 routines to where their cell had been).
///
/// ⚠️ Tested through the pure helper rather than through <see cref="ScheduleResource"/>: a
/// <c>Resource</c> is a native Godot object and constructing one in this project crashes the test host
/// with an <c>AccessViolationException</c>. That is the same constraint <c>ShopStock</c> exists for.
/// </summary>
public class ScheduleOriginTests
{
    [Fact]
    public void ACellAtTheOriginLeavesADestinationExactlyWhereItWasAuthored()
    {
        Assert.Equal(new Vector3(6, 0, -18),
            ScheduleMath.Destination(new Vector3(6, 0, -18), Vector3.Zero));
    }

    [Fact]
    public void ACellMovesADestinationByExactlyThatCellsCentre()
    {
        // A merchant's stall read out of a cell .tscn at local (-9, 0, -14), in a cell streamed to
        // (0, 0, 85). Getting this wrong walks a merchant into the town square.
        Assert.Equal(new Vector3(-9, 0, 71),
            ScheduleMath.Destination(new Vector3(-9, 0, -14), new Vector3(0, 0, 85)));
    }

    [Fact]
    public void TheOffsetIsAppliedOnEveryAxis()
    {
        Assert.Equal(new Vector3(3, 5, 12),
            ScheduleMath.Destination(new Vector3(1, 2, 3), new Vector3(2, 3, 9)));
    }
}
