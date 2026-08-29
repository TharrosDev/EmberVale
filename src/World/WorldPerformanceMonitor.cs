using System.Collections.Generic;
using Embervale.Core.Diagnostics;
using Godot;

namespace Embervale.World;

/// <summary>
/// Samples the active region against its authored budgets. Transient spikes must persist across
/// several one-second samples before they warn, keeping cell-load compilation noise out of reports.
/// </summary>
public sealed partial class WorldPerformanceMonitor : Node
{
    private readonly Dictionary<string, (int Nodes, int Scatter)> _cells = new();
    private WorldPerformanceBudgetResource? _budget;
    private string _regionId = string.Empty;
    private double _timer;
    private int _consecutiveFailures;
    private int _consecutiveSuccesses;
    private string _lastWarningSignature = string.Empty;

    public WorldPerformanceSnapshot LastSnapshot { get; private set; }
    public bool WithinBudget { get; private set; } = true;
    public bool SamplingEnabled { get; set; } = true;

    public void Configure(string regionId, WorldPerformanceBudgetResource? budget)
    {
        _regionId = regionId;
        _budget = budget;
        _cells.Clear();
        _timer = 0d;
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
        _timer += delta;
        if (_timer < 1d || _budget == null || !SamplingEnabled)
        {
            return;
        }
        _timer = 0d;

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
            Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000d);

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
