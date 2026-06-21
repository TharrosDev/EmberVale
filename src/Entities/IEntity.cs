using System.Collections.Generic;
using Godot;

namespace Embervale.Entities;

/// <summary>
/// Contract shared by every in-world actor, whether it is a static
/// <see cref="Entity"/> (a chest, a prop) or a kinematic
/// <see cref="CharacterEntity"/> (the player, an enemy). Components and gameplay
/// systems depend on this interface rather than a concrete base class, so the
/// same <see cref="EntityComponent"/> (stats, AI, health bars) works on both.
/// </summary>
public interface IEntity
{
    /// <summary>Human-readable name for UI, dialogue and debugging.</summary>
    string DisplayName { get; }

    /// <summary>Stable content/type id (e.g. "enemy.goblin", "player"); used by
    /// quests and systems that match an actor to its archetype. May be empty.</summary>
    string TemplateId { get; }

    /// <summary>Process-unique id assigned on spawn; used by targeting and save.</summary>
    ulong RuntimeId { get; }

    /// <summary>
    /// Stable, session-independent identity for actors whose state must survive
    /// save/load (the player, named NPCs, persistent world objects). When set,
    /// <see cref="EntityComponent"/>s derive their <c>SaveId</c> from this rather
    /// than the volatile <see cref="RuntimeId"/>, so reloads reconnect state to the
    /// same logical actor. Null/empty marks a transient actor (loot, spawned mobs)
    /// whose components are not expected to persist across sessions.
    /// </summary>
    string? PersistentId { get; }

    /// <summary>The spatial node carrying this actor's world transform.</summary>
    Node3D Body { get; }

    T? GetComponent<T>()
        where T : EntityComponent;

    bool TryGetComponent<T>(out T component)
        where T : EntityComponent;

    IEnumerable<T> GetComponents<T>()
        where T : EntityComponent;

    bool HasComponent<T>()
        where T : EntityComponent;
}
