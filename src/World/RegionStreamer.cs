using Embervale.Combat;
using System.Collections.Generic;
using Embervale.Core.Diagnostics;
using Embervale.Core.Events;
using Embervale.Core.Services;
using Embervale.Player;
using Godot;

namespace Embervale.World;

/// <summary>
/// Brings in a region's sub-cells (Phase 25B, rewritten 38M2): requests scenes through Godot's
/// threaded loader, stages completed resources, and instances them within the authored per-frame
/// budget. Cells stay resident until the region changes. It publishes
/// <see cref="RegionCellLoadedEvent"/>/<see cref="RegionCellUnloadedEvent"/> (the seam Phase 25D's
/// persistence hooks), while memory pressure reduces request concurrency without stalling progress.
///
/// <b>Distance streaming is gone (maintainer direction, 38M2).</b> Until now a cell loaded when the
/// player came within its <c>LoadRadius</c> and was freed when they left it plus a hysteresis margin,
/// with the rule in a pure <c>StreamDecision</c>. A region is five cells and the largest is a
/// thousand nodes, so residency costs nothing worth the seams it bought: an NPC's routine walking a
/// cell that is not loaded, a district popping in as the player crests the road, and a whole class of
/// "it only happens when you approach from the north" bug. The unload path survives only in
/// <see cref="UnloadAll"/>, which is what a region transition calls.
///
/// <b>The two regions no longer overlap in world space (the 2026-08-29 geography overhaul).</b> They
/// used to: Frostfang's roosts sat inside the Ember Crown's arena and northern wilds, so residency
/// was mutually exclusive for a reason that was a coordinate accident rather than a design. Frostfang
/// now occupies its own band east of the Ember Crown. Only one region is streamed at a time anyway —
/// a transition still calls <see cref="UnloadAll"/> — but the ambiguity is gone from the numbers.
///
/// ⚠️ <b>The streamer owns the region's <see cref="WorldHeightfield"/>.</b> It is built once in
/// <see cref="Configure"/> from every cell's authored geography and handed to each cell as a clipped
/// view, which is what makes neighbouring cells agree about the ground at their shared edge.
///
/// Pausable (default process mode), so loading halts while the game is paused. The procedural
/// sandbox is the always-loaded base — only the region's authored <see cref="RegionResource.Cells"/>
/// are managed here.
/// </summary>
public sealed partial class RegionStreamer : Node3D
{
    private sealed record ReadyCell(RegionCellResource Cell, PackedScene Scene);

    private readonly List<RegionCellResource> _cells = new();
    private readonly Dictionary<string, Node3D> _loaded = new();
    private readonly List<RegionCellResource> _pending = new();
    private readonly HashSet<string> _pendingIds = new();
    private readonly Dictionary<string, RegionCellResource> _requests = new();
    private readonly List<ReadyCell> _ready = new();

    /// <summary>Cells whose scene could not be requested, loaded or found, and that have used up
    /// <see cref="MaxAttempts"/>. ⚠️ WITHOUT THIS THE STREAMER NEVER STOPS RETRYING ONE. A failed
    /// cell is removed from the pending set but is still absent from <c>_loaded</c>, so the sweep in
    /// <see cref="_Process"/> re-queued it, re-issued the threaded request and re-logged the same
    /// warning EVERY FRAME. One warning per cell, and the sweep gives up on it.
    ///
    /// ⚠️ <b>A cell in here is a BROKEN REGION, not a settled one.</b> <see cref="IsSettled"/> used
    /// to count these towards the region being whole, so a region that had lost a cell — its terrain
    /// collider with it — reported itself ready and the loading screen cleared onto a hole in the
    /// world. <see cref="HasFailedCells"/> is the honest answer and the bootstrap refuses to enter
    /// <c>Playing</c> on it.</summary>
    private readonly HashSet<string> _failed = new();

    /// <summary>Attempts spent per cell. A threaded request can fail for reasons that do not repeat
    /// (a transient I/O error, memory pressure at the moment the request went out), and retiring a
    /// cell forever on the first of those loses a district for the session. Bounded, so the
    /// every-frame retry loop the <see cref="_failed"/> set exists to stop cannot come back.</summary>
    private readonly Dictionary<string, int> _attempts = new();

    /// <summary>Tries a cell gets before it is retired for the session.</summary>
    private const int MaxAttempts = 3;
    private WorldEnvironmentProfileResource? _environmentProfile;
    private WorldHeightfield? _heightfield;
    private WorldTerrainJobs? _terrainJobs;

    /// <summary>The first authored water body in the region, used to colour GENERATED water so a
    /// river reads as the same substance as the lake it runs into.</summary>
    private WorldWaterResource? _waterPalette;
    private WorldPerformanceBudgetResource? _streamingBudget;
    private WorldRegionBackdrop? _backdrop;
    private WorldRecovery? _recovery;
    private WorldPerformanceMonitor? _performance;
    private WorldVisibilityManager? _visibility;

    /// <summary>The region currently being streamed, or empty before the first <see cref="Configure"/>.
    /// The streamer is re-configured at both places the active region changes (world build and each
    /// hard transition), so this is the cheapest honest answer to "where is the player" for systems
    /// that need it — the <see cref="EncounterDirector"/>'s region gate reads it.</summary>
    public string ActiveRegionId { get; private set; } = string.Empty;

    /// <summary>Caches the region's cells; the streamer manages exactly these.</summary>
    public void Configure(RegionResource? region)
    {
        _cells.Clear();
        ActiveRegionId = region?.Id ?? string.Empty;
        _environmentProfile = region?.EnvironmentProfile;
        _heightfield = region == null ? null : WorldTerrainMeshBuilder.HeightfieldFor(region);
        // WARNING: CANCEL BEFORE STARTING, NOT AFTER. Configure is what fast travel and a region
        // change both go through, and the previous region's terrain jobs are still running when it
        // is called. Starting first and cancelling second leaves a window in which a mesh cut from
        // the realm being left could be handed to a cell of the realm being entered.
        _terrainJobs?.Cancel();
        _terrainJobs = region == null || _heightfield == null
            ? null
            : WorldTerrainJobs.Start(region, _heightfield);
        _waterPalette = FirstAuthoredWater(region);
        WorldGround.Set(_heightfield);
        SkyController.RegionAtmosphere = _environmentProfile;
        WorldWater.Set(region == null ? null : WorldWater.BodiesFor(region, _heightfield));
        EnsureRecovery();
        _streamingBudget = region?.PerformanceBudget;
        ClearLoadStages();
        SetProcess(true);
        EnsurePerformanceMonitor();
        _performance!.Configure(ActiveRegionId, region?.PerformanceBudget);
        EnsureVisibilityManager();
        _visibility!.Configure(region?.PerformanceBudget);
        if (_backdrop != null)
        {
            _backdrop.QueueFree();
            _backdrop = null;
        }
        if (region == null)
        {
            return;
        }

        if (_environmentProfile != null)
        {
            _backdrop = WorldRegionBackdrop.Create(_environmentProfile, region, _heightfield!);
            AddChild(_backdrop);
        }

        foreach (RegionCellResource cell in region.Cells)
        {
            if (cell != null)
            {
                _cells.Add(cell);
            }
        }
    }

    /// <summary>Frees every loaded cell and clears the pending queue (Phase 25C hard transitions).
    /// Call before <see cref="Configure"/> when re-targeting to a new region, or the old region's
    /// loaded cells orphan in the loaded set and never unload.</summary>
    public void UnloadAll()
    {
        foreach (string cellId in new List<string>(_loaded.Keys))
        {
            Unload(cellId);
        }

        ClearLoadStages();
        SetProcess(true);
        WorldBiomeScatter.ClearSourceCache();
        // A region the player has left stops burning cores behind them. Configure cancels too, but
        // a transition unloads first and may sit on a loading screen for a while before it targets
        // the next realm - that gap is exactly when the old realm should not still be generating.
        _terrainJobs?.Cancel();
        _terrainJobs = null;
    }

    /// <summary>True when nothing is queued and every one of the region's cells is loaded — the world
    /// has finished coming in. The bootstrap gates the post-transition loading screen on this (Phase
    /// 25.5B) instead of a fixed delay, so the screen holds exactly as long as the cells need and no
    /// longer. Since 38M2 that means <em>all</em> of them rather than the ones near the landing point,
    /// which is a few extra frames and the whole point of the change.</summary>
    public bool IsSettled() => _pending.Count == 0 && _requests.Count == 0 && _ready.Count == 0 &&
                               _loaded.Count == _cells.Count;

    /// <summary>True when at least one cell has exhausted its retries. The region can never settle
    /// while this holds; the caller decides what an unbuildable world means (the bootstrap refuses
    /// to leave the loading screen for it).</summary>
    /// ⚠️ A method, not a property, on purpose: the in-engine probes in <c>tools/</c> reach it
    /// through GDScript's <c>call()</c>, which only sees methods.
    public bool HasFailedCells() => _failed.Count > 0;

    /// <summary>The ids of the cells that could not be brought in, for the error the player sees.</summary>
    public IReadOnlyCollection<string> FailedCellIds => _failed;

    /// <summary>Is this specific cell in the tree? Used by the load gate to require the cell the
    /// player is about to stand in, ahead of the rest of the region.</summary>
    public bool IsCellLoaded(string cellId) => _loaded.ContainsKey(cellId);

    public override void _Process(double delta)
    {
        // No player lookup and no distance test any more: every cell of the active region belongs in
        // the tree, so the only question left is whether it is there yet.
        foreach (RegionCellResource cell in _cells)
        {
            if (!_loaded.ContainsKey(cell.Id) && !_failed.Contains(cell.Id))
            {
                Enqueue(cell);
            }
        }

        StartThreadedRequests();
        PollThreadedRequests();
        InstantiateReadyCells();

        // The region is whole: stop the sweep until something re-targets the streamer. It is not a
        // free callback — it walks every cell, and StartThreadedRequests reads the static-memory
        // performance monitor — and residency (38M2) means there is nothing left for it to decide.
        // Configure and UnloadAll are the only two things that create work, and both re-arm it.
        // ⚠️ EVERY CELL RESOLVED, NOT "NOTHING IN FLIGHT". A cell between retries is in none of the
        // three staging sets for the rest of the frame it failed on, so testing those alone parked
        // the sweep on the frame the last other cell finished — and nothing re-arms it but Configure
        // or UnloadAll, so the cell was left with retries it would never take and the region could
        // never settle. Resolved means loaded, or retired after MaxAttempts.
        if (_loaded.Count + _failed.Count == _cells.Count)
        {
            SetProcess(false);
        }
    }

    private static WorldWaterResource? FirstAuthoredWater(RegionResource? region)
    {
        if (region == null)
        {
            return null;
        }

        foreach (RegionCellResource? cell in region.Cells)
        {
            if (cell?.Presentation == null)
            {
                continue;
            }

            foreach (WorldWaterResource? water in cell.Presentation.Water)
            {
                if (water != null)
                {
                    return water;
                }
            }
        }

        return null;
    }

    private void Enqueue(RegionCellResource cell)
    {
        if (_pendingIds.Add(cell.Id))
        {
            _pending.Add(cell);
        }
    }

    private void StartThreadedRequests()
    {
        int authoredConcurrency = _streamingBudget?.MaxConcurrentLoadRequests ?? 1;
        double memoryMb = Performance.GetMonitor(Performance.Monitor.MemoryStatic) / (1024d * 1024d);
        int concurrency = WorldPerformanceRules.ThreadedLoadConcurrency(
            authoredConcurrency, memoryMb, _streamingBudget?.MaxStaticMemoryMb ?? double.MaxValue);
        while (_requests.Count < concurrency && _pending.Count > 0)
        {
            RegionCellResource cell = _pending[0];
            _pending.RemoveAt(0);
            if (_loaded.ContainsKey(cell.Id))
            {
                _pendingIds.Remove(cell.Id);
                continue;
            }

            if (string.IsNullOrEmpty(cell.ScenePath))
            {
                Fail(cell.Id, "has no scene path");
                continue;
            }

            Error error = ResourceLoader.LoadThreadedRequest(
                cell.ScenePath, "PackedScene", useSubThreads: true, ResourceLoader.CacheMode.Ignore);
            if (error is not Error.Ok and not Error.AlreadyInUse)
            {
                Fail(cell.Id, $"threaded request failed to start ({error})");
                continue;
            }
            _requests[cell.Id] = cell;
        }
    }

    private void PollThreadedRequests()
    {
        foreach (string cellId in new List<string>(_requests.Keys))
        {
            RegionCellResource cell = _requests[cellId];
            ResourceLoader.ThreadLoadStatus status = ResourceLoader.LoadThreadedGetStatus(cell.ScenePath);
            if (status == ResourceLoader.ThreadLoadStatus.InProgress)
            {
                continue;
            }

            _requests.Remove(cellId);
            if (status == ResourceLoader.ThreadLoadStatus.Loaded &&
                ResourceLoader.LoadThreadedGet(cell.ScenePath) is PackedScene scene)
            {
                _ready.Add(new ReadyCell(cell, scene));
                continue;
            }

            Fail(cellId, $"threaded load failed ({status})");
        }
    }

    private void InstantiateReadyCells()
    {
        int budget = _streamingBudget?.MaxCellInstantiationsPerFrame ?? 1;
        while (budget-- > 0 && _ready.Count > 0)
        {
            ReadyCell ready = _ready[0];
            _ready.RemoveAt(0);
            _pendingIds.Remove(ready.Cell.Id);
            if (!_loaded.ContainsKey(ready.Cell.Id))
            {
                Instantiate(ready.Cell, ready.Scene);
            }
        }
    }

    /// <summary>
    /// Adds <see cref="CombatLayers.CameraBlocker"/> to every authored <see cref="StaticBody3D"/> in
    /// a cell.
    ///
    /// ⚠️ <b>Static geometry only, and that is the entire fix.</b> Actors sit on the World layer too
    /// (<c>CharacterEntity</c> defaults to it), so a camera sweeping World cannot tell a wall from a
    /// companion — which is why one walking behind the player used to yank the camera in, a defect a
    /// <c>ponytail:</c> note in <c>PlayerCameraRig</c> recorded and left. Walls block the camera;
    /// people do not. Done on load rather than in 36 scene files per cell so authoring a wall needs
    /// no new step.
    /// </summary>
    public static void MarkCameraBlockers(Node node)
    {
        if (node is StaticBody3D solid && (solid.CollisionLayer & CombatLayers.World) != 0u)
        {
            solid.CollisionLayer |= CombatLayers.CameraBlocker;
        }

        foreach (Node child in node.GetChildren())
        {
            MarkCameraBlockers(child);
        }
    }

    private void Instantiate(RegionCellResource cell, PackedScene scene)
    {
        if (scene.Instantiate() is not Node3D root)
        {
            Log.Warn($"RegionStreamer: cell '{cell.Id}' scene '{cell.ScenePath}' failed to instance.");
            return;
        }
        root.Name = cell.Id;
        root.Position = cell.Center;
        MarkCameraBlockers(root);

        // Order matters and each step reads the one before it: clip the region field to this cell,
        // drop the authored nodes onto the ground (before the terrain collider exists, so the
        // conformer cannot try to lift it), then build terrain, then scatter on top of terrain.
        WorldHeightfield? view = _heightfield != null && cell.Presentation != null
            ? WorldTerrainMeshBuilder.ViewFor(_heightfield, cell.Presentation, cell.Center)
            : _heightfield;
        if (view != null)
        {
            WorldTerrainConform.Apply(root, view, cell.Center);
        }
        WorldCellPresentation.Attach(
            root, _environmentProfile, cell.Presentation, view, cell.Center,
            _terrainJobs?.Take(cell.Id));
        WorldCellWater.Attach(root, cell.Presentation, view, cell.Center, _waterPalette);
        WorldBiomeScatter? scatter = WorldBiomeScatter.Attach(
            root, cell.Presentation, cell.BiomeScatter, view, cell.Center);
        AddChild(root);
        _loaded[cell.Id] = root;
        _performance?.RecordCellLoaded(cell.Id, root, scatter?.InstanceCount ?? 0);
        _visibility?.RecordCellLoaded(cell.Id, scatter);

        Log.Info($"RegionStreamer: loaded cell '{cell.Id}'.");
        EventBus.Instance?.Publish(new RegionCellLoadedEvent(cell.Id, root));
    }

    /// <summary>Retires a cell that cannot be brought in — after <see cref="MaxAttempts"/> tries.
    /// See <see cref="_failed"/> and <see cref="_attempts"/>.</summary>
    private void Fail(string cellId, string reason)
    {
        _pendingIds.Remove(cellId);
        _attempts.TryGetValue(cellId, out int spent);
        spent++;
        _attempts[cellId] = spent;
        if (spent < MaxAttempts)
        {
            // Left out of _failed, so the sweep in _Process re-queues it on the next frame.
            Log.Warn($"RegionStreamer: cell '{cellId}' {reason}; retrying ({spent}/{MaxAttempts}).");
            return;
        }

        _failed.Add(cellId);
        Log.Error($"RegionStreamer: cell '{cellId}' {reason}; giving up after {spent} attempts. " +
                  "The region cannot settle and gameplay must not resume into it.");
    }

    private void ClearLoadStages()
    {
        _pending.Clear();
        _pendingIds.Clear();
        _requests.Clear();
        _ready.Clear();
        _failed.Clear();
        _attempts.Clear();
    }

    private void Unload(string cellId)
    {
        if (!_loaded.TryGetValue(cellId, out Node3D? root))
        {
            return;
        }

        // Announce before freeing so 25D persistence can capture cell state.
        EventBus.Instance?.Publish(new RegionCellUnloadedEvent(cellId));
        _loaded.Remove(cellId);
        _performance?.RecordCellUnloaded(cellId);
        _visibility?.RecordCellUnloaded(cellId);
        root.QueueFree();
        Log.Info($"RegionStreamer: unloaded cell '{cellId}'.");
    }

    private void EnsurePerformanceMonitor()
    {
        if (_performance != null)
        {
            return;
        }

        _performance = new WorldPerformanceMonitor { Name = "WorldPerformanceMonitor" };
        AddChild(_performance);
    }

    /// <summary>
    /// ⚠️ ONE PER STREAMER, NOT ONE PER REGION. The recovery contract is realm-wide: a region with
    /// no water and no pits still wants the node resident, because the player can cross a seam into
    /// one that has both and the safe point must already be remembered when they do.
    /// </summary>
    private void EnsureRecovery()
    {
        if (_recovery != null)
        {
            return;
        }

        _recovery = new WorldRecovery { Name = "WorldRecovery" };
        AddChild(_recovery);
    }

    private void EnsureVisibilityManager()
    {
        if (_visibility != null)
        {
            return;
        }

        _visibility = new WorldVisibilityManager { Name = "WorldVisibilityManager" };
        AddChild(_visibility);
    }

    public WorldPerformanceSnapshot PerformanceSnapshot() =>
        _performance?.LastSnapshot ?? default;

    public bool IsWithinPerformanceBudget() => _performance?.WithinBudget ?? true;

    /// <summary>Visual-capture tools disable timing samples because synchronous PNG writes dominate a frame.</summary>
    public void SetPerformanceSamplingEnabled(bool enabled)
    {
        EnsurePerformanceMonitor();
        _performance!.SamplingEnabled = enabled;
    }
}
