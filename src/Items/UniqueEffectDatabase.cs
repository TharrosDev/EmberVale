using System.Collections.Generic;
using Embervale.Core;
using Godot;

namespace Embervale.Items;

/// <summary>
/// Process-wide registry of <see cref="UniqueEffectResource"/>s, scanned at startup from
/// <c>res://data/unique_effects</c> (mirrors <see cref="AffixDatabase"/>). The folder is allowed to
/// be absent, like <see cref="ItemSetDatabase"/>'s.
/// </summary>
public static class UniqueEffectDatabase
{
    private const string DefaultDirectory = "res://data/unique_effects";

    private static readonly Dictionary<string, UniqueEffectResource> ById = new();
    private static readonly List<UniqueEffectResource> AllList = new();

    public static IReadOnlyList<UniqueEffectResource> All => AllList;

    public static void Initialize(string directory = DefaultDirectory)
    {
        if (!DirAccess.DirExistsAbsolute(directory))
        {
            ById.Clear();
            AllList.Clear();
            return;
        }

        ResourceDirectory.Load<UniqueEffectResource>(
            directory, "unique effect", effect => effect.Id, ById, AllList);
    }

    public static UniqueEffectResource? Get(string id)
    {
        return !string.IsNullOrEmpty(id) && ById.TryGetValue(id, out UniqueEffectResource? effect) ? effect : null;
    }
}
