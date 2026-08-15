using System.Collections.Generic;
using Embervale.Core;

namespace Embervale.Enemies;

/// <summary>
/// Process-wide registry of <see cref="BossResource"/>s, scanned once at startup from
/// <c>res://data/bosses</c> (the established database pattern). An archetype names one through
/// <see cref="EnemyArchetypeResource.BossId"/>, so dropping a <c>.tres</c> in the folder and naming
/// it is all a boss's phase structure needs — no code, and the content validator cross-checks the
/// reference in both directions.
///
/// Initialized <b>before</b> <see cref="EnemyArchetypeDatabase"/> so that cross-check can run.
/// </summary>
public static class BossDatabase
{
    private const string DefaultDirectory = "res://data/bosses";

    private static readonly Dictionary<string, BossResource> ById = new();
    private static readonly List<BossResource> AllList = new();

    public static IReadOnlyList<BossResource> All => AllList;

    public static void Initialize(string directory = DefaultDirectory)
    {
        ResourceDirectory.Load<BossResource>(
            directory, "boss", boss => boss.Id, ById, AllList);
    }

    public static BossResource? Get(string id) =>
        id != null && ById.TryGetValue(id, out BossResource? boss) ? boss : null;
}
