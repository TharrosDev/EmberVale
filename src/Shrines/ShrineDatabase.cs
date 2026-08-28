using System.Collections.Generic;
using Embervale.Core;

namespace Embervale.Shrines;

/// <summary>Registry for the authored shrine blessings under <c>data/shrines</c>.</summary>
public static class ShrineDatabase
{
    private const string DefaultDirectory = "res://data/shrines";

    private static readonly Dictionary<string, ShrineResource> ById = new();
    private static readonly List<ShrineResource> AllList = new();

    public static IReadOnlyList<ShrineResource> All => AllList;

    public static void Initialize(string directory = DefaultDirectory)
    {
        ResourceDirectory.Load(directory, "shrine", shrine => shrine.Id, ById, AllList);
    }

    public static ShrineResource? Get(string id) =>
        ById.TryGetValue(id, out ShrineResource? shrine) ? shrine : null;
}
