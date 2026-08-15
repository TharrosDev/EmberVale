using System.Collections.Generic;
using Embervale.Core;

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
        ResourceDirectory.Load<ServiceResource>(
            directory, "service", service => service.Id, ById, AllList);
    }

    public static ServiceResource? Get(string id) =>
        id != null && ById.TryGetValue(id, out ServiceResource? service) ? service : null;
}
