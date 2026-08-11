namespace Embervale.UI;

/// <summary>What the player is doing, as far as the HUD is concerned (39.5B).</summary>
public enum HudMode
{
    /// <summary>Normal play. The whole HUD is live.</summary>
    Exploration,

    /// <summary>A blocking menu is up (inventory, vendor, pause, character screen). Gameplay is
    /// paused underneath, so every widget on the HUD is reporting a world that is not moving.</summary>
    Menu,

    /// <summary>Not in a play session at all — the main menu, a load, a teardown.</summary>
    Inactive,
}

/// <summary>
/// Which HUD widgets are on, per <see cref="HudMode"/> (39.5B, §35/§52/§55).
///
/// ⚠️ <b>Before this, the gameplay HUD had no visibility logic whatsoever.</b> <c>GameHud</c> was a
/// <c>CanvasLayer</c> that came up with the world and stayed up: the quest tracker, the interaction
/// prompt, the compass, the minimap and the vitals all sat on top of the pause menu, the inventory
/// and the vendor window. The prompt was the worst of them — it kept offering an interaction the
/// player could not take, because a blocking menu pauses the tree.
///
/// ⚠️ <b>There is deliberately no Dead mode</b> (§54, and 40B's "a cut system leaves no stub").
/// Embervale's player death has no duration to transition into: <c>GameBootstrap.OnEntityDied</c>
/// calls <c>RespawnPlayer</c> synchronously, so the player is repositioned and refilled in the same
/// frame they die. There is no death screen, no respawn countdown and no window in which a death HUD
/// could be seen. What death DOES need is transient overlays cleared so a hit taken on the way down
/// is not still fading at the new spawn point, and <c>GameHud</c> does that off the death event
/// directly. Add the mode when the game gains a death state, not before.
///
/// Pure and engine-free so the transitions are testable: a mode table nothing can leave stale is the
/// whole point, and a stale widget is invisible to a build and to a headless run alike.
///
/// ⚠️ <b>It decides nothing about gameplay.</b> The mode is <i>resolved from</i> the existing
/// authorities — <c>GameManager.IsPlaying</c> and <c>UiState.MenuOpen</c> — never tracked alongside
/// them. There is no second pause flag here (CLAUDE.md §7).
/// </summary>
public static class HudVisibility
{
    /// <summary>The mode for a given world state. <paramref name="playing"/> loses to nothing:
    /// outside a session there is no HUD to have a mode.</summary>
    public static HudMode ModeFor(bool playing, bool menuOpen) =>
        !playing ? HudMode.Inactive
        : menuOpen ? HudMode.Menu
        : HudMode.Exploration;

    /// <summary>The HUD as a whole — true only during live play.</summary>
    public static bool ShowsHud(HudMode mode) => mode == HudMode.Exploration;

    /// <summary>
    /// Vitals, spell and status — the "am I alive and what am I holding" group.
    ///
    /// Kept up through a menu on purpose, and it is the one exception worth having: an inventory is
    /// where a player drinks a potion, and hiding the health bar at the moment they are deciding
    /// whether to is the interface losing the plot. Everything else in a menu is noise.
    /// </summary>
    public static bool ShowsVitals(HudMode mode) => mode is HudMode.Exploration or HudMode.Menu;

    /// <summary>Navigation: compass, minimap, quest tracker, clock. Exploration only — none of it is
    /// answering a question the player has while a blocking menu is up.</summary>
    public static bool ShowsNavigation(HudMode mode) => mode == HudMode.Exploration;

    /// <summary>
    /// The interaction prompt.
    ///
    /// ⚠️ <b>Strictly exploration.</b> §31 forbids a prompt for an interaction the player cannot
    /// perform, and a blocking menu pauses the tree — so a prompt surviving into a menu is offering
    /// a key that provably does nothing.
    /// </summary>
    public static bool ShowsPrompt(HudMode mode) => mode == HudMode.Exploration;

    /// <summary>Combat overlays: the lock-on reticle, the damage-direction arcs, the boss frame and
    /// the aimed-at nameplate. Nothing about a fight is live in any other mode.</summary>
    public static bool ShowsCombat(HudMode mode) => mode == HudMode.Exploration;
}
