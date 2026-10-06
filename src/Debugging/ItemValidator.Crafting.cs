using System.Collections.Generic;

namespace Embervale.Debugging;

/// <summary>
/// <see cref="ItemValidator"/>: recipe, station, quality and upgrade rules. Owned by the crafting
/// lane alone; its rules go here and nowhere else. <c>RequireKey</c> in <c>ItemValidator.cs</c> is
/// shared.
/// </summary>
public static partial class ItemValidator
{
    private static void CollectCrafting(List<string> issues)
    {
        _ = issues; // No rules yet: the owning lane replaces this line with its checks.
    }
}
