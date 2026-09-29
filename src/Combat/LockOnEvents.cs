using Embervale.Core.Events;
using Embervale.Entities;
using Godot;

namespace Embervale.Combat;

/// <summary>
/// Raised when a lock ends (the target dies, leaves range, is lost behind cover, is toggled off or is
/// otherwise gone), with why. <see cref="LockChangedEvent"/> says the target changed; this says a lock
/// <em>broke</em> and how, so the UI can show a kill differently from a lost lock. A cycle from one
/// target to another is not a break. Presentation is one-way: nothing reads this to decide a rule.
/// </summary>
/// <param name="Player">The entity that held the lock.</param>
/// <param name="Previous">The target it held, which may already be freed.</param>
/// <param name="Reason">Why it ended.</param>
/// <param name="LastPoint">Where the target was, world space, for a cue that outlives it.</param>
public readonly record struct LockBrokenEvent(
    IEntity Player, IEntity? Previous, LockBreakReason Reason, Vector3 LastPoint) : IGameEvent;
