using System.Collections.Generic;
using Embervale.Core.Diagnostics;
using Godot;

namespace Embervale.Economy;

/// <summary>
/// Process-wide registry of <see cref="ServiceResource"/>s, scanned once at startup from
/// <c>res://data/services</c> — a direct mirror of <see cref="ShopDatabase"/>. A
/// <see cref="ServiceComponent"/> names one by id, so a new service is a <c>.tres</c> and nothing else.
/// </summary>
public static class ServiceDatabase
{
    private const string DefaultDirectory = "res://data/services";

    private static readonly Dictionary<string, ServiceResource> ById = new();
    private static readonly List<ServiceResource> AllList = new();

    public static IReadOnlyList<ServiceResource> All => AllList;

    public static void Initialize(string directory = DefaultDirectory)
    {
        ById.Clear();
        AllList.Clear();

        if (!DirAccess.DirExistsAbsolute(directory))
        {
            Log.Warn($"ServiceDatabase: directory '{directory}' not found; no services loaded.");
            return;
        }

        foreach (string file in DirAccess.GetFilesAt(directory))
        {
            string name = file.EndsWith(".remap") ? file[..^6] : file;
            if (!name.EndsWith(".tres"))
            {
                continue;
            }

            var service = GD.Load<ServiceResource>($"{directory}/{name}");
            if (service == null)
            {
                continue;
            }

            if (ById.ContainsKey(service.Id))
            {
                Log.Warn($"Duplicate service id '{service.Id}' in {name}; overwriting.");
            }
            else
            {
                AllList.Add(service);
            }

            ById[service.Id] = service;
        }

        Log.Info($"ServiceDatabase loaded {ById.Count} service(s) from {directory}.");
    }

    public static ServiceResource? Get(string id) =>
        id != null && ById.TryGetValue(id, out ServiceResource? service) ? service : null;
}
