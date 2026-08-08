using Godot;

namespace Embervale.Npc;

/// <summary>
/// Pure schedule-block selection behind <see cref="ScheduleResource.EntryForHour"/>. Kept Godot-free
/// so the wrap-around lookup — which block covers a given hour — is unit-testable without authoring
/// <see cref="ScheduleEntry"/> resources.
/// </summary>
public static class ScheduleMath
{
    /// <summary>
    /// The index into <paramref name="startHours"/> of the block active at <paramref name="hour"/>:
    /// the entry with the greatest start hour at or before it. Hours before the first block wrap to the
    /// latest block of the day (the previous night's activity continues). Returns <c>-1</c> for an empty
    /// schedule. Entries need not be ordered.
    /// </summary>
    public static int ActiveEntryIndex(int[] startHours, int hour)
    {
        int chosen = -1;
        int latest = -1;

        for (int i = 0; i < startHours.Length; i++)
        {
            if (startHours[i] <= hour && (chosen == -1 || startHours[i] > startHours[chosen]))
            {
                chosen = i;
            }

            if (latest == -1 || startHours[i] > startHours[latest])
            {
                latest = i;
            }
        }

        return chosen != -1 ? chosen : latest;
    }

    /// <summary>
    /// Where a routine block actually sends its NPC: the authored point plus the routine's origin
    /// (Phase 38L). A <see cref="ScheduleEntry.Destination"/> is a raw <em>world</em> position, but a
    /// region cell is authored at its own origin and moved to the cell's <c>Center</c> by the
    /// streamer, so a point copied out of a cell's <c>.tscn</c> lands a cell's width away — the 37C
    /// placement bug in a different hat, and silent. <see cref="ScheduleResource.Origin"/> carries the
    /// cell offset; a zero origin is exactly the pre-38L behaviour, which is why the nine routines
    /// authored before it needed no migration.
    ///
    /// <para>Godot-free by the same rule as everything else in this file: <c>Vector3</c> is a plain
    /// struct, so the test project can call this, while a <c>Resource</c> is a native object it
    /// crashes trying to construct.</para>
    /// </summary>
    public static Vector3 Destination(Vector3 authored, Vector3 origin) => authored + origin;
}
