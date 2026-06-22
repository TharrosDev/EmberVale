using System;
using System.Collections.Generic;
using Godot;

namespace Embervale.Core.Pooling;

/// <summary>
/// A simple reuse pool for <see cref="Node"/>s, cutting the per-spawn allocation + scene-tree
/// churn for short-lived, high-frequency objects (spell projectiles today; pickups/effects
/// later). A returned node is <em>removed from the tree</em> (so it stops processing) and kept
/// for reuse; <see cref="Get"/> hands one back — reused if available, freshly built otherwise.
///
/// The pool does not parent or position nodes — the caller adds a <see cref="Get"/>’d node to
/// the scene and configures it. Retention is capped so a burst doesn't pin memory forever.
/// </summary>
public sealed class NodePool<T>
    where T : Node
{
    private readonly Func<T> _factory;
    // Each parked node is held with the census count it was parked under (its detached subtree
    // size), so unparking always reverses exactly what parking added — even if the node was
    // freed externally in the meantime and can no longer be measured.
    private readonly Stack<(T Node, int Count)> _free = new();
    private readonly int _maxRetained;

    public NodePool(Func<T> factory, int prewarm = 0, int maxRetained = 64)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _maxRetained = Mathf.Max(0, maxRetained);

        for (int i = 0; i < prewarm; i++)
        {
            Park(_factory());
        }
    }

    /// <summary>Number of instances currently parked and ready for reuse.</summary>
    public int Available => _free.Count;

    /// <summary>A node ready to be added to the tree: a parked one if available, else a new one.</summary>
    public T Get()
    {
        while (_free.Count > 0)
        {
            (T candidate, int count) = _free.Pop();
            NodePoolCensus.OnUnparked(count);
            if (GodotObject.IsInstanceValid(candidate))
            {
                return candidate;
            }
        }

        return _factory();
    }

    /// <summary>Reclaims a node: detaches it from the tree and retains it for reuse (up to the
    /// cap; excess are freed). Safe to call on an already-freed node.</summary>
    public void Return(T node)
    {
        if (node == null || !GodotObject.IsInstanceValid(node))
        {
            return;
        }

        node.GetParent()?.RemoveChild(node);

        if (_free.Count < _maxRetained)
        {
            Park(node);
        }
        else
        {
            node.QueueFree();
        }
    }

    /// <summary>Frees every parked node (call when the pool's owner is torn down).</summary>
    public void Clear()
    {
        while (_free.Count > 0)
        {
            (T node, int count) = _free.Pop();
            NodePoolCensus.OnUnparked(count);
            if (GodotObject.IsInstanceValid(node))
            {
                node.QueueFree();
            }
        }
    }

    /// <summary>Parks a (detached) node and records its subtree size with the census.</summary>
    private void Park(T node)
    {
        int count = SubtreeCount(node);
        _free.Push((node, count));
        NodePoolCensus.OnParked(count);
    }

    /// <summary>Total nodes in <paramref name="node"/>'s subtree (itself + all descendants) —
    /// what Godot counts as orphan nodes while the subtree is detached.</summary>
    private static int SubtreeCount(Node node)
    {
        int count = 1;
        foreach (Node child in node.GetChildren())
        {
            count += SubtreeCount(child);
        }

        return count;
    }
}
