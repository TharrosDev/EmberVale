using System.Collections.Generic;
using Godot;

namespace Embervale.Entities;

/// <summary>
/// Shared implementation behind the <see cref="IEntity"/> actors. Because C#
/// is single-inheritance, <see cref="Entity"/> (Node3D) and
/// <see cref="CharacterEntity"/> (CharacterBody3D) cannot share a base class,
/// so the common component-host logic lives here and both delegate to it.
/// </summary>
internal static class EntityNode
{
    private static ulong _nextRuntimeId = 1;

    /// <summary>Allocates the next process-unique runtime id (shared across all actor types).</summary>
    public static ulong NextRuntimeId()
    {
        return _nextRuntimeId++;
    }

    public static T? GetComponent<T>(Node host)
        where T : EntityComponent
    {
        foreach (Node child in host.GetChildren())
        {
            if (child is T match)
            {
                return match;
            }
        }

        return null;
    }

    public static IEnumerable<T> GetComponents<T>(Node host)
        where T : EntityComponent
    {
        foreach (Node child in host.GetChildren())
        {
            if (child is T match)
            {
                yield return match;
            }
        }
    }

    /// <summary>Walks up the tree from <paramref name="node"/> to the first <see cref="IEntity"/>.</summary>
    public static IEntity? FindOwner(Node? node)
    {
        while (node != null)
        {
            if (node is IEntity entity)
            {
                return entity;
            }

            node = node.GetParent();
        }

        return null;
    }
}
