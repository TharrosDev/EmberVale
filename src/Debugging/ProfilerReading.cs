using System;
using Embervale.Core.Pooling;
using Embervale.Core.Services;
using Embervale.World;
using Godot;

namespace Embervale.Debugging;

/// <summary>
/// One reading of every counter the profiler shows: the engine's <see cref="Performance"/> monitors,
/// the managed heap, and the active region's streaming state. The F4 overlay formats it and the
/// <c>--perf-report</c> session report writes it as JSON, so the two cannot disagree.
///
/// <para>Frame times and garbage-collector rates are not here: they are measured over a window,
/// which the caller owns (<see cref="FrameStats"/>).</para>
/// </summary>
public readonly record struct ProfilerReading(
    double Fps,
    double ScriptMs,
    double PhysicsMs,
    int DrawCalls,
    long Primitives,
    int Nodes,
    int Orphans,
    int OrphansLeaked,
    double StaticMb,
    double VideoMb,
    double TextureMb,
    double BufferMb,
    double ManagedMb,
    bool HasWorld,
    string RegionId,
    WorldPerformanceSnapshot World,
    int ActiveCells,
    int ResidentCells,
    bool WithinBudget)
{
    private const double BytesPerMb = 1024d * 1024d;

    /// <summary>Reads everything now. About twenty monitor reads; call it on a refresh, not per frame.</summary>
    public static ProfilerReading Read()
    {
        int orphans = (int)Get(Performance.Monitor.ObjectOrphanNodeCount);
        bool hasWorld = false;
        string region = string.Empty;
        WorldPerformanceSnapshot world = default;
        int active = 0;
        int resident = 0;
        bool withinBudget = true;
        if (ServiceLocator.Instance is { } locator && locator.TryGet(out RegionStreamer streamer))
        {
            hasWorld = true;
            region = streamer.ActiveRegionId;
            world = streamer.PerformanceSnapshot();
            active = streamer.ActiveCellCount();
            resident = streamer.ResidentCellCount();
            withinBudget = streamer.IsWithinPerformanceBudget();
        }

        return new ProfilerReading(
            Engine.GetFramesPerSecond(),
            Get(Performance.Monitor.TimeProcess) * 1000d,
            Get(Performance.Monitor.TimePhysicsProcess) * 1000d,
            (int)Get(Performance.Monitor.RenderTotalDrawCallsInFrame),
            (long)Get(Performance.Monitor.RenderTotalPrimitivesInFrame),
            (int)Get(Performance.Monitor.ObjectNodeCount),
            orphans,
            // Nodes parked in a pool are detached on purpose; only the excess is a leak.
            Math.Max(0, orphans - NodePoolCensus.Parked),
            Get(Performance.Monitor.MemoryStatic) / BytesPerMb,
            Get(Performance.Monitor.RenderVideoMemUsed) / BytesPerMb,
            Get(Performance.Monitor.RenderTextureMemUsed) / BytesPerMb,
            Get(Performance.Monitor.RenderBufferMemUsed) / BytesPerMb,
            GC.GetTotalMemory(false) / BytesPerMb,
            hasWorld,
            region,
            world,
            active,
            resident,
            withinBudget);
    }

    private static double Get(Performance.Monitor monitor) => Performance.GetMonitor(monitor);
}
