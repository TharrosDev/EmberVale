using System.Collections.Generic;
using Embervale.Core.Diagnostics;
using Godot;

namespace Embervale.Enemies;

/// <summary>
/// Process-wide registry of <see cref="EnemyArchetypeResource"/>s, scanned once at startup from
/// <c>res://data/enemies</c> (the established database pattern). <see cref="EnemyTemplateRegistry"/>
/// registers a builder for each one, so dropping a <c>.tres</c> in the folder is all a new humanoid
/// enemy needs — encounters, world events and quest kill-targets can reference it by id immediately.
/// </summary>
public static class EnemyArchetypeDatabase
{
    private const string DefaultDirectory = "res://data/enemies";

    private static readonly Dictionary<string, EnemyArchetypeResource> ById = new();
    private static readonly List<EnemyArchetypeResource> AllList = new();

    public static IReadOnlyList<EnemyArchetypeResource> All => AllList;

    public static void Initialize(string directory = DefaultDirectory)
    {
        ById.Clear();
        AllList.Clear();

        if (!DirAccess.DirExistsAbsolute(directory))
        {
            Log.Warn($"EnemyArchetypeDatabase: directory '{directory}' not found; no archetypes loaded.");
            return;
        }

        foreach (string file in DirAccess.GetFilesAt(directory))
        {
            string name = file.EndsWith(".remap") ? file[..^6] : file;
            if (!name.EndsWith(".tres"))
            {
                continue;
            }

            var archetype = GD.Load<EnemyArchetypeResource>($"{directory}/{name}");
            if (archetype == null)
            {
                continue;
            }

            if (ById.ContainsKey(archetype.Id))
            {
                Log.Warn($"Duplicate enemy archetype id '{archetype.Id}' in {name}; overwriting.");
            }
            else
            {
                AllList.Add(archetype);
            }

            ById[archetype.Id] = archetype;
        }

        Log.Info($"EnemyArchetypeDatabase loaded {ById.Count} archetype(s) from {directory}.");
    }

    public static EnemyArchetypeResource? Get(string id) =>
        id != null && ById.TryGetValue(id, out EnemyArchetypeResource? archetype) ? archetype : null;
}
