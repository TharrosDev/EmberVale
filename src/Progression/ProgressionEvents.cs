using Embervale.Core.Events;
using Embervale.Entities;

namespace Embervale.Progression;

/// <summary>Raised when an entity gains experience. Carries post-gain totals.</summary>
public readonly record struct XpGainedEvent(IEntity Entity, int Amount, int CurrentXp, int XpToNext) : IGameEvent;

/// <summary>Raised when an entity reaches a new level. <paramref name="SkillPointsGained"/>
/// is how many points the level-up awarded.</summary>
public readonly record struct LeveledUpEvent(IEntity Entity, int NewLevel, int SkillPointsGained) : IGameEvent;

/// <summary>Raised when a perk is learned or ranked up (or reset on load).</summary>
public readonly record struct PerkChangedEvent(IEntity Entity, string PerkId, int Rank) : IGameEvent;
