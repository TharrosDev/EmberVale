namespace Embervale.Quests;

/// <summary>Pure rules shared by quest completion and content validation (41E).</summary>
public static class QuestCompletionRules
{
    /// <summary>A completion flag is written only when it names a flag and is not already present.</summary>
    public static bool ShouldSetFlag(string? flagId, bool alreadySet) =>
        !string.IsNullOrWhiteSpace(flagId) && !alreadySet;

    /// <summary>Completion effects may only write the project's persistent story-flag id family.</summary>
    public static bool IsValidFlagId(string? flagId) =>
        string.IsNullOrEmpty(flagId) || flagId.StartsWith("flag.", System.StringComparison.Ordinal);

    /// <summary>
    /// Whether the auto-start scan should start a quest. <paramref name="changedFlag"/> is the flag
    /// that was just set, or null on a load/catch-up scan (then the held flag is what counts).
    /// A quest already in the log never restarts, and a quest whose OWN completion flag is already
    /// held never starts: that is the legacy catch-up rule (the world already moved past it).
    /// </summary>
    public static bool ShouldAutoStart(
        string? autoStartFlagId, string? changedFlag, bool autoStartFlagHeld,
        string? completionFlagId, bool completionFlagHeld, bool alreadyInLog)
    {
        if (string.IsNullOrEmpty(autoStartFlagId) || alreadyInLog)
        {
            return false;
        }

        if (!string.IsNullOrEmpty(completionFlagId) && completionFlagHeld)
        {
            return false;
        }

        return changedFlag != null ? autoStartFlagId == changedFlag : autoStartFlagHeld;
    }
}
