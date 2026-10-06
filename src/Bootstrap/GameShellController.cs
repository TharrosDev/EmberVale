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
    private MainMenu? _menu;

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
        if (HasFlag("--shellshots"))
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
    /// build — can be driven deterministically from the command line. The five capture flags imply
    /// it, because each harness needs a live session with a real player in it.
    ///
    /// <para>Runs once, from <c>_Ready</c>. It used to need a consumed-latch, because the method
    /// that held it ran again every time the player quit to the title and would drop them straight
    /// back into the save they had just left. Quitting no longer reloads the scene, so the flag is
    /// read exactly once and the latch is gone with the reload that made it necessary.</para>
    /// </summary>
    private void RunCommandLineSessionIfRequested()
    {
        bool hudShots = HasFlag("--hudshots");
        bool panelShots = HasFlag("--panelshots");
        bool shrineShots = HasFlag("--shrine-shots");
        bool guildShots = HasFlag("--guild-shots");
        bool enemyShots = HasFlag("--enemy-shots");
        bool combatShots = HasFlag("--combat-shots") || HasFlag("--combatshots");
        bool metaShots = HasFlag("--metashots");
        bool tradeShots = HasFlag("--tradeshots");
        bool lookShots = HasFlag("--look-shots");
        bool uiShots = HasFlag("--uishots");
        bool spellShots = HasFlag("--spellshots");
        bool camShots = HasFlag("--camshots");
        bool vfxPerf = HasFlag("--vfxperf");
        bool capture = hudShots || panelShots || shrineShots || guildShots || enemyShots || combatShots || lookShots || uiShots
            || metaShots || tradeShots || spellShots || camShots;

        // The effect scenario needs a session like the capture harnesses do, but it is there to
        // measure frame times, so the world's performance sampling stays on for it.
        bool scenario = capture || vfxPerf;

        if ((!scenario && !HasFlag("--play")) || MostRecentSlot() is not { } slot)
        {
            return;
        }

        string mode = hudShots ? "--hudshots"
            : panelShots ? "--panelshots"
            : shrineShots ? "--shrine-shots"
            : guildShots ? "--guild-shots"
            : enemyShots ? "--enemy-shots"
            : combatShots ? (HasFlag("--combatshots") ? "--combatshots" : "--combat-shots")
            : lookShots ? "--look-shots"
            : uiShots ? "--uishots"
            : metaShots ? "--metashots"
            : tradeShots ? "--tradeshots"
            : spellShots ? "--spellshots"
            : camShots ? "--camshots"
            : vfxPerf ? "--vfxperf"
            : "--play";
        Log.Info($"{mode}: continuing most recent save '{slot}'.");
        StartLoadedGame(slot);

        if (Lifecycle.Session is not { } session)
        {
            return;
        }

        if (capture)
        {
            // Synchronous viewport readback and PNG compression dominate capture frames. Keep those
            // tool costs out of the world's sustained-performance telemetry; an ordinary --play run
            // continues to sample the exact same budgets.
            session.WorldDirector.Streamer?.SetPerformanceSamplingEnabled(false);
        }

#if EMBERVALE_TOOLING
        if (hudShots)
        {
            AddChild(new Debugging.HudShots { Name = "HudShots" });
        }

        if (panelShots)
        {
            AddChild(new Debugging.PanelShots
            {
                Name = "PanelShots",
                Map = session.Ui.Map,
                Journal = session.Ui.QuestLog,
                Character = session.Ui.Inventory,
                Vendor = session.Ui.Vendor,
                Dialogue = session.Ui.Dialogue,
            });
        }

        if (uiShots)
        {
            AddChild(new Debugging.UiAuditShots { Name = "UiAuditShots" });
        }

        if (shrineShots)
        {
            AddChild(new Debugging.ShrineShots { Name = "ShrineShots" });
        }

        if (guildShots)
        {
            AddChild(new Debugging.GuildShots { Name = "GuildShots", Dialogue = session.Ui.Dialogue });
        }

        if (enemyShots)
        {
            AddChild(new Debugging.EnemyShots { Name = "EnemyShots" });
        }

        if (combatShots)
        {
            AddChild(new Debugging.CombatShots { Name = "CombatShots" });
        }

        if (lookShots)
        {
            AddChild(new Debugging.LookShots { Name = "LookShots" });
        }

        if (metaShots)
        {
            AddChild(new Debugging.MetaShots { Name = "MetaShots" });
        }

        if (tradeShots)
        {
            AddChild(new Debugging.TradeShots { Name = "TradeShots" });
        }

        if (spellShots)
        {
            AddChild(new Debugging.SpellShots { Name = "SpellShots" });
        }

        if (camShots)
        {
            AddChild(new Debugging.CamShots { Name = "CamShots" });
        }

        if (vfxPerf)
        {
            AddChild(new Debugging.VfxPerfScenario { Name = "VfxPerf" });
        }
#endif
    }

    /// <summary>True if <paramref name="flag"/> was passed after <c>--</c>.</summary>
    private static bool HasFlag(string flag)
    {
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg == flag)
            {
                return true;
            }
        }

        return false;
    }

    private static string? MostRecentSlot()
    {
        if (SaveManager.Instance is not { } manager)
        {
            return null;
        }

        // EMBERVALE_SLOT picks a slot by name, for a capture that needs a particular world state (no
        // live event banner, say). Unknown names fall through to the newest save.
        string wanted = OS.GetEnvironment("EMBERVALE_SLOT");
        SaveSlotInfo? latest = null;
        foreach (SaveSlotInfo info in manager.ListSlots())
        {
            if (wanted.Length > 0 && info.Slot == wanted)
            {
                return info.Slot;
            }

            if (latest == null || info.TimestampUnix > latest.TimestampUnix)
            {
                latest = info;
            }
        }

        return latest?.Slot;
    }
}
