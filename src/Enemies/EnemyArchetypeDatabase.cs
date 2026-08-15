using System.Collections.Generic;
using Embervale.Core;

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
        ResourceDirectory.Load<EnemyArchetypeResource>(
            directory, "enemy archetype", archetype => archetype.Id, ById, AllList);
    }

    public static EnemyArchetypeResource? Get(string id) =>
        id != null && ById.TryGetValue(id, out EnemyArchetypeResource? archetype) ? archetype : null;
}
