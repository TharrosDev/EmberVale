using System;
using System.Collections.Generic;
using Embervale.Core;
using Embervale.Core.Diagnostics;
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

    public override void _EnterTree()
    {
        // The pause menu asks for a teardown from a paused tree, so this must keep processing.
        ProcessMode = ProcessModeEnum.Always;
    }

    public bool HasSession => Session != null && IsInstanceValid(Session);

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
    /// onto the registered saveables, continuing that save's playtime.</summary>
    public void StartLoadedGame(string slot)
    {
        CharacterProfile profile = CharacterProfile.Human;
        string regionId = GameIds.Regions.EmberCrown;

        // Restore the saved character before building: the race must be known at spawn (the player
        // factory reads it) so its stat deltas apply. The innate grants come back via the LoadGame
        // overlay below, so they are not re-granted here.
        if (SaveManager.Instance?.ReadHeader(slot) is { } header)
        {
            profile = CharacterProfile.FromHeaderFields(new Dictionary<string, string>
            {
                ["race_id"] = header.RaceId,
                ["char_name"] = header.CharacterName,
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
            AbortToTitle($"Save slot '{slot}' failed to restore; returning to the title screen.");
            return;
        }

        // Same gate as a new game and a portal: the player is at their saved transform, but the
        // cells carrying the collision under it are still streaming. Idempotent on the timer — a
        // cross-region restore may already have opened a gate, and this only replaces its action.
        string name = profile.CharacterName;
        string race = profile.RaceId;
        session.Loading.Begin($"Loading {name}...", () =>
            Log.Info($"Loaded game from slot '{slot}' as {name} ({race}). Sandbox ready."));
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
    /// that over the good file.
    /// </summary>
    public void AbortToTitle(string reason)
    {
        Log.Error(reason);
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
