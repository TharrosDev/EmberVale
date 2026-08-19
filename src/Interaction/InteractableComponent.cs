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
    /// <summary>
    /// Optional stable id naming <b>this specific interactable</b> so a quest can target it
    /// (Phase 41C, <see cref="Quests.ObjectiveType.Interact"/>). Empty — the default — means the
    /// thing is usable but not quest-targetable, which is true of almost everything.
    ///
    /// ⚠️ <b>It lives on the base class because nothing else covers the family.</b> Each of the
    /// thirteen subclasses carries its own domain id (<c>SpellId</c>, <c>PropertyId</c>,
    /// <c>ShopId</c>, <c>StationName</c>…) and a waystone, a container or a trophy stand carries
    /// none at all, so a quest that named "the thing you use" had nothing to name.
    ///
    /// ⚠️ <b>It is a scene-authored id with no database behind it</b>, which makes it the second of
    /// its kind after <c>MapLocationComponent.LocationId</c> — and it is validated the same way, by
    /// scanning the cell scenes in <em>both</em> directions. Unique across the project by rule: two
    /// nodes sharing an id would advance one objective twice.
    /// </summary>
    [Godot.Export] public string InteractId { get; set; } = string.Empty;

    /// <summary>Short verb shown in the interaction prompt, e.g. "Pick up Health Potion".</summary>
    public abstract string Prompt { get; }

    /// <summary>Performs the interaction on behalf of <paramref name="instigator"/>.</summary>
    public abstract void Interact(IEntity instigator);
}
