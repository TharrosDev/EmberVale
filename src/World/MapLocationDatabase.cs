using System.Collections.Generic;
using Embervale.Core.Diagnostics;
using Godot;

namespace Embervale.World;

/// <summary>
/// Process-wide registry of <see cref="MapLocationResource"/>s (Phase 39.5A), scanned once at
/// startup from <c>res://data/map_locations</c> — a direct mirror of
/// <see cref="Embervale.Economy.ServiceDatabase"/>, so a new map location is a <c>.tres</c> and
/// nothing else.
/// </summary>
public static class MapLocationDatabase
{
    private const string DefaultDirectory = "res://data/map_locations";

    private static readonly Dictionary<string, MapLocationResource> ById = new();
    private static readonly List<MapLocationResource> AllList = new();

    public static IReadOnlyList<MapLocationResource> All => AllList;

    public static void Initialize(string directory = DefaultDirectory)
    {
        ById.Clear();
        AllList.Clear();

        if (!DirAccess.DirExistsAbsolute(directory))
        {
            Log.Warn($"MapLocationDatabase: directory '{directory}' not found; no locations loaded.");
            return;
        }

        foreach (string file in DirAccess.GetFilesAt(directory))
        {
            string name = file.EndsWith(".remap") ? file[..^6] : file;
            if (!name.EndsWith(".tres"))
            {
                continue;
            }

            var location = GD.Load<MapLocationResource>($"{directory}/{name}");
            if (location == null)
            {
                continue;
            }

            if (ById.ContainsKey(location.Id))
            {
                Log.Warn($"Duplicate map location id '{location.Id}' in {name}; overwriting.");
            }
            else
            {
                AllList.Add(location);
            }

            ById[location.Id] = location;
        }

        Log.Info($"MapLocationDatabase loaded {ById.Count} map location(s) from {directory}.");
    }

    public static MapLocationResource? Get(string id) =>
        id != null && ById.TryGetValue(id, out MapLocationResource? location) ? location : null;
}
