using Embervale.Core.Events;

namespace Embervale.Narrative;

/// <summary>
/// Raised once when the vertical slice's arc closes (Phase 33D). <paramref name="AbsorbedEmber"/>
/// records the choice the slice was built around — whether the player took the Iron King's ember —
/// so the closing card can reflect it rather than ending on the same words either way.
/// </summary>
public readonly record struct SliceCompletedEvent(bool AbsorbedEmber) : IGameEvent;
