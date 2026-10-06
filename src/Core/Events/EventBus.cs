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

    /// <summary>
    /// One event type's subscribers. Type-erased so the bus can count and clear every channel
    /// without knowing its event type.
    /// </summary>
    private abstract class Channel
    {
        public abstract int Count { get; }

        public abstract void Clear();
    }

    /// <summary>
    /// The handler list one dispatch iterates. <see cref="Readers"/> counts the dispatches currently
    /// walking it; while that is non-zero the list is frozen and a subscribe/unsubscribe swaps a
    /// copy in instead (see <see cref="Channel{T}.Writable"/>).
    /// </summary>
    private sealed class Bucket<T>
    {
        public readonly List<Action<T>> Items;
        public int Readers;

        public Bucket(List<Action<T>> items) => Items = items;
    }

    private sealed class Channel<T> : Channel
    {
        public Bucket<T> Live = new(new List<Action<T>>());

        /// <summary>Mirror of the live list for the O(1) duplicate check. Delegate equality is
        /// target + method, the same test <c>List.Contains</c> applied.</summary>
        public readonly HashSet<Action<T>> Members = new();

        public override int Count => Live.Items.Count;

        /// <summary>The list a mutation may edit. If a dispatch is walking the live one, that
        /// dispatch keeps it (it is its snapshot) and the channel moves on to a copy.</summary>
        public List<Action<T>> Writable()
        {
            if (Live.Readers > 0)
            {
                Live = new Bucket<T>(new List<Action<T>>(Live.Items));
            }

            return Live.Items;
        }

        public override void Clear()
        {
            Members.Clear();
            if (Live.Readers > 0)
            {
                Live = new Bucket<T>(new List<Action<T>>());
            }
            else
            {
                Live.Items.Clear();
            }
        }
    }

    /// <summary>
    /// The channel for <typeparamref name="T"/>, resolved by the runtime's generic static lookup
    /// rather than a dictionary probe on every publish. The bus is a singleton autoload, so the
    /// channels are process-wide; <see cref="_ExitTree"/> and <see cref="Clear"/> empty them.
    /// </summary>
    private static class Slot<T>
    {
        public static Channel<T>? Value;
    }

    private static readonly List<Channel> Channels = new();

    private static Channel<T> ChannelFor<T>()
    {
        Channel<T>? channel = Slot<T>.Value;
        if (channel == null)
        {
            channel = new Channel<T>();
            Slot<T>.Value = channel;
            Channels.Add(channel);
        }

        return channel;
    }

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
            Clear();
            Instance = null!;
        }
    }

    public void Subscribe<T>(Action<T> handler)
        where T : IGameEvent
    {
        ArgumentNullException.ThrowIfNull(handler);

        Channel<T> channel = ChannelFor<T>();
        if (channel.Members.Add(handler))
        {
            channel.Writable().Add(handler);
        }
    }

    public void Unsubscribe<T>(Action<T> handler)
        where T : IGameEvent
    {
        if (handler == null)
        {
            return;
        }

        Channel<T>? channel = Slot<T>.Value;
        if (channel != null && channel.Members.Remove(handler))
        {
            channel.Writable().Remove(handler);
        }
    }

    public void Publish<T>(T gameEvent)
        where T : IGameEvent
    {
        Bucket<T>? bucket = Slot<T>.Value?.Live;
        if (bucket == null)
        {
            return;
        }

        List<Action<T>> handlers = bucket.Items;
        int count = handlers.Count;
        if (count == 0)
        {
            return;
        }

        // This is the hottest path in the game (resource/combat/status events fire constantly), so
        // a publish allocates nothing and copies nothing. The list is iterated in place and marked
        // as being read; a handler that subscribes or unsubscribes mid-dispatch makes the channel
        // copy the list first (Channel.Writable), which leaves this dispatch walking exactly the
        // handlers that were registered when it began — the same contract the old per-publish
        // snapshot gave, paid for only on the rare reentrant mutation instead of on every event.
        bucket.Readers++;
        try
        {
            for (int i = 0; i < count; i++)
            {
                try
                {
                    handlers[i].Invoke(gameEvent);
                }
                catch (Exception ex)
                {
                    Log.Error($"Handler for {typeof(T).Name} threw: {ex}");
                }
            }
        }
        finally
        {
            bucket.Readers--;
        }
    }

    /// <summary>Removes every registered handler. Primarily for scene resets.</summary>
    public void Clear()
    {
        foreach (Channel channel in Channels)
        {
            channel.Clear();
        }
    }

    /// <summary>Number of live handlers for an event type. For leak diagnostics/tests.</summary>
    public int SubscriberCount<T>()
        where T : IGameEvent
    {
        return Slot<T>.Value?.Count ?? 0;
    }

    /// <summary>Total handlers across all event types. A non-zero baseline after a scene
    /// reset points at subscriptions that were never paired with an unsubscribe.</summary>
    public int TotalSubscriberCount()
    {
        int total = 0;
        foreach (Channel channel in Channels)
        {
            total += channel.Count;
        }

        return total;
    }
}
