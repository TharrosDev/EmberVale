using System.Collections.Generic;
using Embervale.Core.Diagnostics;
using Godot;

namespace Embervale.Housing;

/// <summary>
/// Process-wide registry of <see cref="PropertyResource"/>s, scanned once at startup from
/// <c>res://data/properties</c> (the established database pattern — a direct mirror of
/// <see cref="Enemies.BossDatabase"/>). A <see cref="PropertyDeedComponent"/> names one by id, so a
/// new holding is a <c>.tres</c> and a deed placed in a cell, with no code.
/// </summary>
public static class PropertyDatabase
{
    private const string DefaultDirectory = "res://data/properties";

    private static readonly Dictionary<string, PropertyResource> ById = new();
    private static readonly List<PropertyResource> AllList = new();

    public static IReadOnlyList<PropertyResource> All => AllList;

    public static void Initialize(string directory = DefaultDirectory)
    {
        ById.Clear();
        AllList.Clear();

        if (!DirAccess.DirExistsAbsolute(directory))
        {
            Log.Warn($"PropertyDatabase: directory '{directory}' not found; no properties loaded.");
            return;
        }

        foreach (string file in DirAccess.GetFilesAt(directory))
        {
            string name = file.EndsWith(".remap") ? file[..^6] : file;
            if (!name.EndsWith(".tres"))
            {
                continue;
            }

            var property = GD.Load<PropertyResource>($"{directory}/{name}");
            if (property == null)
            {
                continue;
            }

            if (ById.ContainsKey(property.Id))
            {
                Log.Warn($"Duplicate property id '{property.Id}' in {name}; overwriting.");
            }
            else
            {
                AllList.Add(property);
            }

            ById[property.Id] = property;
        }

        Log.Info($"PropertyDatabase loaded {ById.Count} property(s) from {directory}.");
    }

    public static PropertyResource? Get(string id) =>
        id != null && ById.TryGetValue(id, out PropertyResource? property) ? property : null;
}
