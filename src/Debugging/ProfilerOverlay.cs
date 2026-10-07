using System;
using System.Globalization;
using System.Text;
using Embervale.Core.Services;
using Embervale.UI;
using Embervale.World;
using Godot;

namespace Embervale.Debugging;

/// <summary>
/// A lightweight profiling overlay (toggled with <c>F4</c>): frame/physics time, draw calls,
/// node/orphan counts, memory and garbage-collector activity, read from Godot's
/// <see cref="Performance"/> monitors and the .NET runtime. Hidden by default and not processed at
/// all while hidden, so it costs nothing when off. Built through <see cref="UiTheme"/> and parked
/// top-right out of the game HUD's way.
///
/// <para>⚠️ <b>The readout refreshes four times a second, and that is what keeps it honest.</b> It
/// used to sort its frame window with LINQ and rebuild a dozen interpolated strings on every frame
/// it was shown, so the allocation and script time it reported were partly its own. Every frame is
/// still recorded (into a fixed ring, which costs two array writes); the sort, the monitor reads and
/// the text are done on the refresh, into buffers kept between refreshes.</para>
/// </summary>
public partial class ProfilerOverlay : CanvasLayer
{
    private const int WindowFrames = 120;
    private const double RefreshSeconds = 0.25d;

    private PanelContainer _panel = null!;
    private Label _text = null!;
    private bool _shown;

    // The last WindowFrames frame times as a ring, and the scratch they are sorted in.
    private readonly double[] _frames = new double[WindowFrames];
    private readonly double[] _sorted = new double[WindowFrames];
    private int _frameCount;
    private int _frameNext;

    private readonly StringBuilder _sb = new(768);
    private double _sinceRefresh;

    // Garbage-collector readings at the previous refresh, for the rates between two refreshes.
    private long _allocatedBefore;
    private double _allocatedSeconds;
    private double _allocatedPerSecond;
    private int _gen0Before;
    private int _gen1Before;
    private int _gen2Before;
    private int _gen0PerSecond;
    private int _gen1PerSecond;
    private int _gen2PerSecond;
    private double _collectionSeconds;

    public override void _Ready()
    {
        Layer = 8;
        Build();
        SetShown(false);
    }

    /// <summary>Shows/hides the overlay (bound to F4 by the bootstrap).</summary>
    public void Toggle() => SetShown(!_shown);

    public override void _Process(double delta)
    {
        if (!_shown)
        {
            return;
        }

        _frames[_frameNext] = delta * 1000d;
        _frameNext = (_frameNext + 1) % WindowFrames;
        if (_frameCount < WindowFrames)
        {
            _frameCount++;
        }

        _sinceRefresh += delta;
        _allocatedSeconds += delta;
        _collectionSeconds += delta;
        if (_sinceRefresh < RefreshSeconds)
        {
            return;
        }

        _sinceRefresh = 0d;
        Refresh();
    }

    private void Refresh()
    {
        Array.Copy(_frames, _sorted, _frameCount);
        Array.Sort(_sorted, 0, _frameCount);
        double median = _frameCount == 0 ? 0d : _sorted[_frameCount / 2];
        double worst = _frameCount == 0 ? 0d : _sorted[_frameCount - 1];

        SampleGarbageCollector();

        // One reading, shared with the --perf-report JSON, so the overlay and the file agree.
        ProfilerReading now = ProfilerReading.Read();
        CultureInfo inv = CultureInfo.InvariantCulture;
        StringBuilder sb = _sb;
        sb.Clear();
        sb.Append("FPS         ").Append(now.Fps.ToString("0", inv)).Append('\n');
        sb.Append("frame med   ").Append(median.ToString("0.00", inv)).Append(" ms\n");
        sb.Append("frame worst ").Append(worst.ToString("0.00", inv)).Append(" ms (120f)\n");
        sb.Append("scripts     ").Append(now.ScriptMs.ToString("0.00", inv)).Append(" ms\n");
        sb.Append("physics     ").Append(now.PhysicsMs.ToString("0.00", inv)).Append(" ms\n");
        sb.Append("draw calls  ").Append(now.DrawCalls).Append('\n');
        sb.Append("primitives  ").Append((now.Primitives / 1000d).ToString("0", inv)).Append("k\n");
        sb.Append("nodes       ").Append(now.Nodes).Append('\n');
        sb.Append("orphans     ").Append(now.Orphans).Append('\n');
        sb.Append("static mem  ").Append(now.StaticMb.ToString("0.0", inv)).Append(" MB\n");

        // The numbers a stutter hunt needs. Video memory is the suspect on a shared-memory GPU;
        // a gen-2 collection or a high allocation rate is what a periodic hitch usually is.
        sb.Append("video mem   ").Append(now.VideoMb.ToString("0", inv))
          .Append(" MB (tex ").Append(now.TextureMb.ToString("0", inv))
          .Append(" · buf ").Append(now.BufferMb.ToString("0", inv)).Append(")\n");
        sb.Append("managed     ").Append(now.ManagedMb.ToString("0.0", inv))
          .Append(" MB · alloc ").Append((_allocatedPerSecond / 1024d).ToString("0", inv)).Append(" KB/s\n");
        sb.Append("GC /s       gen0 ").Append(_gen0PerSecond).Append(" · gen1 ").Append(_gen1PerSecond)
          .Append(" · gen2 ").Append(_gen2PerSecond).Append('\n');
        sb.Append("GC total    ").Append(_gen0Before).Append(" / ").Append(_gen1Before).Append(" / ").Append(_gen2Before);

        if (now.HasWorld)
        {
            WorldPerformanceSnapshot world = now.World;
            sb.Append("\nworld       ").Append(world.ResidentRuntimeNodes).Append(" nodes · ")
              .Append(world.ResidentScatterInstances).Append(" scatter");
            sb.Append("\nworld frame ").Append(world.P50FrameMilliseconds.ToString("0.00", inv)).Append('/')
              .Append(world.P95FrameMilliseconds.ToString("0.00", inv)).Append('/')
              .Append(world.P99FrameMilliseconds.ToString("0.00", inv)).Append(" ms p50/p95/p99");
            sb.Append("\ncells       ").Append(now.ActiveCells).Append(" active · ")
              .Append(now.ResidentCells).Append(" resident");
            sb.Append(now.WithinBudget ? " · budget OK" : " · OVER BUDGET");
        }

        if (ServiceLocator.Instance is { } services && services.TryGet(out SkyController visuals))
        {
            sb.Append("\nvisuals     ").Append(visuals.Diagnostics);
        }

        _text.Text = sb.ToString();
    }

    /// <summary>
    /// Allocation rate and collections since the last refresh. The allocation rate is averaged over a
    /// refresh; the collection counts over a whole second, because a count per quarter second is
    /// almost always 0 or 1 and flickers too fast to read.
    /// </summary>
    private void SampleGarbageCollector()
    {
        long allocated = GC.GetTotalAllocatedBytes(false);
        if (_allocatedSeconds > 0d)
        {
            _allocatedPerSecond = (allocated - _allocatedBefore) / _allocatedSeconds;
        }

        _allocatedBefore = allocated;
        _allocatedSeconds = 0d;

        if (_collectionSeconds >= 1d)
        {
            int gen0 = GC.CollectionCount(0);
            int gen1 = GC.CollectionCount(1);
            int gen2 = GC.CollectionCount(2);
            _gen0PerSecond = (int)Math.Round((gen0 - _gen0Before) / _collectionSeconds);
            _gen1PerSecond = (int)Math.Round((gen1 - _gen1Before) / _collectionSeconds);
            _gen2PerSecond = (int)Math.Round((gen2 - _gen2Before) / _collectionSeconds);
            _gen0Before = gen0;
            _gen1Before = gen1;
            _gen2Before = gen2;
            _collectionSeconds = 0d;
        }
    }

    private void Build()
    {
        _panel = UiTheme.Panel();
        _panel.MouseFilter = Control.MouseFilterEnum.Ignore;
        _panel.SetAnchorsPreset(Control.LayoutPreset.TopRight);
        _panel.OffsetRight = -16;
        _panel.OffsetTop = 220;
        _panel.GrowHorizontal = Control.GrowDirection.Begin;
        AddChild(_panel);

        MarginContainer pad = UiTheme.Padding(10);
        _panel.AddChild(pad);

        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 3);
        col.AddChild(UiTheme.Header("PROFILER  (F4)"));
        _text = UiTheme.Body("", UiTheme.Dim);
        col.AddChild(_text);
        pad.AddChild(col);
    }

    private void SetShown(bool shown)
    {
        _shown = shown;
        _panel.Visible = shown;
        SetProcess(shown);

        if (shown)
        {
            // Start the window and the rates from now, not from whenever it was last open.
            _frameCount = 0;
            _frameNext = 0;
            _sinceRefresh = RefreshSeconds; // paint on the first frame instead of a blank quarter second
            _allocatedBefore = GC.GetTotalAllocatedBytes(false);
            _allocatedSeconds = 0d;
            _allocatedPerSecond = 0d;
            _gen0Before = GC.CollectionCount(0);
            _gen1Before = GC.CollectionCount(1);
            _gen2Before = GC.CollectionCount(2);
            _gen0PerSecond = 0;
            _gen1PerSecond = 0;
            _gen2PerSecond = 0;
            _collectionSeconds = 0d;
        }
    }
}
