using System.Collections.Generic;
using Embervale.Core.Diagnostics;
using Godot;

namespace Embervale.World;

/// <summary>
/// Samples the active region against its authored budgets. Transient spikes must persist across
/// several one-second samples before they warn, keeping cell-load compilation noise out of reports.
///
/// <para>⚠️ <b>It measures only while something is reading it.</b> The snapshot has one reader, the F4
/// profiler overlay, which registers itself with <see cref="AddObserver"/> while it is shown. With
/// no observer the node does not process at all, so a normal play session pays nothing for a
/// measurement nobody is looking at. The frame window is a fixed ring: a steady-state sample
/// allocates nothing.</para>
/// </summary>
public sealed partial class WorldPerformanceMonitor : Node
{
    private readonly Dictionary<string, (int Nodes, int Scatter)> _cells = new();
    private WorldPerformanceBudgetResource? _budget;
    private string _regionId = string.Empty;
    private double _timer;

    /// <summary>Frames kept per one-second window. A second holds more than this only above
    /// 512 fps, where the oldest frames of the window are overwritten.</summary>
    private const int WindowCapacity = 512;

    // The window as a ring, and the scratch it is sorted in: both allocated once.
    private readonly double[] _frameWindow = new double[WindowCapacity];
    private readonly double[] _sorted = new double[WindowCapacity];
    private int _frameCount;
    private int _frameNext;

    private static int _observers;
    private static WorldPerformanceMonitor? _live;

    /// <summary>Longest frame seen since the last sample. See <see cref="_Process"/>.</summary>
    private double _worstFrameMs;
    private int _consecutiveFailures;
    private int _consecutiveSuccesses;
    private string _lastWarningSignature = string.Empty;

    public WorldPerformanceSnapshot LastSnapshot { get; private set; }
    public bool WithinBudget { get; private set; } = true;
    public bool SamplingEnabled { get; set; } = true;

    /// <summary>Registers a reader of <see cref="LastSnapshot"/> (the profiler overlay while it is
    /// shown). The monitor processes only while at least one is registered.</summary>
    public static void AddObserver()
    {
        _observers++;
        Refresh();
    }

    /// <summary>Releases a registration made with <see cref="AddObserver"/>.</summary>
    public static void RemoveObserver()
    {
        _observers = System.Math.Max(0, _observers - 1);
        Refresh();
    }

    private static void Refresh()
    {
        if (_live == null || !IsInstanceValid(_live))
        {
            return;
        }

        bool observed = _observers > 0;
        if (observed && !_live.IsProcessing())
        {
            // Waking up: whatever the window held is from before the gap, not from this second.
            _live.ClearWindow();
            _live._worstFrameMs = 0d;
            _live._timer = 0d;
        }

        _live.SetProcess(observed);
    }

    public override void _EnterTree()
    {
        _live = this;
    }

    public override void _Ready()
    {
        SetProcess(_observers > 0);
    }

    public override void _ExitTree()
    {
        if (ReferenceEquals(_live, this))
        {
            _live = null;
        }
    }

    public void Configure(string regionId, WorldPerformanceBudgetResource? budget)
    {
        _regionId = regionId;
        _budget = budget;
        _cells.Clear();
        _timer = 0d;
        _worstFrameMs = 0d;
        ClearWindow();
        _consecutiveFailures = 0;
        _consecutiveSuccesses = 0;
        _lastWarningSignature = string.Empty;
        WithinBudget = true;
    }

    public void RecordCellLoaded(string cellId, Node root, int scatterInstances)
    {
        _cells[cellId] = (CountNodes(root), scatterInstances);
    }

    public void RecordCellUnloaded(string cellId) => _cells.Remove(cellId);

    public override void _Process(double delta)
    {
        if (!SamplingEnabled || _budget == null)
        {
            // Probes deliberately disable sampling while doing synchronous work. Retaining every
            // frame during that interval turned the monitor itself into an unbounded soak leak.
            ClearWindow();
            _worstFrameMs = 0d;
            _timer = 0d;
            return;
        }

        // ⚠️ EVERY FRAME IS MEASURED, EVEN THOUGH ONLY ONE IN SIXTY IS REPORTED. The sample below
        // reads Performance.Monitor.TimeProcess, which is the cost of the frame it happens to run
        // on — so the monitor saw about one frame in sixty and a hitch that landed on any of the
        // other fifty-nine was invisible. This carries the worst frame of the window into the
        // snapshot, which is the number a player actually feels.
        double frameMs = delta * 1000d;
        if (frameMs > _worstFrameMs)
        {
            _worstFrameMs = frameMs;
        }
        _frameWindow[_frameNext] = frameMs;
        _frameNext = (_frameNext + 1) % WindowCapacity;
        if (_frameCount < WindowCapacity)
        {
            _frameCount++;
        }

        _timer += delta;
        if (_timer < 1d)
        {
            return;
        }
        _timer = 0d;
        double worstFrameMs = _worstFrameMs;
        _worstFrameMs = 0d;
        WorldFrameDistribution distribution = WindowDistribution();
        ClearWindow();

        int authoredNodes = 0;
        int scatterInstances = 0;
        foreach ((int nodes, int scatter) in _cells.Values)
        {
            authoredNodes += nodes;
            scatterInstances += scatter;
        }

        LastSnapshot = new WorldPerformanceSnapshot(
            authoredNodes,
            scatterInstances,
            (int)Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame),
            (int)Performance.GetMonitor(Performance.Monitor.ObjectNodeCount),
            Performance.GetMonitor(Performance.Monitor.MemoryStatic) / (1024d * 1024d),
            Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000d,
            worstFrameMs,
            distribution.P50,
            distribution.P95,
            distribution.P99);

        IReadOnlyList<string> issues = WorldPerformanceRules.Assess(_budget.Limits(), LastSnapshot);
        WithinBudget = issues.Count == 0;
        if (WithinBudget)
        {
            _consecutiveFailures = 0;
            _consecutiveSuccesses++;
            // One lucky frame inside an otherwise sustained overage is not a recovery. Only let
            // the same incident warn again after the region has remained healthy for as long as a
            // new failure must persist before its first warning.
            if (_consecutiveSuccesses >= _budget.ConsecutiveSamplesBeforeWarning)
            {
                _lastWarningSignature = string.Empty;
            }
            return;
        }

        _consecutiveSuccesses = 0;
        _consecutiveFailures++;
        if (_consecutiveFailures < _budget.ConsecutiveSamplesBeforeWarning)
        {
            return;
        }

        string warning = string.Join(", ", issues);
        string signature = WorldPerformanceRules.FailureSignature(_budget.Limits(), LastSnapshot);
        if (signature != _lastWarningSignature)
        {
            _lastWarningSignature = signature;
            Log.Warn($"World performance budget exceeded in '{_regionId}': {warning}.");
        }
    }

    private void ClearWindow()
    {
        _frameCount = 0;
        _frameNext = 0;
    }

    /// <summary>
    /// The window's distribution, sorted in the preallocated scratch. The same arithmetic as
    /// <see cref="WorldPerformanceRules.Distribution"/> (nearest-rank percentiles), which stays the
    /// tested statement of the rule; this is that rule without the array it allocates per call.
    /// </summary>
    private WorldFrameDistribution WindowDistribution()
    {
        int count = _frameCount;
        if (count == 0)
        {
            return default;
        }

        double sum = 0d;
        for (int i = 0; i < count; i++)
        {
            _sorted[i] = _frameWindow[i];
            sum += _frameWindow[i];
        }

        System.Array.Sort(_sorted, 0, count);
        return new WorldFrameDistribution(
            sum / count,
            _sorted[Rank(0.50d, count)],
            _sorted[Rank(0.95d, count)],
            _sorted[Rank(0.99d, count)],
            _sorted[count - 1]);
    }

    private static int Rank(double percentile, int count) =>
        System.Math.Clamp((int)System.Math.Ceiling(percentile * count) - 1, 0, count - 1);

    private static int CountNodes(Node root)
    {
        int count = 1;
        foreach (Node child in root.GetChildren())
        {
            count += CountNodes(child);
        }
        return count;
    }
}
