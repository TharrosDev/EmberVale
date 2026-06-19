using System;
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

        // Snapshot so handlers may subscribe/unsubscribe during dispatch safely.
        Delegate[] snapshot = list.ToArray();
        foreach (Delegate del in snapshot)
        {
            try
            {
                ((Action<T>)del).Invoke(gameEvent);
            }
            catch (Exception ex)
            {
                Log.Error($"Handler for {typeof(T).Name} threw: {ex}");
            }
        }
    }

    /// <summary>Removes every registered handler. Primarily for scene resets.</summary>
    public void Clear()
    {
        _handlers.Clear();
    }
}
