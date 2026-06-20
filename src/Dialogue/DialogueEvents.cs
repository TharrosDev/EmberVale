using Embervale.Core.Events;
using Embervale.Entities;

namespace Embervale.Dialogue;

/// <summary>Raised when a <see cref="DialogueComponent"/> opens a conversation between
/// the player and an NPC. The UI listens for this to drive the dialogue panel.</summary>
public readonly record struct DialogueStartedEvent(
    IEntity Player, IEntity Speaker, DialogueResource Dialogue) : IGameEvent;

/// <summary>Raised when a conversation closes (a choice ended it, or the player
/// dismissed it).</summary>
public readonly record struct DialogueEndedEvent(IEntity Player, DialogueResource Dialogue) : IGameEvent;
