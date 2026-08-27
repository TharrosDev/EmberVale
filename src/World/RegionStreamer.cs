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
/// ⚠️ <b>Both regions cannot be resident at once</b> and this is not the code's fault: Frostfang's
/// <c>dragon_roost</c> (25, 0, -20) and <c>ancient_aerie</c> (25, 0, -110) share coordinate space
/// with the Ember Crown's <c>arena</c> (55, 0, -10) and <c>wilds_north</c> (0, 0, -65). Two regions
/// loaded together would be two worlds inside each other. Whole-realm residency needs the world
/// re-laid-out first, which is a Phase 44 (region design) decision and not this node's.
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
    private WorldEnvironmentProfileResource? _environmentProfile;
    private WorldPerformanceBudgetResource? _streamingBudget;
    private WorldRegionBackdrop? _backdrop;
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
        _streamingBudget = region?.PerformanceBudget;
        ClearLoadStages();
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
            _backdrop = WorldRegionBackdrop.Create(_environmentProfile);
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

        _pending.Clear();
        _pendingIds.Clear();
        _requests.Clear();
        _ready.Clear();
        WorldBiomeScatter.ClearSourceCache();
    }

    /// <summary>True when nothing is queued and every one of the region's cells is loaded — the world
    /// has finished coming in. The bootstrap gates the post-transition loading screen on this (Phase
    /// 25.5B) instead of a fixed delay, so the screen holds exactly as long as the cells need and no
    /// longer. Since 38M2 that means <em>all</em> of them rather than the ones near the landing point,
    /// which is a few extra frames and the whole point of the change.</summary>
    public bool IsSettled() => _pending.Count == 0 && _requests.Count == 0 && _ready.Count == 0 &&
                               _loaded.Count == _cells.Count;

    public override void _Process(double delta)
    {
        // No player lookup and no distance test any more: every cell of the active region belongs in
        // the tree, so the only question left is whether it is there yet.
        foreach (RegionCellResource cell in _cells)
        {
            if (!_loaded.ContainsKey(cell.Id))
            {
                Enqueue(cell);
            }
        }

        StartThreadedRequests();
        PollThreadedRequests();
        InstantiateReadyCells();
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
                _pendingIds.Remove(cell.Id);
                Log.Warn($"RegionStreamer: cell '{cell.Id}' has no scene path.");
                continue;
            }

            Error error = ResourceLoader.LoadThreadedRequest(
                cell.ScenePath, "PackedScene", useSubThreads: true, ResourceLoader.CacheMode.Ignore);
            if (error is not Error.Ok and not Error.AlreadyInUse)
            {
                _pendingIds.Remove(cell.Id);
                Log.Warn($"RegionStreamer: threaded request for cell '{cell.Id}' failed to start ({error}).");
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

            _pendingIds.Remove(cellId);
            Log.Warn($"RegionStreamer: threaded load for cell '{cell.Id}' failed ({status}).");
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

    private void Instantiate(RegionCellResource cell, PackedScene scene)
    {
        if (scene.Instantiate() is not Node3D root)
        {
            Log.Warn($"RegionStreamer: cell '{cell.Id}' scene '{cell.ScenePath}' failed to instance.");
            return;
        }
        root.Name = cell.Id;
        root.Position = cell.Center;
        WorldCellPresentation.Attach(root, _environmentProfile, cell.Presentation);
        WorldBiomeScatter? scatter = WorldBiomeScatter.Attach(root, cell.Presentation, cell.BiomeScatter);
        AddChild(root);
        _loaded[cell.Id] = root;
        _performance?.RecordCellLoaded(cell.Id, root, scatter?.InstanceCount ?? 0);
        _visibility?.RecordCellLoaded(cell.Id, scatter);

        Log.Info($"RegionStreamer: loaded cell '{cell.Id}'.");
        EventBus.Instance?.Publish(new RegionCellLoadedEvent(cell.Id, root));
    }

    private void ClearLoadStages()
    {
        _pending.Clear();
        _pendingIds.Clear();
        _requests.Clear();
        _ready.Clear();
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
