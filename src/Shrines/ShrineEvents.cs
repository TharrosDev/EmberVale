using Embervale.Core.Events;
using Embervale.Entities;

namespace Embervale.Shrines;

/// <summary>Published by the blessing ownership choke point when a first shrine visit succeeds.</summary>
public readonly record struct BlessingClaimedEvent(IEntity Player, ShrineResource Shrine) : IGameEvent;

/// <summary>Published by a shrine caller when the player returns after its blessing was claimed.</summary>
public readonly record struct ShrineAlreadyVisitedEvent(ShrineResource Shrine) : IGameEvent;
