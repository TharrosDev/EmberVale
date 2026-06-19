using System.Collections.Generic;
using Embervale.Core.Events;
using Godot;

namespace Embervale.Entities;

/// <summary>
/// Root node for a kinematic actor that moves under physics: the player and,
/// later, enemies and NPCs. It is a <see cref="CharacterBody3D"/> so a
/// locomotion component can drive it with <c>MoveAndSlide</c>, while still being
/// a full <see cref="IEntity"/> component host (stats, AI, etc.).
///
/// Mirrors <see cref="Entity"/>'s identity/component behaviour via the shared
/// <see cref="EntityNode"/> helpers.
/// </summary>
[GlobalClass]
public partial class CharacterEntity : CharacterBody3D, IEntity
{
    [Export]
    public string DisplayName { get; set; } = "Character";

    [Export]
    public string TemplateId { get; set; } = string.Empty;

    public ulong RuntimeId { get; private set; }

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
