using Embervale.Core.Diagnostics;
using Embervale.Core.Events;
using Godot;

namespace Embervale.Core;

/// <summary>
/// Authoritative owner of the top-level <see cref="GameState"/> machine,
/// registered as the <c>GameManager</c> autoload.
///
/// Every system that cares about flow (input routing, world simulation, UI,
/// audio) reacts to <see cref="GameStateChangedEvent"/> rather than polling
/// this object, keeping the manager free of system-specific knowledge.
/// </summary>
public sealed partial class GameManager : Node
{
    public static GameManager Instance { get; private set; } = null!;

    public GameState State { get; private set; } = GameState.Boot;

    public override void _EnterTree()
    {
        if (Instance != null && Instance != this)
        {
            QueueFree();
            return;
        }

        Instance = this;

        // The manager must keep ticking while the tree is paused so it can
        // resume the game out of the Paused state.
        ProcessMode = ProcessModeEnum.Always;
        UiState.Changed += RefreshPause;
    }

    public override void _ExitTree()
    {
        if (Instance == this)
        {
            UiState.Changed -= RefreshPause;
            Instance = null!;
        }
    }

    /// <summary>
    /// The one place the scene tree's paused flag is decided: paused while the game state says so,
    /// <b>or</b> while a blocking menu is open.
    ///
    /// The menu half is not cosmetic. A modal panel suspends the player's movement, guard, dodge and
    /// casts (see <c>PlayerInputRouter</c>), and before this it suspended nothing else — so reading
    /// the inventory mid-fight left a frozen, un-blocking player being hit by enemies that never
    /// stopped, with damage-over-time still ticking. Suspending the world here covers every system
    /// at once instead of relying on each to remember a <c>UiState.MenuOpen</c> check (only two of
    /// them ever did). Cinematic locks opt out via <c>UiState.Open(owner, pausesWorld: false)</c>.
    /// </summary>
    private void RefreshPause()
    {
        if (IsInsideTree())
        {
            GetTree().Paused = State == GameState.Paused || UiState.WorldPaused;
        }
    }

    public override void _Ready()
    {
        Log.Info("GameManager online.");
    }

    // Feed the device tracker (30.5J) from the one node guaranteed alive in every state
    // (ProcessMode Always, exists from boot) so prompt glyphs stay device-correct everywhere.
    public override void _Input(InputEvent @event) => InputDevice.Observe(@event);

    /// <summary>Transitions to a new state, pausing the tree as appropriate.</summary>
    public void ChangeState(GameState next)
    {
        if (next == State)
        {
            return;
        }

        GameState previous = State;
        State = next;

        // Halt the scene tree's simulation while paused; only nodes with
        // ProcessMode == Always (menus, this manager) keep running.
        RefreshPause();

        Log.Info($"GameState: {previous} -> {next}");
        EventBus.Instance?.Publish(new GameStateChangedEvent(previous, next));
    }

    public void TogglePause()
    {
        if (State == GameState.Playing)
        {
            ChangeState(GameState.Paused);
        }
        else if (State == GameState.Paused)
        {
            ChangeState(GameState.Playing);
        }
    }

    public bool IsPlaying => State == GameState.Playing;
}
