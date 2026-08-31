using System.Collections.Generic;
using Embervale.Core;
using Embervale.Core.Diagnostics;
using Embervale.Core.Events;
using Embervale.Progression;
using Embervale.Quests;
using Godot;

namespace Embervale.Save;

/// <summary>
/// Owns the <b>autosave cadence</b> (Phase 24D) on top of the slot-based <see cref="SaveManager"/>.
/// Created by the bootstrap when a world is built and registered in the <c>ServiceLocator</c>; the
/// <see cref="SaveManager"/> stays the low-level writer (this mirrors the EncounterDirector /
/// WorldEventDirector pattern and keeps save I/O decoupled from gameplay events).
///
/// Autosaves rotate through a small <see cref="RingSlots"/> ring (<c>auto1..auto3</c>) so a bad
/// write never clobbers the only recent copy; the next slot is chosen empty-or-oldest from the
/// on-disk headers (<see cref="NextAutosaveSlot"/>), so rotation survives restarts with no extra
/// persistence. They never touch <see cref="SaveManager.ActiveSlot"/>, so F5/F9 and pause-menu
/// Save/Load keep targeting the player's chosen slot.
///
/// Triggers: a time interval over <b>active</b> play, <see cref="QuestCompletedEvent"/>,
/// <see cref="LeveledUpEvent"/>, and a documented region-change seam
/// (<see cref="RequestRegionChangeAutosave"/>) the Phase 25 streamer will call. Every path is
/// guarded so autosave only fires while <see cref="GameManager.IsPlaying"/> and is debounced by
/// <see cref="MinSecondsBetweenAutosaves"/> so two triggers in quick succession can't double-write.
/// </summary>
public sealed partial class AutosaveService : Node
{
    /// <summary>The rotating autosave slot ids, oldest-overwritten. Shared with the load browser.</summary>
    public static readonly string[] RingSlots = { "auto1", "auto2", "auto3" };

    /// <summary>Whether a slot id belongs to the autosave ring (used by the slot browser).</summary>
    public static bool IsAutosaveSlot(string slot) => System.Array.IndexOf(RingSlots, slot) >= 0;

    private const double IntervalSeconds = 300d;           // ~5 min of active play between interval autosaves
    private const double MinSecondsBetweenAutosaves = 60d; // debounce: floor between any two autosaves

    /// <summary>Active seconds before a <b>failed</b> autosave is tried again. ⚠️ A failure used to
    /// reset the full interval, so a transient write error (the file locked by a backup tool, a
    /// full disk that emptied a moment later) cost the player the next five minutes of play as
    /// surely as a successful save would have — and silently, because the only trace was one
    /// warning. The clocks are now only reset on a write that actually landed.</summary>
    private const double RetrySeconds = 30d;

    private double _sinceInterval;                         // active-time accumulator toward the next interval save
    private double _sinceLastAutosave = double.PositiveInfinity; // active-time since the last autosave (debounce)

    public override void _EnterTree()
    {
        EventBus bus = EventBus.Instance;
        bus?.Subscribe<QuestCompletedEvent>(OnQuestCompleted);
        bus?.Subscribe<LeveledUpEvent>(OnLeveledUp);
    }

    public override void _ExitTree()
    {
        EventBus? bus = EventBus.Instance;
        bus?.Unsubscribe<QuestCompletedEvent>(OnQuestCompleted);
        bus?.Unsubscribe<LeveledUpEvent>(OnLeveledUp);
    }

    public override void _Process(double delta)
    {
        // Only active gameplay time counts — paused/loading/menu time must not advance the cadence
        // (and must never autosave). Same IsPlaying gate the SaveManager uses for playtime.
        if (GameManager.Instance is not { IsPlaying: true })
        {
            return;
        }

        _sinceInterval += delta;
        _sinceLastAutosave += delta;

        if (_sinceInterval >= IntervalSeconds)
        {
            TryAutosave("interval");
        }
    }

    private void OnQuestCompleted(QuestCompletedEvent e) => TryAutosave("quest complete");

    private void OnLeveledUp(LeveledUpEvent e) => TryAutosave("level up");

    /// <summary>Phase 25 region-streaming seam: the streamer calls this on a hard region transition
    /// so the player keeps an autosave at each region boundary. Currently uncalled (no region event
    /// exists yet); wired now so streaming only has to invoke it.</summary>
    /// ⚠️ <b>IT HAS TO IGNORE THE <c>IsPlaying</c> GATE, WHICH IS WHY IT TAKES THE FLAG.</b> The
    /// boundary autosave is requested from the middle of <see cref="GameState.Loading"/> — that is
    /// what a region change <em>is</em> — so the guard every other trigger wants rejected every
    /// single one of these, and the feature had never once fired. The state is fully live at that
    /// moment (the player has landed, the roster has regrouped); only the streamer is still working.
    public void RequestRegionChangeAutosave() => TryAutosave("region change", whileLoading: true);

    /// <summary>Forces an autosave immediately, bypassing the debounce (the <c>autosave</c> dev
    /// command). Still respects the IsPlaying guard. Returns the slot written, or null if skipped.</summary>
    public string? ForceAutosave()
    {
        if (GameManager.Instance is not { IsPlaying: true })
        {
            return null;
        }

        return WriteAutosave("forced");
    }

    private void TryAutosave(string reason, bool whileLoading = false)
    {
        if (GameManager.Instance is not { IsPlaying: true } &&
            !(whileLoading && GameManager.Instance is { State: GameState.Loading }))
        {
            return;
        }

        // Debounce: a trigger that lands within the floor of the previous autosave is dropped, so an
        // interval save immediately followed by a quest/level save can't double-write.
        if (_sinceLastAutosave < MinSecondsBetweenAutosaves)
        {
            return;
        }

        WriteAutosave(reason);
    }

    private string? WriteAutosave(string reason)
    {
        if (SaveManager.Instance is not { } manager)
        {
            return null;
        }

        string slot = NextAutosaveSlot(manager.ListSlots());
        if (!manager.SaveGame(slot, isAutosave: true))
        {
            // Back off briefly rather than restarting the whole interval: the clocks below are the
            // record of "the player's progress is safe on disk", and a failed write did not make it so.
            _sinceInterval = System.Math.Max(0d, IntervalSeconds - RetrySeconds);
            _sinceLastAutosave = MinSecondsBetweenAutosaves;
            Log.Warn($"Autosave ({reason}) to '{slot}' failed; retrying in ~{RetrySeconds:0} s of play.");
            return null;
        }

        // Reset both clocks for any successful autosave so the interval restarts after event-driven
        // saves too, and the debounce window opens from the actual write.
        _sinceInterval = 0d;
        _sinceLastAutosave = 0d;
        Log.Info($"Autosaved ({reason}) to '{slot}'.");
        return slot;
    }

    /// <summary>
    /// Picks the next <see cref="RingSlots"/> member to overwrite: the first slot with no on-disk
    /// header, else the one with the oldest timestamp. Pure (no Godot/disk access) so it is unit
    /// testable — pass the current slot headers from <see cref="SaveManager.ListSlots"/>.
    /// </summary>
    public static string NextAutosaveSlot(IReadOnlyList<SaveSlotInfo> existing)
    {
        var headerBySlot = new Dictionary<string, SaveSlotInfo>();
        foreach (SaveSlotInfo info in existing)
        {
            headerBySlot[info.Slot] = info;
        }

        string oldest = RingSlots[0];
        double oldestStamp = double.PositiveInfinity;
        foreach (string slot in RingSlots)
        {
            if (!headerBySlot.TryGetValue(slot, out SaveSlotInfo? info))
            {
                return slot; // an empty ring slot is always preferred over overwriting a real one
            }

            if (info.TimestampUnix < oldestStamp)
            {
                oldestStamp = info.TimestampUnix;
                oldest = slot;
            }
        }

        return oldest;
    }
}
