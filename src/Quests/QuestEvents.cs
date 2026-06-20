using Embervale.Core.Events;
using Embervale.Entities;

namespace Embervale.Quests;

/// <summary>Raised when a quest is added to an actor's log.</summary>
public readonly record struct QuestStartedEvent(IEntity Owner, QuestResource Quest) : IGameEvent;

/// <summary>Raised when an objective's progress count changes.</summary>
public readonly record struct QuestObjectiveAdvancedEvent(
    IEntity Owner, QuestResource Quest, int ObjectiveIndex, int Count, int Required) : IGameEvent;

/// <summary>Raised when all of a quest's objectives are met and rewards are granted.</summary>
public readonly record struct QuestCompletedEvent(IEntity Owner, QuestResource Quest) : IGameEvent;
