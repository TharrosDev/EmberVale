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

    private void StartNewGame(string slot, Races.CharacterProfile profile)
    {
        DismissTitle();
        Lifecycle.StartNewGame(slot, profile);
    }

    private void StartLoadedGame(string slot)
    {
        DismissTitle();
        Lifecycle.StartLoadedGame(slot);
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
        bool capture = hudShots || panelShots || shrineShots || guildShots || enemyShots;

        if ((!capture && !HasFlag("--play")) || MostRecentSlot() is not { } slot)
        {
            return;
        }

        string mode = hudShots ? "--hudshots"
            : panelShots ? "--panelshots"
            : shrineShots ? "--shrine-shots"
            : guildShots ? "--guild-shots"
            : enemyShots ? "--enemy-shots"
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

        SaveSlotInfo? latest = null;
        foreach (SaveSlotInfo info in manager.ListSlots())
        {
            if (latest == null || info.TimestampUnix > latest.TimestampUnix)
            {
                latest = info;
            }
        }

        return latest?.Slot;
    }
}
