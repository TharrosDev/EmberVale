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
    [Export(PropertyHint.Range, "8,100,0.01")] public float MaxFrameMilliseconds { get; set; } = 16.67f;
    [Export(PropertyHint.Range, "1,30,1")] public int ConsecutiveSamplesBeforeWarning { get; set; } = 5;

    [ExportGroup("60 FPS subsystem targets")]
    [Export(PropertyHint.Range, "0.1,16.67,0.05")] public float MainThreadP95Milliseconds { get; set; } = 8f;
    [Export(PropertyHint.Range, "0.1,16.67,0.05")] public float RenderCpuP95Milliseconds { get; set; } = 4f;
    [Export(PropertyHint.Range, "0.1,16.67,0.05")] public float GpuP95Milliseconds { get; set; } = 14f;
    [Export(PropertyHint.Range, "0.1,8,0.05")] public float PhysicsP95Milliseconds { get; set; } = 2f;
    [Export(PropertyHint.Range, "0.1,8,0.05")] public float AiP95Milliseconds { get; set; } = 1.5f;
    [Export(PropertyHint.Range, "0.1,8,0.05")] public float NavigationP95Milliseconds { get; set; } = 1f;
    [Export(PropertyHint.Range, "0.1,8,0.05")] public float StreamingP95Milliseconds { get; set; } = 1.5f;
    [Export(PropertyHint.Range, "0.1,8,0.05")] public float AnimationP95Milliseconds { get; set; } = 1.5f;
    [Export(PropertyHint.Range, "0.1,8,0.05")] public float ActivationBudgetMilliseconds { get; set; } = 2f;

    [ExportGroup("Streaming tiers")]
    [Export(PropertyHint.Range, "20,500,5")] public float NearDistance { get; set; } = 85f;
    [Export(PropertyHint.Range, "40,700,5")] public float MidDistance { get; set; } = 170f;
    [Export(PropertyHint.Range, "80,1000,5")] public float FarDistance { get; set; } = 300f;
    [Export(PropertyHint.Range, "120,1500,5")] public float BackdropDistance { get; set; } = 460f;
    [Export(PropertyHint.Range, "0,100,1")] public float StreamingHysteresis { get; set; } = 30f;
    [Export(PropertyHint.Range, "0,5,0.1")] public float PredictionSeconds { get; set; } = 2f;
    [Export(PropertyHint.Range, "0.25,1,0.05")] public float PredictionDistanceWeight { get; set; } = 0.65f;

    [ExportGroup("Visibility and loading")]
    [Export(PropertyHint.Range, "50,1000,10")] public float BiomeCullDistance { get; set; } = 320f;
    [Export(PropertyHint.Range, "0.05,2,0.05")] public float VisibilityUpdateInterval { get; set; } = 0.25f;
    [Export(PropertyHint.Range, "1,8,1")] public int MaxConcurrentLoadRequests { get; set; } = 2;
    [Export(PropertyHint.Range, "1,4,1")] public int MaxCellInstantiationsPerFrame { get; set; } = 1;

    public WorldPerformanceLimits Limits() => new(
        MaxAuthoredNodesPerCell, MaxResidentAuthoredNodes, MaxResidentRuntimeNodes,
        MaxScatterInstancesPerCell, MaxResidentScatterInstances,
        MaxDrawCalls, MaxNodeCount, MaxStaticMemoryMb, MaxFrameMilliseconds);

    public WorldStreamingLimits StreamingLimits() => new(
        NearDistance, MidDistance, FarDistance, BackdropDistance, StreamingHysteresis,
        PredictionSeconds, PredictionDistanceWeight);
}
