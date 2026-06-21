using System.Collections.Generic;
using Embervale.Core.Diagnostics;
using Godot;

namespace Embervale.Factions;

/// <summary>
/// Process-wide registry of <see cref="FactionResource"/>s, scanned once at startup
/// from <c>res://data/factions</c> (mirrors the established database pattern). The
/// <see cref="ReputationComponent"/> seeds its standings from <see cref="All"/> and
/// resolves a faction by id; <see cref="FactionComponent"/> tags actors. New faction =
/// drop a <c>.tres</c>, no code change.
/// </summary>
public static class FactionDatabase
{
    private const string DefaultDirectory = "res://data/factions";

    private static readonly Dictionary<string, FactionResource> ById = new();
    private static readonly List<FactionResource> AllList = new();

    public static IReadOnlyList<FactionResource> All => AllList;

    public static void Initialize(string directory = DefaultDirectory)
    {
        ById.Clear();
        AllList.Clear();

        if (!DirAccess.DirExistsAbsolute(directory))
        {
            Log.Warn($"FactionDatabase: directory '{directory}' not found; no factions loaded.");
            return;
        }

        foreach (string file in DirAccess.GetFilesAt(directory))
        {
            string name = file.EndsWith(".remap") ? file[..^6] : file;
            if (!name.EndsWith(".tres"))
            {
                continue;
            }

            var faction = GD.Load<FactionResource>($"{directory}/{name}");
            if (faction == null)
            {
                continue;
            }

            if (ById.ContainsKey(faction.Id))
            {
                Log.Warn($"Duplicate faction id '{faction.Id}' in {name}; overwriting.");
            }
            else
            {
                AllList.Add(faction);
            }

            ById[faction.Id] = faction;
        }

        Log.Info($"FactionDatabase loaded {ById.Count} faction(s) from {directory}.");
    }

    public static FactionResource? Get(string id)
    {
        return ById.TryGetValue(id, out FactionResource? faction) ? faction : null;
    }
}
