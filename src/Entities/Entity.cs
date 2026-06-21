using System.Collections.Generic;
using Embervale.Core.Events;
using Godot;

namespace Embervale.Entities;

/// <summary>
/// Root node for a static (non-physics) in-world actor: chests, props,
/// interactables, training targets. Behaviour comes entirely from
/// <see cref="EntityComponent"/> children. For actors that move under physics,
/// use <see cref="CharacterEntity"/> instead — both satisfy <see cref="IEntity"/>.
///
/// Identity is assigned in <c>_EnterTree</c> (top-down) so component
/// <c>_Ready</c> (bottom-up) can rely on it.
/// </summary>
[GlobalClass]
public partial class Entity : Node3D, IEntity
{
    [Export]
    public string DisplayName { get; set; } = "Entity";

    [Export]
    public string TemplateId { get; set; } = string.Empty;

    public ulong RuntimeId { get; private set; }

    /// <summary>
    /// Stable id for persistence (see <see cref="IEntity.PersistentId"/>). Authored
    /// in the editor for fixed world actors, or assigned by a factory at spawn.
    /// </summary>
    [Export]
    public string PersistentId { get; set; } = string.Empty;

    public Node3D Body => this;

    public override void _EnterTree()
    {
        RuntimeId = EntityNode.NextRuntimeId();
    }

    public override void _Ready()
    {
        EventBus.Instance?.Publish(new EntitySpawnedEvent(this));
    }

    public override void _ExitTree()
    {
        EventBus.Instance?.Publish(new EntityDespawnedEvent(this));
    }

    public T? GetComponent<T>()
        where T : EntityComponent => EntityNode.GetComponent<T>(this);

    public bool TryGetComponent<T>(out T component)
        where T : EntityComponent
    {
        T? found = GetComponent<T>();
        component = found!;
        return found != null;
    }

    public IEnumerable<T> GetComponents<T>()
        where T : EntityComponent => EntityNode.GetComponents<T>(this);

    public bool HasComponent<T>()
        where T : EntityComponent => GetComponent<T>() != null;

    public T AddComponent<T>(T component)
        where T : EntityComponent
    {
        AddChild(component);
        return component;
    }
}
