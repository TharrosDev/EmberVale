using System.Collections.Generic;
using Embervale.Core.Diagnostics;
using Godot;

namespace Embervale.Enemies;

/// <summary>
/// Process-wide registry of <see cref="BestiaryEntryResource"/>s, scanned once at startup from
/// <c>res://data/bestiary</c> (the established database pattern). The bestiary screen reads its
/// pages from here, so adding a creature's lore is a <c>.tres</c> rather than a rebuild.
/// </summary>
public static class BestiaryDatabase
{
    private const string DefaultDirectory = "res://data/bestiary";

    private static readonly Dictionary<string, BestiaryEntryResource> ById = new();
    private static readonly List<BestiaryEntryResource> AllList = new();

    public static IReadOnlyList<BestiaryEntryResource> All => AllList;

    public static void Initialize(string directory = DefaultDirectory)
    {
        ById.Clear();
        AllList.Clear();

        if (!DirAccess.DirExistsAbsolute(directory))
        {
            Log.Warn($"BestiaryDatabase: directory '{directory}' not found; no entries loaded.");
            return;
        }

        foreach (string file in DirAccess.GetFilesAt(directory))
        {
            string name = file.EndsWith(".remap") ? file[..^6] : file;
            if (!name.EndsWith(".tres"))
            {
                continue;
            }

            var entry = GD.Load<BestiaryEntryResource>($"{directory}/{name}");
            if (entry == null)
            {
                continue;
            }

            if (ById.ContainsKey(entry.Id))
            {
                Log.Warn($"Duplicate bestiary entry id '{entry.Id}' in {name}; overwriting.");
            }
            else
            {
                AllList.Add(entry);
            }

            ById[entry.Id] = entry;
        }

        Log.Info($"BestiaryDatabase loaded {ById.Count} bestiary entry(s) from {directory}.");
    }

    public static BestiaryEntryResource? Get(string id) =>
        id != null && ById.TryGetValue(id, out BestiaryEntryResource? entry) ? entry : null;

    public static bool IsRegistered(string id) => !string.IsNullOrEmpty(id) && ById.ContainsKey(id);
}
