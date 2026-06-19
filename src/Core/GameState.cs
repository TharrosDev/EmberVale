namespace Embervale.Core;

/// <summary>
/// Top-level application/game flow states. The active state gates which input
/// is processed, whether the world simulates, and which UI is shown.
/// </summary>
public enum GameState
{
    /// <summary>Engine starting, autoloads initializing, before anything is shown.</summary>
    Boot,

    /// <summary>Title / main menu is active; no world simulating.</summary>
    MainMenu,

    /// <summary>A world is streaming in (level load, fast travel, save load).</summary>
    Loading,

    /// <summary>Normal interactive gameplay.</summary>
    Playing,

    /// <summary>Gameplay suspended; pause menu shown, simulation halted.</summary>
    Paused,

    /// <summary>Player has died; death/respawn flow is active.</summary>
    GameOver,
}
