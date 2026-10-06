using System;
using System.Collections.Generic;
using Embervale.Combat;
using Embervale.Core;
using Embervale.Core.Diagnostics;
using Embervale.Core.Events;
using Embervale.Core.Services;
using Embervale.Player;
using Godot;

namespace Embervale.World;

/// <summary>
/// Loads prepared region cells through Godot's threaded loader and moves them through predictive
/// Near/Mid/Far/Backdrop fidelity. Position, velocity and a look-ahead point prioritize I/O; tier
/// hysteresis prevents boundary thrash. Presentation, terrain collision, navigation and gameplay
/// activate in bounded stages instead of one frame.
///
/// <see cref="RegionCellLoadedEvent"/>/<see cref="RegionCellUnloadedEvent"/> describe gameplay
/// ownership (Near activation), not merely whether a visual resource is resident. Persistence can
/// therefore snapshot a cell while a distant representation remains visible.
///
/// <b>The two regions no longer overlap in world space (the 2026-08-29 geography overhaul).</b> They
/// used to: Frostfang's roosts sat inside the Ember Crown's arena and northern wilds, so residency
/// was mutually exclusive for a reason that was a coordinate accident rather than a design. Frostfang
/// now occupies its own band east of the Ember Crown. Only one region is streamed at a time anyway —
/// a transition still calls <see cref="UnloadAll"/> — but the ambiguity is gone from the numbers.
///
/// ⚠️ <b>The streamer consumes the region's prepared field and cells.</b> Source generation is an
/// offline concern owned by <c>tools/world_bake.py</c>. Missing prepared data blocks activation.
///
/// Pausable (default process mode), so loading halts while the game is paused. The procedural
/// sandbox is the always-loaded base — only the region's authored <see cref="RegionResource.Cells"/>
/// are managed here.
/// </summary>
public sealed partial class RegionStreamer : Node3D
{
    private readonly record struct ReadyCell(RegionCellResource Cell, PackedScene Scene);

    /// <summary>An in-flight threaded load and the path it was requested under. The path is kept
    /// because polling asks the loader about it every frame, and building it is a string format.</summary>
    private readonly record struct LoadRequest(RegionCellResource Cell, string ScenePath);

    /// <summary>The dummy renderer cannot take concurrent background loads; see
    /// <see cref="StartThreadedRequests"/>. Read once, on first use rather than in a type
    /// initializer: the display server does not change, and the name is a string per call.</summary>
    private static bool? _headless;

    private static bool Headless => _headless ??= DisplayServer.GetName() == "headless";

    private readonly List<RegionCellResource> _cells = new();
    private readonly Dictionary<string, Node3D> _loaded = new();
    private readonly Dictionary<string, WorldCellActivation> _runtime = new();
    private readonly Dictionary<string, WorldStreamingTier> _desired = new();
    private readonly Queue<string> _activationQueue = new();
    private readonly HashSet<string> _activationQueued = new();
    private readonly HashSet<string> _gameplayActive = new();
    private readonly List<RegionCellResource> _pending = new();
    private readonly HashSet<string> _pendingIds = new();
    private readonly Dictionary<string, LoadRequest> _requests = new();
    private readonly List<ReadyCell> _ready = new();
    private readonly List<string> _requestPollIds = new();
    private readonly Comparison<RegionCellResource> _comparePending;
    private bool _pendingSortDirty;
    private Vector3 _priorityFocus;

    /// <summary>Set whenever a desired tier, a load stage or a retry may have left a cell out of
    /// step with its target; <see cref="_Process"/> reconciles and clears it. Every decision tick
    /// sets it too, so a missed trigger costs a quarter of a second rather than a cell.</summary>
    private bool _reconcileDirty;

    /// <summary>A cell was instantiated on the previous frame, so this one belongs to activation.</summary>
    private bool _instantiatedLastFrame;

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
    private WorldPreparedRegionResource? _preparedRegion;

    /// <summary>The first authored water body in the region, used to colour GENERATED water so a
    /// river reads as the same substance as the lake it runs into.</summary>
    private WorldWaterResource? _waterPalette;
    private WorldPerformanceBudgetResource? _streamingBudget;
    private Node3D? _backdrop;
    private WorldRecovery? _recovery;
    private WorldPerformanceMonitor? _performance;
    private WorldVisibilityManager? _visibility;
    private Vector3 _fallbackFocus;
    private Vector3? _toolFocus;
    private string? _requiredCellId;
    private float _decisionTimer;
    private WorldStreamingDebugDraw? _debugDraw;

    public RegionStreamer()
    {
        _comparePending = ComparePending;
    }

    public override void _EnterTree() => WorldQualityScale.Changed += OnQualityChanged;

    public override void _ExitTree() => WorldQualityScale.Changed -= OnQualityChanged;

    /// <summary>The Far radius follows the draw distance; decide again now rather than next tick.</summary>
    private void OnQualityChanged() => _decisionTimer = 0f;

    /// <summary>The region currently being streamed, or empty before the first <see cref="Configure"/>.
    /// The streamer is re-configured at both places the active region changes (world build and each
    /// hard transition), so this is the cheapest honest answer to "where is the player" for systems
    /// that need it — the <see cref="EncounterDirector"/>'s region gate reads it.</summary>
    public string ActiveRegionId { get; private set; } = string.Empty;

    /// <summary>Caches the region's cells; the streamer manages exactly these.</summary>
    public void Configure(RegionResource? region)
    {
        _cells.Clear();
        _desired.Clear();
        _toolFocus = null;
        _requiredCellId = null;
        ActiveRegionId = region?.Id ?? string.Empty;
        _environmentProfile = region?.EnvironmentProfile;
        bool authoringGeneration = WorldGenerationDebug.Mode != WorldGenerationDebugMode.None;
        _preparedRegion = region == null || authoringGeneration
            ? null
            : ResidentResources.Load<WorldPreparedRegionResource>(WorldBakePaths.Region(region.Id));
        bool missingPrepared = region != null &&
                               !authoringGeneration &&
                               (_preparedRegion == null || !_preparedRegion.IsValidFor(region));
        if (missingPrepared)
        {
            _preparedRegion = null;
            Log.Error($"RegionStreamer: prepared production data for '{region!.Id}' is missing or invalid. " +
                      "Gameplay activation is blocked; run python tools/world_bake.py --bake.");
        }
        _heightfield = region == null
            ? null
            : authoringGeneration
                ? WorldTerrainMeshBuilder.HeightfieldFor(region)
                : _preparedRegion?.CreateRuntimeField(region);
        // WARNING: CANCEL BEFORE STARTING, NOT AFTER. Configure is what fast travel and a region
        // change both go through, and the previous region's terrain jobs are still running when it
        // is called. Starting first and cancelling second leaves a window in which a mesh cut from
        // the realm being left could be handed to a cell of the realm being entered.
        _terrainJobs?.Cancel();
        _terrainJobs = region == null || _heightfield == null || _preparedRegion != null
            ? null
            : WorldTerrainJobs.Start(region, _heightfield);
        _waterPalette = FirstAuthoredWater(region);
        WorldGround.Set(_heightfield);
        SkyController.RegionAtmosphere = _environmentProfile;
        WorldWater.Set(region == null ? null : WorldWater.BodiesFor(region, _heightfield));
        EnsureRecovery();
        _streamingBudget = region?.PerformanceBudget;
        ClearLoadStages();
        if (missingPrepared && WorldGenerationDebug.Mode == WorldGenerationDebugMode.None)
        {
            _failed.Add($"{region!.Id}:prepared-data");
        }
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
        if (region == null || missingPrepared)
        {
            return;
        }

        _fallbackFocus = region.SpawnPoint;

        // A package may carry WorldRegionBackdrop directly or a compatibility Node3D container.
        // Instantiate once and own the complete root: the container branch otherwise leaked its
        // first instance and left an empty container after every region transition.
        Node? preparedBackdrop = _preparedRegion?.Backdrop?.Instantiate();
        if (preparedBackdrop is Node3D preparedBackdropRoot)
        {
            _backdrop = preparedBackdropRoot;
            AddChild(_backdrop);
        }
        else
        {
            preparedBackdrop?.Free();
            // Production with missing prepared data remains blocked above. Do not feed a null
            // field into generation or silently generate a replacement for a missing package.
            if (authoringGeneration && _environmentProfile != null && _heightfield != null)
            {
                _backdrop = WorldRegionBackdrop.Create(_environmentProfile, region, _heightfield);
                AddChild(_backdrop);
            }
        }

        foreach (RegionCellResource cell in region.Cells)
        {
            if (cell != null)
            {
                _cells.Add(cell);
                _desired[cell.Id] = WorldStreamingTier.Unloaded;
            }
        }
        RefreshDesiredTiers(force: true);
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
        _runtime.Clear();
        _desired.Clear();
        _activationQueue.Clear();
        _activationQueued.Clear();
        _gameplayActive.Clear();
    }

    /// <summary>True when every currently relevant tier has reached its requested activation state.</summary>
    public bool IsSettled()
    {
        if (HasFailedCells())
        {
            return false;
        }
        foreach (RegionCellResource cell in _cells)
        {
            WorldStreamingTier target = _desired.GetValueOrDefault(cell.Id);
            if (target == WorldStreamingTier.Unloaded)
            {
                continue;
            }
            if (!_runtime.TryGetValue(cell.Id, out WorldCellActivation? runtime) || runtime.Tier != target)
            {
                return false;
            }
        }
        return _pending.Count == 0 && _requests.Count == 0 && _ready.Count == 0 &&
               _activationQueue.Count == 0;
    }

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

    /// <summary>Pins the landing cell to Near until the loading coordinator releases it.</summary>
    public void RequirePosition(Vector3 position)
    {
        _fallbackFocus = position;
        _requiredCellId = CellAt(position)?.Id;
        RefreshDesiredTiers(force: true);
        SetProcess(true);
    }

    public void ReleaseRequiredPosition() => _requiredCellId = null;

    /// <summary>Tool/probe focus. Production focus comes from the live player.</summary>
    public void SetStreamingFocus(Vector3 position)
    {
        _toolFocus = position;
        RefreshDesiredTiers(force: true);
        SetProcess(true);
    }

    public void ClearStreamingFocus() => _toolFocus = null;

    public bool IsPositionReady(Vector3 position, bool requireNavigation = false)
    {
        RegionCellResource? cell = CellAt(position);
        return cell != null && _runtime.TryGetValue(cell.Id, out WorldCellActivation? runtime) &&
               runtime.Tier == WorldStreamingTier.Near && runtime.HasTerrainCollision() &&
               (!requireNavigation || runtime.HasNavigation());
    }

    public int ActiveCellCount() => _gameplayActive.Count;

    public int ResidentCellCount() => _loaded.Count;

    public void SetDebugVisualization(bool enabled)
    {
        _debugDraw ??= new WorldStreamingDebugDraw { Name = "StreamingCells", Visible = false };
        if (_debugDraw.GetParent() == null)
        {
            AddChild(_debugDraw);
        }
        _debugDraw.Visible = enabled;
        if (enabled)
        {
            _debugDraw.Rebuild(_cells, _desired);
        }
    }

    /// <summary>Parents an emergent actor to the active cell that owns its lifetime.</summary>
    public bool TryAddCellOwnedActor(Node3D actor, Vector3 position)
    {
        RegionCellResource? cell = CellAt(position);
        if (cell == null || !_gameplayActive.Contains(cell.Id) ||
            !_runtime.TryGetValue(cell.Id, out WorldCellActivation? runtime))
        {
            return false;
        }
        runtime.AddTransientActor(actor, position);
        return true;
    }

    public override void _Process(double delta)
    {
        _decisionTimer -= (float)delta;
        if (_decisionTimer <= 0f)
        {
            RefreshDesiredTiers(force: false);
            _decisionTimer = _streamingBudget?.VisibilityUpdateInterval ?? 0.25f;
            _reconcileDirty = true;
        }

        // The sweep over every cell used to run every frame: three dictionary lookups per cell to
        // rediscover that nothing had changed since the last tier decision. It now runs when a
        // decision, a failed load or a cleared load stage says something may have.
        if (_reconcileDirty)
        {
            _reconcileDirty = false;
            ReconcileCells();
        }

        if (_pending.Count > 0)
        {
            SortPendingByPriority();
            StartThreadedRequests();
        }
        if (_requests.Count > 0)
        {
            PollThreadedRequests();
        }

        // ⚠️ INSTANTIATION AND ACTIVATION TAKE TURNS WHILE THE PLAYER IS PLAYING. Instantiating a
        // prepared cell is the one unbounded main-thread cost here, and the activation budget used
        // to be spent on top of it in the same frame. Behind a loading screen (the landing cell is
        // pinned) or under a tool focus nobody is watching frames, so both run, as before.
        bool unthrottled = _requiredCellId != null || _toolFocus != null;
        bool instantiated = false;
        if (_ready.Count > 0 &&
            (unthrottled || _activationQueue.Count == 0 || !_instantiatedLastFrame))
        {
            InstantiateReadyCells();
            instantiated = true;
        }
        if (_activationQueue.Count > 0 && (unthrottled || !instantiated))
        {
            AdvanceActivations();
        }
        _instantiatedLastFrame = instantiated;
    }

    private void ReconcileCells()
    {
        foreach (RegionCellResource cell in _cells)
        {
            WorldStreamingTier target = _desired.GetValueOrDefault(cell.Id);
            bool loaded = _loaded.ContainsKey(cell.Id);
            if (target != WorldStreamingTier.Unloaded && !loaded)
            {
                if (!_failed.Contains(cell.Id))
                {
                    Enqueue(cell);
                }
            }
            else if (target == WorldStreamingTier.Unloaded && loaded)
            {
                Unload(cell.Id);
            }
            else if (_runtime.TryGetValue(cell.Id, out WorldCellActivation? runtime) &&
                     runtime.TargetTier != target)
            {
                ScheduleTier(cell.Id, target);
            }
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
            _pendingSortDirty = true;
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
            if (_loaded.ContainsKey(cell.Id) || _desired.GetValueOrDefault(cell.Id) == WorldStreamingTier.Unloaded)
            {
                _pendingIds.Remove(cell.Id);
                continue;
            }

            if (string.IsNullOrEmpty(cell.ScenePath))
            {
                Fail(cell.Id, "has no scene path");
                continue;
            }

            string scenePath = ScenePathFor(cell);
            // ⚠️ CacheMode.Ignore IS LOAD-BEARING, NOT A LEFTOVER. A prepared cell scene embeds its
            // generated terrain mesh, collision shape and MultiMeshes, and the cell's own nodes
            // dispose those in _ExitTree. Under Reuse, a cell that streams out and back in while the
            // old PackedScene is still alive would be handed that same scene from the cache and
            // instantiate it around disposed resources: an empty terrain shell. The price of Ignore
            // is a disk read per stream-in, and it is paid only on a region change, because the
            // Backdrop radius keeps every cell of the active realm resident.
            //
            // The dummy renderer allocates resource RIDs during both loading and instantiation.
            // Concurrent background loads race those allocations in native headless probes.
            // Load the same full scene on the main thread, at most one per frame, in that backend;
            // rendered gameplay keeps the normal asynchronous streaming path and its budgets.
            if (Headless)
            {
                if (ResourceLoader.Load<PackedScene>(scenePath, cacheMode: ResourceLoader.CacheMode.Ignore) is { } headlessScene)
                    _ready.Add(new ReadyCell(cell, headlessScene));
                else
                    Fail(cell.Id, "headless scene load failed");
                return;
            }
            Error error = ResourceLoader.LoadThreadedRequest(
                scenePath, "PackedScene", useSubThreads: true, ResourceLoader.CacheMode.Ignore);
            if (error is not Error.Ok and not Error.AlreadyInUse)
            {
                Fail(cell.Id, $"threaded request failed to start ({error})");
                continue;
            }
            _requests[cell.Id] = new LoadRequest(cell, scenePath);
        }
    }

    private void PollThreadedRequests()
    {
        _requestPollIds.Clear();
        _requestPollIds.AddRange(_requests.Keys);
        foreach (string cellId in _requestPollIds)
        {
            (RegionCellResource cell, string scenePath) = _requests[cellId];
            ResourceLoader.ThreadLoadStatus status = ResourceLoader.LoadThreadedGetStatus(scenePath);
            if (status == ResourceLoader.ThreadLoadStatus.InProgress)
            {
                continue;
            }

            _requests.Remove(cellId);
            if (status == ResourceLoader.ThreadLoadStatus.Loaded &&
                ResourceLoader.LoadThreadedGet(scenePath) is PackedScene scene)
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
            if (!_loaded.ContainsKey(ready.Cell.Id) &&
                _desired.GetValueOrDefault(ready.Cell.Id) != WorldStreamingTier.Unloaded)
            {
                Instantiate(ready.Cell, ready.Scene);
            }
            // Loaded with CacheMode.Ignore, so this was the only reference. Released here rather
            // than whenever the collector reaches the wrapper: the scene state holds a second
            // reference to every embedded resource, including the scatter meshes ShareSources just
            // dropped, and those stay on the GPU for as long as it lives.
            ready.Scene.Dispose();
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

        // By index: GetChildren builds a new array per node, and this visits every node of a cell
        // inside the frame that instantiates it.
        int children = node.GetChildCount();
        for (int i = 0; i < children; i++)
        {
            MarkCameraBlockers(node.GetChild(i));
        }
    }

    private void Instantiate(RegionCellResource cell, PackedScene scene)
    {
        Node instance = scene.Instantiate();
        if (instance is not Node3D root)
        {
            instance.Free();
            Fail(cell.Id, $"scene '{cell.ScenePath}' has no Node3D root");
            return;
        }
        root.Name = cell.Id;
        root.Position = cell.Center;
        MarkCameraBlockers(root);

        // Order matters and each step reads the one before it: clip the region field to this cell,
        // drop the authored nodes onto the ground (before the terrain collider exists, so the
        // conformer cannot try to lift it), then build terrain, then scatter on top of terrain.
        WorldBiomeScatter? scatter;
        if (_preparedRegion == null)
        {
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
            scatter = WorldBiomeScatter.Attach(
                root, cell.Presentation, cell.BiomeScatter, view, cell.Center);
        }
        else
        {
            scatter = root.GetNodeOrNull<WorldBiomeScatter>("BiomeScatter");
            scatter?.ShareSources(cell.BiomeScatter);
        }
        // Audit authored collision before staged activation intentionally clears its layers.
        foreach (string issue in WorldPhysicsContract.Validate(root))
        {
            Log.Error($"World collision contract: {issue}");
        }
        var activation = new WorldCellActivation(root);
        AddChild(root);
        _loaded[cell.Id] = root;
        _runtime[cell.Id] = activation;
        _performance?.RecordCellLoaded(cell.Id, root, scatter?.InstanceCount ?? 0);
        _visibility?.RecordCellLoaded(cell.Id, scatter);

        ScheduleTier(cell.Id, _desired.GetValueOrDefault(cell.Id));
        Log.Info($"RegionStreamer: resident cell '{cell.Id}'.");
    }

    private string ScenePathFor(RegionCellResource cell) =>
        _preparedRegion != null ? WorldBakePaths.Cell(ActiveRegionId, cell.Id) : cell.ScenePath;

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
            _reconcileDirty = true;
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
        _requestPollIds.Clear();
        foreach (ReadyCell ready in _ready)
        {
            ready.Scene.Dispose();
        }
        _ready.Clear();
        _pendingSortDirty = false;
        _reconcileDirty = true;
        _instantiatedLastFrame = false;
        _failed.Clear();
        _attempts.Clear();
    }

    private void Unload(string cellId)
    {
        if (!_loaded.TryGetValue(cellId, out Node3D? root))
        {
            return;
        }

        // Announce before freeing so persistence can capture cell-owned state.
        if (_gameplayActive.Remove(cellId))
        {
            EventBus.Instance?.Publish(new RegionCellUnloadedEvent(cellId));
        }
        _loaded.Remove(cellId);
        _runtime.Remove(cellId);
        _performance?.RecordCellUnloaded(cellId);
        _visibility?.RecordCellUnloaded(cellId);
        root.QueueFree();
        Log.Info($"RegionStreamer: unloaded resident cell '{cellId}'.");
    }

    private void RefreshDesiredTiers(bool force)
    {
        if (_cells.Count == 0)
        {
            return;
        }

        Vector3 position = _toolFocus ?? _fallbackFocus;
        Vector3 velocity = Vector3.Zero;
        if (_toolFocus == null && ServiceLocator.Instance is { } locator &&
            locator.TryGet(out PlayerCharacter player) && IsInstanceValid(player))
        {
            position = player.GlobalPosition;
            velocity = player.Velocity;
        }

        WorldStreamingLimits limits = WorldStreamingPolicy.ScaleForQuality(
            _streamingBudget?.StreamingLimits() ?? new WorldStreamingLimits(
                85f, 170f, 300f, 460f, 30f, 2f, 0.65f),
            WorldQualityScale.DrawDistance);
        _priorityFocus = position;
        _pendingSortDirty = true;
        foreach (RegionCellResource cell in _cells)
        {
            WorldStreamingTier current = _runtime.TryGetValue(cell.Id, out WorldCellActivation? runtime)
                ? runtime.Tier
                : WorldStreamingTier.Unloaded;
            Vector2 halfExtent = cell.Presentation == null
                ? new Vector2(30f, 30f)
                : new Vector2(cell.Presentation.Width * 0.5f, cell.Presentation.Depth * 0.5f);
            WorldStreamingTier target = WorldStreamingPolicy.DesiredTier(
                position, velocity, cell.Center, halfExtent, current, limits,
                cell.Id == _requiredCellId);
            if (force || _desired.GetValueOrDefault(cell.Id) != target)
            {
                _desired[cell.Id] = target;
                _reconcileDirty = true;
                if (runtime != null)
                {
                    ScheduleTier(cell.Id, target);
                }
            }
        }

        SortPendingByPriority();
        if (_debugDraw?.Visible == true)
        {
            _debugDraw.Rebuild(_cells, _desired);
        }
    }

    private RegionCellResource? CellAt(Vector3 position)
    {
        RegionCellResource? nearest = null;
        float nearestDistance = float.MaxValue;
        foreach (RegionCellResource cell in _cells)
        {
            Vector2 half = cell.Presentation == null
                ? new Vector2(30f, 30f)
                : new Vector2(cell.Presentation.Width * 0.5f, cell.Presentation.Depth * 0.5f);
            float distance = WorldStreamingPolicy.DistanceToFootprint(position, cell.Center, half);
            if (distance <= 0.01f)
            {
                return cell;
            }
            if (distance < nearestDistance)
            {
                nearest = cell;
                nearestDistance = distance;
            }
        }
        return nearest;
    }

    private void SortPendingByPriority()
    {
        if (!_pendingSortDirty)
        {
            return;
        }
        _pendingSortDirty = false;
        if (_pending.Count > 1)
        {
            _pending.Sort(_comparePending);
        }
    }

    private int ComparePending(RegionCellResource a, RegionCellResource b)
    {
        if (a.Id == _requiredCellId)
        {
            return b.Id == _requiredCellId ? 0 : -1;
        }
        if (b.Id == _requiredCellId)
        {
            return 1;
        }
        int tier = ((int)_desired.GetValueOrDefault(b.Id)).CompareTo(
            (int)_desired.GetValueOrDefault(a.Id));
        return tier != 0
            ? tier
            : a.Center.DistanceSquaredTo(_priorityFocus).CompareTo(b.Center.DistanceSquaredTo(_priorityFocus));
    }

    private void ScheduleTier(string cellId, WorldStreamingTier target)
    {
        if (!_runtime.TryGetValue(cellId, out WorldCellActivation? runtime))
        {
            return;
        }
        if (_gameplayActive.Contains(cellId) && target != WorldStreamingTier.Near)
        {
            _gameplayActive.Remove(cellId);
            EventBus.Instance?.Publish(new RegionCellUnloadedEvent(cellId));
            runtime.RetireTransientActors();
        }
        runtime.Retarget(target);
        if (_activationQueued.Add(cellId))
        {
            _activationQueue.Enqueue(cellId);
        }
    }

    private void AdvanceActivations()
    {
        // The deadline goes into the cell: one cell's presentation pass is hundreds of nodes, and
        // checking the clock only between cells let a single pass run several budgets long.
        ulong deadline = Time.GetTicksUsec() +
                         (ulong)((_streamingBudget?.ActivationBudgetMilliseconds ?? 2f) * 1000f);
        while (_activationQueue.Count > 0 && Time.GetTicksUsec() < deadline)
        {
            string cellId = _activationQueue.Dequeue();
            _activationQueued.Remove(cellId);
            if (!_runtime.TryGetValue(cellId, out WorldCellActivation? runtime))
            {
                continue;
            }
            bool complete = runtime.Advance(deadline);
            if (!complete)
            {
                _activationQueued.Add(cellId);
                _activationQueue.Enqueue(cellId);
                continue;
            }
            if (runtime.Tier == WorldStreamingTier.Near && _gameplayActive.Add(cellId))
            {
                EventBus.Instance?.Publish(new RegionCellLoadedEvent(cellId, runtime.Root));
            }
        }
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
