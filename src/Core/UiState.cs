namespace Embervale.Core;

/// <summary>
/// Tiny shared flag for "a blocking menu is open" (inventory, future shop/dialogue).
/// While set, the player controller suspends look/move/attack so UI clicks don't
/// also drive the character. Kept in Core so gameplay code can read it without
/// depending on the UI layer.
/// </summary>
public static class UiState
{
    public static bool MenuOpen { get; set; }
}
