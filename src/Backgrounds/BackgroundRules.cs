using System;
using System.Collections.Generic;
using Embervale.Dialogue;

namespace Embervale.Backgrounds;

/// <summary>
/// The pure rules a <see cref="BackgroundResource"/> must obey, Godot-free so the validator and the
/// unit tests share one definition. A background is a soft nudge, never a build: its whole kit (items
/// at their <c>Value</c> plus starting gold) stays under <see cref="MaxKitValue"/>, its stat deltas
/// within <see cref="MaxStatDelta"/> and its reputation tweaks within <see cref="MaxReputationTweak"/>,
/// so none of the eight can out-earn a few minutes of play or gate anything behind a pick.
/// </summary>
public static class BackgroundRules
{
    /// <summary>Id prefix every background carries; <c>CharacterProfile.Background</c> holds one of these or is "none".</summary>
    public const string IdPrefix = "background.";

    /// <summary>Prefix of the story flag a background sets (one per background, each read by a hub dialogue).</summary>
    public const string FlagPrefix = "flag.background.";

    /// <summary>Most gold-equivalent a starting kit may be worth: items at their value plus starting gold.</summary>
    public const int MaxKitValue = 80;

    /// <summary>Largest absolute stat delta a background may grant (races go to 5).</summary>
    public const float MaxStatDelta = 1f;

    /// <summary>Largest absolute starting-standing tweak with any one faction.</summary>
    public const int MaxReputationTweak = 5;

    /// <summary>Perk branches a background may lean toward (a UI badge only; "perks shape, never gate").
    /// Empty means no lean. Strings rather than an enum until the perk catalogue defines its branches.</summary>
    public static readonly string[] LeanBranches = { "warrior", "archer", "mage", "rogue", "crafter", "social" };

    public static bool IsLeanBranch(string branch) =>
        branch.Length == 0 || Array.IndexOf(LeanBranches, branch) >= 0;

    /// <summary>True when <paramref name="id"/> has the shape of a background id (not whether one exists).</summary>
    public static bool IsBackgroundId(string? id) =>
        id != null && id.StartsWith(IdPrefix, StringComparison.Ordinal) && id.Length > IdPrefix.Length;

    /// <summary>The background id a profile's free-form <c>Background</c> field resolves to: the trimmed
    /// text when it has the id shape, otherwise empty. Pre-background saves stored a line of free text
    /// there; that reads as no background and is left alone rather than rewritten.</summary>
    public static string ResolveId(string? raw)
    {
        string trimmed = raw?.Trim() ?? string.Empty;
        return IsBackgroundId(trimmed) ? trimmed : string.Empty;
    }

    /// <summary>Splits a kit entry <c>"item.id"</c> or <c>"item.id:count"</c>; false for a malformed or non-positive entry.</summary>
    public static bool TryParseItem(string? entry, out string itemId, out int count) =>
        DialogueRules.TryParseIdAmount(entry, 1, out itemId, out count) && count >= 1;

    /// <summary>Gold-equivalent of a kit: <paramref name="gold"/> plus each item's unit value times its count.</summary>
    public static int KitValue(int gold, IEnumerable<(int UnitValue, int Count)> items)
    {
        int total = Math.Max(0, gold);
        foreach ((int unitValue, int count) in items)
        {
            total += Math.Max(0, unitValue) * Math.Max(0, count);
        }

        return total;
    }

    public static bool KitWithinCap(int kitValue) => kitValue <= MaxKitValue;

    public static bool StatDeltaWithinCap(float amount) => Math.Abs(amount) <= MaxStatDelta;

    public static bool ReputationWithinCap(int amount) => Math.Abs(amount) <= MaxReputationTweak;
}
