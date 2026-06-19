namespace Embervale.Core.Events;

/// <summary>
/// Marker interface for every message that flows through the <see cref="EventBus"/>.
/// Events are immutable value/record types describing something that has
/// already happened (past tense), never a command. This keeps systems
/// decoupled: a publisher does not need to know who, if anyone, reacts.
/// </summary>
public interface IGameEvent
{
}
