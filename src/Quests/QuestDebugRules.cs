namespace Embervale.Quests;

/// <summary>
/// Godot-free decisions behind Phase 41F's quest console. Keeping the refusal vocabulary here lets
/// the log keep its authoritative mutations while the edge cases remain unit-testable.
/// </summary>
public enum QuestDebugAdvanceResult
{
    Advanced,
    MissingQuest,
    NotActive,
    InvalidObjective,
    InertObjective,
    CompleteObjective,
    InvalidAmount,
}

public enum QuestDebugCompleteResult
{
    Completed,
    AlreadyCompleted,
    NotActive,
    NoLiveObjectives,
}

public static class QuestDebugRules
{
    public static QuestDebugAdvanceResult CanAdvance(
        QuestStatus status, bool validIndex, bool active, bool complete, int amount) =>
        status != QuestStatus.Active ? QuestDebugAdvanceResult.NotActive :
        !validIndex ? QuestDebugAdvanceResult.InvalidObjective :
        amount <= 0 ? QuestDebugAdvanceResult.InvalidAmount :
        !active ? QuestDebugAdvanceResult.InertObjective :
        complete ? QuestDebugAdvanceResult.CompleteObjective :
        QuestDebugAdvanceResult.Advanced;

    public static string Describe(QuestDebugAdvanceResult result, string questId, int objectiveIndex) => result switch
    {
        QuestDebugAdvanceResult.MissingQuest => $"{questId} is not in the log",
        QuestDebugAdvanceResult.NotActive => $"{questId} is not active",
        QuestDebugAdvanceResult.InvalidObjective => $"{questId} has no objective {objectiveIndex}",
        QuestDebugAdvanceResult.InertObjective =>
            $"{questId} objective {objectiveIndex} is inert (choose or finish the earlier branch first)",
        QuestDebugAdvanceResult.CompleteObjective => $"{questId} objective {objectiveIndex} is already complete",
        QuestDebugAdvanceResult.InvalidAmount => "advance amount must be positive",
        _ => $"cannot advance {questId}",
    };
}
