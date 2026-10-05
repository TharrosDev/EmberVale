using System.Collections.Generic;
using Embervale.Core;

namespace Embervale.Appearance;

/// <summary>
/// Process-wide registry of <see cref="AppearanceOptionResource"/>s, scanned once at startup from
/// <c>res://data/appearance</c> (mirrors <see cref="Embervale.Backgrounds.BackgroundDatabase"/>).
/// </summary>
public static class AppearanceDatabase
{
    private const string DefaultDirectory = "res://data/appearance";

    private static readonly Dictionary<string, AppearanceOptionResource> ById = new();
    private static readonly List<AppearanceOptionResource> AllList = new();

    public static IReadOnlyList<AppearanceOptionResource> All => AllList;

    public static void Initialize(string directory = DefaultDirectory)
    {
        ResourceDirectory.Load<AppearanceOptionResource>(
            directory, "appearance", option => option.Id, ById, AllList);
    }

    public static AppearanceOptionResource? Get(string id)
    {
        return ById.TryGetValue(id, out AppearanceOptionResource? option) ? option : null;
    }

    /// <summary>The option that reproduces the unmodified model for <paramref name="slot"/>, or null if none is authored.</summary>
    public static AppearanceOptionResource? DefaultFor(AppearanceSlot slot)
    {
        foreach (AppearanceOptionResource option in AllList)
        {
            if (option.Slot == slot && option.IsDefault)
            {
                return option;
            }
        }

        return null;
    }
}
