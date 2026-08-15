using System.Collections.Generic;
using Embervale.Core;

namespace Embervale.World;

/// <summary>
/// Process-wide registry of <see cref="RegionResource"/>s, scanned once at startup from
/// <c>res://data/regions</c> (mirrors the established database pattern, e.g.
/// <see cref="WeatherDatabase"/>). The save header resolves the active region's display name by id;
/// the streamer (25B) and map (25E) read the indexed regions. New region = drop a <c>.tres</c>,
/// no code change.
/// </summary>
public static class RegionDatabase
{
    private const string DefaultDirectory = "res://data/regions";

    private static readonly Dictionary<string, RegionResource> ById = new();
    private static readonly List<RegionResource> AllList = new();

    public static IReadOnlyList<RegionResource> All => AllList;

    public static void Initialize(string directory = DefaultDirectory)
    {
        ResourceDirectory.Load<RegionResource>(
            directory, "region", region => region.Id, ById, AllList);
    }

    public static RegionResource? Get(string id)
    {
        return ById.TryGetValue(id, out RegionResource? region) ? region : null;
    }

    /// <summary>
    /// One cell by its <c>&lt;region&gt;.&lt;cell&gt;</c> id, across every region (Phase 38G) — cells are
    /// authored as sub-resources inside their region, so before this the only way to find one was to
    /// walk `All` and its `Cells`, which four call sites did by hand.
    ///
    /// A linear scan on purpose: 14 cells in the realm, and a second dictionary is a second thing to
    /// keep in step with <see cref="Initialize"/>.
    /// </summary>
    public static RegionCellResource? Cell(string id)
    {
        if (string.IsNullOrEmpty(id))
        {
            return null;
        }

        foreach (RegionResource region in AllList)
        {
            foreach (RegionCellResource cell in region.Cells)
            {
                if (cell.Id == id)
                {
                    return cell;
                }
            }
        }

        return null;
    }
}
