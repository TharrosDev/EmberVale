using System.Collections.Generic;
using Embervale.Core.Diagnostics;
using Godot;

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
        ById.Clear();
        AllList.Clear();

        if (!DirAccess.DirExistsAbsolute(directory))
        {
            Log.Warn($"RegionDatabase: directory '{directory}' not found; none loaded.");
            return;
        }

        foreach (string file in DirAccess.GetFilesAt(directory))
        {
            string name = file.EndsWith(".remap") ? file[..^6] : file;
            if (!name.EndsWith(".tres"))
            {
                continue;
            }

            var region = GD.Load<RegionResource>($"{directory}/{name}");
            if (region == null)
            {
                continue;
            }

            if (ById.ContainsKey(region.Id))
            {
                Log.Warn($"Duplicate region id '{region.Id}' in {name}; overwriting.");
            }
            else
            {
                AllList.Add(region);
            }

            ById[region.Id] = region;
        }

        Log.Info($"RegionDatabase loaded {ById.Count} region(s) from {directory}.");
    }

    public static RegionResource? Get(string id)
    {
        return ById.TryGetValue(id, out RegionResource? region) ? region : null;
    }
}
