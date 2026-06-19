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

    /// <summary>Setup hook invoked once the owning <see cref="Entity"/> is known.</summary>
    protected virtual void OnInitialize()
    {
    }

    /// <summary>Cleanup hook invoked when the component leaves the tree.</summary>
    protected virtual void OnTeardown()
    {
    }
}
