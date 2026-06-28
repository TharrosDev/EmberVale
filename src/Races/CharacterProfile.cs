using System;
using System.Collections.Generic;

namespace Embervale.Races;

/// <summary>
/// The choices made at character creation (Phase 26C): the race that shapes the player's
/// starting stats/perks/standing, plus the cosmetic/identity fields the creator screen (26D)
/// will fill. Pure data — no Godot dependency — so it round-trips through the save header and
/// is unit-testable. <see cref="RaceComponent"/> applies the race at spawn; the bootstrap
/// persists this via <see cref="SaveManager.HeaderProvider"/>.
/// </summary>
public sealed class CharacterProfile
{
    public string RaceId { get; set; } = "race.human";
    public string CharacterName { get; set; } = "Wanderer";
    public string[] AppearanceOptionIds { get; set; } = Array.Empty<string>();
    public string Background { get; set; } = string.Empty;

    /// <summary>The default new-character profile until the creator (26D) supplies a chosen one.</summary>
    public static CharacterProfile Human => new();

    // Appearance ids are joined with ';' for the flat string→string header; ids never contain it.
    private const char AppearanceSeparator = ';';

    /// <summary>Flatten to the string fields stored in the save header.</summary>
    public Dictionary<string, string> ToHeaderFields() => new()
    {
        ["race_id"] = RaceId,
        ["char_name"] = CharacterName,
        ["appearance"] = string.Join(AppearanceSeparator, AppearanceOptionIds),
        ["background"] = Background,
    };

    /// <summary>Rebuild from header fields; any missing key falls back to the Human default.</summary>
    public static CharacterProfile FromHeaderFields(IReadOnlyDictionary<string, string> fields)
    {
        var profile = new CharacterProfile();
        if (fields.TryGetValue("race_id", out string? race) && !string.IsNullOrEmpty(race)) { profile.RaceId = race; }
        if (fields.TryGetValue("char_name", out string? name) && !string.IsNullOrEmpty(name)) { profile.CharacterName = name; }
        if (fields.TryGetValue("appearance", out string? appearance) && !string.IsNullOrEmpty(appearance))
        {
            profile.AppearanceOptionIds = appearance.Split(AppearanceSeparator, StringSplitOptions.RemoveEmptyEntries);
        }
        if (fields.TryGetValue("background", out string? background)) { profile.Background = background; }
        return profile;
    }
}
