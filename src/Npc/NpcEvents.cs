using Embervale.Core.Events;
using Embervale.Entities;

namespace Embervale.Npc;

/// <summary>Raised when an NPC's current activity changes (schedule block changed,
/// started fleeing, etc.). UI/logging can surface what the village is doing.</summary>
public readonly record struct NpcActivityChangedEvent(IEntity Npc, string Activity) : IGameEvent;
