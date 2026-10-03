namespace Embervale.Quests;

/// <summary>What the HUD is currently following, for the start-tracking decision.</summary>
public enum TrackedKind
{
    /// <summary>Nothing active is tracked (or only a ledger quest is).</summary>
    None,

    /// <summary>A side quest is tracked.</summary>
    Side,

    /// <summary>A main-thread quest is tracked.</summary>
    Main,
}

/// <summary>Pure tracking policy for <see cref="QuestLogComponent"/>.</summary>
public static class QuestTrackingRules
{
    /// <summary>
    /// Whether a quest that was just started should become the tracked quest. A ledger quest never
    /// does; a tracked main quest is never displaced; with nothing tracked anything is tracked; a
    /// tracked side quest is displaced only by a main quest.
    /// </summary>
    public static bool ShouldTrackOnStart(bool newIsMain, bool newIsLedger, TrackedKind current)
    {
        if (newIsLedger)
        {
            return false;
        }

        return current switch
        {
            TrackedKind.None => true,
            TrackedKind.Main => false,
            _ => newIsMain,
        };
    }

    /// <summary>
    /// Fallback rank when nothing is explicitly tracked: the first ACTIVE quest with the highest rank
    /// wins (main non-ledger, then any non-ledger). A negative rank is never a candidate.
    /// </summary>
    public static int FallbackRank(bool isMain, bool isLedger) => isLedger ? -1 : isMain ? 2 : 1;
}
