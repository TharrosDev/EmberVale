using Embervale.Core.Events;
using Embervale.Entities;
using Godot;

namespace Embervale.Enemies;

/// <summary>
/// Broadcast when an enemy first spots the target. Nearby allies that are not yet
/// engaged react by investigating <paramref name="Position"/>, producing simple
/// group coordination without direct coupling between AI instances.
/// </summary>
public readonly record struct EnemyAlertedEvent(IEntity Source, Vector3 Position) : IGameEvent;

/// <summary>Raised when an enemy's AI transitions between behaviour states.</summary>
public readonly record struct EnemyStateChangedEvent(IEntity Enemy, EnemyState State) : IGameEvent;

/// <summary>Raised when a boss crosses an HP threshold into a new phase (1-based). The healthbar /
/// intro-defeat work (Phase 28C) and the future <c>BossController</c> (Phase 36) react to this.</summary>
public readonly record struct BossPhaseChangedEvent(IEntity Boss, int Phase, int TotalPhases) : IGameEvent;

/// <summary>
/// Raised once when a boss fight begins. Drives the healthbar and the intro lock. Published through
/// <c>BossController.BeginEncounter</c>, which the summoning brazier calls on the entrance beat and
/// the controller self-calls on the first damage traded — so a lair boss nobody summons gets one too.
/// <paramref name="DisplayName"/> is already localized (the archetype's <c>NameKey</c> resolved at
/// build time); the bar showed a raw <c>"boss.name"</c> key for everyone before 36E.
/// </summary>
public readonly record struct BossEncounterStartedEvent(
    IEntity Boss, string DisplayName, int TotalPhases) : IGameEvent;
