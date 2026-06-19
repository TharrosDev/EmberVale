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

    /// <summary>Process-unique id assigned on spawn; used by targeting and save.</summary>
    ulong RuntimeId { get; }

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
