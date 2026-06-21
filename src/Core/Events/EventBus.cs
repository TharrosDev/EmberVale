using System;
using System.Buffers;
using System.Collections.Generic;
using Embervale.Core.Diagnostics;
using Godot;

namespace Embervale.Core.Events;

/// <summary>
/// Strongly-typed publish/subscribe hub registered as the <c>EventBus</c>
/// autoload singleton.
///
/// Unlike Godot signals (which require a declared signal per message and tie
/// emitters to a specific Node), this bus dispatches arbitrary
/// <see cref="IGameEvent"/> payloads. New event types can be introduced
/// anywhere in the codebase without modifying the bus, which is essential for
/// a project meant to grow for years.
///
/// Usage:
///   EventBus.Instance.Subscribe&lt;EntityDiedEvent&gt;(OnEntityDied);
///   EventBus.Instance.Publish(new EntityDiedEvent(entity));
/// Always pair a Subscribe with an Unsubscribe in _ExitTree / Dispose to avoid
/// dangling handlers that keep freed objects alive.
/// </summary>
public sealed partial class EventBus : Node
{
    public static EventBus Instance { get; private set; } = null!;

    private readonly Dictionary<Type, List<Delegate>> _handlers = new();

    public override void _EnterTree()
    {
        if (Instance != null && Instance != this)
        {
            Log.Warn("A second EventBus was created; ignoring the duplicate.");
            QueueFree();
            return;
        }

        Instance = this;
    }

    public override void _ExitTree()
    {
        if (Instance == this)
        {
            _handlers.Clear();
            Instance = null!;
        }
    }

    public void Subscribe<T>(Action<T> handler)
        where T : IGameEvent
    {
        ArgumentNullException.ThrowIfNull(handler);

        Type key = typeof(T);
        if (!_handlers.TryGetValue(key, out List<Delegate>? list))
        {
            list = new List<Delegate>();
            _handlers[key] = list;
        }

        if (!list.Contains(handler))
        {
            list.Add(handler);
        }
    }

    public void Unsubscribe<T>(Action<T> handler)
        where T : IGameEvent
    {
        if (handler == null)
        {
            return;
        }

        if (_handlers.TryGetValue(typeof(T), out List<Delegate>? list))
        {
            list.Remove(handler);
        }
    }

    public void Publish<T>(T gameEvent)
        where T : IGameEvent
    {
        if (!_handlers.TryGetValue(typeof(T), out List<Delegate>? list) || list.Count == 0)
        {
            return;
        }

        // Snapshot so handlers may subscribe/unsubscribe during dispatch safely. This is the
        // hottest path in the game (resource/combat/status events fire constantly), so the
        // snapshot buffer is rented from a shared pool to avoid per-publish GC churn. The copy
        // (not the live list) is iterated, so reentrant sub/unsubscribe stays safe.
        int count = list.Count;
        Delegate[] snapshot = ArrayPool<Delegate>.Shared.Rent(count);
        try
        {
            list.CopyTo(snapshot, 0);
            for (int i = 0; i < count; i++)
            {
                try
                {
                    ((Action<T>)snapshot[i]).Invoke(gameEvent);
                }
                catch (Exception ex)
                {
                    Log.Error($"Handler for {typeof(T).Name} threw: {ex}");
                }
            }
        }
        finally
        {
            // Clear only the used slots before returning so the pool doesn't pin freed handlers.
            Array.Clear(snapshot, 0, count);
            ArrayPool<Delegate>.Shared.Return(snapshot);
        }
    }

    /// <summary>Removes every registered handler. Primarily for scene resets.</summary>
    public void Clear()
    {
        _handlers.Clear();
    }

    /// <summary>Number of live handlers for an event type. For leak diagnostics/tests.</summary>
    public int SubscriberCount<T>()
        where T : IGameEvent
    {
        return _handlers.TryGetValue(typeof(T), out List<Delegate>? list) ? list.Count : 0;
    }

    /// <summary>Total handlers across all event types. A non-zero baseline after a scene
    /// reset points at subscriptions that were never paired with an unsubscribe.</summary>
    public int TotalSubscriberCount()
    {
        int total = 0;
        foreach (List<Delegate> list in _handlers.Values)
        {
            total += list.Count;
        }

        return total;
    }
}
