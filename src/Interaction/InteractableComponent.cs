using Embervale.Entities;

namespace Embervale.Interaction;

/// <summary>Raised when an actor actually uses an interactable — a door opened, an NPC talked to, a
/// pickup taken. Published by the player controller at the moment the interaction fires, so systems
/// that care whether the verb was <em>performed</em> (onboarding, analytics) don't have to guess
/// from a keypress that may have hit nothing.</summary>
public readonly record struct InteractionPerformedEvent(
    Embervale.Entities.IEntity Instigator, InteractableComponent Target) : Embervale.Core.Events.IGameEvent;

/// <summary>
/// Base for anything the player can interact with via the <c>interact</c> action:
/// item pickups now, and later doors, levers, containers and NPC dialogue. The
/// player raycasts from the camera and calls <see cref="Interact"/> on the first
/// interactable it hits. Subclasses provide a <see cref="Prompt"/> for UI.
/// </summary>
public abstract partial class InteractableComponent : EntityComponent
{
    /// <summary>Short verb shown in the interaction prompt, e.g. "Pick up Health Potion".</summary>
    public abstract string Prompt { get; }

    /// <summary>Performs the interaction on behalf of <paramref name="instigator"/>.</summary>
    public abstract void Interact(IEntity instigator);
}
