using System;
using System.Collections.Generic;
using Embervale.Core.Diagnostics;
using Godot;

namespace Embervale.Core.Services;

/// <summary>
/// How long a service is allowed to live. This is the whole ownership model, and it is ordered:
/// a scope may resolve services from any LONGER-lived scope, never from a shorter-lived one.
/// </summary>
public enum ServiceLifetime
{
    /// <summary>Process start to process exit. Settings, input configuration, save-file IO.</summary>
    Application = 0,

    /// <summary>One save/game runtime. Created by New Game or Load, destroyed by quit-to-title.</summary>
    Session = 1,

    /// <summary>The loaded world. Created once the session has a world, destroyed before the session
    /// is. A region transition reconfigures this scope's services; it does not recreate them.</summary>
    World = 2,
}

/// <summary>
/// A node that owns a <see cref="ServiceScope"/>. <see cref="ServiceScope.For(Node)"/> walks a
/// node's ancestors to find its owner, so <b>where a service is parented decides how long it may
/// live</b> — which is the one ownership rule this codebase can enforce without a DI container.
/// </summary>
public interface IServiceScopeHost
{
    ServiceScope Scope { get; }
}

/// <summary>
/// A lifetime's worth of services, owned by the node that created it.
///
/// <para>This replaces the single process-wide dictionary the <see cref="ServiceLocator"/> used to
/// be. That dictionary outlived everything in it, which is why services had to remember to
/// unregister, why eleven call sites forgot <c>IsInstanceValid</c>, and why the locator ended up
/// silently dropping freed registrants to stay upright. <b>Disposing a scope removes exactly the
/// registrations it owns</b>, so a stale registration is no longer something to defend against —
/// there is nowhere for one to survive.</para>
///
/// <para>Register through <see cref="RegisterOwned{T}(Node,T)"/> wherever the service is a
/// <see cref="Node"/>: it ties the registration to the node's own <c>TreeExiting</c>, so a service
/// freed early takes its registration with it without any caller remembering to.</para>
/// </summary>
public sealed class ServiceScope : IDisposable
{
    private readonly Dictionary<Type, object> _services = new();
    private bool _disposed;

    public ServiceScope(ServiceLifetime lifetime)
    {
        Lifetime = lifetime;
        ServiceLocator.Instance?.Attach(this);
    }

    public ServiceLifetime Lifetime { get; }

    /// <summary>How many services this scope holds. Zero after <see cref="Dispose"/>, which is what
    /// the lifecycle probe asserts after every session/world teardown.</summary>
    public int Count => _services.Count;

    /// <summary>
    /// The scope owning <paramref name="node"/> — the nearest <see cref="IServiceScopeHost"/> at or
    /// above it in the tree. Null when the node is not under one, which is a wiring bug everywhere
    /// except a unit test.
    /// </summary>
    public static ServiceScope? For(Node? node)
    {
        for (Node? current = node; current != null; current = current.GetParent())
        {
            if (current is IServiceScopeHost host)
            {
                return host.Scope;
            }
        }

        return null;
    }

    /// <summary>
    /// Registers <paramref name="service"/> into the scope that owns <paramref name="owner"/>, and
    /// removes it again when <paramref name="owner"/> leaves the tree.
    ///
    /// <para>This is the normal way a Godot-node service registers itself. The lifetime it gets is
    /// the one its parent grants it, so moving a service to a different host in the scene tree is
    /// the whole of changing its lifetime — there is no second declaration to keep in sync.</para>
    /// </summary>
    public static void RegisterOwned<T>(Node owner, T service)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(service);

        if (For(owner) is not { } scope)
        {
            Invariant.Check(false, $"{typeof(T).Name} has no service scope above it; it cannot be registered.");
            return;
        }

        scope.Register(service);
        owner.TreeExiting += () => scope.Unregister(service);
    }

    public void Register<T>(T service)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(service);

        if (_disposed)
        {
            Invariant.Check(false, $"{typeof(T).Name} was registered into a disposed {Lifetime} scope.");
            return;
        }

        Type key = typeof(T);
        if (_services.TryGetValue(key, out object? existing) && !ReferenceEquals(existing, service))
        {
            // A warning, not an invariant violation: a legitimate replacement (the player respawning
            // before the old body's TreeExiting has run) looks exactly like this. The duplicate that
            // IS a bug — two sessions or two worlds open at once — is caught one level up, by
            // ServiceLocator.Attach, where it cannot be confused with a replacement.
            Log.Warn($"{key.Name} in the {Lifetime} scope is being replaced.");
        }

        _services[key] = service;
    }

    /// <summary>Removes the registration for <typeparamref name="T"/> only if it still points at
    /// <paramref name="instance"/>, so a replaced actor tearing down cannot evict its successor.</summary>
    public void Unregister<T>(T instance)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(instance);

        if (_services.TryGetValue(typeof(T), out object? current) && ReferenceEquals(current, instance))
        {
            _services.Remove(typeof(T));
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _services.Clear();
        ServiceLocator.Instance?.Detach(this);
    }

    /// <summary>Resolution against this scope alone. The chain walk lives in the locator.</summary>
    public bool TryResolveLocal(Type key, out object? service)
    {
        if (!_services.TryGetValue(key, out service))
        {
            return false;
        }

        if (service is GodotObject godot && !GodotObject.IsInstanceValid(godot))
        {
            // With scope-owned registrations this is no longer something to absorb quietly: a freed
            // service still in a live scope means an owner was freed without its TreeExiting running,
            // which is a real leak. Drop it so nothing dereferences a released handle, and say so.
            _services.Remove(key);
            Invariant.Check(false, $"{key.Name} was freed while still registered in the {Lifetime} scope.");
            service = null;
            return false;
        }

        return true;
    }
}
