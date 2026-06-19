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

    public T Get<T>()
        where T : class
    {
        if (_services.TryGetValue(typeof(T), out object? service))
        {
            return (T)service;
        }

        throw new InvalidOperationException($"No service registered for {typeof(T).Name}.");
    }

    public bool TryGet<T>(out T service)
        where T : class
    {
        if (_services.TryGetValue(typeof(T), out object? found))
        {
            service = (T)found;
            return true;
        }

        service = null!;
        return false;
    }

    public bool IsRegistered<T>()
        where T : class
    {
        return _services.ContainsKey(typeof(T));
    }
}
