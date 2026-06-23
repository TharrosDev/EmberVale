using System.Collections.Generic;
using Embervale.Save;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// Covers the pure ring-rotation choice behind the Phase 24D autosave cadence. The cadence itself
/// (timers, event triggers, the IsPlaying guard, debounce) runs against Godot and is exercised
/// in-engine via the <c>autosave</c> dev command, but <see cref="AutosaveService.NextAutosaveSlot"/>
/// decides which of <c>auto1..auto3</c> gets overwritten and is load-bearing for "never clobber the
/// only recent copy", so it is pinned here.
/// </summary>
public class AutosaveServiceTests
{
    private static SaveSlotInfo Slot(string id, double stamp) =>
        new() { Slot = id, TimestampUnix = stamp };

    [Fact]
    public void EmptyRing_PicksFirstSlot()
    {
        Assert.Equal("auto1", AutosaveService.NextAutosaveSlot(new List<SaveSlotInfo>()));
    }

    [Fact]
    public void PartiallyFilled_PicksFirstEmptySlot()
    {
        // auto1 exists; auto2 is the first empty ring member and wins over overwriting auto1.
        var existing = new List<SaveSlotInfo> { Slot("auto1", 100d) };
        Assert.Equal("auto2", AutosaveService.NextAutosaveSlot(existing));
    }

    [Fact]
    public void FullRing_PicksOldestTimestamp()
    {
        var existing = new List<SaveSlotInfo>
        {
            Slot("auto1", 300d),
            Slot("auto2", 100d), // oldest
            Slot("auto3", 200d),
        };
        Assert.Equal("auto2", AutosaveService.NextAutosaveSlot(existing));
    }

    [Fact]
    public void FullRing_IgnoresNonRingSlots()
    {
        // Manual slots and the quick slot must never be chosen for autosave, even if older.
        var existing = new List<SaveSlotInfo>
        {
            Slot("quick", 1d),
            Slot("slot1", 2d),
            Slot("auto1", 300d),
            Slot("auto2", 250d),
            Slot("auto3", 100d), // oldest ring member
        };
        Assert.Equal("auto3", AutosaveService.NextAutosaveSlot(existing));
    }
}
