using System;
using System.Collections.Generic;
using Embervale.Core;
using Embervale.Core.Diagnostics;
using Embervale.Core.Events;
using Embervale.Core.Services;
using Embervale.Races;
using Embervale.Save;
using Embervale.World;
using Godot;

namespace Embervale.Bootstrap;

/// <summary>
/// Opens and closes sessions. This is the piece the old bootstrap did not have.
///
/// <para>Before this existed there was a <c>_sandboxBuilt</c> latch that made a session a
/// once-per-process event, and quitting to the title called <c>GetTree().ReloadCurrentScene()</c>
/// — throwing the entire scene tree away because there was no way to dismantle a world in place.
/// The pause menu's own comment said so. Both are gone: <see cref="DestroySession"/> frees the
/// session node, which disposes the session and world scopes and takes every service, actor and
/// panel with them, and then resets the handful of process-lifetime statics the scene reload used
/// to clear as a side effect.</para>
///
/// <para><b>The static reset list in <see cref="ResetSessionStatics"/> is the load-bearing part of
/// this class.</b> Anything held in a <c>static</c> that describes a session outlives the session
/// node, and the scene reload was silently covering for all of it. <c>SessionResetTests</c> asserts
/// the list so a newly added static fails a test rather than leaking into the next playthrough.</para>
/// </summary>
public sealed partial class SessionLifecycleCoordinator : Node
{
    /// <summary>The live session, or null at the title screen.</summary>
    public GameSession? Session { get; private set; }

    private bool _reloadPending;
    private int _reloadRequestGeneration;
    private string? _titleNoticeKey;

    /// <summary>The title-screen message for a session that ended because it could not be trusted.</summary>
    public const string NoticeSessionAborted = "title.notice.session_aborted";

    /// <summary>The title-screen message for a save that passed inspection and still failed to restore.</summary>
    public const string NoticeLoadFailed = "title.notice.load_failed";

    /// <summary>
    /// Seconds of <b>active play</b> since this session's state last matched a file on disk: reset by
    /// every save that lands, by a load, and by a new session. It is what the unsaved-progress
    /// confirms and the save-on-quit ask about. Paused and loading time does not count, so a player
    /// who saves from the pause menu and then quits has exactly nothing unsaved.
    /// </summary>
    public double SecondsSinceLastSave { get; private set; }

    public override void _EnterTree()
    {
        // The pause menu asks for a teardown from a paused tree, so this must keep processing.
        ProcessMode = ProcessModeEnum.Always;
        EventBus.Instance?.Subscribe<GameSavedEvent>(OnGameSaved);
        EventBus.Instance?.Subscribe<GameLoadedEvent>(OnGameLoaded);
    }

    public override void _Process(double delta)
    {
        if (HasSession && GameManager.Instance is { IsPlaying: true })
        {
            SecondsSinceLastSave += delta;
        }
    }

    private void OnGameSaved(GameSavedEvent e) => SecondsSinceLastSave = 0d;

    private void OnGameLoaded(GameLoadedEvent e) => SecondsSinceLastSave = 0d;

    public bool HasSession => Session != null && IsInstanceValid(Session);

    /// <summary>The message the title screen should show for the way the last session ended or the
    /// last load was refused, handed over once. Null when there is nothing to say.</summary>
    public string? ConsumeTitleNotice()
    {
        string? key = _titleNoticeKey;
        _titleNoticeKey = null;
        return key;
    }

    /// <summary>The title-screen message for a slot that cannot be loaded.</summary>
    public static string NoticeKeyFor(SaveHealth health) => health switch
    {
        SaveHealth.Corrupt => "title.notice.save_corrupt",
        SaveHealth.Newer => "title.notice.save_newer",
        _ => "title.notice.save_missing",
    };

    /// <summary>
    /// A save the player asked for (the pause menu, F5). Refused, with the reason published as a
    /// <see cref="SaveFailedEvent"/> for the toast feed, while a boss fight, a conversation or another
    /// save holds a block; never writes the autosave ring. A manual save makes its slot the session's
    /// own, so the next one-press save and F9 follow it. <paramref name="failureKey"/> is the locale
    /// key saying why it did not land, empty on success.
    /// </summary>
    public bool TrySave(string slot, out string failureKey)
    {
        failureKey = SaveManager.ReasonWriteFailed;
        if (SaveManager.Instance is not { } saves || !HasSession)
        {
            failureKey = SaveManager.ReasonNoWorld;
            return false;
        }

        if (!SaveSlotPolicy.IsPlayerWritable(slot))
        {
            Log.Warn($"Refused a player save into '{slot}': the autosave ring is not a player slot.");
            return false;
        }

        if (!saves.CanSaveNow(out string blockedKey))
        {
            failureKey = blockedKey;
            EventBus.Instance?.Publish(new SaveFailedEvent(slot, blockedKey));
            return false;
        }

        // SaveGame publishes its own SaveFailedEvent when the write does not land.
        if (!saves.SaveGame(slot))
        {
            return false;
        }

        if (SaveManager.KindOfSlot(slot) == SaveKind.Manual)
        {
            saves.ActiveSlot = slot;
        }

        failureKey = string.Empty;
        return true;
    }

    /// <summary>Play since the last save below which leaving does not bother to autosave: saving
    /// from the pause menu, resuming for a moment and quitting should not rotate the ring.</summary>
    private const double QuitAutosaveFloorSeconds = 5d;

    /// <summary>
    /// Writes an autosave before the session is left, when there is anything unsaved. True when the
    /// player loses nothing by quitting now (it saved, or nothing has happened since the last save);
    /// false, with the reason, when saving is blocked or the write failed and the caller should ask.
    /// </summary>
    public bool AutosaveBeforeQuit(out string failureKey)
    {
        failureKey = string.Empty;
        if (!HasSession || SaveManager.Instance is not { } saves || SecondsSinceLastSave < QuitAutosaveFloorSeconds)
        {
            return true;
        }

        if (!saves.CanSaveNow(out failureKey))
        {
            return false;
        }

        // The session's own ring position when it has one, so this save and the service agree on
        // which slot is oldest.
        string slot = ServiceLocator.Instance is { } locator && locator.TryGet(out AutosaveService autosave)
            ? autosave.NextSlot
            : AutosaveService.NextAutosaveSlot(saves.ListSlots());

        // The player is about to leave on the strength of this save, so it has to be on disk before
        // the answer is given: the queue is drained here, and a save that landed has reset the
        // unsaved clock through GameSavedEvent by the time Flush returns.
        if (saves.SaveGame(slot, isAutosave: true))
        {
            SaveWriteQueue.Flush();
            if (SecondsSinceLastSave < QuitAutosaveFloorSeconds)
            {
                Log.Info($"Autosaved to '{slot}' before leaving the session.");
                return true;
            }
        }

        failureKey = SaveManager.ReasonWriteFailed;
        return false;
    }

    /// <summary>Reloads a checkpoint through a fresh session. Rebuilding restores authored actors
    /// removed after the checkpoint and uses the saved race/region even when an old header has no
    /// location. The application-owned coordinator holds the deferred callback, never the menu or
    /// input node that the reload destroys. At most one request may be pending per session.</summary>
    public bool RequestReload(GameSession requestingSession, string slot)
    {
        if (!HasSession || !ReferenceEquals(Session, requestingSession) || _reloadPending ||
            GameManager.Instance?.State is not (GameState.Playing or GameState.Paused))
        {
            return false;
        }
        // Inspected, not merely present: a reload destroys the running session, so a slot that is
        // corrupt or from a newer build has to be refused while there is still a session to keep.
        if (SaveManager.Instance is not { } saves || saves.InspectSlot(slot) is not { Health: SaveHealth.Ok })
        {
            Log.Warn($"Cannot reload slot '{slot}': it holds no loadable checkpoint.");
            return false;
        }

        _reloadPending = true;
        int generation = ++_reloadRequestGeneration;
        Callable.From(() =>
        {
            if (!IsInstanceValid(this) || IsQueuedForDeletion() || !_reloadPending ||
                generation != _reloadRequestGeneration || !HasSession ||
                !ReferenceEquals(Session, requestingSession))
            {
                return;
            }
            _reloadPending = false;
            StartLoadedGame(slot);
        }).CallDeferred();
        return true;
    }

    public override void _ExitTree()
    {
        _reloadPending = false;
        _reloadRequestGeneration++;
        EventBus.Instance?.Unsubscribe<GameSavedEvent>(OnGameSaved);
        EventBus.Instance?.Unsubscribe<GameLoadedEvent>(OnGameLoaded);
    }

    /// <summary>Raised after a session is torn down, so the shell can show the title again.</summary>
    public event Action? SessionEnded;

    /// <summary>Starts a fresh game into <paramref name="slot"/>: builds the session from the
    /// creator's chosen profile, granting the race's innate perks/spells/reputation.</summary>
    public void StartNewGame(string slot, CharacterProfile profile)
    {
        GameSession session = BeginSession(slot, profile, applyStartingGrants: true, GameIds.Regions.EmberCrown);

        SaveManager.Instance?.ResetPlaytime();
        session.Build();

        // ⚠️ THE SAME GATE THE PORTALS USE. The session spawns the player and *then* creates the
        // streamer, so on the frame this used to enter Playing not one cell — and therefore not one
        // terrain collider — was in the tree. The player was handed control standing over a hole and
        // fell out of the world; the prologue's camera hid it for exactly as long as it ran.
        session.Loading.Begin(
            $"Entering {RegionDatabase.Get(session.CurrentRegionId)?.DisplayName ?? "Embervale"}...",
            () =>
            {
                // The prologue plays over the already-built world, so creation flows into the
                // narration and the narration lifts with nothing left to load. A load skips it.
                session.Opening?.Play(profile);
                Log.Info($"New game started in slot '{slot}'. Prologue playing; the Ember Crown is built behind it.");
            });
    }

    /// <summary>Loads an existing save into a freshly-built session, then overlays the slot's state
    /// onto the registered saveables, continuing that save's playtime.
    ///
    /// <para>False when no session came of it. A slot that is missing, corrupt or from a newer build
    /// is refused <b>before</b> anything is torn down, so a live session survives a bad F9; a save
    /// that passes inspection and still fails to restore ends at the title screen. Either way
    /// <see cref="ConsumeTitleNotice"/> has the message.</para></summary>
    public bool StartLoadedGame(string slot)
    {
        // ⚠️ The header comes from the inspection, not from ReadHeader: ReadHeader prefers the
        // header.json mirror, which describes the newest save, and when that save is damaged the
        // load reads the backup generation. Race, name and region must come from the same
        // generation the load is about to apply.
        SaveSlotInfo? header = null;
        if (SaveManager.Instance is { } inspector)
        {
            header = inspector.InspectSlot(slot);
            SaveHealth health = header?.Health ?? SaveHealth.Missing;
            if (health != SaveHealth.Ok)
            {
                Log.Error($"Save slot '{slot}' cannot be loaded ({health}); any running session is left untouched.");
                _titleNoticeKey = NoticeKeyFor(health);
                return false;
            }
        }

        CharacterProfile profile = CharacterProfile.Human;
        string regionId = GameIds.Regions.EmberCrown;

        // Restore the saved character before building: the race must be known at spawn (the player
        // factory reads it) so its stat deltas apply. The innate grants come back via the LoadGame
        // overlay below, so they are not re-granted here.
        if (header != null)
        {
            profile = CharacterProfile.FromHeaderFields(new Dictionary<string, string>
            {
                ["race_id"] = header.RaceId,
                ["char_name"] = header.CharacterName,
                ["appearance"] = header.Appearance,
                ["background"] = header.Background,
            });

            // The saved region has to be current BEFORE the build so the streamer, portals, safe
            // zones and map all configure for it; the transform lands after the overlay.
            if (!string.IsNullOrEmpty(header.RegionId) && RegionDatabase.Get(header.RegionId) != null)
            {
                regionId = header.RegionId;
            }
        }

        GameSession session = BeginSession(slot, profile, applyStartingGrants: false, regionId);
        session.Build();

        // LoadGame calls the location applier at the end of its overlay, returning the player to
        // where they saved. The region was switched above, so that call finds it current and only
        // writes the transform.
        if (SaveManager.Instance?.LoadGame(slot) == false)
        {
            AbortToTitle($"Save slot '{slot}' failed to restore; returning to the title screen.", NoticeLoadFailed);
            return false;
        }

        // Same gate as a new game and a portal: the player is at their saved transform, but the
        // cells carrying the collision under it are still streaming. Idempotent on the timer — a
        // cross-region restore may already have opened a gate, and this only replaces its action.
        string name = profile.CharacterName;
        string race = profile.RaceId;
        session.Loading.Begin($"Loading {name}...", () =>
            Log.Info($"Loaded game from slot '{slot}' as {name} ({race}). Sandbox ready."));
        return true;
    }

    /// <summary>
    /// Ends the current session and returns to a state a new one can be started from — without a
    /// scene reload, which is the whole point of the lifetime model.
    ///
    /// <para>The session node is removed from the tree <b>synchronously</b> (rather than only
    /// queued) so every <c>_ExitTree</c> beneath it — scope disposal, saveable unregistration,
    /// event unsubscription — has run by the time this returns, and the next New Game starts
    /// against an empty registry. <c>QueueFree</c> then reclaims the memory at end of frame.</para>
    /// </summary>
    public void DestroySession()
    {
        // A queued request belongs to the session that made it. A quit or a different session
        // start cancels it before its callback can load over that newer state.
        _reloadPending = false;
        _reloadRequestGeneration++;
        SecondsSinceLastSave = 0d;
        if (!HasSession)
        {
            return;
        }

        GameSession session = Session!;
        Session = null;

        RemoveChild(session);
        session.QueueFree();

        ResetSessionStatics();

        if (SaveManager.Instance is { } saves)
        {
            saves.HeaderProvider = null;
            saves.LocationApplier = null;

            // A block belongs to something in the world (a boss, a conversation). That world is gone
            // and can no longer release it, and a block that outlived it would refuse every save in
            // the next session.
            saves.ClearSaveBlocks();

            int stranded = 0;
            foreach (string _ in saves.RegisteredSaveIds)
            {
                stranded++;
            }

            if (stranded > 0)
            {
                Log.Warn($"{stranded} saveable(s) survived session teardown (check ISaveable unregistration).");
            }
        }

        Input.MouseMode = Input.MouseModeEnum.Visible;
        GameManager.Instance?.ChangeState(GameState.MainMenu);

        SessionEnded?.Invoke();
    }

    /// <summary>
    /// Leaves a session that cannot be trusted — a partial save restore, a cell that failed to
    /// load, a loading gate that timed out. Continuing would hand the player a world assembled from
    /// some of the save and some of whatever was already live, and the next autosave would write
    /// that over the good file. <paramref name="noticeKey"/> is what the title screen then tells
    /// the player, so the return is never silent.
    /// </summary>
    public void AbortToTitle(string reason, string noticeKey = NoticeSessionAborted)
    {
        Log.Error(reason);
        _titleNoticeKey = noticeKey;
        DestroySession();
    }

    /// <summary>
    /// Every process-lifetime static that describes a session. The scene reload used to clear all
    /// of these for free; nothing does now except this method, which is why the test that asserts
    /// its contents exists.
    /// </summary>
    public static void ResetSessionStatics()
    {
        SafeZones.Clear();
        Weave.Reset();
        PersistentActorRegistry.Clear();
        UiState.ClearAll();
        Magic.SpellActions.Clear();
        Invariant.Reset();
    }

    private GameSession BeginSession(string slot, CharacterProfile profile, bool applyStartingGrants, string regionId)
    {
        // A second session is never additive: whatever is live goes first.
        DestroySession();

        // No GC drain here. The previous session's C# Resource wrappers used to be collectable while
        // still in Godot's resource cache, and Build re-loading one was the lifecycle FATAL; that is
        // closed at the load itself (ResidentResources), which a GC.Collect here could not do — the
        // old session is only queued for free at this point, so its wrappers were not garbage yet.
        var session = new GameSession
        {
            Lifecycle = this,
            Slot = slot,
            Profile = profile,
            ApplyStartingGrants = applyStartingGrants,
            CurrentRegionId = regionId,
        };

        Session = session;
        AddChild(session);

        if (SaveManager.Instance is { } saves)
        {
            // Subsequent quick/manual saves target this slot, and headers are stamped from live
            // gameplay state via the provider without coupling the manager to gameplay.
            saves.ActiveSlot = slot;
            saves.HeaderProvider = session.Header.Build;
            saves.LocationApplier = session.Header.ApplySavedLocation;
        }

        return session;
    }
}
