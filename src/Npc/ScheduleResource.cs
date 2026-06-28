using System.Collections.Generic;
using Godot;

namespace Embervale.Npc;

/// <summary>
/// A designer-authored daily routine: an ordered set of <see cref="ScheduleEntry"/>
/// blocks keyed by start hour. Authored as a <c>.tres</c> under <c>data/schedules/</c>
/// and indexed by <see cref="ScheduleDatabase"/>; a <see cref="ScheduleComponent"/> on an
/// NPC follows the block whose time window covers the current <see cref="World.WorldClock"/>
/// hour.
///
/// New routine = a <c>.tres</c>, no code change.
/// </summary>
[GlobalClass]
public partial class ScheduleResource : Resource
{
    /// <summary>Stable unique id, e.g. "schedule.elder". The database key.</summary>
    [Export] public string Id { get; set; } = "schedule.unknown";

    /// <summary>Routine blocks. Untyped so authored sub-resource arrays bind cleanly;
    /// elements are read back as <see cref="ScheduleEntry"/>.</summary>
    [Export] public Godot.Collections.Array Entries { get; set; } = new();

    /// <summary>The entries read back as their concrete type, skipping bad elements.</summary>
    public List<ScheduleEntry> EntryList()
    {
        var list = new List<ScheduleEntry>();
        foreach (Variant element in Entries)
        {
            if (element.As<ScheduleEntry>() is { } entry)
            {
                list.Add(entry);
            }
        }

        return list;
    }

    /// <summary>
    /// The block active at <paramref name="hour"/>: the entry with the greatest
    /// <see cref="ScheduleEntry.StartHour"/> at or before it. Hours before the first
    /// block wrap to the last block of the day (the previous night's activity continues).
    /// </summary>
    public ScheduleEntry? EntryForHour(int hour)
    {
        List<ScheduleEntry> entries = EntryList();
        var startHours = new int[entries.Count];
        for (int i = 0; i < entries.Count; i++)
        {
            startHours[i] = entries[i].StartHour;
        }

        int index = ScheduleMath.ActiveEntryIndex(startHours, hour);
        return index >= 0 ? entries[index] : null;
    }
}
