using System.Collections.Generic;
using Embervale.Core.Diagnostics;
using Godot;

namespace Embervale.World;

/// <summary>
/// Process-wide registry of <see cref="WeatherResource"/>s, scanned once at startup
/// from <c>res://data/weather</c> (mirrors the established database pattern). The
/// <see cref="WeatherDirector"/> picks from <see cref="All"/> and resolves a saved
/// state back by id. New weather state = drop a <c>.tres</c>, no code change.
/// </summary>
public static class WeatherDatabase
{
    private const string DefaultDirectory = "res://data/weather";

    private static readonly Dictionary<string, WeatherResource> ById = new();
    private static readonly List<WeatherResource> AllList = new();

    public static IReadOnlyList<WeatherResource> All => AllList;

    public static void Initialize(string directory = DefaultDirectory)
    {
        ById.Clear();
        AllList.Clear();

        if (!DirAccess.DirExistsAbsolute(directory))
        {
            Log.Warn($"WeatherDatabase: directory '{directory}' not found; none loaded.");
            return;
        }

        foreach (string file in DirAccess.GetFilesAt(directory))
        {
            string name = file.EndsWith(".remap") ? file[..^6] : file;
            if (!name.EndsWith(".tres"))
            {
                continue;
            }

            var weather = GD.Load<WeatherResource>($"{directory}/{name}");
            if (weather == null)
            {
                continue;
            }

            if (ById.ContainsKey(weather.Id))
            {
                Log.Warn($"Duplicate weather id '{weather.Id}' in {name}; overwriting.");
            }
            else
            {
                AllList.Add(weather);
            }

            ById[weather.Id] = weather;
        }

        Log.Info($"WeatherDatabase loaded {ById.Count} weather state(s) from {directory}.");
    }

    public static WeatherResource? Get(string id)
    {
        return ById.TryGetValue(id, out WeatherResource? weather) ? weather : null;
    }
}
