using System.Collections.Generic;
using Embervale.Core;

namespace Embervale.Crafting;

/// <summary>
/// Process-wide registry of <see cref="CraftingRecipeResource"/>s, scanned once at
/// startup from <c>res://data/recipes</c> (mirrors the established database pattern).
/// The crafting UI lists <see cref="All"/> filtered by station + known recipes, and a
/// <see cref="CraftingComponent"/> resolves a known recipe back by id. New recipe = drop
/// a <c>.tres</c>, no code change.
/// </summary>
public static class RecipeDatabase
{
    private const string DefaultDirectory = "res://data/recipes";

    private static readonly Dictionary<string, CraftingRecipeResource> ById = new();
    private static readonly List<CraftingRecipeResource> AllList = new();

    public static IReadOnlyList<CraftingRecipeResource> All => AllList;

    public static void Initialize(string directory = DefaultDirectory)
    {
        ResourceDirectory.Load<CraftingRecipeResource>(
            directory, "recipe", recipe => recipe.Id, ById, AllList);
    }

    public static CraftingRecipeResource? Get(string id)
    {
        return ById.TryGetValue(id, out CraftingRecipeResource? recipe) ? recipe : null;
    }
}
