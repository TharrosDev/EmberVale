using System.Collections.Generic;

namespace Embervale.Magic;

/// <summary>
/// Ids of spells the 2026-09 magic upgrade retired, and what replaced each. A save, a dialogue effect or
/// an enemy loadout that still names the old id resolves to the new spell through
/// <see cref="SpellDatabase.Get"/>, so nothing that predates the upgrade breaks. Append-only: an alias
/// is never removed, because a save can be years old.
/// </summary>
public static class SpellAliases
{
    private static readonly Dictionary<string, string> Replacements = new()
    {
        ["spell.firebolt"] = "spell.emberlash",
        ["spell.fireball"] = "spell.sunfall",
        ["spell.arcane_lance"] = "spell.null_lance",
        ["spell.lesser_heal"] = "spell.mending_bloom",
    };

    /// <summary>The current id for <paramref name="id"/>: itself unless it was retired.</summary>
    public static string Resolve(string id) =>
        Replacements.TryGetValue(id, out string? current) ? current : id;

    public static bool IsRetired(string id) => Replacements.ContainsKey(id);

    public static IReadOnlyDictionary<string, string> All => Replacements;
}
