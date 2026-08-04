using System;
using System.Collections.Generic;
using Embervale.Core.Diagnostics;
using Godot;

namespace Embervale.Core.Services;

/// <summary>
/// Lightweight registry for long-lived systems that are not Godot autoloads
/// (e.g. the active player, world manager, spawn director). Autoloads cover
/// engine singletons; this covers gameplay singletons whose lifetime is tied
/// to a loaded world rather than the whole process.
///
/// Registered as the <c>ServiceLocator</c> autoload. Resolution is by concrete
/// type or interface; only one instance per type is held.
/// </summary>
public sealed partial class ServiceLocator : Node
{
    public static ServiceLocator Instance { get; private set; } = null!;

    private readonly Dictionary<Type, object> _services = new();

    public override void _EnterTree()
    {
        if (Instance != null && Instance != this)
        {
            QueueFree();
            return;
        }

        Instance = this;
    }

    public override void _ExitTree()
    {
        if (Instance == this)
        {
            _services.Clear();
            Instance = null!;
        }
    }

    public void Register<T>(T service)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(service);

        Type key = typeof(T);
        if (_services.ContainsKey(key))
        {
            Log.Warn($"Service {key.Name} is being replaced.");
        }

        _services[key] = service;
    }

    public void Unregister<T>()
        where T : class
    {
        _services.Remove(typeof(T));
    }

    /// <summary>
    /// Removes the registration for <typeparamref name="T"/> only if it still points at
    /// <paramref name="instance"/>. A replaced/respawned actor tearing down must not evict
    /// a newer instance that already took its slot.
    /// </summary>
    public void Unregister<T>(T instance)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(instance);

        if (_services.TryGetValue(typeof(T), out object? current) && ReferenceEquals(current, instance))
        {
            _services.Remove(typeof(T));
        }
    }

    public T Get<T>()
        where T : class
    {
        if (TryGet(out T service))
        {
            return service;
        }

        throw new InvalidOperationException($"No service registered for {typeof(T).Name}.");
    }

    public bool TryGet<T>(out T service)
        where T : class
    {
        if (Resolve(typeof(T)) is T found)
        {
            service = found;
            return true;
        }

        service = null!;
        return false;
    }

    public bool IsRegistered<T>()
        where T : class
    {
        return Resolve(typeof(T)) != null;
    }

    /// <summary>
    /// The one read path, and the one place a dead registration is caught. Most services here are
    /// Godot <see cref="Node"/>s whose lifetime is a loaded world's, and several register without
    /// ever unregistering — so a freed registrant would otherwise be handed out as a live service
    /// and dereferenced, which in .NET Godot is a hard <c>gchandle.is_released</c> crash rather than
    /// a null check away. Dropping it here fixes every caller at once instead of asking two dozen
    /// call sites to remember <c>IsInstanceValid</c> (eleven of them did not).
    /// </summary>
    private object? Resolve(Type key)
    {
        if (!_services.TryGetValue(key, out object? service))
        {
            return null;
        }

        if (service is GodotObject godot && !GodotObject.IsInstanceValid(godot))
        {
            _services.Remove(key);
            Log.Warn($"Service {key.Name} was freed without unregistering; dropped.");
            return null;
        }

        return service;
    }
}
