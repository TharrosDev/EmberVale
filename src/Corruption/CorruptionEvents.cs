using Embervale.Core.Events;

namespace Embervale.Corruption;

/// <summary>Raised whenever the player's corruption value changes (and once on load),
/// carrying the new value and the tier it falls into.</summary>
public readonly record struct CorruptionChangedEvent(int Value, CorruptionTier Tier) : IGameEvent;

/// <summary>Raised only when a corruption change crosses a tier boundary (and once on load),
/// carrying the tier left behind and the tier now occupied. Appearance, the HUD vignette, and
/// NPC dread key off this rather than the per-point <see cref="CorruptionChangedEvent"/>.</summary>
public readonly record struct CorruptionTierChangedEvent(CorruptionTier Previous, CorruptionTier Current) : IGameEvent;
