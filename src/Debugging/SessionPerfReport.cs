using System;
using System.Collections.Generic;
using System.Globalization;
using Embervale.Bootstrap;
using Embervale.Core;
using Embervale.Core.Diagnostics;
using Embervale.Core.Events;
using Embervale.Core.Services;
using Embervale.Player;
using Embervale.Quests;
using Embervale.World;
using Godot;

namespace Embervale.Debugging;

/// <summary>
/// The self-checking session verdict: <c>-- --play --perf-report --quit-after=20</c> (or
/// <c>--new-game</c> for <c>--play</c>). It samples every frame of a live session, then prints ONE
/// line, <c>EMBERVALE_RESULT {json}</c> with gate <c>perf-report</c>, and quits: exit 0 when the run
/// was clean, 1 when an invariant broke, an error was logged, the integrity check found something
/// or no frame was sampled. <c>--report=&lt;path&gt;</c> writes the same JSON to a file.
///
/// <para><b>Length.</b> <c>--perf-report=&lt;seconds&gt;</c> samples that long after the warm-up and
/// quits on its own. A bare <c>--perf-report</c> takes its length from <c>--quit-after</c> and ends a
/// quarter second before it, so the report's exit code is the one the process returns; with neither
/// it samples 20 s. <c>--warmup=&lt;seconds&gt;</c> (default 3) is skipped first: streaming, shader
/// compilation and the first frames of a session are not what a run is asked about.</para>
///
/// <para><b>Facts.</b> Frame time p50/p95/p99/max/avg and hitch counts over 33/50/100 ms with the
/// worst five and what else happened on that frame (a cell finished loading, a gen-2 collection);
/// script and physics time, draw calls and primitives as medians over the window; node and orphan
/// counts; static, video, texture, buffer and managed memory; allocation rate and collections;
/// region, cells, seconds over the region's budget; whether the player stood in a safe zone and how
/// many enemies were alive (a clean run in a safe zone with no enemies says nothing about combat);
/// invariant violations with their messages; and the log's error and warning counts.</para>
///
/// <para>Frame times are wall-clock tick deltas, so they are comparable between runs on one machine
/// and are not the engine's smoothed FPS. Run it with a window: under <c>--headless</c> nothing is
/// drawn and the frame time is the script cost only (fact <c>headless</c> says which).</para>
/// </summary>
public sealed partial class SessionPerfReport : Node
{
    public const string Flag = "--perf-report";
    public const string WarmupArgument = "--warmup";

    private const double DefaultSeconds = 20d;
    private const double DefaultWarmup = 3d;

    /// <summary>How long before <c>--quit-after</c> the report ends the run itself.</summary>
    private const double QuitMargin = 0.25d;

    private const int MonitorEveryFrames = 10;
    private const double IntegritySeconds = 5d;

    private readonly FrameStats _frames = new();
    private readonly List<double> _scriptMs = new();
    private readonly List<double> _physicsMs = new();
    private readonly List<double> _drawCalls = new();
    private readonly List<double> _primitives = new();

    private double _warmup = DefaultWarmup;
    private double _total;
    private ulong _startTick;
    private ulong _lastTick;
    private ulong _sampleStartTick;
    private bool _sampling;
    private bool _finished;
    private int _frameIndex;

    private long _allocatedAtStart;
    private int _gen0AtStart;
    private int _gen1AtStart;
    private int _gen2AtStart;
    private int _gen2Last;
    private bool _cellLoaded;

    private double _sinceSecond;
    private double _sinceIntegrity;
    private int _budgetSeconds;
    private int _overBudgetSeconds;
    private double _worstSecondMs;

    public override void _Ready()
    {
        // Frame time is frame time whether or not a menu has the tree paused.
        ProcessMode = ProcessModeEnum.Always;

        double own = HeadlessArgs.User.Float(Flag, -1f);
        double quitAfter = HeadlessArgs.User.Float(GameShellController.QuitAfterArgument, -1f);
        _warmup = Math.Max(0d, HeadlessArgs.User.Float(WarmupArgument, (float)DefaultWarmup));
        _total = Plan(own, quitAfter, ref _warmup);

        _startTick = Time.GetTicksUsec();
        EventBus.Instance?.Subscribe<RegionCellLoadedEvent>(OnCellLoaded);

        // A tree timer with the shell's flags, created in the same frame as the shell's own
        // --quit-after timer (the shell attaches this node from its _Ready). Both count the same
        // frame deltas, so this one, a quarter second shorter, always fires first.
        SceneTreeTimer timer = GetTree().CreateTimer(
            _total, processAlways: true, processInPhysics: false, ignoreTimeScale: true);
        timer.Timeout += () => Finish(quit: true);
        Log.Info($"{Flag}: sampling {_total - _warmup:0.##} s after a {_warmup:0.##} s warm-up.");
    }

    /// <summary>
    /// Seconds from attach to the report, and the warm-up clamped to fit inside them.
    /// <paramref name="own"/> is <c>--perf-report=&lt;s&gt;</c> and <paramref name="quitAfter"/> is
    /// <c>--quit-after=&lt;s&gt;</c>; a negative value means the flag gave no number.
    /// </summary>
    public static double Plan(double own, double quitAfter, ref double warmup)
    {
        double total = own > 0d ? warmup + own : quitAfter > 0d ? quitAfter - QuitMargin : warmup + DefaultSeconds;
        if (quitAfter > 0d)
        {
            total = Math.Min(total, quitAfter - QuitMargin);
        }

        total = Math.Max(total, 0.05d);

        // A warm-up that would swallow the run leaves it half, so something is always sampled.
        warmup = Math.Min(warmup, total * 0.5d);
        return total;
    }

    public override void _ExitTree()
    {
        EventBus.Instance?.Unsubscribe<RegionCellLoadedEvent>(OnCellLoaded);
        if (!_finished)
        {
            // The process is ending before the deadline (the window was closed, or another flag
            // quit first). The line is still owed; the exit code is whoever quit's.
            Finish(quit: false);
        }
    }

    private void OnCellLoaded(RegionCellLoadedEvent e) => _cellLoaded = true;

    public override void _Process(double delta)
    {
        ulong tick = Time.GetTicksUsec();
        double ms = _lastTick == 0 ? 0d : (tick - _lastTick) / 1000d;
        _lastTick = tick;
        if (_finished)
        {
            return;
        }

        if (!_sampling)
        {
            if ((tick - _startTick) / 1_000_000d < _warmup)
            {
                _cellLoaded = false;
                return;
            }

            _sampling = true;
            _sampleStartTick = tick;
            _allocatedAtStart = GC.GetTotalAllocatedBytes(false);
            _gen0AtStart = GC.CollectionCount(0);
            _gen1AtStart = GC.CollectionCount(1);
            _gen2AtStart = GC.CollectionCount(2);
            _gen2Last = _gen2AtStart;
            _cellLoaded = false;
            return; // This frame's delta began inside the warm-up.
        }

        int gen2 = GC.CollectionCount(2);
        string cause = _cellLoaded ? "cell-load" : gen2 != _gen2Last ? "gc2" : string.Empty;
        _gen2Last = gen2;
        _cellLoaded = false;
        _frames.Add(ms, (tick - _sampleStartTick) / 1_000_000d, cause);

        if (++_frameIndex % MonitorEveryFrames == 0)
        {
            _scriptMs.Add(Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000d);
            _physicsMs.Add(Performance.GetMonitor(Performance.Monitor.TimePhysicsProcess) * 1000d);
            _drawCalls.Add(Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame));
            _primitives.Add(Performance.GetMonitor(Performance.Monitor.RenderTotalPrimitivesInFrame));
        }

        double seconds = ms / 1000d;
        _sinceSecond += seconds;
        if (_sinceSecond >= 1d)
        {
            _sinceSecond = 0d;
            if (ServiceLocator.Instance is { } locator && locator.TryGet(out RegionStreamer streamer))
            {
                _budgetSeconds++;
                if (!streamer.IsWithinPerformanceBudget())
                {
                    _overBudgetSeconds++;
                }

                _worstSecondMs = Math.Max(_worstSecondMs, streamer.PerformanceSnapshot().WorstFrameMilliseconds);
            }
        }

        // A developer session already runs the standing checker every five seconds. A capture run
        // has none, so the report runs it; twice would double every violation in the count.
        _sinceIntegrity += seconds;
        if (_sinceIntegrity >= IntegritySeconds && !BuildProfile.ShowDeveloperTools)
        {
            _sinceIntegrity = 0d;
            WorldIntegrityChecker.Check();
        }
    }

    private void Finish(bool quit)
    {
        if (_finished)
        {
            return;
        }

        _finished = true;
        var report = new HeadlessReport(Flag.TrimStart('-'));
        ProfilerReading now = ProfilerReading.Read();
        WorldFrameDistribution frame = _frames.Distribution();
        double sampled = _sampling ? (Time.GetTicksUsec() - _sampleStartTick) / 1_000_000d : 0d;

        report.Fact("suite", "session")
            .Fact("mode", HeadlessArgs.User.Has(GameShellController.NewGameArgument) ? "new-game" : "continue")
            .Fact("args", string.Join(' ', OS.GetCmdlineUserArgs()))
            .Fact("headless", DisplayServer.GetName() == "headless")
            .Fact("capture", BuildProfile.IsCapture)
            .Fact("adapter", RenderingServer.GetVideoAdapterName())
            .Fact("resolution", $"{DisplayServer.WindowGetSize().X}x{DisplayServer.WindowGetSize().Y}")
            .Fact("seconds", Round(sampled))
            .Fact("warmup_seconds", Round(_warmup))
            .Fact("frames", _frames.Count)
            .Fact("frame_ms_p50", Round(frame.P50))
            .Fact("frame_ms_p95", Round(frame.P95))
            .Fact("frame_ms_p99", Round(frame.P99))
            .Fact("frame_ms_max", Round(frame.Worst))
            .Fact("frame_ms_avg", Round(frame.Average))
            .Fact("hitches_gt33", _frames.Over33)
            .Fact("hitches_gt50", _frames.Over50)
            .Fact("hitches_gt100", _frames.Over100)
            .Fact("worst_hitches", Hitches())
            .Fact("script_ms", Round(FrameStats.Median(_scriptMs)))
            .Fact("physics_ms", Round(FrameStats.Median(_physicsMs)))
            .Fact("draw_calls", (int)FrameStats.Median(_drawCalls))
            .Fact("primitives", (long)FrameStats.Median(_primitives))
            .Fact("nodes", now.Nodes)
            .Fact("orphans", now.Orphans)
            .Fact("orphans_leaked", now.OrphansLeaked)
            .Fact("static_mb", Round(now.StaticMb))
            .Fact("video_mb", Round(now.VideoMb))
            .Fact("texture_mb", Round(now.TextureMb))
            .Fact("buffer_mb", Round(now.BufferMb))
            .Fact("managed_mb", Round(now.ManagedMb));

        if (_sampling && sampled > 0d)
        {
            report.Fact("alloc_kb_per_s", Round((GC.GetTotalAllocatedBytes(false) - _allocatedAtStart) / 1024d / sampled))
                .Fact("gc_gen0", GC.CollectionCount(0) - _gen0AtStart)
                .Fact("gc_gen1", GC.CollectionCount(1) - _gen1AtStart)
                .Fact("gc_gen2", GC.CollectionCount(2) - _gen2AtStart);
        }

        if (now.HasWorld)
        {
            report.Fact("region", now.RegionId)
                .Fact("active_cells", now.ActiveCells)
                .Fact("resident_cells", now.ResidentCells)
                .Fact("world_seconds", _budgetSeconds)
                .Fact("over_budget_seconds", _overBudgetSeconds)
                .Fact("world_worst_second_ms", Round(_worstSecondMs));
            if (_overBudgetSeconds > 0)
            {
                report.Warn($"{_overBudgetSeconds} of {_budgetSeconds} sampled second(s) were over the region's performance budget");
            }
        }

        if (ServiceLocator.Instance is { } locator && locator.TryGet(out PlayerCharacter player))
        {
            report.Fact("safe_zone", SafeZones.Contains(player.GlobalPosition));
        }

        if (IsInsideTree())
        {
            report.Fact("enemies", GetTree().GetNodesInGroup(ObjectiveLocator.EnemyGroup).Count);
        }

        if (quit)
        {
            // The structured list. Skipped when the tree is already coming down: the player is
            // unregistered by then and every check would report the teardown.
            IntegrityReport integrity = WorldIntegrityChecker.Check();
            report.Fact("integrity_ok", integrity.Ok).Fact("integrity_issues", integrity.Codes());
        }
        else
        {
            report.Fact("truncated", true);
            report.Warn("the process ended before the report's deadline; the exit code is not this report's");
        }

        int violations = Invariant.Violations;
        report.Fact("violations", violations)
            .Fact("log_errors", Log.ErrorCount)
            .Fact("log_warnings", Log.WarnCount);
        foreach (string message in Invariant.Recent)
        {
            report.Fail($"invariant: {message}");
        }

        report.Check(violations == 0, $"{violations} invariant violation(s)");
        report.Check(Log.ErrorCount == 0, $"{Log.ErrorCount} error(s) were logged");
        report.Check(_frames.Count > 0, "no frame was sampled: the run ended inside the warm-up");

        int code = report.Finish();
        if (quit)
        {
            GetTree().Quit(code);
        }
    }

    private List<string> Hitches()
    {
        var lines = new List<string>();
        foreach (FrameHitch hitch in _frames.WorstHitches)
        {
            string cause = hitch.Cause.Length > 0 ? " " + hitch.Cause : string.Empty;
            lines.Add(string.Create(CultureInfo.InvariantCulture,
                $"{hitch.AtSeconds:0.0}s {hitch.Milliseconds:0.0}ms{cause}"));
        }

        return lines;
    }

    private static double Round(double value) => Math.Round(value, 2);
}
