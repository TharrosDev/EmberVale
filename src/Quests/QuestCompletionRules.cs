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
}
