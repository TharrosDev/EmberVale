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

#if EMBERVALE_TOOLING
    /// <summary>True when a command-line session was asked for and did not start.</summary>
    private bool _sessionEntryFailed;
#endif

    public SessionLifecycleCoordinator Lifecycle { get; init; } = null!;

    public override void _Ready()
    {
        Lifecycle.SessionEnded += ShowTitle;
        ShowTitle();
#if EMBERVALE_TOOLING
        if (ScheduleQuitIfRequested())
        {
            _sessionEntryFailed = !RunCommandLineSessionIfRequested();
        }
#else
        RunCommandLineSessionIfRequested();
#endif
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
        if (HeadlessArgs.User.Has("--shellshots"))
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
        if (what != NotificationWMCloseRequest || !Lifecycle.HasSession ||
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
    /// newest. In a tooling build, <c>--new-game</c> starts a fresh game with the default profile
    /// instead of continuing anything, refused unless the run has an isolated user directory
    /// (<c>SessionEntryRules</c>), and <c>--quit-after=&lt;seconds&gt;</c> ends the process on its
    /// own: exit 0, or 1 when invariant violations were recorded or the requested session never
    /// started. A shipping build reads neither.</para>
    ///
    /// <para>These are read from the user arguments (after <c>--</c>) only, as they always were:
    /// the engine has a <c>--quit-after</c> of its own, counted in frames.</para>
    ///
    /// <para>Runs once, from <c>_Ready</c>. It used to need a consumed-latch, because the method
    /// that held it ran again every time the player quit to the title and would drop them straight
    /// back into the save they had just left. Quitting no longer reloads the scene, so the flag is
    /// read exactly once and the latch is gone with the reload that made it necessary.</para>
    /// </summary>
    /// <returns>False when a session was asked for and did not start.</returns>
    private bool RunCommandLineSessionIfRequested()
    {
        List<(SessionHarness Harness, string Flag)> harnesses = SessionHarnesses.Requested();
#if EMBERVALE_TOOLING
        bool newGame = HeadlessArgs.User.Has(NewGameArgument);
#else
        bool newGame = false;
#endif
        if (harnesses.Count == 0 && !newGame && !HeadlessArgs.User.Has(PlayArgument))
        {
            return true;
        }

        string mode = harnesses.Count > 0 ? harnesses[0].Flag : newGame ? NewGameArgument : PlayArgument;
        if (newGame)
        {
#if EMBERVALE_TOOLING
            if (!StartCommandLineNewGame(mode))
            {
                return false;
            }
#endif
        }
        else if (MostRecentSlot() is { } slot)
        {
            Log.Info($"{mode}: continuing most recent save '{slot}'.");
            StartLoadedGame(slot);
        }
        else
        {
            // Staying on the title is the documented behaviour; saying so is what keeps an
            // unattended run from waiting on a harness that was never attached. Info, not a
            // warning: this used to be silent, and a warning would fail a strict SDK run that
            // passed before. --quit-after reports it in the exit code.
            Log.Info($"{mode}: there is no save to continue, so the game stays on the title and nothing " +
                     $"was attached. Pass {NewGameArgument} to start a fresh game instead.");
            return false;
        }

        if (Lifecycle.Session is not { } session)
        {
            return false;
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

        return true;
    }

#if EMBERVALE_TOOLING
    /// <summary>
    /// <c>--new-game</c>: the real New Game path with the default profile, into <c>--slot</c> or
    /// <see cref="SessionEntryRules.AutomationSlot"/>. Only under an isolated user directory, the
    /// rule <see cref="ApplicationRoot.AutomationNewGame"/> applies: a session saves through more
    /// doors than the shell can close (the autosave ring, the pause menu's quit, F5), so the only
    /// run that cannot write over a player's save is one whose saves live somewhere else.
    /// </summary>
    private bool StartCommandLineNewGame(string mode)
    {
        string slot = RequestedSlot() ?? SessionEntryRules.AutomationSlot;
        bool isolated = SessionEntryRules.IsIsolated(OS.GetEnvironment("EMBERVALE_USER_DIR"));
        if (SessionEntryRules.NewGameRefusal(slot, isolated) is { } refusal)
        {
            Log.Error($"{NewGameArgument} refused: {refusal}.");
            return false;
        }

        Log.Info($"{mode}: starting a new game in slot '{slot}' (isolated user directory).");
        StartNewGame(slot, Races.CharacterProfile.Human);
        return true;
    }

    /// <summary><c>--quit-after=&lt;seconds&gt;</c>: real seconds, counted through pause and time
    /// scale, from the moment the shell is ready. False when the value was unusable and the process
    /// is already quitting.</summary>
    private bool ScheduleQuitIfRequested()
    {
        if (!HeadlessArgs.User.Has(QuitAfterArgument))
        {
            return true;
        }

        float seconds = HeadlessArgs.User.Float(QuitAfterArgument, -1f);
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
#endif

    /// <summary>The slot named by <c>--slot=&lt;name&gt;</c>, else by <c>EMBERVALE_SLOT</c>, else null.</summary>
    private static string? RequestedSlot()
    {
        string? wanted = HeadlessArgs.User.Value(SlotArgument);
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
