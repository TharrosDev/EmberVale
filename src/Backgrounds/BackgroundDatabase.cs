using System.Collections.Generic;
using Embervale.Core;

namespace Embervale.Backgrounds;

/// <summary>
/// Process-wide registry of <see cref="BackgroundResource"/>s, scanned once at startup from
/// <c>res://data/backgrounds</c> (mirrors <see cref="Embervale.Races.RaceDatabase"/>). The creator lists
/// <see cref="All"/>; <see cref="BackgroundApplier"/> and <c>RaceComponent</c> resolve a profile's id with
/// <see cref="Resolve"/>. New background = drop a <c>.tres</c> and its Loc rows, no code change.
/// </summary>
public static class BackgroundDatabase
{
    private const string DefaultDirectory = "res://data/backgrounds";

    /// <summary>The no-op default the creator preselects.</summary>
    public const string DefaultId = "background.wayfarer";

    private static readonly Dictionary<string, BackgroundResource> ById = new();
    private static readonly List<BackgroundResource> AllList = new();

    public static IReadOnlyList<BackgroundResource> All => AllList;

    public static void Initialize(string directory = DefaultDirectory)
    {
        ResourceDirectory.Load<BackgroundResource>(
            directory, "background", background => background.Id, ById, AllList);
    }

    public static BackgroundResource? Get(string id)
    {
        return ById.TryGetValue(id, out BackgroundResource? background) ? background : null;
    }

    /// <summary>The background a profile's <c>Background</c> field names, or null for none: an empty
    /// field, an old free-text line, or an id that no longer exists all mean "no background".</summary>
    public static BackgroundResource? Resolve(string? profileBackground)
    {
        string id = BackgroundRules.ResolveId(profileBackground);
        return id.Length == 0 ? null : Get(id);
    }
}
