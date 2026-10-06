using System.Collections.Generic;

namespace Embervale.Debugging;

/// <summary>
/// <see cref="ItemValidator"/>: affix and loot-table rules. Owned by the loot lane alone; its rules
/// go here and nowhere else. <c>RequireKey</c> in <c>ItemValidator.cs</c> is shared.
/// </summary>
public static partial class ItemValidator
{
    private static void CollectLoot(List<string> issues)
    {
        _ = issues; // No rules yet: the owning lane replaces this line with its checks.
    }
}
