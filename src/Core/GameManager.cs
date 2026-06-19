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
    }

    public override void _ExitTree()
    {
        if (Instance == this)
        {
            Instance = null!;
        }
    }

    public override void _Ready()
    {
        Log.Info("GameManager online.");
    }

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
        GetTree().Paused = next == GameState.Paused;

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
