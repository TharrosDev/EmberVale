using Embervale.Core.Events;
using Embervale.Entities;

namespace Embervale.Dialogue;

/// <summary>Raised when a story flag is set or cleared on an actor's
/// <see cref="StoryFlagsComponent"/>.</summary>
public readonly record struct StoryFlagChangedEvent(IEntity Owner, string Flag, bool Value) : IGameEvent;
