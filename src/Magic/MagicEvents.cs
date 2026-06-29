using Embervale.Core.Events;
using Embervale.Entities;

namespace Embervale.Magic;

/// <summary>Raised when an entity successfully casts a spell (mana spent, effect launched).</summary>
public readonly record struct SpellCastEvent(IEntity Caster, string SpellId) : IGameEvent;

/// <summary>Raised when a caster changes its selected/prepared spell.</summary>
public readonly record struct SpellSelectedEvent(IEntity Caster, string SpellId) : IGameEvent;

/// <summary>Raised when a caster's known spells or ranks change (bought/upgraded), so UI can refresh.</summary>
public readonly record struct SpellsChangedEvent(IEntity Caster) : IGameEvent;

/// <summary>Raised when a status effect is first applied to an entity (not on refresh).</summary>
public readonly record struct StatusEffectAppliedEvent(IEntity Target, string EffectId, IEntity? Source) : IGameEvent;

/// <summary>Raised when a status effect expires or is cleared from an entity.</summary>
public readonly record struct StatusEffectRemovedEvent(IEntity Target, string EffectId) : IGameEvent;
