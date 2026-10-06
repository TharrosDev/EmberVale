using System;

namespace Embervale.Save;

/// <summary>
/// The pure decisions behind the autosave cadence: when a save is due, when a due save waits, and
/// which ring slot it goes to. <see cref="AutosaveService"/> feeds it active play time and what the
/// world says; this class holds no Godot type, so every rule here is unit-tested.
///
/// The shape of one autosave:
///   1. something makes it <b>pending</b> (the interval elapsing, or <see cref="Request"/> from a
///      quest completion, a level-up or a region arrival, each with a short delay so the write
///      never lands inside the event that asked for it);
///   2. once its delay has run it is <b>due</b>, and the world is asked, at most once per
///      <see cref="DeferPollSeconds"/>, whether now is a good moment (<see cref="Decide"/>);
///   3. a hard block (a boss fight, a conversation) defers it for as long as the block lasts; a
///      soft one (mid-combat, airborne) defers it for at most <see cref="MaxSoftDeferSeconds"/>,
///      because a save in an awkward moment still beats no save at all;
///   4. it is written, and <see cref="Saved"/> or <see cref="Failed"/> closes the request.
/// </summary>
public sealed class AutosaveCadence
{
    /// <summary>Active play between interval autosaves.</summary>
    public const double IntervalSeconds = 300d;

    /// <summary>Debounce: an event-driven request inside this window of the last autosave is dropped.</summary>
    public const double MinSecondsBetweenAutosaves = 60d;

    /// <summary>Active seconds before a <b>failed</b> autosave is tried again. A failure does not
    /// restart the interval: the clocks record "progress is safe on disk", and a failed write did
    /// not make it so.</summary>
    public const double RetrySeconds = 30d;

    /// <summary>How long after a quest completion or a level-up the autosave is written. The reward
    /// toasts, the follow-up quest start and the level-up grants all land in the frames after the
    /// event, and a save taken inside the handler captured the world halfway through them.</summary>
    public const double EventDelaySeconds = 2.5d;

    /// <summary>How long after play resumes in a new region the boundary autosave is written.</summary>
    public const double RegionArrivalDelaySeconds = 1d;

    /// <summary>How often a due but deferred autosave asks the world again.</summary>
    public const double DeferPollSeconds = 1d;

    /// <summary>The longest a due autosave waits on a soft reason (combat, a jump).</summary>
    public const double MaxSoftDeferSeconds = 90d;

    /// <summary>The reason an interval autosave carries, for the log.</summary>
    public const string IntervalReason = "interval";

    private readonly int _ringSize;
    private double _sinceInterval;
    private double _sinceLastAutosave = double.PositiveInfinity;
    private double _sinceAnySave;
    private string? _pendingReason;
    private double _pendingDelay;
    private double _dueFor;
    private double _pollIn;

    public AutosaveCadence(int ringSize)
    {
        _ringSize = Math.Max(1, ringSize);
    }

    /// <summary>The ring member the next autosave overwrites.</summary>
    public int RingIndex { get; private set; }

    /// <summary>Active play seconds since the last save of any kind landed (or since the session
    /// began, which for a loaded game is the moment it matched the disk).</summary>
    public double SecondsSinceLastSave => _sinceAnySave;

    /// <summary>The reason of the autosave waiting to be written, or null.</summary>
    public string? PendingReason => _pendingReason;

    /// <summary>Whether a due autosave is being held back by the world.</summary>
    public bool IsDeferred => _pendingReason != null && _pendingDelay <= 0d && _dueFor > 0d;

    /// <summary>Points the ring at <paramref name="index"/> (wrapped). Called once, from the slot
    /// headers on disk, so rotation survives a restart without being re-read on every autosave.</summary>
    public void StartRingAt(int index)
    {
        RingIndex = ((index % _ringSize) + _ringSize) % _ringSize;
    }

    /// <summary>
    /// Asks for an event-driven autosave <paramref name="delaySeconds"/> of active play from now.
    /// Returns false when it is dropped by the debounce. A request made while another is pending
    /// joins it (the earlier reason is kept, the shorter delay wins) rather than queueing a second
    /// write.
    /// </summary>
    public bool Request(string reason, double delaySeconds)
    {
        if (_sinceLastAutosave < MinSecondsBetweenAutosaves)
        {
            return false;
        }

        delaySeconds = Math.Max(0d, delaySeconds);
        if (_pendingReason != null)
        {
            _pendingDelay = Math.Min(_pendingDelay, delaySeconds);
            return true;
        }

        _pendingReason = reason;
        _pendingDelay = delaySeconds;
        _dueFor = 0d;
        _pollIn = 0d;
        return true;
    }

    /// <summary>
    /// Advances the clocks by <paramref name="delta"/> seconds of <b>active</b> play. Returns true
    /// when an autosave is due and it is time to ask the world (<see cref="Decide"/>); false means
    /// there is nothing to do this frame, which is the answer on almost every frame.
    /// </summary>
    public bool Advance(double delta)
    {
        if (delta <= 0d)
        {
            return false;
        }

        _sinceInterval += delta;
        _sinceLastAutosave += delta;
        _sinceAnySave += delta;

        if (_pendingReason == null)
        {
            if (_sinceInterval < IntervalSeconds)
            {
                return false;
            }

            _pendingReason = IntervalReason;
            _pendingDelay = 0d;
            _dueFor = 0d;
            _pollIn = 0d;
        }
        else if (_pendingDelay > 0d)
        {
            _pendingDelay -= delta;
            if (_pendingDelay > 0d)
            {
                return false;
            }
        }
        else
        {
            _dueFor += delta;
        }

        _pollIn -= delta;
        return _pollIn <= 0d;
    }

    /// <summary>
    /// Decides a due autosave. <paramref name="blocked"/> is a hard refusal (the save manager says
    /// no); <paramref name="busy"/> is a soft one (combat, airborne). Returns the reason to write
    /// now, or null when the save waits, in which case <see cref="Advance"/> asks again after
    /// <see cref="DeferPollSeconds"/>.
    /// </summary>
    public string? Decide(bool blocked, bool busy)
    {
        if (_pendingReason == null || _pendingDelay > 0d)
        {
            return null;
        }

        if (blocked || (busy && _dueFor < MaxSoftDeferSeconds))
        {
            _pollIn = DeferPollSeconds;

            // Count the wait from the first refusal even when Advance has not accrued any yet, so
            // IsDeferred is true from this moment.
            _dueFor = Math.Max(_dueFor, double.Epsilon);
            return null;
        }

        return _pendingReason;
    }

    /// <summary>An autosave was written (or accepted by the write queue): the interval and the
    /// debounce restart and the ring moves on. <see cref="SecondsSinceLastSave"/> is not touched
    /// here; it follows <see cref="SaveLanded"/>, which is the disk confirming it.</summary>
    public void Saved()
    {
        _sinceInterval = 0d;
        _sinceLastAutosave = 0d;
        ClearPending();
        RingIndex = (RingIndex + 1) % _ringSize;
    }

    /// <summary>
    /// An autosave did not land: the interval backs off to <see cref="RetrySeconds"/> and the
    /// debounce opens. <paramref name="afterSaved"/> is for a write that was accepted and failed
    /// later (a queued write): it also steps the ring back, so the retry goes to the slot that
    /// still holds its old save instead of moving on to overwrite the next good one.
    /// </summary>
    public void Failed(bool afterSaved = false)
    {
        _sinceInterval = Math.Max(0d, IntervalSeconds - RetrySeconds);
        _sinceLastAutosave = MinSecondsBetweenAutosaves;
        ClearPending();
        if (afterSaved)
        {
            RingIndex = (RingIndex + _ringSize - 1) % _ringSize;
        }
    }

    /// <summary>
    /// A save of any kind reached the disk. For a manual or quick save the interval also restarts
    /// and a pending autosave is dropped: progress is safe, and writing a ring slot seconds after
    /// the player saved by hand would only spend the oldest autosave on a copy. The debounce and
    /// the ring are left alone.
    /// </summary>
    public void SaveLanded(bool isAutosave)
    {
        _sinceAnySave = 0d;
        if (!isAutosave)
        {
            _sinceInterval = 0d;
            ClearPending();
        }
    }

    private void ClearPending()
    {
        _pendingReason = null;
        _pendingDelay = 0d;
        _dueFor = 0d;
        _pollIn = 0d;
    }
}
