using System;
using Embervale.Core.Diagnostics;
using Godot;

namespace Embervale.Core.Services;

/// <summary>
/// Read side of the service registry: resolution across the three live
/// <see cref="ServiceScope"/>s, innermost first (World, then Session, then Application).
///
/// <para>It no longer owns any storage. A scope does, and a scope is owned by the node that made it
/// — see <see cref="ServiceScope"/> for why that is the whole point. What remains here is the one
/// thing that genuinely is process-wide: knowing which scopes are currently open, so a leaf
/// component can ask for "the player" or "the clock" without being handed a reference through six
/// constructors it does not otherwise need.</para>
///
/// <para><b>Prefer an explicit reference where one is natural.</b> A composition root that builds a
/// service should hand it to the things it builds; a component should reach its siblings through
/// <c>Entity.GetComponent</c>. This exists for the genuinely late-bound case — an actor spawned by
/// the world asking the session a question — and its shrinking call count is a health metric.</para>
///
/// <para>Registered as the <c>ServiceLocator</c> autoload so it outlives every scope.</para>
/// </summary>
public sealed partial class ServiceLocator : Node
{
    private const int LifetimeCount = 3;

    /// <summary>One scope per lifetime, indexed by <see cref="ServiceLifetime"/>. A second scope of
    /// the same lifetime opening while one is live is the duplicate-session/world bug, and
    /// <see cref="Attach"/> refuses it loudly.</summary>
    private readonly ServiceScope?[] _scopes = new ServiceScope?[LifetimeCount];

    public static ServiceLocator Instance { get; private set; } = null!;

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
            Array.Clear(_scopes);
            Instance = null!;
        }
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

    /// <summary>Total live registrations across all open scopes. The lifecycle probe reads it to
    /// prove a teardown left nothing behind.</summary>
    public int RegisteredCount
    {
        get
        {
            int total = 0;
            foreach (ServiceScope? scope in _scopes)
            {
                total += scope?.Count ?? 0;
            }

            return total;
        }
    }

    internal void Attach(ServiceScope scope)
    {
        int index = (int)scope.Lifetime;
        if (_scopes[index] is { } live && live != scope)
        {
            Invariant.Check(false, $"A second {scope.Lifetime} service scope opened while one was still live.");
        }

        _scopes[index] = scope;
    }

    internal void Detach(ServiceScope scope)
    {
        int index = (int)scope.Lifetime;
        if (ReferenceEquals(_scopes[index], scope))
        {
            _scopes[index] = null;
        }
    }

    /// <summary>
    /// Innermost scope wins: a world service shadows a session service of the same type, which
    /// shadows an application one. Static and taking the array so the ordering rule can be tested
    /// without a running engine — it is the one piece of behaviour here that is not bookkeeping.
    /// </summary>
    public static object? ResolveFrom(ServiceScope?[] scopes, Type key)
    {
        for (int i = scopes.Length - 1; i >= 0; i--)
        {
            if (scopes[i] is { } scope && scope.TryResolveLocal(key, out object? service))
            {
                return service;
            }
        }

        return null;
    }

    private object? Resolve(Type key) => ResolveFrom(_scopes, key);
}
