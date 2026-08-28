using Embervale.Core.Events;
using Embervale.Entities;

namespace Embervale.Shrines;

/// <summary>Published by the blessing ownership choke point when a first shrine visit succeeds.</summary>
public readonly record struct BlessingClaimedEvent(IEntity Player, ShrineResource Shrine) : IGameEvent;

/// <summary>Published by the blessing ownership choke point when the player returns after its
/// blessing was claimed.</summary>
public readonly record struct ShrineAlreadyVisitedEvent(ShrineResource Shrine) : IGameEvent;

/// <summary>Published when a god refuses a supplicant whose corruption has reached its tolerance
/// (41.5C). Carries the corruption reading that caused it so consequence systems do not have to
/// re-query the player mid-handler.</summary>
public readonly record struct ShrineRefusedEvent(ShrineResource Shrine, int Corruption) : IGameEvent;
