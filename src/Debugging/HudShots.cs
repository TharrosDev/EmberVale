using System.Collections.Generic;
using Embervale.Core;
using Embervale.Core.Diagnostics;
using Embervale.Core.Services;
using Embervale.Entities;
using Embervale.Magic;
using Embervale.Player;
using Embervale.Quests;
using Embervale.Stats;
using Embervale.World;
using Godot;

namespace Embervale.Debugging;

/// <summary>
/// The HUD screenshot harness — <c>godot --path . -- --hudshots</c> (39.5B).
///
/// ⚠️ <b>THIS IS THE TOOL THE REPO HAS BEEN MISSING FOR TWO SUB-PHASES.</b> 39.5A shipped three
/// screen-space defects through a fully green battery and named the gap; 39.5B then built a minimap,
/// a HUD slot, an overlay and a visibility system with no way to look at any of them. The reason is
/// structural and neither half of the toolchain closes it: <c>--play</c> boots the world but cannot
/// press a key, and the Godot MCP drives the <b>editor</b>, where the HUD does not exist at all
/// because <see cref="Bootstrap.GameBootstrap"/> constructs it at runtime.
///
/// So this drives the real HUD, in a real session, through real state, and renders each state to a
/// PNG an agent can actually open. It is <b>not</b> a test: it asserts nothing and gates nothing.
/// Its whole job is to turn "reviewed against the API" into "looked at".
///
/// ⚠️ <b>Every state is driven through the authoritative system, never by poking the HUD.</b> Low
/// health is <see cref="StatsComponent.SetCurrent"/>; a status chip is
/// <see cref="StatusEffectsComponent.Apply"/>; the menu state is <see cref="UiState.Open"/>. A
/// harness that set the widgets directly would photograph itself rather than the HUD, and would go
/// on producing perfect screenshots after the bindings broke.
/// </summary>
public sealed partial class HudShots : Node
{
    /// <summary>Where the PNGs land. <c>user://</c> resolves to the project's app-data folder; the
    /// absolute path is logged on every capture so it can be opened without guessing.</summary>
    private const string OutputDir = "user://hudshots";

    /// <summary>Frames to let the world settle before the first capture — the region streams in over
    /// several frames and a shot taken too early photographs a half-loaded world.</summary>
    private const int SettleFrames = 90;

    /// <summary>Frames between driving a state and capturing it. The HUD updates in
    /// <c>_Process</c> and several widgets ease over <see cref="UI.UiTheme"/> durations, so a capture
    /// on the next frame catches the transition rather than the state.</summary>
    private const int HoldFrames = 30;

    private readonly List<(string Name, System.Action Drive)> _shots = new();
    private int _index = -1;
    private int _countdown = SettleFrames;
    private bool _capturePending;

    public override void _Ready()
    {
        // Pause-immune: one of the states IS a blocking menu, and a paused harness cannot photograph
        // it or advance past it (CLAUDE.md §7's pause deadlock, from the other side).
        ProcessMode = ProcessModeEnum.Always;

        DirAccess.MakeDirRecursiveAbsolute(OutputDir);
        BuildShotList();
        Log.Info($"--hudshots: {_shots.Count} state(s) queued; output -> {ProjectSettings.GlobalizePath(OutputDir)}");
    }

    /// <summary>
    /// The states worth looking at, in the order the brief's §69 asks for them.
    ///
    /// Ordered so each builds on the last rather than resetting: the resources drain in sequence, the
    /// statuses land on the drained bars, and the menu shot comes last because it is the only one
    /// that changes what is on screen rather than what the widgets say.
    /// </summary>
    private void BuildShotList()
    {
        _shots.Add(("01-exploration", () => Stats()?.RefillResources()));

        _shots.Add(("02-health-low", () => SetFraction(StatType.Health, 0.18f)));

        _shots.Add(("03-mana-low", () => SetFraction(StatType.Mana, 0.08f)));

        _shots.Add(("04-endurance-empty", () => SetFraction(StatType.Stamina, 0f)));

        _shots.Add(("05-statuses", ApplyStatuses));

        // ⚠️ The save this harness loads has no active quest, so without this the tracker — and the
        // distance/bearing readout that is one of 39.5B's headline changes — never appears in a single
        // image. A capture set that silently omits the feature under review is the failure mode this
        // whole tool exists to prevent.
        _shots.Add(("05b-quest-tracked", StartAndTrackAQuest));

        _shots.Add(("06-night", () => SetHour(23)));

        _shots.Add(("07-dawn", () => SetHour(6)));

        // The visibility rule this sub-phase added — the one shot that proves a HUD is ABSENT.
        _shots.Add(("08-menu-open", () => UiState.Open(this)));

        _shots.Add(("09-menu-closed", () => UiState.Close(this)));
    }

    public override void _Process(double delta)
    {
        if (--_countdown > 0)
        {
            return;
        }

        // ⚠️ THE CAPTURE MUST COME AFTER THE HOLD, NOT ON THE FRAME AFTER THE DRIVE.
        //
        // The first version checked `_capturePending` at the top of the loop, so every image was
        // taken one frame after its state was driven — and `GetImage` returns the LAST DRAWN frame,
        // which is the one rendered before the drive landed. Every PNG was therefore a photograph of
        // the PREVIOUS state, correctly named after the current one. It was only obvious because the
        // clock is on screen: the shot named `07-dawn` read "23:00 (Night)". **A capture harness that
        // is off by one is worse than none — it produces confident evidence for the wrong claim.**
        if (_capturePending)
        {
            _capturePending = false;
            Capture(_shots[_index].Name);

            if (_index + 1 >= _shots.Count)
            {
                Log.Info($"--hudshots: wrote {_shots.Count} image(s) to {ProjectSettings.GlobalizePath(OutputDir)}");
                GetTree().Quit(0);
                return;
            }

            _countdown = 1; // drive the next state on the following frame
            return;
        }

        _index++;
        _countdown = HoldFrames;
        _capturePending = true;

        (string name, System.Action drive) = _shots[_index];
        drive();
        Log.Info($"--hudshots: [{_index + 1}/{_shots.Count}] {name}");
    }

    /// <summary>Renders the current frame to <c>OutputDir/&lt;name&gt;.png</c>.</summary>
    private void Capture(string name)
    {
        if (GetViewport()?.GetTexture()?.GetImage() is not { } image)
        {
            Log.Warn($"--hudshots: no viewport image for '{name}' — is this a headless run? " +
                     "This harness needs a real window; run it WITHOUT --headless.");
            return;
        }

        string path = $"{OutputDir}/{name}.png";
        Error error = image.SavePng(path);
        if (error != Error.Ok)
        {
            Log.Warn($"--hudshots: could not write '{path}' ({error}).");
            return;
        }

        Log.Info($"--hudshots: wrote {ProjectSettings.GlobalizePath(path)} ({image.GetWidth()}x{image.GetHeight()})");
    }

    // --- State drivers, all through the owning system ------------------------

    private static IEntity? Player() =>
        ServiceLocator.Instance is { } locator && locator.TryGet(out PlayerCharacter player) ? player : null;

    private static StatsComponent? Stats() => Player()?.GetComponent<StatsComponent>();

    private static void SetFraction(StatType type, float fraction)
    {
        if (Stats() is { } stats)
        {
            stats.SetCurrent(type, stats.GetMax(type) * fraction);
        }
    }

    /// <summary>Applies whatever status effects the game actually has, up to three — the row's
    /// crowding is the thing being looked at, and inventing effects to fill it would photograph a
    /// HUD this game cannot produce (§73).</summary>
    private static void ApplyStatuses()
    {
        if (Player() is not { } player ||
            player.GetComponent<StatusEffectsComponent>() is not { } effects)
        {
            return;
        }

        int applied = 0;
        foreach (StatusEffectResource definition in StatusEffectDatabase.All())
        {
            effects.Apply(definition, player);
            if (++applied >= 3)
            {
                return;
            }
        }
    }

    /// <summary>Starts the first quest the player can actually take and tracks it, so the tracker,
    /// its objective rows and the distance/bearing readout are all on screen to be looked at.</summary>
    private static void StartAndTrackAQuest()
    {
        if (Player()?.GetComponent<QuestLogComponent>() is not { } log)
        {
            return;
        }

        foreach (QuestResource quest in QuestDatabase.All)
        {
            if (log.StartQuest(quest))
            {
                log.Track(quest.Id);
                return;
            }
        }
    }

    private static void SetHour(int hour)
    {
        if (ServiceLocator.Instance is { } locator && locator.TryGet(out WorldClock clock))
        {
            clock.SetTimeOfDay(hour);
        }
    }
}
