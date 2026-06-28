namespace Embervale.Quests;

/// <summary>
/// Pure objective-completion predicates behind <see cref="QuestProgress"/>. Kept Godot-free (plain ints)
/// so the "no stuck objectives" boundary — when a count satisfies its required total — is unit-testable
/// without authoring <see cref="QuestResource"/>/<see cref="ObjectiveResource"/> instances.
/// </summary>
public static class ObjectiveProgress
{
    /// <summary>An objective is met once its progress reaches its required total. A non-positive
    /// requirement is satisfied immediately, so such an objective can never get stuck.</summary>
    public static bool IsComplete(int count, int required) => count >= required;

    /// <summary>True when every objective's count meets its requirement. Mismatched lengths compare
    /// only the overlap (extra requirements count as unmet); an empty list is trivially met.</summary>
    public static bool AllMet(int[] counts, int[] required)
    {
        if (required.Length > counts.Length)
        {
            return false;
        }

        for (int i = 0; i < required.Length; i++)
        {
            if (!IsComplete(counts[i], required[i]))
            {
                return false;
            }
        }

        return true;
    }
}
