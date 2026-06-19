using System.Collections.Generic;
using Embervale.Core.Events;
using Godot;

namespace Embervale.Entities;

/// <summary>
/// Root node for any in-world actor: the player, enemies, NPCs, destructibles,
/// interactables. An entity is a thin spatial container; all behaviour lives in
/// <see cref="EntityComponent"/> children. This keeps the type hierarchy flat
/// (no deep Enemy : Character : Actor chains) and makes actors fully data-driven.
///
/// Identity is assigned in <c>_EnterTree</c> (top-down) so that components,
/// whose <c>_Ready</c> runs afterwards (bottom-up), can rely on it.
/// </summary>
[GlobalClass]
public partial class Entity : Node3D
{
    private static ulong _nextRuntimeId = 1;

    /// <summary>Human-readable name used by UI, dialogue and debug tooling.</summary>
    [Export]
    public string DisplayName { get; set; } = "Entity";

    /// <summary>
    /// Stable identifier for content/templates (e.g. "enemy.wolf.dire").
    /// Distinct from <see cref="RuntimeId"/>, which is per-session.
    /// </summary>
    [Export]
    public string TemplateId { get; set; } = string.Empty;

    /// <summary>Process-unique id assigned on spawn; used by save and targeting.</summary>
    public ulong RuntimeId { get; private set; }

    public override void _EnterTree()
    {
        RuntimeId = _nextRuntimeId++;
    }

    public override void _Ready()
    {
        EventBus.Instance?.Publish(new EntitySpawnedEvent(this));
    }

    public override void _ExitTree()
    {
        EventBus.Instance?.Publish(new EntityDespawnedEvent(this));
    }

    /// <summary>Returns the first child component of type <typeparamref name="T"/>, or null.</summary>
    public T? GetComponent<T>()
        where T : EntityComponent
    {
        foreach (Node child in GetChildren())
        {
            if (child is T match)
            {
                return match;
            }
        }

        return null;
    }

    public bool TryGetComponent<T>(out T component)
        where T : EntityComponent
    {
        T? found = GetComponent<T>();
        component = found!;
        return found != null;
    }

    /// <summary>Enumerates every direct child component of type <typeparamref name="T"/>.</summary>
    public IEnumerable<T> GetComponents<T>()
        where T : EntityComponent
    {
        foreach (Node child in GetChildren())
        {
            if (child is T match)
            {
                yield return match;
            }
        }
    }

    public bool HasComponent<T>()
        where T : EntityComponent
    {
        return GetComponent<T>() != null;
    }

    /// <summary>Adds a component instance at runtime and returns it for chaining.</summary>
    public T AddComponent<T>(T component)
        where T : EntityComponent
    {
        AddChild(component);
        return component;
    }
}
