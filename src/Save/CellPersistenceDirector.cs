using System.Collections.Generic;
using Embervale.Core.Diagnostics;
using Embervale.Core.Events;
using Embervale.Core.Services;
using Embervale.Entities;
using Embervale.World;
using Godot;

namespace Embervale.Save;

/// <summary>
/// Bridges region streaming (Phase 25B/C) to per-actor persistence (Phase 25D): keeps streamed-in
/// actors that carry a <see cref="IEntity.PersistentId"/> remembering themselves across cell
/// unload/reload — a killed enemy stays dead, a looted pickup stays gone, and a surviving actor's
/// component state (health, inventory) is restored.
///
/// The base <see cref="SaveManager"/> only restores actors alive at load time; a cell that is
/// streamed out and back in re-instances its scene fresh, losing all that. This director closes the
/// gap without changing the authoring model — authored actors stay in the cell <c>.tscn</c>:
///
///   * on cell load it reconciles each persistent actor against a per-session ledger — culling ones
///     recorded as removed, re-applying stored <see cref="ISaveable"/>-component state to survivors,
///   * a removal is detected uniformly via the actor body's <c>TreeExiting</c> (so death and pickup
///     despawn both count) — suppressed while the cell itself is unloading,
///   * on cell unload it snapshots survivors' component state,
///   * it is itself <see cref="ISaveable"/>, so the ledger round-trips through a full save/load too.
///
/// Transient actors (no <see cref="IEntity.PersistentId"/>) are ignored by design.
/// </summary>
[GlobalClass]
public partial class CellPersistenceDirector : Node, ISaveable
{
    public string SaveId => "cell_persistence";

    // Component SaveId -> its last serialized blob (state of surviving persistent actors).
    private readonly Dictionary<string, Godot.Collections.Dictionary> _state = new();

    // PersistentIds of actors that have been removed from the world (dead/looted) and must not
    // reappear when their cell reloads.
    private readonly HashSet<string> _removed = new();

    // Currently-loaded cell roots, and the set of cells whose actors are leaving the tree because
    // the cell is unloading (so those frees are not mistaken for gameplay removals).
    private readonly Dictionary<string, Node3D> _cells = new();
    private readonly HashSet<string> _unloading = new();

    public override void _EnterTree()
    {
        ServiceLocator.Instance?.Register(this);
        SaveManager.Instance?.Register(this);
        EventBus bus = EventBus.Instance;
        bus?.Subscribe<RegionCellLoadedEvent>(OnCellLoaded);
        bus?.Subscribe<RegionCellUnloadedEvent>(OnCellUnloaded);
    }

    public override void _ExitTree()
    {
        EventBus? bus = EventBus.Instance;
        bus?.Unsubscribe<RegionCellLoadedEvent>(OnCellLoaded);
        bus?.Unsubscribe<RegionCellUnloadedEvent>(OnCellUnloaded);
        SaveManager.Instance?.Unregister(this);
        ServiceLocator.Instance?.Unregister(this);
    }

    private void OnCellLoaded(RegionCellLoadedEvent e)
    {
        _unloading.Remove(e.CellId); // a fresh load: clear any stale unloading flag for this cell
        _cells[e.CellId] = e.Root;
        Reconcile(e.CellId, e.Root);
    }

    private void OnCellUnloaded(RegionCellUnloadedEvent e)
    {
        // The streamer frees the cell root right after this returns; mark it unloading so the
        // actors' TreeExiting (fired at end of frame) is not read as a gameplay removal.
        _unloading.Add(e.CellId);
        if (_cells.TryGetValue(e.CellId, out Node3D? root))
        {
            Snapshot(root);
            _cells.Remove(e.CellId);
        }
    }

    /// <summary>Culls removed actors and restores stored state on the survivors of a freshly-loaded cell.</summary>
    private void Reconcile(string cellId, Node3D root)
    {
        foreach (IEntity actor in PersistentActorsIn(root))
        {
            string pid = actor.PersistentId!;
            if (_removed.Contains(pid))
            {
                ((Node)actor.Body).QueueFree();
                continue;
            }

            foreach (ISaveable saveable in SaveablesOf(actor))
            {
                if (_state.TryGetValue(saveable.SaveId, out Godot.Collections.Dictionary? blob))
                {
                    saveable.Load(blob);
                }
            }

            HookRemoval(cellId, actor);
        }
    }

    /// <summary>Stores the current component state of a cell's surviving persistent actors.</summary>
    private void Snapshot(Node3D root)
    {
        foreach (IEntity actor in PersistentActorsIn(root))
        {
            if (_removed.Contains(actor.PersistentId!))
            {
                continue;
            }

            foreach (ISaveable saveable in SaveablesOf(actor))
            {
                _state[saveable.SaveId] = saveable.Save();
            }
        }
    }

    private void HookRemoval(string cellId, IEntity actor)
    {
        string pid = actor.PersistentId!;
        var body = (Node)actor.Body;
        body.TreeExiting += () =>
        {
            // Leaving because the cell is streaming out is not a removal; a death/pickup is.
            if (_unloading.Contains(cellId))
            {
                return;
            }

            _removed.Add(pid);
            DropState(actor);
        };
    }

    private void DropState(IEntity actor)
    {
        foreach (ISaveable saveable in SaveablesOf(actor))
        {
            _state.Remove(saveable.SaveId);
        }
    }

    private static IEnumerable<IEntity> PersistentActorsIn(Node root)
    {
        if (root is IEntity entity && !string.IsNullOrEmpty(entity.PersistentId))
        {
            yield return entity;
        }

        foreach (Node child in root.GetChildren())
        {
            foreach (IEntity nested in PersistentActorsIn(child))
            {
                yield return nested;
            }
        }
    }

    private static IEnumerable<ISaveable> SaveablesOf(IEntity actor)
    {
        foreach (EntityComponent component in actor.GetComponents<EntityComponent>())
        {
            if (component is ISaveable saveable)
            {
                yield return saveable;
            }
        }
    }

    public Godot.Collections.Dictionary Save()
    {
        // Capture the live state of currently-loaded cells so a save mid-exploration is complete.
        foreach (Node3D root in _cells.Values)
        {
            if (IsInstanceValid(root))
            {
                Snapshot(root);
            }
        }

        var removed = new Godot.Collections.Array();
        foreach (string pid in _removed)
        {
            removed.Add(pid);
        }

        var state = new Godot.Collections.Dictionary();
        foreach (KeyValuePair<string, Godot.Collections.Dictionary> kv in _state)
        {
            state[kv.Key] = kv.Value;
        }

        return new Godot.Collections.Dictionary { ["removed"] = removed, ["state"] = state };
    }

    public void Load(Godot.Collections.Dictionary data)
    {
        _removed.Clear();
        _state.Clear();

        if (data.TryGetValue("removed", out Variant removedV) && removedV.VariantType == Variant.Type.Array)
        {
            foreach (Variant pid in removedV.AsGodotArray())
            {
                _removed.Add(pid.AsString());
            }
        }

        if (data.TryGetValue("state", out Variant stateV) && stateV.VariantType == Variant.Type.Dictionary)
        {
            foreach (KeyValuePair<Variant, Variant> kv in stateV.AsGodotDictionary())
            {
                if (kv.Value.VariantType == Variant.Type.Dictionary)
                {
                    _state[kv.Key.AsString()] = kv.Value.AsGodotDictionary();
                }
            }
        }

        // Apply to any cells already streamed in (e.g. loading a save while a cell is live).
        foreach (KeyValuePair<string, Node3D> cell in new Dictionary<string, Node3D>(_cells))
        {
            if (IsInstanceValid(cell.Value))
            {
                Reconcile(cell.Key, cell.Value);
            }
        }
    }
}
