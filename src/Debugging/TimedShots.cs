using System;
using System.Collections.Generic;
using Embervale.Core.Diagnostics;
using Godot;

namespace Embervale.Debugging;

/// <summary>
/// A <see cref="ShotHarness"/> whose shots are taken at a moment rather than after a fixed number of
/// frames. The base holds every state for thirty frames, which suits a menu and is useless for a
/// spell: its wind-up is over in a quarter of a second and its impact is one flash.
///
/// <para>The base owns the capture, the PNG checks and the exit code, and none of that is repeated
/// here. This class only decides WHEN the base's frame counter runs: after a shot is driven the
/// counter is held until the shot's own condition is met (in game time), then run out in one frame,
/// so the capture lands on the frame asked for. <c>GetImage</c> returns the last frame drawn, so what
/// is captured is the frame before the one the condition was met on; state for a log line is
/// sampled on <c>RenderingServer.FramePreDraw</c>, which is that same frame.</para>
///
/// <para>A shot that did not reach its state still gets its PNG, because the picture of what went
/// wrong is the evidence. The problem is recorded, and a last shot (<c>zz-summary</c>) fails the run
/// with every problem listed.</para>
/// </summary>
public abstract partial class TimedShots : ShotHarness
{
    private const string SummaryShot = "zz-summary";
    private const double StepTimeout = 6.0;

    private readonly Dictionary<string, Action> _inspections = new();
    private readonly List<string> _problems = new();
    private readonly List<(Func<bool> When, Action Do)> _steps = new();
    private readonly List<(string Name, Image Image)> _burst = new();
    private readonly List<Action> _nextFrame = new();

    private Func<bool>? _until;
    private double _deadline;
    private double _stepDeadline = -1;
    private int _minFrames;
    private int _burstFrames;
    private int _frames;
    private bool _armed;
    private bool _gateOpen;
    private bool _captured;
    private bool _finished;
    private int _registered;
    private int _done;

    /// <summary>Seconds of game time since the harness started.</summary>
    protected double Clock { get; private set; }

    /// <summary>Physics ticks since the harness started.</summary>
    protected long PhysicsTicks { get; private set; }

    /// <summary>The shot being driven or waited on.</summary>
    protected string CurrentShot { get; private set; } = string.Empty;

    /// <summary>Registers the shots, in order, via <see cref="TimedShot"/>.</summary>
    protected abstract void BuildTimedShots();

    /// <summary>Per-frame work, before the capture decision.</summary>
    protected virtual void Frame(double delta)
    {
    }

    /// <summary>Per-physics-tick work, after any queued step has run.</summary>
    protected virtual void PhysicsTick(double delta)
    {
    }

    /// <summary>State sampling on the frame about to be drawn: what a capture on the next frame shows.</summary>
    protected virtual void PreDraw()
    {
    }

    /// <summary>One frame of a burst was grabbed; the last <see cref="PreDraw"/> sample describes it.</summary>
    protected virtual void BurstFrame(string name)
    {
    }

    /// <summary>A reason no picture of this shot can mean anything (no player, no camera), or null.</summary>
    protected virtual string? Fatal(string name) => null;

    protected sealed override void BuildShotList()
    {
        BuildTimedShots();
        TimedShot(SummaryShot, () => { }, () => true);
    }

    /// <summary>
    /// Adds a shot captured when <paramref name="until"/> first holds after <paramref name="drive"/>.
    /// </summary>
    /// <param name="inspect">Runs at the capture: log the state, and <see cref="Problem"/> what is wrong.</param>
    /// <param name="timeout">Seconds to wait for <paramref name="until"/> before capturing anyway and
    /// recording that it never held.</param>
    /// <param name="minFrames">Frames to draw after the drive before a capture. A drive that changes
    /// nothing may pass 1, for a frame as close to the previous shot's as the capture allows.</param>
    /// <param name="burst">Frames grabbed on consecutive draws just before the capture and written as
    /// <c>&lt;name&gt;_f1..</c>; the shot itself is the frame after the last of them.</param>
    protected void TimedShot(
        string name, Action drive, Func<bool> until, Action? inspect = null,
        double timeout = 10.0, int minFrames = 3, int burst = 0)
    {
        _registered++;
        if (inspect != null)
        {
            _inspections[name] = inspect;
        }

        Shot(name, () =>
        {
            CurrentShot = name;
            Guard(drive, $"'{name}' drive");
            _until = until;
            _deadline = Clock + timeout;
            _minFrames = Math.Max(1, minFrames);
            _burstFrames = burst;
            _frames = 0;
            _gateOpen = false;
            _armed = true;
        });
    }

    /// <summary>Queues work for a physics tick: <paramref name="action"/> runs on the first tick
    /// <paramref name="when"/> holds, after everything queued before it. A condition that has not held
    /// a few seconds after its turn came is given up on and the action runs anyway, so one stuck step
    /// cannot strand the rest of the run.</summary>
    protected void Then(Func<bool> when, Action action) => _steps.Add((when, action));

    /// <summary>Queues work for the next physics tick.</summary>
    protected void Then(Action action) => Then(() => true, action);

    /// <summary>Queues work for the start of the next frame, outside the physics tick. ⚠️ A simulated
    /// button edge belongs here: <c>IsActionJustPressed</c> compares the physics frame the press was
    /// stamped with, so a press made inside a tick is invisible to every node that already ran in it
    /// and to all of them on the next. Made between ticks, the whole of the next tick sees it once.</summary>
    protected void NextFrame(Action action) => _nextFrame.Add(action);

    /// <summary>Records something wrong with the run. It fails the run at the summary shot.</summary>
    protected void Problem(string message)
    {
        // Once each: a fault in per-frame work would otherwise repeat for every frame of the run.
        if (_problems.Contains(message))
        {
            return;
        }

        _problems.Add(message);
        Log.Error($"{Flag}: {message}");
    }

    public override void _Ready()
    {
        base._Ready();
        RenderingServer.FramePreDraw += OnFramePreDraw;
    }

    public override void _ExitTree()
    {
        RenderingServer.FramePreDraw -= OnFramePreDraw;
    }

    private void OnFramePreDraw() => Guard(PreDraw, "pre-draw sampling");

    public override void _PhysicsProcess(double delta)
    {
        PhysicsTicks++;
        if (_steps.Count > 0)
        {
            // The wait is counted from the step's own turn, not from when it was queued: a step
            // behind a slow one must not inherit a deadline that has already passed.
            if (_stepDeadline < 0)
            {
                _stepDeadline = Clock + StepTimeout;
            }

            (Func<bool> when, Action action) = _steps[0];
            bool due = false;
            Guard(() => due = when(), "a queued step's condition");
            if (due || Clock >= _stepDeadline)
            {
                _steps.RemoveAt(0);
                _stepDeadline = -1;
                if (!due)
                {
                    Log.Warn($"{Flag}: a step of '{CurrentShot}' ran without its condition being met.");
                }

                Guard(action, $"a step of '{CurrentShot}'");
            }
        }

        Guard(() => PhysicsTick(delta), "the physics tick");
    }

    public override void _Process(double delta)
    {
        Clock += delta;
        if (_nextFrame.Count > 0)
        {
            Action[] queued = _nextFrame.ToArray();
            _nextFrame.Clear();
            foreach (Action action in queued)
            {
                Guard(action, $"a queued action of '{CurrentShot}'");
            }
        }

        Guard(() => Frame(delta), "the frame tick");
        if (_finished)
        {
            return;
        }

        // Settling, or about to drive the next shot: the base counts frames and calls the drive.
        if (!_armed)
        {
            base._Process(delta);
            return;
        }

        _frames++;
        if (!_gateOpen)
        {
            bool ready = false;
            Guard(() => ready = _until == null || _until(), $"'{CurrentShot}' condition");
            bool expired = Clock >= _deadline;
            if ((!ready || _steps.Count > 0 || _nextFrame.Count > 0) && !expired)
            {
                return;
            }

            if (_frames < _minFrames)
            {
                return;
            }

            if (!ready)
            {
                Problem($"'{CurrentShot}' never reached its state; captured as it stood after the wait.");
            }

            _gateOpen = true;
        }

        if (_burst.Count < _burstFrames)
        {
            if (GetViewport()?.GetTexture()?.GetImage() is { } image)
            {
                string name = $"{CurrentShot}_f{_burst.Count + 1}";
                _burst.Add((name, image));
                Guard(() => BurstFrame(name), "a burst frame");
                return;
            }

            Problem($"'{CurrentShot}' burst frame {_burst.Count + 1} had no viewport image.");
            _burstFrames = _burst.Count;
        }

        // Run the base's hold out in this one frame; it captures when the hold ends.
        _captured = false;
        for (int i = 0; i < 10_000 && !_captured; i++)
        {
            base._Process(delta);
        }

        _armed = false;
        WriteBurst();
    }

    protected sealed override string? ValidateShotState(string name)
    {
        _captured = true;
        _done++;
        if (_done >= _registered)
        {
            _finished = true;
        }

        if (name == SummaryShot)
        {
            return _problems.Count == 0
                ? null
                : $"{_problems.Count} problem(s) in this run:\n    " + string.Join("\n    ", _problems);
        }

        if (Fatal(name) is { } fatal)
        {
            return fatal;
        }

        if (_inspections.TryGetValue(name, out Action? inspect))
        {
            Guard(inspect, $"'{name}' inspection");
        }

        return null;
    }

    /// <summary>The burst frames are written after the shot they lead into, so encoding them does not
    /// stretch the frames between them.</summary>
    private void WriteBurst()
    {
        if (_burst.Count == 0)
        {
            return;
        }

        string artifacts = OS.GetEnvironment("EMBERVALE_ARTIFACTS");
        string directory = string.IsNullOrEmpty(artifacts)
            ? OutputDir
            : System.IO.Path.Combine(artifacts, Flag.TrimStart('-'));
        foreach ((string name, Image image) in _burst)
        {
            string path = $"{directory}/{name}.png";
            Error error = image.SavePng(path);
            if (error != Error.Ok)
            {
                Problem($"could not write burst frame '{path}' ({error})");
            }
            else
            {
                Log.Info($"{Flag}: wrote {ProjectSettings.GlobalizePath(path)} (burst frame)");
            }
        }

        _burst.Clear();
    }

    /// <summary>Runs harness code so that a throw is a recorded problem and not a stalled run.</summary>
    private void Guard(Action action, string what)
    {
        try
        {
            action();
        }
        catch (Exception e)
        {
            Problem($"{what} threw {e.GetType().Name}: {e.Message}");
        }
    }
}
