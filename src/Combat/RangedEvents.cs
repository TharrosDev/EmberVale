using Embervale.Core.Events;
using Embervale.Entities;

namespace Embervale.Combat;

/// <summary>
/// Raised when an arrow strikes a body. <paramref name="Charge"/> is the draw (0..1; 0 for a shooter
/// with no draw, an AI archer), <paramref name="Headshot"/> is true for a struck weak point or the
/// head of a tall body, and <paramref name="Distance"/> is how far the arrow flew. Presentation reads
/// this for the marker at the crosshair; nothing in the rules depends on it.
/// </summary>
public readonly record struct ArrowHitEvent(
    IEntity? Shooter, IEntity Target, float Charge, bool Headshot, float Distance) : IGameEvent;
