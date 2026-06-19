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
