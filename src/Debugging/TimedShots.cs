using System;
using System.Collections.Generic;
using Embervale.Bootstrap;
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
///
/// <para><b>Film.</b> <c>--film[=FRAMESxSTRIDE]</c> (default 12x4) keeps, for every selected shot, the
/// last FRAMES drawn frames taken STRIDE apart between its drive and its capture, and writes them as
/// ONE image, <c>&lt;shot&gt;.film.png</c>, left to right and top to bottom, with
/// <c>&lt;shot&gt;.film.json</c> giving the spacing. It is how motion is read from a still: the
/// frames that led up to the moment the shot captures. Run at a fixed frame rate
/// (<c>--fixed-fps 60</c>, which the SDK's <c>shots</c> command passes) and the spacing is exact.</para>
///
/// <para><b>Focus.</b> The engine lets go of every held action when the window loses focus. That is
/// recorded per shot (the manifest's <c>focus_lost</c>), and it fails the run when the harness says
/// the shot depended on a held input (<see cref="FocusLossSpoils"/>). <c>--direct-input</c>, or a
/// window that is not focused, makes <see cref="DirectInput"/> true so a harness can drive the
/// component itself instead of a button.</para>
/// </summary>
public abstract partial class TimedShots : ShotHarness
{
    public const string FilmArgument = "--film";
    public const string DirectInputArgument = "--direct-input";

    private const string SummaryShot = "zz-summary";
    private const int FilmCellWidth = 320;
    private const double StepTimeout = 6.0;

    private readonly Dictionary<string, Action> _inspections = new();
    private readonly List<string> _problems = new();
    private readonly List<(Func<bool> When, Action Do)> _steps = new();
    private readonly List<(string Name, Image Image)> _burst = new();
    private readonly List<Action> _nextFrame = new();
    private readonly List<(Image Image, double Clock)> _film = new();
    private readonly List<string> _focusLost = new();
    private int _filmFrames;
    private int _filmStride = FilmLayout.DefaultStride;
    private bool _focused = true;

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

    /// <summary>True when the shot in progress is being driven by an input the harness holds down,
    /// so the window losing focus now (the engine releases held actions) makes its frames wrong.</summary>
    protected virtual bool FocusLossSpoils() => false;

    /// <summary>True when a harness should act on the component directly rather than press a
    /// button: <c>--direct-input</c> was passed, or the window does not have focus.</summary>
    protected bool DirectInput =>
        HeadlessArgs.User.Has(DirectInputArgument) || !_focused || !DisplayServer.WindowIsFocused();

    /// <summary>The summary shot checks the run; it is always part of it.</summary>
    protected override bool IsSelected(string name) => name == SummaryShot || base.IsSelected(name);

    /// <summary>The summary shot has nothing to show; it is only written when the whole suite runs,
    /// as it always was.</summary>
    protected override bool WritesImage(string name) => name != SummaryShot || !Filtering;

    protected override void Summarize(ShotManifest manifest) => manifest.FocusLost.AddRange(_focusLost);

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
        if (HeadlessArgs.User.Has(FilmArgument))
        {
            if (FilmLayout.TryParse(HeadlessArgs.User.Value(FilmArgument), out int frames, out int stride))
            {
                _filmFrames = frames;
                _filmStride = stride;
            }
            else
            {
                Problem($"{FilmArgument}='{HeadlessArgs.User.Value(FilmArgument)}' is not FRAMESxSTRIDE (e.g. 12x4); no film was recorded.");
            }
        }

        if (DisplayServer.GetName() != "headless")
        {
            DisplayServer.WindowMoveToForeground();
        }
    }

    /// <summary>Records the window losing focus: the engine releases every held action when it does.</summary>
    public override void _Notification(int what)
    {
        if (what == NotificationApplicationFocusIn || what == NotificationWMWindowFocusIn)
        {
            _focused = true;
        }
        else if ((what == NotificationApplicationFocusOut || what == NotificationWMWindowFocusOut) && _focused)
        {
            _focused = false;
            string shot = CurrentShot.Length > 0 ? CurrentShot : "(before the first shot)";
            if (!_focusLost.Contains(shot))
            {
                _focusLost.Add(shot);
            }

            bool spoils = false;
            Guard(() => spoils = FocusLossSpoils(), "the focus check");
            if (spoils)
            {
                Problem($"the window lost focus during '{shot}' while a held input was driving it; the engine " +
                        "released the input, so this shot is not evidence. Keep the window focused, or pass " +
                        $"{DirectInputArgument}.");
            }
        }
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
        if (_filmFrames > 0 && _frames % _filmStride == 0 && CurrentShot != SummaryShot && CurrentRecord is { Selected: true })
        {
            GrabFilmFrame();
        }

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

        // The base will not capture into a window that is the wrong size; wait for it here, because
        // the loop below would otherwise spin on a base that returns at once.
        if (!WindowReady())
        {
            return;
        }

        // Run the base's hold out in this one frame; it captures when the hold ends.
        ShotRecord? record = CurrentRecord;
        _captured = false;
        for (int i = 0; i < 10_000 && !_captured; i++)
        {
            base._Process(delta);
        }

        _armed = false;
        WriteBurst();
        WriteFilm(record);
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

        string directory = CaptureDirectory;
        foreach ((string name, Image image) in _burst)
        {
            string path = $"{directory}/{name}.png";
            Error error = image.SavePng(path);
            if (error != Error.Ok)
            {
                Problem($"could not write burst frame '{path}' ({error})");
            }
            else if (Verbose)
            {
                Log.Info($"{Flag}: wrote {ProjectSettings.GlobalizePath(path)} (burst frame)");
            }
        }

        _burst.Clear();
    }

    /// <summary>Keeps one drawn frame for the film, already shrunk to a cell so a strip costs a
    /// megabyte and not forty.</summary>
    private void GrabFilmFrame()
    {
        if (GetViewport()?.GetTexture()?.GetImage() is not { } image || image.IsEmpty())
        {
            return;
        }

        if (image.GetWidth() > FilmCellWidth)
        {
            image.Resize(FilmCellWidth, Math.Max(1, image.GetHeight() * FilmCellWidth / image.GetWidth()), Image.Interpolation.Bilinear);
        }

        image.Convert(Image.Format.Rgb8);
        _film.Add((image, Clock));
        if (_film.Count > _filmFrames)
        {
            _film.RemoveAt(0);
        }
    }

    /// <summary>Writes the frames kept for the shot just captured as one sheet and its timing.</summary>
    private void WriteFilm(ShotRecord? record)
    {
        if (_film.Count == 0)
        {
            return;
        }

        (Image Image, double Clock)[] frames = _film.ToArray();
        _film.Clear();
        if (record is not { File: not null })
        {
            return;
        }

        Image first = frames[0].Image;
        FilmLayout layout = FilmLayout.For(frames.Length, first.GetWidth(), first.GetHeight(), first.GetWidth());
        Image sheet = Image.CreateEmpty(layout.Width, layout.Height, false, Image.Format.Rgb8);
        sheet.Fill(new Color(0.07f, 0.08f, 0.10f));
        var cell = new Rect2I(0, 0, layout.CellWidth, layout.CellHeight);
        var times = new double[frames.Length];
        for (int i = 0; i < frames.Length; i++)
        {
            (int x, int y) = layout.Cell(i);
            sheet.BlitRect(frames[i].Image, cell, new Vector2I(x, y));
            sheet.FillRect(new Rect2I(x, y, layout.BarWidth(i), FilmLayout.BarHeight), new Color(1f, 0.85f, 0.2f));
            times[i] = Math.Round(frames[i].Clock - frames[^1].Clock, 4);
        }

        string stem = $"{CaptureDirectory}/{record.Name}.film";
        Error error = sheet.SavePng(stem + ".png");
        if (error != Error.Ok)
        {
            Problem($"could not write film '{stem}.png' ({error})");
            return;
        }

        record.Film = record.Name + ".film.png";
        using FileAccess? file = FileAccess.Open(stem + ".json", FileAccess.ModeFlags.Write);
        file?.StoreString(System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["name"] = record.Name,
            ["frames"] = frames.Length,
            ["stride"] = _filmStride,
            ["columns"] = layout.Columns,
            ["rows"] = layout.Rows,
            ["cell"] = new[] { layout.CellWidth, layout.CellHeight },
            ["order"] = "left to right, top to bottom; the bar on each cell fills as the strip advances",
            ["seconds_before_capture"] = times,
        }));
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
