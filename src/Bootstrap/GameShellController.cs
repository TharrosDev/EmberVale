using System.Collections.Generic;
using Embervale.Core;
using Embervale.Core.Diagnostics;
using Embervale.Save;
using Embervale.UI;
using Godot;

namespace Embervale.Bootstrap;

/// <summary>
/// The meta shell: the title screen, and the command-line flags that drive a session for a tool.
///
/// <para>It is the only thing between the application root and a session. When a session ends it
/// puts the title screen back up — which it can now do without reloading the scene, because
/// <see cref="SessionLifecycleCoordinator.DestroySession"/> actually destroys the session.</para>
/// </summary>
public sealed partial class GameShellController : Node
{
    public const string PlayArgument = "--play";
    public const string NewGameArgument = "--new-game";
    public const string SlotArgument = "--slot";
    public const string QuitAfterArgument = "--quit-after";

    private MainMenu? _menu;

    /// <summary>True when a command-line session was asked for and did not start.</summary>
    private bool _sessionEntryFailed;

    /// <summary>True for a command-line new game outside an isolated user directory: nothing may be
    /// written to the autosave ring, including on window close.</summary>
    private bool _protectPlayerSaves;

    public SessionLifecycleCoordinator Lifecycle { get; init; } = null!;

    public override void _Ready()
    {
        Lifecycle.SessionEnded += ShowTitle;
        ShowTitle();
        RunCommandLineSessionIfRequested();
    }

    public override void _ExitTree()
    {
        Lifecycle.SessionEnded -= ShowTitle;
    }

    /// <summary>Shows the title screen and parks the game in <see cref="GameState.MainMenu"/>.</summary>
    public void ShowTitle()
    {
        if (_menu != null && IsInstanceValid(_menu))
        {
            return;
        }

        _menu = new MainMenu
        {
            NewCharacterRequested = StartNewGame,
            LoadGameRequested = StartLoadedGame,

            // A session that ended because it could not be trusted says so here; without it a failed
            // load is a loading screen followed by the title, which reads as the game restarting.
            NoticeKey = Lifecycle.ConsumeTitleNotice(),
        };
        AddChild(_menu);
        GameManager.Instance?.ChangeState(GameState.MainMenu);
        Log.Info("Main menu ready. New Game to enter the world.");

#if EMBERVALE_TOOLING
        if (HeadlessArgs.Has("--shellshots"))
        {
            AddChild(new Debugging.ShellShots { Name = "ShellShots", Menu = _menu });
        }
#endif
    }

#if EMBERVALE_TOOLING
    public void AutomationNewGame() => StartNewGame("automation", Races.CharacterProfile.Human);
#endif

    private void StartNewGame(string slot, Races.CharacterProfile profile)
    {
        DismissTitle();
        Lifecycle.StartNewGame(slot, profile);
    }

    /// <summary>
    /// Loads a slot from the title. The title is dismissed only once a session exists: a slot the
    /// coordinator refuses (missing, corrupt, newer) or one that fails to restore leaves this menu up
    /// with the reason on it. ⚠️ A restore that fails after the build destroys the session, which
    /// raises <c>SessionEnded</c> while this menu is still alive, so <see cref="ShowTitle"/> returns
    /// early and the notice is delivered here instead.
    /// </summary>
    private void StartLoadedGame(string slot)
    {
        if (Lifecycle.StartLoadedGame(slot))
        {
            DismissTitle();
            return;
        }

        string? notice = Lifecycle.ConsumeTitleNotice();
        if (notice != null && _menu != null && IsInstanceValid(_menu))
        {
            _menu.ShowNotice(notice);
        }
    }

    /// <summary>
    /// Closing the window (the title bar, Alt+F4) while a session is live writes an autosave first,
    /// the same one the pause menu's quit buttons write. There is no asking here: the engine quits
    /// after this notification returns, so a blocked or failed save is simply lost progress, logged.
    /// </summary>
    public override void _Notification(int what)
    {
        if (what != NotificationWMCloseRequest || _protectPlayerSaves || !Lifecycle.HasSession ||
            GameManager.Instance?.State is not (GameState.Playing or GameState.Paused))
        {
            return;
        }

        if (!Lifecycle.AutosaveBeforeQuit(out string failureKey))
        {
            Log.Warn($"Window closed with unsaved progress; the autosave was refused ({failureKey}).");
        }
    }

    private void DismissTitle()
    {
        _menu?.QueueFree();
        _menu = null;
    }

    /// <summary>
    /// Dev convenience, parallel to <c>--validate</c>: launching with <c>-- --play</c> boots straight
    /// into the most recent save, so gameplay — and the systems that only initialise on a session
    /// build — can be driven deterministically from the command line. Every flag in
    /// <see cref="SessionHarnesses"/> implies it, because each harness needs a live session with a
    /// real player in it.
    ///
    /// <para><c>--slot=&lt;name&gt;</c> (or <c>EMBERVALE_SLOT</c>) continues that save instead of the
    /// newest. <c>--new-game</c> starts a fresh game with the default profile instead of continuing
    /// anything, refused where it could land on a player's save (<see cref="SessionEntryRules"/>).
    /// <c>--quit-after=&lt;seconds&gt;</c> ends the process on its own: exit 0, or 1 when invariant
    /// violations were recorded or the requested session never started.</para>
    ///
    /// <para>Runs once, from <c>_Ready</c>. It used to need a consumed-latch, because the method
    /// that held it ran again every time the player quit to the title and would drop them straight
    /// back into the save they had just left. Quitting no longer reloads the scene, so the flag is
    /// read exactly once and the latch is gone with the reload that made it necessary.</para>
    /// </summary>
    private void RunCommandLineSessionIfRequested()
    {
        if (!ScheduleQuitIfRequested())
        {
            return;
        }

        List<(SessionHarness Harness, string Flag)> harnesses = SessionHarnesses.Requested();
        bool newGame = HeadlessArgs.Has(NewGameArgument);
        if (harnesses.Count == 0 && !newGame && !HeadlessArgs.Has(PlayArgument))
        {
            return;
        }

        string mode = harnesses.Count > 0 ? harnesses[0].Flag : newGame ? NewGameArgument : PlayArgument;
        if (newGame)
        {
            if (!StartCommandLineNewGame(mode))
            {
                _sessionEntryFailed = true;
                return;
            }
        }
        else if (MostRecentSlot() is { } slot)
        {
            Log.Info($"{mode}: continuing most recent save '{slot}'.");
            StartLoadedGame(slot);
        }
        else
        {
            // Staying on the title is the documented behaviour; saying so is what keeps an
            // unattended run from waiting on a harness that was never attached.
            Log.Warn($"{mode}: there is no save to continue, so the game stays on the title and nothing " +
                     $"was attached. Pass {NewGameArgument} to start a fresh game instead.");
            _sessionEntryFailed = true;
            return;
        }

        if (Lifecycle.Session is not { } session)
        {
            _sessionEntryFailed = true;
            return;
        }

        foreach ((SessionHarness harness, _) in harnesses)
        {
            if (harness.Capture)
            {
                // Synchronous viewport readback and PNG compression dominate capture frames. Keep
                // those tool costs out of the world's sustained-performance telemetry; an ordinary
                // --play run continues to sample the exact same budgets.
                session.WorldDirector.Streamer?.SetPerformanceSamplingEnabled(false);
            }

            AddChild(harness.Create(session));
        }
    }

    /// <summary>
    /// <c>--new-game</c>: the real New Game path with the default profile, into <c>--slot</c> or
    /// <see cref="SessionEntryRules.AutomationSlot"/>. Outside an isolated user directory only an
    /// automation slot is accepted, and then autosaves (the ring and the one on window close) are
    /// off for the process, so the run writes nothing a player owns.
    /// </summary>
    private bool StartCommandLineNewGame(string mode)
    {
#if EMBERVALE_TOOLING
        const bool tooling = true;
#else
        const bool tooling = false;
#endif
        string slot = RequestedSlot() ?? SessionEntryRules.AutomationSlot;
        bool isolated = SessionEntryRules.IsIsolated(OS.GetEnvironment("EMBERVALE_USER_DIR"), tooling);
        if (SessionEntryRules.NewGameRefusal(slot, isolated) is { } refusal)
        {
            Log.Error($"{NewGameArgument} refused: {refusal}.");
            return false;
        }

        if (!isolated)
        {
            AutosaveService.Suppressed = true;
            _protectPlayerSaves = true;
        }

        Log.Info($"{mode}: starting a new game in slot '{slot}'" +
                 (isolated ? " (isolated user directory)." : " (autosaves off)."));
        StartNewGame(slot, Races.CharacterProfile.Human);
        return true;
    }

    /// <summary><c>--quit-after=&lt;seconds&gt;</c>: real seconds, counted through pause and time
    /// scale, from the moment the shell is ready. False when the value was unusable and the process
    /// is already quitting.</summary>
    private bool ScheduleQuitIfRequested()
    {
        if (!HeadlessArgs.Has(QuitAfterArgument))
        {
            return true;
        }

        float seconds = HeadlessArgs.Float(QuitAfterArgument, -1f);
        if (seconds < 0f)
        {
            Log.Error($"{QuitAfterArgument} needs a number of seconds, e.g. {QuitAfterArgument}=20; quitting now.");
            GetTree().Quit(1);
            return false;
        }

        SceneTreeTimer timer = GetTree().CreateTimer(
            Mathf.Max(seconds, 0.05f), processAlways: true, processInPhysics: false, ignoreTimeScale: true);
        timer.Timeout += () =>
        {
            int violations = Invariant.Violations;
            int code = violations > 0 || _sessionEntryFailed ? 1 : 0;
            Log.Info($"{QuitAfterArgument}: {seconds:0.##}s elapsed, {violations} invariant violation(s)" +
                     (_sessionEntryFailed ? ", the requested session never started" : string.Empty) +
                     $"; exit {code}.");
            GetTree().Quit(code);
        };
        return true;
    }

    /// <summary>The slot named by <c>--slot=&lt;name&gt;</c>, else by <c>EMBERVALE_SLOT</c>, else null.</summary>
    private static string? RequestedSlot()
    {
        string? wanted = HeadlessArgs.Value(SlotArgument);
        if (string.IsNullOrEmpty(wanted))
        {
            wanted = OS.GetEnvironment("EMBERVALE_SLOT");
        }

        return string.IsNullOrEmpty(wanted) ? null : wanted;
    }

    private static string? MostRecentSlot()
    {
        if (SaveManager.Instance is not { } manager)
        {
            return null;
        }

        // --slot / EMBERVALE_SLOT picks a slot by name, for a capture that needs a particular world
        // state (no live event banner, say). Unknown names fall through to the newest save.
        string? wanted = RequestedSlot();
        SaveSlotInfo? latest = null;
        foreach (SaveSlotInfo info in manager.ListSlots())
        {
            if (wanted != null && info.Slot == wanted)
            {
                return info.Slot;
            }

            if (latest == null || info.TimestampUnix > latest.TimestampUnix)
            {
                latest = info;
            }
        }

        if (wanted != null && latest != null)
        {
            Log.Warn($"Requested slot '{wanted}' does not exist; continuing the newest save '{latest.Slot}' instead.");
        }

        return latest?.Slot;
    }
}
