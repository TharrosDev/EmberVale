using System.Collections.Generic;
using Embervale.Core;

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
        ResourceDirectory.Load<WeatherResource>(
            directory, "weather", weather => weather.Id, ById, AllList);
    }

    public static WeatherResource? Get(string id)
    {
        return ById.TryGetValue(id, out WeatherResource? weather) ? weather : null;
    }
}
