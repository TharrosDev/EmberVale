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

    /// <summary>
    /// World-space offset added to every entry's <see cref="ScheduleEntry.Destination"/> (Phase 38L).
    /// Zero — the default — is the behaviour every routine authored before 38L already had, so the
    /// nine existing schedules needed no migration. The same "the default is the ungated case" trick
    /// 38I's three stock gates play.
    ///
    /// <para><b>Why it exists.</b> <see cref="ScheduleEntry.Destination"/> is a raw <em>world</em>
    /// position, but a region cell is authored at its own origin and moved to the cell's
    /// <c>Center</c> by the <see cref="World.RegionStreamer"/> — so a destination copied out of a
    /// cell's <c>.tscn</c> lands a cell's width from where it was read. That is the 37C placement bug
    /// wearing a different hat, it is silent, and the Embermarket is 46 m south of the origin the
    /// town square's schedules were written against. Setting <c>Origin</c> once per file lets a
    /// routine be authored in the coordinates a designer actually has in front of them.</para>
    /// </summary>
    [Export] public Vector3 Origin { get; set; } = Vector3.Zero;

    /// <summary>The world destination of a routine block: its authored point plus this routine's
    /// <see cref="Origin"/>. The single place the two are combined, so no caller can forget.</summary>
    public Vector3 DestinationOf(ScheduleEntry entry) =>
        ScheduleMath.Destination(entry.Destination, Origin);

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
