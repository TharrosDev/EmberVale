using System;

namespace Embervale.Save;

/// <summary>
/// The save key of a scripted set piece (<c>SetPieceSpawnComponent</c>). Until format 4 the key
/// embedded the owning cell's scene path, <c>setpiece:res://scenes/regions/ember_crown/citadel.tscn#RampSentries</c>
/// (or, in a baked world, the baked cell's <c>res://data/world_bake/cells/...scn</c> path), which
/// breaks the "references are ids, never paths" rule twice over: moving the scenes folder or
/// changing the bake layout orphaned every fired and cleared piece, and a save made in a baked
/// world was not claimed in an unbaked one. The stable form keeps what identifies the piece (the
/// region/cell slug and the node inside it) and drops where the file happens to live:
/// <c>setpiece:ember_crown/citadel#RampSentries</c>. Godot-free.
/// </summary>
public static class SetPieceSaveIds
{
    public const string Prefix = "setpiece:";

    private const string ResourceRoot = "res://";
    private const string RegionsFolder = "scenes/regions/";
    private const string BakedCellsFolder = "data/world_bake/cells/";
    private const string BakedRegionPrefix = "region_";

    /// <summary>The stable key for a piece: its cell slug and its node path inside the cell.</summary>
    public static string Build(string? cellScenePath, string nodePath) =>
        $"{Prefix}{CellSlug(cellScenePath)}#{nodePath}";

    /// <summary>
    /// The region/cell slug of a cell scene, the same for both places a cell is loaded from:
    /// the authored <c>res://scenes/regions/ember_crown/citadel.tscn</c> and the baked
    /// <c>res://data/world_bake/cells/region_ember_crown/ember_crown_citadel.scn</c> both become
    /// <c>ember_crown/citadel</c>, so a save made against one is claimed under the other. A scene
    /// anywhere else keeps its project-relative path without the extension; an empty path (a
    /// piece with no owning scene) stays empty.
    /// </summary>
    public static string CellSlug(string? cellScenePath)
    {
        string slug = cellScenePath ?? string.Empty;
        if (slug.StartsWith(ResourceRoot, StringComparison.Ordinal))
        {
            slug = slug.Substring(ResourceRoot.Length);
        }

        int slash = slug.LastIndexOf('/');
        int dot = slug.LastIndexOf('.');
        if (dot > slash)
        {
            slug = slug.Substring(0, dot);
        }

        if (slug.StartsWith(RegionsFolder, StringComparison.Ordinal))
        {
            return slug.Substring(RegionsFolder.Length);
        }

        if (!slug.StartsWith(BakedCellsFolder, StringComparison.Ordinal))
        {
            return slug;
        }

        // Baked: "region_<region>/<region>_<cell>" (WorldBakePaths.Cell). Both prefixes repeat
        // what the folder already says and are dropped when they are there.
        slug = slug.Substring(BakedCellsFolder.Length);
        slash = slug.IndexOf('/');
        if (slash < 0)
        {
            return slug;
        }

        string region = slug.Substring(0, slash);
        string cell = slug.Substring(slash + 1);
        if (region.StartsWith(BakedRegionPrefix, StringComparison.Ordinal))
        {
            region = region.Substring(BakedRegionPrefix.Length);
        }

        if (cell.StartsWith(region + "_", StringComparison.Ordinal))
        {
            cell = cell.Substring(region.Length + 1);
        }

        return $"{region}/{cell}";
    }

    /// <summary>True for a pre-v4 key, the kind that carries a <c>res://</c> scene path.</summary>
    public static bool IsLegacy(string? saveId) =>
        saveId != null && saveId.StartsWith(Prefix, StringComparison.Ordinal) &&
        saveId.AsSpan(Prefix.Length).StartsWith(ResourceRoot, StringComparison.Ordinal);

    /// <summary>Rewrites a pre-v4 key to the stable form; every other string is returned as it is,
    /// so the call is safe on any save key and on a key that was already rewritten.</summary>
    public static string Normalize(string saveId)
    {
        if (!IsLegacy(saveId))
        {
            return saveId;
        }

        string rest = saveId.Substring(Prefix.Length);
        int hash = rest.IndexOf('#');
        return hash < 0 ? Build(rest, string.Empty) : Build(rest.Substring(0, hash), rest.Substring(hash + 1));
    }
}
