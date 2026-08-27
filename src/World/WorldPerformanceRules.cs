using System.Collections.Generic;

namespace Embervale.World;

/// <summary>Engine-free limits used by authoring validation and runtime telemetry.</summary>
public readonly record struct WorldPerformanceLimits(
    int MaxAuthoredNodesPerCell,
    int MaxResidentAuthoredNodes,
    int MaxResidentRuntimeNodes,
    int MaxScatterInstancesPerCell,
    int MaxResidentScatterInstances,
    int MaxDrawCalls,
    int MaxNodeCount,
    double MaxStaticMemoryMb,
    double MaxFrameMilliseconds);

/// <summary>A single runtime sample after the active region has settled.</summary>
public readonly record struct WorldPerformanceSnapshot(
    int ResidentRuntimeNodes,
    int ResidentScatterInstances,
    int DrawCalls,
    int NodeCount,
    double StaticMemoryMb,
    double FrameMilliseconds);

/// <summary>Pure budget comparisons shared by xUnit tests and the Godot runtime monitor.</summary>
public static class WorldPerformanceRules
{
    public static IReadOnlyList<string> Assess(
        WorldPerformanceLimits limits, WorldPerformanceSnapshot sample)
    {
        var issues = new List<string>();
        AddIfOver(issues, "resident runtime nodes", sample.ResidentRuntimeNodes, limits.MaxResidentRuntimeNodes);
        AddIfOver(issues, "resident scatter instances", sample.ResidentScatterInstances, limits.MaxResidentScatterInstances);
        AddIfOver(issues, "draw calls", sample.DrawCalls, limits.MaxDrawCalls);
        AddIfOver(issues, "node count", sample.NodeCount, limits.MaxNodeCount);
        AddIfOver(issues, "static memory MB", sample.StaticMemoryMb, limits.MaxStaticMemoryMb);
        AddIfOver(issues, "frame ms", sample.FrameMilliseconds, limits.MaxFrameMilliseconds);
        return issues;
    }

    public static bool Valid(WorldPerformanceLimits limits) =>
        limits.MaxAuthoredNodesPerCell > 0 && limits.MaxResidentAuthoredNodes > 0 &&
        limits.MaxResidentRuntimeNodes > 0 &&
        limits.MaxScatterInstancesPerCell >= 0 && limits.MaxResidentScatterInstances >= 0 &&
        limits.MaxDrawCalls > 0 && limits.MaxNodeCount > 0 &&
        limits.MaxStaticMemoryMb > 0d && limits.MaxFrameMilliseconds > 0d;

    private static void AddIfOver(List<string> issues, string label, double actual, double limit)
    {
        if (actual > limit)
        {
            issues.Add($"{label} {actual:0.##} > {limit:0.##}");
        }
    }
}
