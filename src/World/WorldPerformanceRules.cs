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

/// <summary>
/// A single runtime sample after the active region has settled.
///
/// ⚠️ <b><paramref name="WorstFrameMilliseconds"/> IS THE ONE THAT CATCHES A HITCH.</b>
/// <paramref name="FrameMilliseconds"/> is one instantaneous reading taken once a second, so it sees
/// about 1.7% of the frames in a 60 Hz session — a 300 ms stall that happens between two readings
/// leaves no trace at all, which is exactly the class of problem a player notices and this monitor
/// was supposed to find. The worst frame observed since the previous sample sees all of them.
/// Defaulted so a caller that has no per-frame history (a test, a probe) can still build a snapshot.
/// </summary>
public readonly record struct WorldPerformanceSnapshot(
    int ResidentRuntimeNodes,
    int ResidentScatterInstances,
    int DrawCalls,
    int NodeCount,
    double StaticMemoryMb,
    double FrameMilliseconds,
    double WorstFrameMilliseconds = 0d,
    double P50FrameMilliseconds = 0d,
    double P95FrameMilliseconds = 0d,
    double P99FrameMilliseconds = 0d);

public readonly record struct WorldFrameDistribution(
    double Average, double P50, double P95, double P99, double Worst);

/// <summary>Pure budget comparisons shared by xUnit tests and the Godot runtime monitor.</summary>
public static class WorldPerformanceRules
{
    public static WorldFrameDistribution Distribution(IReadOnlyList<double> samples)
    {
        if (samples.Count == 0)
        {
            return default;
        }
        var ordered = new double[samples.Count];
        double sum = 0d;
        for (int i = 0; i < samples.Count; i++)
        {
            ordered[i] = samples[i];
            sum += samples[i];
        }
        System.Array.Sort(ordered);
        return new WorldFrameDistribution(
            sum / ordered.Length,
            Percentile(ordered, 0.50d),
            Percentile(ordered, 0.95d),
            Percentile(ordered, 0.99d),
            ordered[^1]);
    }

    /// <summary>
    /// Stable identity for the set of exceeded dimensions. Runtime values deliberately do not
    /// participate: frame time and memory fluctuate every sample and would otherwise turn one
    /// sustained problem into a new log warning every second.
    /// </summary>
    public static string FailureSignature(
        WorldPerformanceLimits limits, WorldPerformanceSnapshot sample)
    {
        var dimensions = new List<string>();
        AddDimensionIfOver(
            dimensions, "resident-runtime-nodes",
            sample.ResidentRuntimeNodes, limits.MaxResidentRuntimeNodes);
        AddDimensionIfOver(
            dimensions, "resident-scatter-instances",
            sample.ResidentScatterInstances, limits.MaxResidentScatterInstances);
        AddDimensionIfOver(dimensions, "draw-calls", sample.DrawCalls, limits.MaxDrawCalls);
        AddDimensionIfOver(dimensions, "node-count", sample.NodeCount, limits.MaxNodeCount);
        AddDimensionIfOver(dimensions, "static-memory", sample.StaticMemoryMb, limits.MaxStaticMemoryMb);
        AddDimensionIfOver(dimensions, "frame-time", sample.FrameMilliseconds, limits.MaxFrameMilliseconds);
        AddDimensionIfOver(
            dimensions, "worst-frame-time", sample.WorstFrameMilliseconds, limits.MaxFrameMilliseconds);
        return string.Join(",", dimensions);
    }

    public static int ThreadedLoadConcurrency(int authoredConcurrency, double currentMemoryMb, double limitMb) =>
        currentMemoryMb > limitMb ? 1 : System.Math.Max(1, authoredConcurrency);

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
        AddIfOver(issues, "worst frame ms", sample.WorstFrameMilliseconds, limits.MaxFrameMilliseconds);
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

    private static void AddDimensionIfOver(
        List<string> dimensions, string dimension, double actual, double limit)
    {
        if (actual > limit)
        {
            dimensions.Add(dimension);
        }
    }

    private static double Percentile(double[] ordered, double percentile)
    {
        int index = (int)System.Math.Ceiling(percentile * ordered.Length) - 1;
        return ordered[System.Math.Clamp(index, 0, ordered.Length - 1)];
    }
}
