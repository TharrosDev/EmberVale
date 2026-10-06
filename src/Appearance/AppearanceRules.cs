using System;
using System.Collections.Generic;

namespace Embervale.Appearance;

/// <summary>
/// The pure rules of character appearance, Godot-free so the player factory, the creator, the validator and
/// the unit tests share one definition. A profile stores one option id per <see cref="AppearanceSlot"/>;
/// <see cref="Resolve"/> turns whatever a profile holds (nothing, an old save, a stale or disallowed id)
/// into exactly one valid pick per slot, falling back to the slot's default, which reproduces the unmodified model.
/// </summary>
public static class AppearanceRules
{
    /// <summary>Id prefix every appearance option carries.</summary>
    public const string IdPrefix = "appearance.";

    public const int SlotCount = 5;

    /// <summary>A build may narrow or widen the body by at most this fraction (the shipped builds are 0.92 / 1.0 / 1.1).</summary>
    public const float MaxBuildDeviation = 0.15f;

    // Average sRGB colour of each mask region of chr_player_base_texture_0.png, printed by tools/gen_player_mask.py.
    // A region's tint is moved by (option tint - reference), so an option equal to the reference is the unmodified look.
    public static readonly (float R, float G, float B) SkinReference = (0.676f, 0.506f, 0.439f);
    public static readonly (float R, float G, float B) HairReference = (0.191f, 0.153f, 0.125f);
    public static readonly (float R, float G, float B) EyeReference = (0.287f, 0.213f, 0.179f);

    /// <summary>The default Ember glow colour: the Embers tier's own, so the unmodified arc is unchanged.</summary>
    public static readonly (float R, float G, float B) EmberReference = (0.82f, 0.34f, 0.10f);

    /// <summary>How close a default option's tint must be to its reference (colours are authored to 3 decimals).</summary>
    public const float ReferenceTolerance = 0.002f;

    public static bool MatchesReference((float R, float G, float B) tint, (float R, float G, float B) reference) =>
        Math.Abs(tint.R - reference.R) <= ReferenceTolerance &&
        Math.Abs(tint.G - reference.G) <= ReferenceTolerance &&
        Math.Abs(tint.B - reference.B) <= ReferenceTolerance;

    public static bool IsAppearanceId(string? id) =>
        id != null && id.StartsWith(IdPrefix, StringComparison.Ordinal) && id.Length > IdPrefix.Length;

    public static bool BuildWithinCap(float scale) => Math.Abs(scale - 1f) <= MaxBuildDeviation + 1e-4f;

    /// <summary>
    /// One option id per slot (indexed by <see cref="AppearanceSlot"/>; empty only when a slot has no authored
    /// default). For each slot the last id in <paramref name="saved"/> that belongs to it and is
    /// <paramref name="allowed"/> wins, otherwise the slot's default. Replaces, never merges.
    /// </summary>
    /// <param name="slotOf">The slot of an option id, or null when no such option exists.</param>
    /// <param name="allowed">Whether the profile's race offers the id (defaults are always valid and are not asked).</param>
    /// <param name="defaultOf">The default option id of a slot, or null.</param>
    public static string[] Resolve(
        IEnumerable<string>? saved,
        Func<string, AppearanceSlot?> slotOf,
        Func<string, bool> allowed,
        Func<AppearanceSlot, string?> defaultOf)
    {
        var picks = new string[SlotCount];
        for (int i = 0; i < SlotCount; i++)
        {
            picks[i] = defaultOf((AppearanceSlot)i) ?? string.Empty;
        }

        if (saved == null)
        {
            return picks;
        }

        foreach (string id in saved)
        {
            if (!string.IsNullOrEmpty(id) && slotOf(id) is { } slot && (int)slot >= 0 && (int)slot < SlotCount && allowed(id))
            {
                picks[(int)slot] = id;
            }
        }

        return picks;
    }

    /// <summary>The ids to store in <c>CharacterProfile.AppearanceOptionIds</c>: the non-empty picks, slot order.</summary>
    public static string[] ToProfileIds(IReadOnlyList<string> picks)
    {
        var ids = new List<string>();
        foreach (string pick in picks)
        {
            if (pick.Length > 0)
            {
                ids.Add(pick);
            }
        }

        return ids.ToArray();
    }
}
