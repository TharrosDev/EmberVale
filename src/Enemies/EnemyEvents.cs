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

/// <summary>Raised when a boss fight begins (the boss is summoned). Drives the boss healthbar + the
/// intro lock/title (Phase 28C). <paramref name="NameKey"/> is a <c>Loc</c> key for the boss's name.</summary>
public readonly record struct BossEncounterStartedEvent(IEntity Boss, string NameKey) : IGameEvent;
