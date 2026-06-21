namespace Embervale.Quests;

/// <summary>What an objective measures. Each kind binds to a gameplay event the
/// <see cref="QuestLogComponent"/> listens for.</summary>
// APPEND ONLY: ordinals persist in .tres/saves — never reorder/insert/remove (EnumStabilityTests).
public enum ObjectiveType
{
    /// <summary>Slay N actors whose <c>TemplateId</c> matches the objective target.</summary>
    Kill,

    /// <summary>Pick up N of an item whose id matches the objective target.</summary>
    Collect,
}

/// <summary>Lifecycle state of a quest in the player's log.</summary>
// APPEND ONLY: ordinals persist in .tres/saves — never reorder/insert/remove (EnumStabilityTests).
public enum QuestStatus
{
    Active,
    Completed,
}
