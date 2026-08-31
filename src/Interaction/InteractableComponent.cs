using Embervale.Entities;

namespace Embervale.Interaction;

/// <summary>Raised when an actor actually uses an interactable — a door opened, an NPC talked to, a
/// pickup taken. Published by the player controller at the moment the interaction fires, so systems
/// that care whether the verb was <em>performed</em> (onboarding, analytics) don't have to guess
/// from a keypress that may have hit nothing.
///
/// ⚠️ <b>PERFORMED MEANS IT SUCCEEDED.</b> It used to mean "the player pressed E while looking at
/// something", because <see cref="InteractableComponent.Interact"/> returned <c>void</c> and the
/// controller published unconditionally. Every refusal in the game — a shop that is shut, a deed
/// the player cannot afford, a tome behind a story flag, a pickup into a full pack — therefore
/// advanced an <c>Interact</c> quest objective and taught the tutorial that the verb had been
/// learned. <see cref="InteractableComponent.Interact"/> now reports whether it did anything.</summary>
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
    /// <summary>Perform the interaction. <b>Returns true only when it actually did something</b> —
    /// every refusal path returns false, and the caller publishes
    /// <see cref="InteractionPerformedEvent"/> on true alone.</summary>
    public abstract bool Interact(IEntity instigator);
}
