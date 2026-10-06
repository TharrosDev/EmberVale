using System.Collections.Generic;
using Embervale.Combat;
using Embervale.Core;
using Embervale.Core.Diagnostics;
using Embervale.Core.Events;
using Embervale.Core.Services;
using Embervale.Enemies;
using Embervale.Entities;
using Embervale.Player;
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
/// write never clobbers the only recent copy. The ring position is read from the three ring
/// headers <b>once</b>, when the service starts (empty slot first, else the oldest), and kept in
/// memory from then on, so an autosave no longer lists and parses every slot on disk. They never
/// touch <see cref="SaveManager.ActiveSlot"/>, so F5/F9 and pause-menu Save/Load keep targeting the
/// player's chosen slot.
///
/// Triggers: a time interval over <b>active</b> play, <see cref="QuestCompletedEvent"/>,
/// <see cref="LeveledUpEvent"/>, and a region arrival (<see cref="RequestRegionChangeAutosave"/>).
/// None of them writes from inside the event: each asks the <see cref="AutosaveCadence"/>, which
/// writes a moment later from <see cref="_Process"/>, and only when the world allows it. A save is
/// held back while <see cref="SaveManager.CanSaveNow"/> says no (a boss fight, a conversation),
/// while a menu or a cinematic holds the screen, and for up to
/// <see cref="AutosaveCadence.MaxSoftDeferSeconds"/> while the player is fighting or in the air.
///
/// It also keeps <see cref="SaveThumbnailService"/>'s frame: the picture a pause-menu save uses is
/// taken here, as play is left, before the menu is drawn.
/// </summary>
public sealed partial class AutosaveService : Node
{
    /// <summary>The rotating autosave slot ids, oldest-overwritten. Shared with the load browser.</summary>
    public static readonly string[] RingSlots = SaveSlots.AutoRing;

    /// <summary>Whether a slot id belongs to the autosave ring (used by the slot browser).</summary>
    public static bool IsAutosaveSlot(string slot) => System.Array.IndexOf(RingSlots, slot) >= 0;

    /// <summary>
    /// Turns every autosave off for the rest of the process. For headless gates and harnesses:
    /// they level characters and complete quests by the dozen, and the ring is the developer's own
    /// saves. Static because a gate builds and destroys many sessions, each with its own service.
    /// </summary>
    public static bool Suppressed { get; set; }

    /// <summary>Seconds after the player last dealt or took a hit during which they still count as
    /// fighting.</summary>
    private const double CombatLullSeconds = 6d;

    /// <summary>An enemy in its combat state only holds an autosave back within this distance of
    /// the player; a guard fighting wolves across the valley is not the player's fight.</summary>
    private const float EngagedRadius = 40f;

    private readonly AutosaveCadence _cadence = new(RingSlots.Length);
    private readonly Dictionary<ulong, IEntity> _engaged = new();
    private readonly List<ulong> _stale = new();
    private double _sincePlayerFought = double.PositiveInfinity;

    // The slot of the write in flight, whether its GameSavedEvent arrived before SaveGame returned,
    // and the slot of a write that was accepted but has not been confirmed by the disk yet.
    private string? _writingSlot;
    private bool _writingLanded;
    private string? _unconfirmedSlot;

    /// <summary>Active play seconds since a save of any kind last reached the disk, or since this
    /// session began (a loaded session starts level with its save). For "last saved ... ago" UI.</summary>
    public double SecondsSinceLastSave => _cadence.SecondsSinceLastSave;

    /// <summary>The ring slot the next autosave overwrites.</summary>
    public string NextSlot => RingSlots[_cadence.RingIndex];

    /// <summary>Whether an autosave is due and waiting for a better moment.</summary>
    public bool IsDeferred => _cadence.IsDeferred;

    public override void _EnterTree()
    {
        EventBus bus = EventBus.Instance;
        bus?.Subscribe<QuestCompletedEvent>(OnQuestCompleted);
        bus?.Subscribe<LeveledUpEvent>(OnLeveledUp);
        bus?.Subscribe<GameSavedEvent>(OnGameSaved);
        bus?.Subscribe<SaveFailedEvent>(OnSaveFailed);
        bus?.Subscribe<GameStateChangedEvent>(OnGameStateChanged);
        bus?.Subscribe<EnemyStateChangedEvent>(OnEnemyState);
        bus?.Subscribe<EntityDiedEvent>(OnEntityDied);
        bus?.Subscribe<BossWithdrewEvent>(OnBossWithdrew);
        bus?.Subscribe<DamageDealtEvent>(OnDamageDealt);
    }

    public override void _Ready()
    {
        // Once per session, and only the ring's own three headers: where the rotation resumes.
        if (SaveManager.Instance is { } manager)
        {
            var headers = new List<SaveSlotInfo>(RingSlots.Length);
            foreach (string slot in RingSlots)
            {
                if (manager.SaveExists(slot) && manager.ReadHeader(slot) is { } info)
                {
                    headers.Add(info);
                }
            }

            _cadence.StartRingAt(System.Array.IndexOf(RingSlots, NextAutosaveSlot(headers)));
        }
    }

    public override void _ExitTree()
    {
        EventBus? bus = EventBus.Instance;
        bus?.Unsubscribe<QuestCompletedEvent>(OnQuestCompleted);
        bus?.Unsubscribe<LeveledUpEvent>(OnLeveledUp);
        bus?.Unsubscribe<GameSavedEvent>(OnGameSaved);
        bus?.Unsubscribe<SaveFailedEvent>(OnSaveFailed);
        bus?.Unsubscribe<GameStateChangedEvent>(OnGameStateChanged);
        bus?.Unsubscribe<EnemyStateChangedEvent>(OnEnemyState);
        bus?.Unsubscribe<EntityDiedEvent>(OnEntityDied);
        bus?.Unsubscribe<BossWithdrewEvent>(OnBossWithdrew);
        bus?.Unsubscribe<DamageDealtEvent>(OnDamageDealt);
        _engaged.Clear();

        // The session is going away (back to the menu, a reload, a quit): whatever is still queued
        // has to be on disk before anything reads the slots again or the process ends.
        SaveWriteQueue.Flush();
        SaveThumbnailService.DropCache();
    }

    public override void _Notification(int what)
    {
        if (what == NotificationWMCloseRequest)
        {
            SaveWriteQueue.Flush();
        }
    }

    public override void _Process(double delta)
    {
        // Only active gameplay time counts: paused/loading/menu time must not advance the cadence
        // (and must never autosave). A menu or a cinematic lock leaves the state at Playing, so it
        // is asked for separately; a save there would also photograph the menu.
        if (Suppressed || GameManager.Instance is not { IsPlaying: true } || UiState.MenuOpen)
        {
            return;
        }

        _sincePlayerFought += delta;
        if (!_cadence.Advance(delta))
        {
            return;
        }

        bool blocked = SaveManager.Instance is not { } manager || !manager.CanSaveNow(out _);
        if (_cadence.Decide(blocked, busy: !blocked && PlayerIsBusy()) is { } reason)
        {
            WriteAutosave(reason);
        }
    }

    private void OnQuestCompleted(QuestCompletedEvent e) => Request("quest complete", AutosaveCadence.EventDelaySeconds);

    private void OnLeveledUp(LeveledUpEvent e) => Request("level up", AutosaveCadence.EventDelaySeconds);

    /// <summary>
    /// Asks for an autosave at a region boundary. <c>WorldSessionDirector.PerformRegionLoad</c> calls
    /// it from the middle of the hard load, once the player has landed and the party has regrouped.
    ///
    /// The save is <b>not</b> written there. It is written about a second after play resumes in the
    /// new region, which is the first moment three things are true at once: the destination's cells
    /// have streamed in, so the cell ledger describes the place the player is standing; the screen
    /// shows the new region rather than the loading screen, so the thumbnail does too; and the
    /// write does not land on top of the load's own work. Like every event-driven autosave it is
    /// dropped inside the debounce window, so hopping back and forth through a portal does not
    /// spend the whole ring.
    /// </summary>
    /// ⚠️ <b>The request itself has to be accepted during <see cref="GameState.Loading"/></b>, which
    /// is why it passes the flag: a region change <em>is</em> a load, and the guard every other
    /// trigger wants once rejected every single one of these.
    public void RequestRegionChangeAutosave() =>
        Request("region change", AutosaveCadence.RegionArrivalDelaySeconds, whileLoading: true);

    /// <summary>Forces an autosave immediately, bypassing the debounce and every deferral (the
    /// <c>autosave</c> dev command). Still respects the IsPlaying guard. Returns the slot written,
    /// or null if skipped.</summary>
    public string? ForceAutosave()
    {
        if (Suppressed || GameManager.Instance is not { IsPlaying: true })
        {
            return null;
        }

        return WriteAutosave("forced");
    }

    private void Request(string reason, double delaySeconds, bool whileLoading = false)
    {
        // Play and a pause over play are "a game is running". A load is not, for most triggers: a
        // level or a quest granted while a session is still being built or restored must not book
        // an autosave for its first seconds. The region boundary is the one request that is made
        // from inside a load on purpose.
        GameState? state = GameManager.Instance?.State;
        bool running = state is GameState.Playing or GameState.Paused ||
                       (whileLoading && state == GameState.Loading);
        if (Suppressed || !running)
        {
            return;
        }

        _cadence.Request(reason, delaySeconds);
    }

    private string? WriteAutosave(string reason)
    {
        if (SaveManager.Instance is not { } manager)
        {
            _cadence.Failed();
            return null;
        }

        string slot = NextSlot;
        _writingSlot = slot;
        _writingLanded = false;
        bool accepted = manager.SaveGame(slot, isAutosave: true);
        _writingSlot = null;

        if (!accepted)
        {
            // Back off briefly rather than restarting the whole interval: the clocks are the record
            // of "the player's progress is safe on disk", and a failed write did not make it so.
            _cadence.Failed();
            Log.Warn($"Autosave ({reason}) to '{slot}' failed; retrying in ~{AutosaveCadence.RetrySeconds:0} s of play.");
            return null;
        }

        // Accepted. With queued writes the disk confirms later (OnGameSaved / OnSaveFailed); when
        // the write ran inline its GameSavedEvent has already been through OnGameSaved.
        _cadence.Saved();
        _unconfirmedSlot = _writingLanded ? null : slot;
        Log.Info($"Autosaved ({reason}) to '{slot}'.");
        return slot;
    }

    private void OnGameSaved(GameSavedEvent e)
    {
        _cadence.SaveLanded(e.IsAutosave);
        if (e.Slot == _writingSlot)
        {
            _writingLanded = true;
        }

        if (e.Slot == _unconfirmedSlot)
        {
            _unconfirmedSlot = null;
        }
    }

    private void OnSaveFailed(SaveFailedEvent e)
    {
        // Only a write that was accepted and then failed on the worker is news here; a save that
        // failed before SaveGame returned has already been handled by its return value.
        if (_unconfirmedSlot == null || e.Slot != _unconfirmedSlot)
        {
            return;
        }

        _unconfirmedSlot = null;
        _cadence.Failed(afterSaved: true);
        Log.Warn($"Autosave to '{e.Slot}' did not reach the disk; retrying in ~{AutosaveCadence.RetrySeconds:0} s of play.");
    }

    private void OnGameStateChanged(GameStateChangedEvent e)
    {
        if (e.Previous == GameState.Playing)
        {
            // The handler runs before the next frame is drawn, so the viewport still holds the last
            // frame of play: no pause menu, no loading screen. Leaving play any other way (a menu
            // over a menu, a load that began on the map) must not replace it.
            SaveThumbnailService.CacheFrame(GetViewport());
        }
        else if (e.Current == GameState.Playing)
        {
            SaveThumbnailService.DropCache();
        }
    }

    // --- "is the player in a fight" ------------------------------------------------------------
    // The same signal the music uses (MusicDirector): enemies in their Combat or Retreat state,
    // dropped when they die, withdraw or are freed. No combat system of its own.

    private void OnEnemyState(EnemyStateChangedEvent e)
    {
        if (e.State is EnemyState.Combat or EnemyState.Retreat)
        {
            _engaged[e.Enemy.RuntimeId] = e.Enemy;
        }
        else
        {
            _engaged.Remove(e.Enemy.RuntimeId);
        }
    }

    private void OnEntityDied(EntityDiedEvent e) => _engaged.Remove(e.Entity.RuntimeId);

    private void OnBossWithdrew(BossWithdrewEvent e) => _engaged.Remove(e.Boss.RuntimeId);

    private void OnDamageDealt(DamageDealtEvent e)
    {
        if (e.Target is PlayerCharacter || e.Source is PlayerCharacter)
        {
            _sincePlayerFought = 0d;
        }
    }

    private bool PlayerIsBusy()
    {
        if (_sincePlayerFought < CombatLullSeconds)
        {
            return true;
        }

        if (ServiceLocator.Instance is not { } locator || !locator.TryGet(out PlayerCharacter player) ||
            !IsInstanceValid(player) || !player.IsInsideTree())
        {
            return false;
        }

        if (!player.IsOnFloor())
        {
            return true;
        }

        if (_engaged.Count == 0)
        {
            return false;
        }

        bool fighting = false;
        Vector3 here = player.GlobalPosition;
        _stale.Clear();
        foreach (KeyValuePair<ulong, IEntity> pair in _engaged)
        {
            // A region change or a load frees combatants without a death event.
            if (pair.Value.Body is not { } body || !IsInstanceValid(body) || !body.IsInsideTree())
            {
                _stale.Add(pair.Key);
            }
            else if (body.GlobalPosition.DistanceSquaredTo(here) <= EngagedRadius * EngagedRadius)
            {
                fighting = true;
            }
        }

        foreach (ulong id in _stale)
        {
            _engaged.Remove(id);
        }

        return fighting;
    }

    /// <summary>
    /// Picks the <see cref="RingSlots"/> member to overwrite from a set of slot headers: the first
    /// slot with no header, else the one with the oldest timestamp. Pure (no Godot/disk access) so
    /// it is unit testable. The service asks it once, at start, to seed its in-memory ring position.
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
