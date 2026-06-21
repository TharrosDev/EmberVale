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
    private readonly Stack<T> _free = new();
    private readonly int _maxRetained;

    public NodePool(Func<T> factory, int prewarm = 0, int maxRetained = 64)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _maxRetained = Mathf.Max(0, maxRetained);

        for (int i = 0; i < prewarm; i++)
        {
            _free.Push(_factory());
        }
    }

    /// <summary>Number of instances currently parked and ready for reuse.</summary>
    public int Available => _free.Count;

    /// <summary>A node ready to be added to the tree: a parked one if available, else a new one.</summary>
    public T Get()
    {
        while (_free.Count > 0)
        {
            T candidate = _free.Pop();
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
            _free.Push(node);
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
            T node = _free.Pop();
            if (GodotObject.IsInstanceValid(node))
            {
                node.QueueFree();
            }
        }
    }
}
