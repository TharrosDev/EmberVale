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

/// <summary>
/// Raised when a quest is lost rather than finished (Phase 41B) — the escortee went down, or the
/// player died with a hold unfinished. Deliberately a sibling of <see cref="QuestCompletedEvent"/>
/// rather than a flag on it: the toast, the journal and any future ending flag all want to know
/// which of the two happened without inspecting the quest's state afterwards.
/// </summary>
public readonly record struct QuestFailedEvent(IEntity Owner, QuestResource Quest) : IGameEvent;

/// <summary>
/// Raised when developer tooling removes a quest from the journal (41F). It has no player-facing
/// toast: it exists so the journal drops a stale card if reset is issued while the panel is open.
/// </summary>
public readonly record struct QuestResetEvent(IEntity Owner, QuestResource Quest) : IGameEvent;

/// <summary>Raised when an objective becomes live: its gates or sequence unlocked it, or its quest just
/// started. Not published on a load (a load restores state, it does not narrate one).</summary>
public readonly record struct QuestObjectiveActivatedEvent(string QuestId, int ObjectiveIndex) : IGameEvent;

/// <summary>Raised whenever a required or optional objective completes
/// (<paramref name="Completed"/> true) or newly activates (false). The one event the journal and
/// toasts need to refresh a quest's stage.</summary>
public readonly record struct QuestStageChangedEvent(string QuestId, int ObjectiveIndex, bool Completed) : IGameEvent;

/// <summary>Raised when a quest with a non-empty <c>ChapterKey</c> starts. Once-per-chapter-per-save
/// is the subscriber's job (via <c>flag.chapter.&lt;key&gt;</c>); this is published on every such start.</summary>
public readonly record struct ChapterStartedEvent(string ChapterKey, string QuestId) : IGameEvent;

/// <summary>Raised when the player marks a quest's current stage as seen (the journal's "updated" dot
/// should be re-derived for this quest).</summary>
public readonly record struct QuestSeenChangedEvent(string QuestId) : IGameEvent;
