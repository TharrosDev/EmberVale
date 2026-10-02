using System;
using Embervale.Core.Events;
using Embervale.Core.Services;
using Embervale.Entities;
using Godot;

namespace Embervale.Magic;

/// <summary>
/// A delivery belongs to the live caster and timeline that emitted it. Loads cancel it before
/// saveables restore; caster removal cancels it before a freed body can be used by a later tick.
/// The delivery's cancellation callback makes gameplay inert immediately and queues its cleanup.
/// </summary>
public sealed class SpellLifetime : IDisposable
{
    private readonly Node _owner;
    private readonly Action _cancel;
    private readonly bool _needsCaster;
    private IEntity? _caster;
    private Node3D? _casterBody;
    private EventBus? _bus;
    private bool _disposed;

    public SpellLifetime(Node owner, IEntity? caster, Action cancel)
    {
        _owner = owner;
        _cancel = cancel;
        _needsCaster = caster != null;
        _caster = caster;
        _casterBody = BodyOf(caster);
        _bus = EventBus.Instance;
        _bus?.Subscribe<GameLoadingEvent>(OnGameLoading);
        _bus?.Subscribe<EntityDespawnedEvent>(OnEntityDespawned);
        if (_casterBody != null)
        {
            _casterBody.TreeExiting += OnCasterExiting;
        }
    }

    /// <summary>A spatial host with the caster's session/world lifetime. Standalone scenes and
    /// native probes fall back to their current scene rather than following a moving actor.</summary>
    public static Node HostFor(IEntity? caster, Node context)
    {
        Node source = BodyOf(caster) ?? context;
        for (Node? node = source; node != null; node = node.GetParent())
        {
            if (node is IServiceScopeHost host && host.Scope.Lifetime is ServiceLifetime.Session or ServiceLifetime.World)
            {
                return node;
            }
        }

        Node? scene = context.GetTree()?.CurrentScene;
        return scene != null && GodotObject.IsInstanceValid(scene) && !scene.IsQueuedForDeletion()
            ? scene
            : source.GetParent() ?? context;
    }

    /// <summary>Rechecks native validity before a delivery tick or another hit in the same tick.
    /// This also catches a caster queued for removal before its exit signal has run.</summary>
    public bool Check()
    {
        if (_disposed)
        {
            return false;
        }

        if (!GodotObject.IsInstanceValid(_owner) || !_owner.IsInsideTree() || _owner.IsQueuedForDeletion() ||
            (_needsCaster && (_casterBody == null || !GodotObject.IsInstanceValid(_casterBody) ||
                !_casterBody.IsInsideTree() || _casterBody.IsQueuedForDeletion())))
        {
            Cancel();
            return false;
        }

        return true;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_bus != null && GodotObject.IsInstanceValid(_bus))
        {
            _bus.Unsubscribe<GameLoadingEvent>(OnGameLoading);
            _bus.Unsubscribe<EntityDespawnedEvent>(OnEntityDespawned);
        }

        if (_casterBody != null && GodotObject.IsInstanceValid(_casterBody))
        {
            _casterBody.TreeExiting -= OnCasterExiting;
        }

        _bus = null;
        _caster = null;
        _casterBody = null;
    }

    private void OnGameLoading(GameLoadingEvent _) => Cancel();

    private void OnEntityDespawned(EntityDespawnedEvent e)
    {
        if (ReferenceEquals(e.Entity, _caster))
        {
            Cancel();
        }
    }

    private void OnCasterExiting() => Cancel();

    private void Cancel()
    {
        if (_disposed)
        {
            return;
        }

        Dispose();
        if (GodotObject.IsInstanceValid(_owner))
        {
            _cancel();
        }
    }

    private static Node3D? BodyOf(IEntity? caster)
    {
        if (caster == null || caster is GodotObject native && !GodotObject.IsInstanceValid(native))
        {
            return null;
        }

        Node3D body = caster.Body;
        return GodotObject.IsInstanceValid(body) ? body : null;
    }
}
