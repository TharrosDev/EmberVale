using System.Collections.Generic;
using Embervale.Save;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// Covers the pure half of the autosave cadence: <see cref="AutosaveService.NextAutosaveSlot"/>,
/// which seeds the ring from the slot headers and is load-bearing for "never clobber the only
/// recent copy", and <see cref="AutosaveCadence"/>, which decides when a save is due, when a due
/// save waits, and where the ring goes next. What runs against Godot (the event subscriptions, the
/// IsPlaying guard, the combat and airborne probes) is exercised in-engine.
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

    // --- AutosaveCadence -----------------------------------------------------------------------

    private const int Ring = 3;

    /// <summary>Advances in one-second steps and returns the first reason the cadence wants written.</summary>
    private static string? RunUntilWrite(AutosaveCadence cadence, double seconds, bool blocked = false, bool busy = false)
    {
        for (double t = 0d; t < seconds; t += 1d)
        {
            if (cadence.Advance(1d) && cadence.Decide(blocked, busy) is { } reason)
            {
                return reason;
            }
        }

        return null;
    }

    [Fact]
    public void Interval_FiresOnlyAfterTheFullInterval()
    {
        var cadence = new AutosaveCadence(Ring);
        Assert.Null(RunUntilWrite(cadence, AutosaveCadence.IntervalSeconds - 1d));
        Assert.Equal(AutosaveCadence.IntervalReason, RunUntilWrite(cadence, 2d));
    }

    [Fact]
    public void Request_IsWrittenAfterItsDelay_NotInsideTheEvent()
    {
        var cadence = new AutosaveCadence(Ring);
        Assert.True(cadence.Request("quest complete", 3d));
        Assert.False(cadence.Advance(1d));
        Assert.False(cadence.Advance(1d));
        Assert.True(cadence.Advance(1d));
        Assert.Equal("quest complete", cadence.Decide(blocked: false, busy: false));
    }

    [Fact]
    public void Request_InsideTheDebounceWindow_IsDropped()
    {
        var cadence = new AutosaveCadence(Ring);
        cadence.Saved();
        cadence.Advance(AutosaveCadence.MinSecondsBetweenAutosaves - 1d);
        Assert.False(cadence.Request("level up", 0d));
        Assert.Null(cadence.PendingReason);

        cadence.Advance(1d);
        Assert.True(cadence.Request("level up", 0d));
    }

    [Fact]
    public void SecondRequest_JoinsTheFirst_InsteadOfQueueingASecondWrite()
    {
        var cadence = new AutosaveCadence(Ring);
        cadence.Request("quest complete", 5d);
        cadence.Request("level up", 1d);
        Assert.Equal("quest complete", RunUntilWrite(cadence, 2d));
        cadence.Saved();
        Assert.Null(cadence.PendingReason);
    }

    [Fact]
    public void HardBlock_DefersForAsLongAsItLasts_ThenWrites()
    {
        var cadence = new AutosaveCadence(Ring);
        cadence.Request("quest complete", 0d);
        Assert.Null(RunUntilWrite(cadence, AutosaveCadence.MaxSoftDeferSeconds * 3d, blocked: true));
        Assert.True(cadence.IsDeferred);
        Assert.Equal("quest complete", RunUntilWrite(cadence, AutosaveCadence.DeferPollSeconds + 1d));
    }

    [Fact]
    public void DeferredSave_AsksTheWorldOncePerPoll_NotEveryFrame()
    {
        var cadence = new AutosaveCadence(Ring);
        cadence.Request("quest complete", 0d);
        Assert.True(cadence.Advance(0.1d));
        Assert.Null(cadence.Decide(blocked: true, busy: false));

        int asks = 0;
        for (int frame = 0; frame < 100; frame++)
        {
            if (cadence.Advance(0.1d))
            {
                asks++;
                cadence.Decide(blocked: true, busy: false);
            }
        }

        Assert.InRange(asks, 8, 11); // ten seconds of frames, about one poll a second
    }

    [Fact]
    public void SoftBusy_DefersButGivesUpAtTheCap()
    {
        var cadence = new AutosaveCadence(Ring);
        cadence.Request("level up", 0d);
        Assert.Null(RunUntilWrite(cadence, AutosaveCadence.MaxSoftDeferSeconds - 5d, busy: true));
        Assert.Equal("level up", RunUntilWrite(cadence, 10d, busy: true));
    }

    [Fact]
    public void Saved_AdvancesTheRing_AndWraps()
    {
        var cadence = new AutosaveCadence(Ring);
        cadence.StartRingAt(1);
        cadence.Saved();
        Assert.Equal(2, cadence.RingIndex);
        cadence.Saved();
        Assert.Equal(0, cadence.RingIndex);
    }

    [Fact]
    public void Failed_KeepsTheRingAndRetriesAfterTheBackoff()
    {
        var cadence = new AutosaveCadence(Ring);
        cadence.StartRingAt(2);
        Assert.Equal(AutosaveCadence.IntervalReason, RunUntilWrite(cadence, AutosaveCadence.IntervalSeconds + 1d));
        cadence.Failed();
        Assert.Equal(2, cadence.RingIndex);
        Assert.Null(RunUntilWrite(cadence, AutosaveCadence.RetrySeconds - 1d));
        Assert.Equal(AutosaveCadence.IntervalReason, RunUntilWrite(cadence, 2d));
    }

    [Fact]
    public void FailedAfterAccepted_StepsTheRingBackToTheSlotThatStillHoldsItsOldSave()
    {
        var cadence = new AutosaveCadence(Ring);
        cadence.StartRingAt(0);
        cadence.Saved();
        Assert.Equal(1, cadence.RingIndex);
        cadence.Failed(afterSaved: true);
        Assert.Equal(0, cadence.RingIndex);
    }

    [Fact]
    public void SecondsSinceLastSave_FollowsTheDisk_NotTheRequest()
    {
        var cadence = new AutosaveCadence(Ring);
        cadence.Advance(40d);
        cadence.Saved(); // accepted, not yet confirmed
        Assert.Equal(40d, cadence.SecondsSinceLastSave, 3);
        cadence.SaveLanded(isAutosave: true);
        Assert.Equal(0d, cadence.SecondsSinceLastSave, 3);
    }

    [Fact]
    public void ManualSave_RestartsTheIntervalAndDropsAPendingAutosave_ButLeavesTheRing()
    {
        var cadence = new AutosaveCadence(Ring);
        cadence.StartRingAt(1);
        cadence.Advance(AutosaveCadence.IntervalSeconds - 10d);
        cadence.Request("quest complete", 5d);
        cadence.SaveLanded(isAutosave: false);

        Assert.Null(cadence.PendingReason);
        Assert.Equal(1, cadence.RingIndex);
        Assert.Null(RunUntilWrite(cadence, AutosaveCadence.IntervalSeconds - 1d));
        Assert.Equal(AutosaveCadence.IntervalReason, RunUntilWrite(cadence, 2d));
    }
}
