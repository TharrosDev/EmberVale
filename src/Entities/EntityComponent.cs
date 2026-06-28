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

    // Pairs OnInitialize/OnTeardown exactly once. _Ready fires once, but Godot fires _ExitTree on
    // EVERY removal from the tree, so without this a detach/re-attach (or any double _ExitTree) would
    // run OnTeardown twice — double-unsubscribe/unregister, and a component left dead.
    private bool _initialized;

    public override void _Ready()
    {
        Entity = EntityNode.FindOwner(GetParent());
        if (Entity == null)
        {
            Log.Warn($"{GetType().Name} '{Name}' has no owning Entity ancestor; it will be inert.");
            return;
        }

        OnInitialize();
        _initialized = true;
    }

    public override void _ExitTree()
    {
        if (_initialized)
        {
            _initialized = false;
            OnTeardown();
        }
    }

    /// <summary>
    /// Builds a save key for an <see cref="Save.ISaveable"/> component from the owner's identity
    /// (see <see cref="Save.SaveKeyPolicy"/>). Only ever reached for a <em>registered</em> saveable,
    /// which <see cref="RegisterSaveable"/> guarantees has a stable <see cref="IEntity.PersistentId"/>.
    /// </summary>
    protected string SaveKey(string prefix) =>
        Entity == null ? $"{prefix}:0" : Save.SaveKeyPolicy.Key(prefix, Entity.PersistentId, Entity.RuntimeId);

    /// <summary>
    /// Registers this component with the <see cref="Save.SaveManager"/> — but <b>only if its owner
    /// persists</b> (has a stable <see cref="IEntity.PersistentId"/>). Transient actors (spawned mobs,
    /// the training dummy) are session-only; registering them would write runtime-keyed state that can
    /// never be reclaimed after a world rebuild and instead orphans on the next load (Phase 25.5A).
    /// <see cref="Save.ISaveable"/> components call this from <see cref="OnInitialize"/> instead of
    /// <c>SaveManager.Register</c> directly. The matching <c>Unregister</c> in
    /// <see cref="OnTeardown"/> is a safe no-op when this skipped registration.
    /// </summary>
    protected void RegisterSaveable()
    {
        if (this is Save.ISaveable saveable && Entity != null &&
            Save.SaveKeyPolicy.ShouldPersist(Entity.PersistentId))
        {
            Save.SaveManager.Instance?.Register(saveable);
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
