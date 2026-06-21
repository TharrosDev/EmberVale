using Embervale.Core.Events;

namespace Embervale.Factions;

/// <summary>Raised when the player's standing with a faction changes (and once per faction
/// on load), carrying the new value and the tier it falls into.</summary>
public readonly record struct ReputationChangedEvent(string FactionId, int Value, ReputationTier Tier) : IGameEvent;
