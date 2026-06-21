using System.Collections.Generic;
using Embervale.Core.Diagnostics;
using Godot;

namespace Embervale.World;

/// <summary>
/// Process-wide registry of <see cref="WorldEventResource"/>s, scanned once at startup
/// from <c>res://data/world_events</c> (mirrors the established database pattern). The
/// <see cref="WorldEventDirector"/> rolls from <see cref="All"/> filtered by day phase
/// and cooldown. New event = drop a <c>.tres</c>, no code change.
/// </summary>
public static class WorldEventDatabase
{
    private const string DefaultDirectory = "res://data/world_events";

    private static readonly Dictionary<string, WorldEventResource> ById = new();
    private static readonly List<WorldEventResource> AllList = new();

    public static IReadOnlyList<WorldEventResource> All => AllList;

    public static void Initialize(string directory = DefaultDirectory)
    {
        ById.Clear();
        AllList.Clear();

        if (!DirAccess.DirExistsAbsolute(directory))
        {
            Log.Warn($"WorldEventDatabase: directory '{directory}' not found; none loaded.");
            return;
        }

        foreach (string file in DirAccess.GetFilesAt(directory))
        {
            string name = file.EndsWith(".remap") ? file[..^6] : file;
            if (!name.EndsWith(".tres"))
            {
                continue;
            }

            var worldEvent = GD.Load<WorldEventResource>($"{directory}/{name}");
            if (worldEvent == null)
            {
                continue;
            }

            if (ById.ContainsKey(worldEvent.Id))
            {
                Log.Warn($"Duplicate world event id '{worldEvent.Id}' in {name}; overwriting.");
            }
            else
            {
                AllList.Add(worldEvent);
            }

            ById[worldEvent.Id] = worldEvent;
        }

        Log.Info($"WorldEventDatabase loaded {ById.Count} world event(s) from {directory}.");
    }

    public static WorldEventResource? Get(string id)
    {
        return ById.TryGetValue(id, out WorldEventResource? worldEvent) ? worldEvent : null;
    }
}
