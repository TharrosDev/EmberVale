using System;
using System.Reflection;
using Embervale.Core.Diagnostics;
using Embervale.Core.Events;

namespace Embervale.UI;

/// <summary>
/// Subscribes to a game event by its full type name, when the type exists in this build.
///
/// The campaign overhaul lands its story events (a banner request, a companion bark) from another workstream
/// on a different branch, so the UI that consumes them cannot name the types at compile time without
/// breaking whichever side builds first. This resolves the type at runtime instead: absent means no
/// subscription (and a one-line log), present means a normal <see cref="EventBus"/> subscription whose
/// handler receives the boxed event. Property reads are by name and case-insensitive, so a positional record
/// field spelled <c>key</c> or <c>Key</c> is the same field.
///
/// Once both branches are merged a consumer may switch to a direct <c>Subscribe&lt;T&gt;</c>; nothing here
/// depends on that.
/// </summary>
public static class OptionalEventBridge
{
    /// <summary>Subscribes <paramref name="handler"/> to the event type named <paramref name="typeName"/>.
    /// Returns the unsubscribe action, or null when the type is not in this build or the bus is not up.</summary>
    public static Action? Subscribe(string typeName, Action<object> handler)
    {
        EventBus? bus = EventBus.Instance;
        Type? type = typeof(EventBus).Assembly.GetType(typeName);
        if (bus == null || type == null || !typeof(IGameEvent).IsAssignableFrom(type))
        {
            if (bus != null)
            {
                Log.Info($"OptionalEventBridge: '{typeName}' is not in this build; nothing subscribed.");
            }

            return null;
        }

        MethodInfo typed = typeof(OptionalEventBridge)
            .GetMethod(nameof(MakeHandler), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(type);
        Delegate action = (Delegate)typed.Invoke(null, new object[] { handler })!;

        MethodInfo subscribe = typeof(EventBus).GetMethod(nameof(EventBus.Subscribe))!.MakeGenericMethod(type);
        MethodInfo unsubscribe = typeof(EventBus).GetMethod(nameof(EventBus.Unsubscribe))!.MakeGenericMethod(type);
        subscribe.Invoke(bus, new object[] { action });

        return () =>
        {
            if (EventBus.Instance is { } live)
            {
                unsubscribe.Invoke(live, new object[] { action });
            }
        };
    }

    /// <summary>Reads a string property of a boxed event by name, ignoring case. Empty when absent.</summary>
    public static string StringOf(object gameEvent, string property) =>
        gameEvent.GetType().GetProperty(property, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)
            ?.GetValue(gameEvent) as string ?? string.Empty;

    private static Action<T> MakeHandler<T>(Action<object> handler) => e => handler(e!);
}
