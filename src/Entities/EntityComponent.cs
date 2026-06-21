using Embervale.Core.Diagnostics;
using Godot;

namespace Embervale.Entities;

/// <summary>
/// Base class for all gameplay components. A component is a child <see cref="Node"/>
/// of an <see cref="Entity"/> that contributes one slice of behaviour or data
/// (stats, health, AI, inventory, movement, ...). Composition over inheritance:
/// an entity's capabilities are the sum of the components attached to it, which
/// can be authored in scenes or added at runtime.
///
/// The owning entity is resolved automatically by walking up the tree, so
/// components work whether they are direct children or nested under helper nodes.
/// Override <see cref="OnInitialize"/> instead of <c>_Ready</c> for setup that
/// needs the owning entity to be available.
/// </summary>
public abstract partial class EntityComponent : Node
{
    /// <summary>The entity this component belongs to, or null if unparented.</summary>
    public IEntity? Entity { get; private set; }

    private bool _saveKeyFallbackWarned;

    public override void _Ready()
    {
        Entity = EntityNode.FindOwner(GetParent());
        if (Entity == null)
        {
            Log.Warn($"{GetType().Name} '{Name}' has no owning Entity ancestor; it will be inert.");
            return;
        }

        OnInitialize();
    }

    public override void _ExitTree()
    {
        if (Entity != null)
        {
            OnTeardown();
        }
    }

    /// <summary>
    /// Builds a save key for an <see cref="Save.ISaveable"/> component, preferring the
    /// owner's stable <see cref="IEntity.PersistentId"/> and falling back to the volatile
    /// <see cref="IEntity.RuntimeId"/> only for transient actors. A warning is logged when a
    /// component that is meant to persist falls back to a runtime id (these do not survive
    /// reload and the legacy "<c>prefix:0</c>" form collides across unresolved owners).
    /// </summary>
    protected string SaveKey(string prefix)
    {
        if (Entity == null)
        {
            if (!_saveKeyFallbackWarned)
            {
                _saveKeyFallbackWarned = true;
                Log.Warn($"{GetType().Name} '{Name}' built save key '{prefix}:0' with no owner; state will not persist correctly.");
            }

            return $"{prefix}:0";
        }

        if (!string.IsNullOrEmpty(Entity.PersistentId))
        {
            return $"{prefix}:{Entity.PersistentId}";
        }

        // Transient actors (spawned mobs, the dummy) legitimately have no PersistentId and
        // round-trip only within a session. Warn once per component so a forgotten id on a
        // would-be-persistent actor is visible without spamming the log every save.
        if (!_saveKeyFallbackWarned)
        {
            _saveKeyFallbackWarned = true;
            Log.Warn($"{GetType().Name} on '{Entity.DisplayName}' has no PersistentId; using a session-only runtime id ('{prefix}:{Entity.RuntimeId}'). Its state will not survive a reload.");
        }

        return $"{prefix}:{Entity.RuntimeId}";
    }

    /// <summary>Setup hook invoked once the owning <see cref="Entity"/> is known.</summary>
    protected virtual void OnInitialize()
    {
    }

    /// <summary>Cleanup hook invoked when the component leaves the tree.</summary>
    protected virtual void OnTeardown()
    {
    }
}
