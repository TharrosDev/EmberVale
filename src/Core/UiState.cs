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

    /// <summary>True while any blocking menu is open.</summary>
    public static bool MenuOpen => _owners.Count > 0;

    /// <summary>How many blocking menus are open (diagnostics / tests).</summary>
    public static int OpenCount => _owners.Count;

    /// <summary>Registers a blocking menu owner (idempotent — a repeat open is harmless).</summary>
    public static void Open(object owner) => _owners.Add(owner);

    /// <summary>Removes a blocking menu owner (no-op if it wasn't registered).</summary>
    public static void Close(object owner) => _owners.Remove(owner);
}
