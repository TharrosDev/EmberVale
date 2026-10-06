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
///
/// <b>What a save costs here.</b> <see cref="Save"/> used to re-walk every node of every loaded
/// cell, on every save, to find the handful of persistent actors among thousands of meshes and
/// colliders. Each cell now carries a <see cref="CellLedger"/>: the actor list found by the one
/// walk a cell load already needs, kept current by the removal hook, and re-walked only when the
/// cell is marked stale or its root's child count has changed. <b>The dirty flag covers membership,
/// not component state</b>: nothing in the game announces "this component's saved state changed",
/// and a guessed list of events that imply it would be one missed event away from a chest that
/// reloads full. So the listed actors' components are still asked for their state on every save,
/// which is a few dozen small dictionaries, and a cell with no persistent actors costs nothing.
/// A cell streaming out is always walked in full, exactly as before.
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

    // Currently-loaded cells, and the set of cells whose actors are leaving the tree because the
    // cell is unloading (so those frees are not mistaken for gameplay removals).
    private readonly Dictionary<string, CellLedger> _cells = new();
    private readonly HashSet<string> _unloading = new();

    /// <summary>A loaded cell and the persistent actors known to be in it.</summary>
    private sealed class CellLedger
    {
        public CellLedger(Node3D root)
        {
            Root = root;
        }

        public Node3D Root { get; }

        /// <summary>The cell's persistent actors as of the last walk, less the ones removed since.</summary>
        public List<IEntity> Actors { get; } = new();

        /// <summary>The root's child count at the last walk. A different count means something was
        /// added to or taken from the cell, which is reason enough to look again.</summary>
        public int ChildCount { get; set; } = -1;

        /// <summary>Set when the actor list can no longer be trusted; the next snapshot re-walks.</summary>
        public bool Stale { get; set; } = true;
    }

    // Instance ids of bodies whose TreeExiting is already hooked, so a body is hooked once and not
    // once per reconcile. See HookRemoval — entries are dropped by the handler itself as it fires.
    private readonly HashSet<ulong> _hooked = new();

    public override void _EnterTree()
    {
        ServiceScope.RegisterOwned(this, this);
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
    }

    private void OnCellLoaded(RegionCellLoadedEvent e)
    {
        _unloading.Remove(e.CellId); // a fresh load: clear any stale unloading flag for this cell
        var cell = new CellLedger(e.Root);
        _cells[e.CellId] = cell;
        Reconcile(e.CellId, cell);
    }

    private void OnCellUnloaded(RegionCellUnloadedEvent e)
    {
        // The streamer frees the cell root right after this returns; mark it unloading so the
        // actors' TreeExiting (fired at end of frame) is not read as a gameplay removal.
        _unloading.Add(e.CellId);
        if (_cells.TryGetValue(e.CellId, out CellLedger? cell))
        {
            // Always a full walk: this is the last look at the cell before it is freed, and the
            // ledger is the only thing that will remember it.
            if (IsInstanceValid(cell.Root))
            {
                cell.Stale = true;
                Snapshot(e.CellId, cell);
            }

            _cells.Remove(e.CellId);
        }
    }

    /// <summary>Culls removed actors and restores stored state on the survivors of a freshly-loaded
    /// cell. The walk it needs anyway is also what fills the cell's actor list.</summary>
    private void Reconcile(string cellId, CellLedger cell)
    {
        cell.Actors.Clear();
        foreach (IEntity actor in PersistentActorsIn(cell.Root))
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

            cell.Actors.Add(actor);
            HookRemoval(cellId, actor);
        }

        cell.ChildCount = cell.Root.GetChildCount();
        cell.Stale = false;
    }

    /// <summary>
    /// Stores the current component state of a cell's surviving persistent actors.
    ///
    /// The actors come from the cell's list. The tree is only walked again when the list is stale
    /// or the root's child count moved, and an actor that walk turns up for the first time is
    /// hooked like any other, so one added after the cell loaded is both saved and tracked.
    /// </summary>
    private void Snapshot(string cellId, CellLedger cell)
    {
        if (cell.Stale || cell.Root.GetChildCount() != cell.ChildCount)
        {
            cell.Actors.Clear();
            foreach (IEntity actor in PersistentActorsIn(cell.Root))
            {
                if (_removed.Contains(actor.PersistentId!))
                {
                    continue;
                }

                cell.Actors.Add(actor);
                if (!_unloading.Contains(cellId))
                {
                    HookRemoval(cellId, actor);
                }
            }

            cell.ChildCount = cell.Root.GetChildCount();
            cell.Stale = false;
        }

        foreach (IEntity actor in cell.Actors)
        {
            // The list can outlive an actor by a frame (the removal hook fires as it leaves the
            // tree); anything no longer in the tree is not this cell's to describe.
            if (actor.Body is not Node body || !IsInstanceValid(body) || !body.IsInsideTree() ||
                _removed.Contains(actor.PersistentId!))
            {
                continue;
            }

            foreach (ISaveable saveable in SaveablesOf(actor))
            {
                _state[saveable.SaveId] = saveable.Save();
            }
        }
    }

    /// <summary>
    /// Attaches the removal detector to an actor's body — <b>once per body</b>.
    ///
    /// ⚠️ <b>The guard is the point.</b> <see cref="Reconcile"/> runs on every
    /// <c>RegionCellLoadedEvent</c> and again from <see cref="Load"/>, so a save loaded while a cell
    /// is live — or any path that reconciles the same cell twice — used to attach a second closure to
    /// the same body, each capturing <c>cellId</c>, <c>pid</c> and <c>actor</c>, with nothing ever
    /// detaching them. The duplicates were invisible because the work is idempotent (a
    /// <see cref="HashSet{T}"/> add and a dictionary remove), so this never produced a wrong result —
    /// it accumulated closures on a live node and would keep accumulating for the session.
    ///
    /// Keyed on the instance id rather than the actor: a cell that unloads and streams back in brings
    /// a <em>new</em> body, which must be hooked again. The handler drops its own entry as it fires,
    /// which is both the cleanup and the reason an unload/reload cycle re-hooks correctly.
    /// </summary>
    private void HookRemoval(string cellId, IEntity actor)
    {
        string pid = actor.PersistentId!;
        var body = (Node)actor.Body;
        ulong instanceId = body.GetInstanceId();
        if (!_hooked.Add(instanceId))
        {
            return;
        }

        body.TreeExiting += () =>
        {
            _hooked.Remove(instanceId);

            // Leaving because the cell is streaming out is not a removal; a death/pickup is.
            if (_unloading.Contains(cellId))
            {
                return;
            }

            _removed.Add(pid);
            DropState(actor);
            if (_cells.TryGetValue(cellId, out CellLedger? cell))
            {
                cell.Actors.Remove(actor);
            }
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
        foreach (KeyValuePair<string, CellLedger> cell in _cells)
        {
            if (IsInstanceValid(cell.Value.Root))
            {
                Snapshot(cell.Key, cell.Value);
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
            SaveManager? saves = SaveManager.Instance;
            foreach (KeyValuePair<Variant, Variant> kv in stateV.AsGodotDictionary())
            {
                if (kv.Value.VariantType != Variant.Type.Dictionary)
                {
                    continue;
                }

                string id = kv.Key.AsString();
                _state[id] = kv.Value.AsGodotDictionary();

                // This ledger is that id's owner until its cell streams back in. Said out loud so the
                // save manager's orphan report doesn't flag the normal case of saving inside a holding
                // and loading somewhere else.
                saves?.ClaimDeferred(id);
            }
        }

        // Apply to any cells already streamed in (e.g. loading a save while a cell is live).
        foreach (KeyValuePair<string, CellLedger> cell in new Dictionary<string, CellLedger>(_cells))
        {
            if (IsInstanceValid(cell.Value.Root))
            {
                Reconcile(cell.Key, cell.Value);
            }
        }
    }
}
