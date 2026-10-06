using Embervale.Core;
using Embervale.Core.Diagnostics;
using Embervale.Core.Events;
using Embervale.Core.Services;
using Embervale.Debugging;
using Embervale.Localization;
using Embervale.Settings;
using Godot;

namespace Embervale.Bootstrap;

/// <summary>
/// The process. Attached to the root of <c>Main.tscn</c>, it is the outermost of the three
/// composition roots and owns exactly what outlives every save file: the command-line tool modes,
/// the content databases, input actions, localization, the audio bus layout and the settings
/// service — plus the shell that shows the title screen and the host that opens and closes
/// sessions.
///
/// <para>It knows nothing about the world, the player, quests or the HUD. Those belong to a
/// <see cref="GameSession"/>, which <see cref="SessionLifecycleCoordinator"/> creates and destroys
/// underneath this node. Before the 2026-09-03 overhaul all of it lived here, in one 1500-line
/// class, and the consequence was that there was no moment at which a session ended: quitting to
/// the title reloaded the scene, because that was the only way to get rid of the world.</para>
/// </summary>
public partial class ApplicationRoot : Node3D, IServiceScopeHost
{
    private ServiceScope? _scope;

    /// <summary>Application lifetime. Settings and input configuration live here; nothing a save
    /// file owns does.</summary>
    public ServiceScope Scope => _scope ??= new ServiceScope(ServiceLifetime.Application);

    public SessionLifecycleCoordinator Lifecycle { get; private set; } = null!;

    public GameShellController Shell { get; private set; } = null!;

#if EMBERVALE_TOOLING
    /// <summary>Finite SDK adapter: uses the real new-game path with an isolated slot.</summary>
    public void AutomationNewGame()
    {
        if (string.IsNullOrEmpty(OS.GetEnvironment("EMBERVALE_USER_DIR")))
        {
            GD.PushError("Automation requires an isolated EMBERVALE_USER_DIR");
            return;
        }
        Shell.AutomationNewGame();
    }

    [Export] public bool AutomationPlaying
    {
        get => GameManager.Instance is { IsPlaying: true } && Lifecycle.Session?.Players.Player != null;
        private set { } // Derived diagnostic state; deserialization cannot change it.
    }
#endif

    public override void _Ready()
    {
        // The tool modes run before anything is built and quit the process: they must be fast and
        // side-effect free, which they can only be if nothing above has assembled a world yet.
        if (RunHeadlessModeIfRequested())
        {
            return;
        }

        Log.Info("=== Embervale starting ===");

        // The application root is the flow manager, so it must keep processing while the tree is
        // paused (something has to be able to unpause).
        ProcessMode = ProcessModeEnum.Always;

        InstallApplicationServices();

        Lifecycle = new SessionLifecycleCoordinator { Name = "SessionHost" };
        AddChild(Lifecycle);

        // The lifecycle probe replaces the shell rather than sitting beside it: it drives sessions
        // itself, and it has to do so through the same boot the player gets or it proves nothing.
        if (HeadlessLifecycle.Requested())
        {
            HeadlessLifecycle.Run(this, Lifecycle);
            return;
        }

        if (HeadlessStory.Requested())
        {
            HeadlessStory.Run(this, Lifecycle);
            return;
        }

        Shell = new GameShellController { Name = "Shell", Lifecycle = Lifecycle };
        AddChild(Shell);
    }

    private static void OnSettingsApplied(SettingsAppliedEvent e) => Embervale.UI.UiSkin.Install();

    public override void _ExitTree()
    {
        EventBus.Instance?.Unsubscribe<SettingsAppliedEvent>(OnSettingsApplied);
        _scope?.Dispose();
        _scope = null;

        // Safety net for process teardown: every gameplay node unsubscribes in its own OnTeardown,
        // but a leaked handler would keep a freed object alive. The autoloads never subscribe, so
        // clearing here is safe. A non-zero count is a bug worth naming, not a routine event.
        int leaked = EventBus.Instance?.TotalSubscriberCount() ?? 0;
        if (leaked > 0)
        {
            Log.Warn($"{leaked} event handler(s) survived application teardown (check OnTeardown unsubscribes).");
        }

        EventBus.Instance?.Clear();
    }

    /// <summary>
    /// The command-line report and production modes. Each loads the content databases, prints, and quits;
    /// only <c>--validate</c> is a gate (exit 1 on failure). Returns true when one ran, in which
    /// case nothing else should be built.
    /// </summary>
    private bool RunHeadlessModeIfRequested()
    {
        SceneTree tree = GetTree();

        if (HeadlessWorldBake.Requested())
        {
            HeadlessWorldBake.Run(tree);
            return true;
        }

        if (HeadlessValidation.Requested())
        {
            HeadlessValidation.Run(tree);
            return true;
        }

        if (HeadlessEconomy.Requested())
        {
            HeadlessEconomy.Run(tree);
            return true;
        }

        if (HeadlessWorldGen.Requested())
        {
            HeadlessWorldGen.Run(tree);
            return true;
        }

        if (HeadlessWorldMap.Requested())
        {
            HeadlessWorldMap.Run(tree);
            return true;
        }

        if (HeadlessState.Requested())
        {
            HeadlessState.Run(tree);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Application-lifetime installation, in the one order that works: input actions, then the
    /// string catalogue (so every UI built later resolves through <c>Loc.T</c>), then the content
    /// databases, then the mixer buses (so the settings apply below finds buses to set), then
    /// settings, then the content validator over the now-populated databases.
    /// </summary>
    private void InstallApplicationServices()
    {
        GameInput.EnsureActions();
        Loc.Initialize();
        ContentDatabases.InitializeAll();
        Embervale.Audio.AudioBusLayout.Ensure();

        var settings = new SettingsService();
        settings.LoadAndApply();
        Scope.Register(settings);

        // The interface's sound, for the title screen as much as for a session. After the buses
        // and their volumes exist; before any screen that could make one.
        AddChild(new Embervale.UI.UiAudio { Name = "UiAudio" });

        // After the settings exist (the skin reads high contrast and text scale) and before any
        // screen is built. Re-run on every apply; it rebuilds only when one of those two moved.
        Embervale.UI.UiSkin.Install();
        EventBus.Instance?.Subscribe<SettingsAppliedEvent>(OnSettingsApplied);

        // What the hosted runtime is actually running with. The project's runtimeconfig is honoured
        // only by an exported game (the editor binary starts .NET from the engine's own), so the
        // csproj cannot be trusted to say; this line can.
        Log.Info($"Runtime: .NET {System.Environment.Version}, " +
                 $"{(System.Runtime.GCSettings.IsServerGC ? "server" : "workstation")} GC, " +
                 $"latency {System.Runtime.GCSettings.LatencyMode}, " +
                 $"{System.Environment.ProcessorCount} logical core(s).");

        // Broken authored references surface here at boot rather than mid-playthrough. A release
        // export skips it: several arms read .tscn source text, which an export ships as binary, so
        // they would report every scene-authored reference as missing. --validate is the real gate.
        //
        // ⚠️ HEADLESS BOOTS ONLY (2026-10 performance pass). The pass is not a cross-reference
        // check any more: ValidateRegions loads every cell scene of every region AND loads and
        // instantiates its prepared twin out of data/world_bake, and pins all six prepared region
        // packages in ResidentResources for the life of the process. Run before the title screen
        // on every windowed debug boot, that was the whole baked world read off disk and built
        // once so a player could look at a menu, and the packages of realms never visited held in
        // memory afterwards. The headless session gates (--lifecycle, --story) still run it here
        // because they read Invariant.Violations; a windowed session gets it from the `validate`
        // console command, and `--validate` remains the gate.
        if (OS.IsDebugBuild() && DisplayServer.GetName() == "headless")
        {
            Log.Info(ContentValidator.Run());
        }
    }
}
