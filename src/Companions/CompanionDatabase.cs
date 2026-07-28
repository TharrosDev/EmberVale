using System.Collections.Generic;
using Embervale.Core.Diagnostics;
using Godot;

namespace Embervale.Companions;

/// <summary>
/// Process-wide registry of <see cref="CompanionResource"/>s, scanned once at startup from
/// <c>res://data/companions</c> (the established database pattern). <see cref="CompanionRegistry"/>
/// seeds its archetypes from <see cref="All"/>, so dropping a <c>.tres</c> in the folder is all a
/// new companion needs on the code side.
/// </summary>
public static class CompanionDatabase
{
    private const string DefaultDirectory = "res://data/companions";

    private static readonly Dictionary<string, CompanionResource> ById = new();
    private static readonly List<CompanionResource> AllList = new();

    public static IReadOnlyList<CompanionResource> All => AllList;

    public static void Initialize(string directory = DefaultDirectory)
    {
        ById.Clear();
        AllList.Clear();

        if (!DirAccess.DirExistsAbsolute(directory))
        {
            Log.Warn($"CompanionDatabase: directory '{directory}' not found; no companions loaded.");
            return;
        }

        foreach (string file in DirAccess.GetFilesAt(directory))
        {
            string name = file.EndsWith(".remap") ? file[..^6] : file;
            if (!name.EndsWith(".tres"))
            {
                continue;
            }

            var companion = GD.Load<CompanionResource>($"{directory}/{name}");
            if (companion == null)
            {
                continue;
            }

            if (ById.ContainsKey(companion.Id))
            {
                Log.Warn($"Duplicate companion id '{companion.Id}' in {name}; overwriting.");
            }
            else
            {
                AllList.Add(companion);
            }

            ById[companion.Id] = companion;
        }

        Log.Info($"CompanionDatabase loaded {ById.Count} companion(s) from {directory}.");
    }

    public static CompanionResource? Get(string id) =>
        id != null && ById.TryGetValue(id, out CompanionResource? companion) ? companion : null;
}
