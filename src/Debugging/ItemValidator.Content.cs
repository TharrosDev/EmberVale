using System.Collections.Generic;

namespace Embervale.Debugging;

/// <summary>
/// <see cref="ItemValidator"/>: coverage rules for the authored catalogue (items, sets, unique
/// effects against <c>tools/items/catalogue.py</c>). Owned by the content lane alone; its rules go
/// here and nowhere else. <c>RequireKey</c> in <c>ItemValidator.cs</c> is shared.
/// </summary>
public static partial class ItemValidator
{
    private static void CollectContent(List<string> issues)
    {
        _ = issues; // No rules yet: the owning lane replaces this line with its checks.
    }
}
