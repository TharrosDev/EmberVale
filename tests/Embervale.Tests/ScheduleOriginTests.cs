using Embervale.Npc;
using Godot;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// Pins <see cref="ScheduleMath.Destination"/> (Phase 38L). Two claims, and the first matters more
/// than the second: a zero origin must be exactly the pre-38L behaviour, because the nine routines
/// authored before it were left untouched on the strength of that, and a silent shift in the town
/// square's schedules would read as the NPCs being broken rather than as a new field.
///
/// ⚠️ Tested through the pure helper rather than through <see cref="ScheduleResource"/>: a
/// <c>Resource</c> is a native Godot object and constructing one in this project crashes the test host
/// with an <c>AccessViolationException</c>. That is the same constraint <c>ShopStock</c> exists for.
/// </summary>
public class ScheduleOriginTests
{
    [Fact]
    public void AZeroOriginLeavesADestinationExactlyWhereItWasAuthored()
    {
        // schedule.vendor_goods' real first block, which must not move.
        Assert.Equal(new Vector3(6, 0, -18),
            ScheduleMath.Destination(new Vector3(6, 0, -18), Vector3.Zero));
    }

    [Fact]
    public void ACellOriginMovesADestinationByExactlyThatCellsCentre()
    {
        // The Embermarket's actual numbers: the cell is authored at its own origin and streamed to
        // Center (0, 0, 46), so StallW1 - read out of embermarket.tscn at local (-9, 0, -14) - is at
        // world (-9, 0, 32). Getting this wrong walks a merchant into the town square.
        Assert.Equal(new Vector3(-9, 0, 32),
            ScheduleMath.Destination(new Vector3(-9, 0, -14), new Vector3(0, 0, 46)));
        Assert.Equal(new Vector3(9, 0, 54),
            ScheduleMath.Destination(new Vector3(9, 0, 8), new Vector3(0, 0, 46)));
    }

    [Fact]
    public void TheOffsetIsAppliedOnEveryAxis()
    {
        Assert.Equal(new Vector3(3, 5, 12),
            ScheduleMath.Destination(new Vector3(1, 2, 3), new Vector3(2, 3, 9)));
    }
}
