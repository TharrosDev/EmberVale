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

    /// <summary>
    /// Turns every collider at or under <paramref name="root"/> on or off, remembering each authored
    /// layer in <paramref name="remembered"/> so re-enabling restores it exactly rather than assuming
    /// one. Used by anything that hides an actor without freeing it (a flag-gated portal, an
    /// unrecruited companion) — hiding a Node3D does not disable its collision, and an invisible body
    /// the interact ray still hits leaves a ghost prompt in the world.
    ///
    /// ⚠️ <b>IT WALKS THE WHOLE SUBTREE AND TESTS THE ROOT ITSELF.</b> Both callers used to look only
    /// at <c>Body.GetChildren()</c>, which sees a collider only when it is a direct child and the body
    /// is not one — so a body that IS a <c>CollisionObject3D</c>, or one whose collider sits under an
    /// imported model node, stayed fully solid and interactable while invisible.
    /// </summary>
    public static void SetCollisionEnabled(
        Node root, bool enabled, Dictionary<CollisionObject3D, uint> remembered)
    {
        if (root is CollisionObject3D collider)
        {
            if (enabled)
            {
                if (remembered.TryGetValue(collider, out uint layer))
                {
                    collider.SetDeferred(CollisionObject3D.PropertyName.CollisionLayer, layer);
                    remembered.Remove(collider);
                }
            }
            else if (collider.CollisionLayer != 0u)
            {
                remembered[collider] = collider.CollisionLayer;
                collider.SetDeferred(CollisionObject3D.PropertyName.CollisionLayer, 0u);
            }
        }

        foreach (Node child in root.GetChildren())
        {
            SetCollisionEnabled(child, enabled, remembered);
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
