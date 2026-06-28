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
}
