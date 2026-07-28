using Embervale.Core.Events;
using Embervale.Entities;

namespace Embervale.Companions;

/// <summary>Raised when a companion joins the party (spawned and following).</summary>
public readonly record struct CompanionRecruitedEvent(string CompanionId, string NameKey, IEntity Companion) : IGameEvent;

/// <summary>Raised when a companion leaves the party (dismissed or the roster was reconciled away).</summary>
public readonly record struct CompanionDismissedEvent(string CompanionId, string NameKey) : IGameEvent;

/// <summary>Raised when a companion's standing order changes (follow ⇄ hold).</summary>
public readonly record struct CompanionStanceChangedEvent(string CompanionId, CompanionStance Stance) : IGameEvent;

/// <summary>Raised when the player issues a party-wide order with the quick command (Phase 32B).
/// The HUD and the toast feed announce it; per-companion changes still raise
/// <see cref="CompanionStanceChangedEvent"/>.</summary>
public readonly record struct CompanionOrderIssuedEvent(CompanionStance Stance) : IGameEvent;

/// <summary>Raised when a companion's AI transitions between behaviour states.</summary>
public readonly record struct CompanionStateChangedEvent(IEntity Companion, CompanionState State) : IGameEvent;

/// <summary>Raised when a companion runs out of health and goes down, and again when it recovers
/// (<paramref name="Downed"/> false). Companions are never permanently lost — a downed companion
/// stands back up after a recovery delay.</summary>
public readonly record struct CompanionDownedEvent(string CompanionId, string NameKey, bool Downed) : IGameEvent;
