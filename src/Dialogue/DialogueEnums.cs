namespace Embervale.Dialogue;

/// <summary>
/// Side effect a <see cref="DialogueChoice"/> fires when picked. Kept declarative
/// (an enum + a string argument) so conversations stay pure data — no scripting in
/// the <c>.tres</c>. <see cref="DialogueSession"/> applies the effect against the
/// speaking actor's components.
/// </summary>
// APPEND ONLY: ordinals persist in .tres/saves — never reorder/insert/remove (EnumStabilityTests).
public enum DialogueEffect
{
    /// <summary>Nothing happens beyond navigating to the next node.</summary>
    None,

    /// <summary>Start the quest whose id is the choice's <c>EffectArg</c> on the player's log.</summary>
    StartQuest,

    /// <summary>Set the story flag named by the choice's <c>EffectArg</c>.</summary>
    SetFlag,

    /// <summary>Clear the story flag named by the choice's <c>EffectArg</c>.</summary>
    ClearFlag,
}

/// <summary>
/// Gate controlling whether a <see cref="DialogueChoice"/> is offered. Evaluated by
/// <see cref="DialogueSession"/> against the player's quest log and story flags;
/// a choice whose condition fails is hidden from the conversation.
/// </summary>
// APPEND ONLY: ordinals persist in .tres/saves — never reorder/insert/remove (EnumStabilityTests).
public enum DialogueCondition
{
    /// <summary>Always shown.</summary>
    Always,

    /// <summary>Shown only while the quest (<c>ConditionArg</c>) can be started.</summary>
    QuestAvailable,

    /// <summary>Shown only while the quest is active in the log.</summary>
    QuestActive,

    /// <summary>Shown only once the quest has been completed.</summary>
    QuestCompleted,

    /// <summary>Shown only while the quest is not in the log at all.</summary>
    QuestNotStarted,

    /// <summary>Shown only when the story flag (<c>ConditionArg</c>) is set.</summary>
    HasFlag,

    /// <summary>Shown only when the story flag is not set.</summary>
    MissingFlag,
}
