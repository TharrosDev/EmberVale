using Godot;

namespace Embervale.World;

/// <summary>Authored shipping budgets for one fully-resident active region.</summary>
[GlobalClass]
public partial class WorldPerformanceBudgetResource : Resource
{
    [Export(PropertyHint.Range, "1,5000,1")] public int MaxAuthoredNodesPerCell { get; set; } = 800;
    [Export(PropertyHint.Range, "1,25000,1")] public int MaxResidentAuthoredNodes { get; set; } = 6000;
    [Export(PropertyHint.Range, "1,50000,1")] public int MaxResidentRuntimeNodes { get; set; } = 10000;
    [Export(PropertyHint.Range, "0,2048,1")] public int MaxScatterInstancesPerCell { get; set; } = 256;
    [Export(PropertyHint.Range, "0,10000,1")] public int MaxResidentScatterInstances { get; set; } = 1200;
    [Export(PropertyHint.Range, "25,20000,1")] public int MaxTerrainVerticesPerCell { get; set; } = 3000;
    [Export(PropertyHint.Range, "25,100000,1")] public int MaxResidentTerrainVertices { get; set; } = 30000;
    [Export(PropertyHint.Range, "1,10000,1")] public int MaxDrawCalls { get; set; } = 1800;
    [Export(PropertyHint.Range, "1,50000,1")] public int MaxNodeCount { get; set; } = 14000;
    [Export(PropertyHint.Range, "64,8192,16")] public float MaxStaticMemoryMb { get; set; } = 2048f;
    [Export(PropertyHint.Range, "8,100,0.5")] public float MaxFrameMilliseconds { get; set; } = 25f;
    [Export(PropertyHint.Range, "1,30,1")] public int ConsecutiveSamplesBeforeWarning { get; set; } = 5;

    [ExportGroup("Visibility and loading")]
    [Export(PropertyHint.Range, "50,1000,10")] public float BiomeCullDistance { get; set; } = 320f;
    [Export(PropertyHint.Range, "0.05,2,0.05")] public float VisibilityUpdateInterval { get; set; } = 0.25f;
    [Export(PropertyHint.Range, "1,8,1")] public int MaxConcurrentLoadRequests { get; set; } = 2;
    [Export(PropertyHint.Range, "1,4,1")] public int MaxCellInstantiationsPerFrame { get; set; } = 1;

    public WorldPerformanceLimits Limits() => new(
        MaxAuthoredNodesPerCell, MaxResidentAuthoredNodes, MaxResidentRuntimeNodes,
        MaxScatterInstancesPerCell, MaxResidentScatterInstances,
        MaxDrawCalls, MaxNodeCount, MaxStaticMemoryMb, MaxFrameMilliseconds);
}
