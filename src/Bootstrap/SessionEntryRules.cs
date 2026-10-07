using System;
using System.IO;

namespace Embervale.Bootstrap;

/// <summary>
/// When a command-line <c>--new-game</c> may run. A fresh game writes a save, so it is only allowed
/// where it cannot land on a player's own: under an isolated <c>EMBERVALE_USER_DIR</c> (any slot),
/// or in a dedicated automation slot (a name starting <c>automation</c>) with autosaves off. Pure,
/// so the rule is unit-tested.
/// </summary>
public static class SessionEntryRules
{
    /// <summary>The slot <c>--new-game</c> writes when none is named, and the prefix that marks a
    /// slot as automation's own.</summary>
    public const string AutomationSlot = "automation";

    /// <summary>True when <paramref name="userDirectory"/> redirects saves away from the player's
    /// folder. Only a tooling build honours the variable (<c>UserDataPaths</c>), so in a shipping
    /// build nothing is ever isolated.</summary>
    public static bool IsIsolated(string? userDirectory, bool toolingBuild) =>
        toolingBuild && !string.IsNullOrWhiteSpace(userDirectory) && Path.IsPathFullyQualified(userDirectory);

    public static bool IsAutomationSlot(string slot) =>
        slot.StartsWith(AutomationSlot, StringComparison.OrdinalIgnoreCase);

    /// <summary>Why a new game into <paramref name="slot"/> is refused, or null when it may run.</summary>
    public static string? NewGameRefusal(string slot, bool isolated)
    {
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

        if (!isolated && !IsAutomationSlot(slot))
        {
            return $"slot '{slot}' could overwrite a player's save: set an absolute EMBERVALE_USER_DIR " +
                   $"(tooling builds) or use a slot named '{AutomationSlot}...'";
        }

        return null;
    }
}
