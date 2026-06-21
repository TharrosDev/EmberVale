using System.Collections.Generic;
using Embervale.Core.Diagnostics;
using Godot;

namespace Embervale.World;

/// <summary>
/// Process-wide registry of <see cref="EncounterResource"/>s, scanned once at startup
/// from <c>res://data/encounters</c> (mirrors the established database pattern). The
/// <see cref="EncounterDirector"/> filters <see cref="All"/> by the current day phase
/// and picks one by weight. New encounter = drop a <c>.tres</c>, no code change.
/// </summary>
public static class EncounterDatabase
{
    private const string DefaultDirectory = "res://data/encounters";

    private static readonly Dictionary<string, EncounterResource> ById = new();
    private static readonly List<EncounterResource> AllList = new();

    public static IReadOnlyList<EncounterResource> All => AllList;

    public static void Initialize(string directory = DefaultDirectory)
    {
        ById.Clear();
        AllList.Clear();

        if (!DirAccess.DirExistsAbsolute(directory))
        {
            Log.Warn($"EncounterDatabase: directory '{directory}' not found; none loaded.");
            return;
        }

        foreach (string file in DirAccess.GetFilesAt(directory))
        {
            string name = file.EndsWith(".remap") ? file[..^6] : file;
            if (!name.EndsWith(".tres"))
            {
                continue;
            }

            var encounter = GD.Load<EncounterResource>($"{directory}/{name}");
            if (encounter == null)
            {
                continue;
            }

            if (ById.ContainsKey(encounter.Id))
            {
                Log.Warn($"Duplicate encounter id '{encounter.Id}' in {name}; overwriting.");
            }
            else
            {
                AllList.Add(encounter);
            }

            ById[encounter.Id] = encounter;
        }

        Log.Info($"EncounterDatabase loaded {ById.Count} encounter(s) from {directory}.");
    }

    public static EncounterResource? Get(string id)
    {
        return ById.TryGetValue(id, out EncounterResource? encounter) ? encounter : null;
    }
}
