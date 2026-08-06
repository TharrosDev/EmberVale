using System.Collections.Generic;

namespace Embervale.Core;

/// <summary>
/// Tracks which blocking menus (inventory, crafting, dialogue, map, settings, dev console) are
/// currently open. While any is open, the player controller suspends look/move/attack so UI clicks
/// don't also drive the character, and the mouse stays free.
///
/// Owners are counted, not a single flag: closing an inner overlay (e.g. the dev console opened over
/// the inventory) must NOT recapture the mouse while an outer menu is still up. Each surface registers
/// itself on open and removes itself on close; <see cref="MenuOpen"/> is the aggregate. Kept in Core
/// (and Godot-free) so gameplay code can read it without depending on the UI layer.
/// </summary>
public static class UiState
{
    private static readonly HashSet<object> _owners = new();
    private static readonly HashSet<object> _worldPausers = new();

    /// <summary>Raised whenever a menu opens or closes. <c>GameManager</c> listens so the scene
    /// tree's paused flag is the single answer to "is a menu or the pause state holding the world",
    /// instead of every gameplay system having to remember to ask.</summary>
    public static event System.Action? Changed;

    /// <summary>True while any blocking menu is open.</summary>
    public static bool MenuOpen => _owners.Count > 0;

    /// <summary>
    /// True while an open menu should freeze the simulation. Distinct from <see cref="MenuOpen"/>
    /// because a <em>cinematic</em> lock (the boss intro, the opening narration) also suspends the
    /// player's controls but must leave the world running — the thing it holds the player still to
    /// watch is in that world.
    /// </summary>
    public static bool WorldPaused => _worldPausers.Count > 0;

    /// <summary>How many blocking menus are open (diagnostics / tests).</summary>
    public static int OpenCount => _owners.Count;

    /// <summary>
    /// Registers a blocking menu owner (idempotent — a repeat open is harmless).
    /// <paramref name="pausesWorld"/> defaults to true: a menu the player reads at their own pace
    /// must stop the clock, because the player controller has already suspended their movement,
    /// guard and dodge, so anything still swinging at them is unanswerable. Pass false only for a
    /// cinematic lock that needs the world to keep playing.
    /// </summary>
    public static void Open(object owner, bool pausesWorld = true)
    {
        _owners.Add(owner);
        if (pausesWorld)
        {
            _worldPausers.Add(owner);
        }

        Changed?.Invoke();
    }

    /// <summary>Removes a blocking menu owner (no-op if it wasn't registered).</summary>
    public static void Close(object owner)
    {
        _owners.Remove(owner);
        _worldPausers.Remove(owner);
        Changed?.Invoke();
    }

    /// <summary>
    /// Drops every owner.
    ///
    /// Two callers, both needing the same thing: a test, so one case's leaked menu cannot fail the
    /// next; and **quit-to-menu** (37.5H), which reloads the scene out from under whatever panels
    /// were open. Owners live in a process-lifetime static that a scene reload does not touch, so
    /// without this the pause menu that triggered the return would still be registered — and since
    /// a registered owner is also a world-pauser, the title screen would come back with the tree
    /// paused and no menu on screen to clear it.
    /// </summary>
    public static void ClearAll()
    {
        _owners.Clear();
        _worldPausers.Clear();
        Changed?.Invoke();
    }
}
