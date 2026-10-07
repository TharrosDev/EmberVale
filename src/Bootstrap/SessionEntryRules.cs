using System.IO;

namespace Embervale.Bootstrap;

/// <summary>
/// When a command-line <c>--new-game</c> may run. A fresh game is a live session, and a session
/// saves through the autosave ring, the pause menu's quit and the quick-save key, so it is only
/// allowed where none of those can land on a player's save: under an isolated
/// <c>EMBERVALE_USER_DIR</c>. Tooling builds only (a shipping build has no <c>--new-game</c> and
/// does not honour the variable). Pure, so the rule is unit-tested.
/// </summary>
public static class SessionEntryRules
{
    /// <summary>The slot <c>--new-game</c> starts in when <c>--slot</c> names none.</summary>
    public const string AutomationSlot = "automation";

    /// <summary>True when <paramref name="userDirectory"/> redirects saves away from the player's
    /// folder, by the same test <c>UserDataPaths</c> applies.</summary>
    public static bool IsIsolated(string? userDirectory) =>
        !string.IsNullOrWhiteSpace(userDirectory) && Path.IsPathFullyQualified(userDirectory);

    /// <summary>Why a new game into <paramref name="slot"/> is refused, or null when it may run.</summary>
    public static string? NewGameRefusal(string slot, bool isolated)
    {
        if (!isolated)
        {
            return "a new game could overwrite a player's saves: set EMBERVALE_USER_DIR to an absolute " +
                   "directory for the run (the SDK does)";
        }

        if (slot.Length == 0 || slot.Length > 64)
        {
            return "the slot name must be 1 to 64 characters";
        }

        // A slot name becomes a directory name.
        foreach (char c in slot)
        {
            if (!(char.IsAsciiLetterOrDigit(c) || c is '_' or '-'))
            {
                return $"the slot name '{slot}' may only contain letters, digits, '_' and '-'";
            }
        }

        return null;
    }
}
