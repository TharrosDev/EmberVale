using System.Collections.Generic;
using Embervale.Core;

namespace Embervale.Enemies;

/// <summary>
/// Process-wide registry of <see cref="AIProfileResource"/>s, scanned once at startup from
/// <c>res://data/ai_profiles</c> (the established database pattern). <see cref="EnemyAIComponent"/>
/// resolves its <c>ProfileId</c> through here, so retuning how an archetype fights is an edit to a
/// <c>.tres</c> rather than a rebuild.
/// </summary>
public static class AIProfileDatabase
{
    private const string DefaultDirectory = "res://data/ai_profiles";

    private static readonly Dictionary<string, AIProfileResource> ById = new();
    private static readonly List<AIProfileResource> AllList = new();

    public static IReadOnlyList<AIProfileResource> All => AllList;

    public static void Initialize(string directory = DefaultDirectory)
    {
        ResourceDirectory.Load<AIProfileResource>(
            directory, "AI profile", profile => profile.Id, ById, AllList);
    }

    public static AIProfileResource? Get(string id) =>
        id != null && ById.TryGetValue(id, out AIProfileResource? profile) ? profile : null;

    public static bool IsRegistered(string id) => !string.IsNullOrEmpty(id) && ById.ContainsKey(id);
}
