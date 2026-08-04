using System.Collections.Generic;
using System.Globalization;

namespace Embervale.Housing;

/// <summary>
/// Names a placed prop's <c>PersistentId</c> (Phase 37C). The form is
/// <c>place.&lt;propertyId&gt;#&lt;n&gt;</c>, which encodes <b>which holding a prop belongs to inside
/// the id itself</b> — so <see cref="Save.PersistentSpawnDirector"/> keeps persisting exactly what it
/// already persists (id, template, position, yaw) and 37D can still ask a holding what stands in it,
/// with no second save record to drift out of step.
///
/// <b>The index must be derived from the ids that already exist, never from a counter.</b>
/// <c>PersistentSpawnDirector._autoId</c> is not persisted: it resets to zero on load, so a counter
/// would hand out <c>#1</c> again after a reload, and <c>Spawn</c> treats a known id as "already
/// spawned" and returns the existing actor instead of placing the new one. The prop would simply not
/// appear, and only in a session that had loaded a save. Scanning is O(n) over a handful of props.
/// </summary>
public static class PlacementIds
{
    /// <summary>Prefix marking a persistent id as a player placement, the one thing that
    /// distinguishes a placed prop from an authored actor at removal time.</summary>
    public const string Prefix = "place.";

    /// <summary>True when <paramref name="persistentId"/> names a prop the player placed.</summary>
    public static bool IsPlacement(string? persistentId) =>
        !string.IsNullOrEmpty(persistentId) && persistentId.StartsWith(Prefix, System.StringComparison.Ordinal);

    /// <summary>The <c>place.&lt;propertyId&gt;#</c> stem every prop in one holding shares.</summary>
    public static string StemFor(string propertyId) => $"{Prefix}{propertyId}#";

    /// <summary>
    /// The next free id for a prop in <paramref name="propertyId"/>, one past the highest index
    /// already present in <paramref name="existingIds"/>. Unparsable or malformed suffixes are
    /// ignored rather than trusted — a stray id must not be able to stop placement working.
    /// </summary>
    public static string Next(IEnumerable<string> existingIds, string propertyId)
    {
        string stem = StemFor(propertyId);
        int highest = 0;

        foreach (string id in existingIds)
        {
            if (id == null || !id.StartsWith(stem, System.StringComparison.Ordinal))
            {
                continue;
            }

            if (int.TryParse(
                    id[stem.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out int index) &&
                index > highest)
            {
                highest = index;
            }
        }

        return $"{stem}{highest + 1}";
    }

    /// <summary>The holding a placed prop belongs to, or empty if the id is not a placement.</summary>
    public static string PropertyOf(string? persistentId)
    {
        if (!IsPlacement(persistentId))
        {
            return string.Empty;
        }

        int hash = persistentId!.LastIndexOf('#');
        return hash <= Prefix.Length ? string.Empty : persistentId[Prefix.Length..hash];
    }
}
