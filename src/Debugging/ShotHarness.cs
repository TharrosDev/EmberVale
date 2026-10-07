using System;
using System.Collections.Generic;
using Embervale.Bootstrap;
using Embervale.Core;
using Embervale.Core.Diagnostics;
using Embervale.Core.Services;
using Embervale.World;
using Godot;

namespace Embervale.Debugging;

/// <summary>
/// The settle → drive → hold → capture loop shared by every screenshot harness (39.5C).
///
/// ⚠️ <b>This exists because the loop is the part that was hard to get right, not the shot list.</b>
/// 39.5B wrote it once inside <see cref="HudShots"/> and it took two attempts: the first version
/// captured on the frame after driving a state, and <c>GetImage</c> returns the last <i>drawn</i>
/// frame — so every PNG photographed the PREVIOUS state under the CURRENT state's filename. A
/// capture harness that is off by one is worse than none, because it produces confident evidence for
/// the wrong claim. Copying that loop into a second harness would have been copying a bug that has
/// already bitten once.
///
/// A subclass supplies a name, an output directory and a list of states. Everything about frames,
/// ordering, writing and quitting lives here.
///
/// <para><b>Every harness takes the same options</b>, as user arguments after <c>--</c>:
/// <c>--only=name,name</c> photographs just those shots (<c>*</c> and <c>?</c> are wildcards; the
/// others are still driven, in order, so a state that builds on the one before it stays valid, but
/// they are held three frames and not captured); <c>--list</c> prints the shot names and exits
/// without rendering; <c>--no-thumbs</c> skips the 480 px JPEG written beside each PNG;
/// <c>--shots-verbose</c> logs each shot. A run ends by writing <c>manifest.json</c> next to its
/// images (<see cref="ShotManifest"/>) and printing one <c>EMBERVALE_RESULT</c> line. Exit 0, 1 (a
/// shot failed) or 2 (the run could not start).</para>
///
/// ⚠️ <b>It needs a real window.</b> Run WITHOUT <c>--headless</c> — there is no framebuffer to read
/// back otherwise, and the failure is a silently empty run.
/// </summary>
public abstract partial class ShotHarness : Node
{
    public const string OnlyArgument = "--only";
    public const string ListArgument = "--list";
    public const string NoThumbsArgument = "--no-thumbs";
    public const string VerboseArgument = "--shots-verbose";
    public const string OnlyVariable = "EMBERVALE_SHOT_ONLY";

    /// <summary>Frames to let the world settle before the first capture. A region streams in over
    /// many frames and a shot taken too early photographs a half-loaded world.</summary>
    private static int SettleFrames => int.TryParse(OS.GetEnvironment("EMBERVALE_FRAMES"), out int frames) ? Math.Max(1, frames) : 90;

    /// <summary>Frames between driving a state and capturing it. UI updates in <c>_Process</c> and
    /// several widgets ease over <see cref="UI.UiTheme"/> durations, so a capture taken too soon
    /// catches the transition rather than the state.</summary>
    private const int HoldFrames = 30;

    /// <summary>Frames a shot the run did not select is held: long enough for the nodes its drive
    /// made to enter the tree, so the next drive finds them.</summary>
    private const int SkipHoldFrames = 3;

    private const int ThumbWidth = 480;

    /// <summary>Consecutive frames the window must hold the capture size before the loop moves on:
    /// enough for a resize to be laid out and drawn, so a frame is never read mid-change.</summary>
    private const int SteadyFrames = 8;

    /// <summary>Frames a run will spend putting the window back before it lets the capture report
    /// the wrong size instead of waiting for ever.</summary>
    private const int MaxRestoreFrames = 240;

    /// <summary>Frames a run will wait for a session to finish loading (and a new game's prologue
    /// to be dismissed) before it starts anyway.</summary>
    private const int MaxSessionHoldFrames = 3600;

    /// <summary>Shot patterns that replace <c>--only</c> for the next harness to start: how
    /// <c>--shot --ui=suite/shot</c> asks another suite for one of its states.</summary>
    internal static string[]? OnlyOverride { get; set; }

    private readonly List<(string Name, Action Drive)> _shots = new();
    private readonly List<string> _failures = new();
    private ShotFilter _filter = new(null);
    private ShotManifest? _manifest;
    private ShotRecord? _record;
    private ShotStats? _previousStats;
    private string? _previousName;
    private ulong _startedMsec;
    private int _index = -1;
    private int _countdown = SettleFrames;
    private bool _capturePending;
    private bool _finished;
    private bool _thumbs = true;
    private bool _verbose;

    private int _steadyFrames;
    private int _restoreFrames;
    private bool _reportedWindow;
    private ulong _windowFrame = ulong.MaxValue;
    private bool _windowReady;

    private bool _sessionReady;
    private int _sessionHoldFrames;
    private int _quietFrames;
    private int _openingFrames;
    private UI.ChapterBanner? _banner;
    private UI.Notifications? _notices;
    private bool _interactHeld;
    private UI.OpeningSequence? _opening;

    /// <summary>The command-line flag this harness answers to, for logging (e.g. <c>--hudshots</c>).</summary>
    protected abstract string Flag { get; }

    /// <summary>Where the PNGs land, as a <c>user://</c> path.</summary>
    protected abstract string OutputDir { get; }

    /// <summary>Where this run writes: <c>$EMBERVALE_ARTIFACTS/&lt;flag&gt;</c> when the variable is
    /// set (every SDK run), else <see cref="OutputDir"/>.</summary>
    protected string CaptureDirectory => string.IsNullOrEmpty(OS.GetEnvironment("EMBERVALE_ARTIFACTS"))
        ? OutputDir : System.IO.Path.Combine(OS.GetEnvironment("EMBERVALE_ARTIFACTS"), Flag.TrimStart('-'));

    /// <summary>Capture size in pixels: 1280x720 unless <c>EMBERVALE_RES</c> (or its older alias
    /// <c>EMBERVALE_SHOT_SIZE</c>) says <c>WIDTHxHEIGHT</c> (e.g. <c>1920x1080</c>, or <c>1280x800</c> for the
    /// handheld aspect), so a layout can be photographed at more than one resolution.</summary>
    protected static Vector2I ShotSize
    {
        get
        {
            string size = OS.GetEnvironment("EMBERVALE_RES");
            string[] parts = (size.Length > 0 ? size : OS.GetEnvironment("EMBERVALE_SHOT_SIZE")).Split('x');
            return parts.Length == 2 && int.TryParse(parts[0], out int w) && int.TryParse(parts[1], out int h) && w >= 640 && h >= 360
                ? new Vector2I(w, h)
                : new Vector2I(1280, 720);
        }
    }

    /// <summary>True when the run named shots with <c>--only</c>.</summary>
    protected bool Filtering => _filter.Active;

    /// <summary>True when each shot is logged (<c>--shots-verbose</c>).</summary>
    protected bool Verbose => _verbose;

    /// <summary>The manifest entry of the shot being driven or captured.</summary>
    protected ShotRecord? CurrentRecord => _record;

    /// <summary>Registers the states to capture, in order, via <see cref="Shot"/>.</summary>
    protected abstract void BuildShotList();

    /// <summary>Subclasses prove the requested gameplay/UI state exists before its pixels become
    /// evidence. Return a diagnostic when a player, panel, camera, model or driven state is absent.</summary>
    protected virtual string? ValidateShotState(string name) => null;

    /// <summary>Whether this run photographs the shot. One it does not is still driven.</summary>
    protected virtual bool IsSelected(string name) => _filter.Matches(name);

    /// <summary>Whether a selected shot is written as an image. False for a shot that exists to
    /// check something and has nothing to show.</summary>
    protected virtual bool WritesImage(string name) => true;

    /// <summary>Adds facts about the frame being written to its <c>.png.json</c> sidecar.</summary>
    protected virtual void Describe(string name, Dictionary<string, object?> metadata)
    {
    }

    /// <summary>Called once, when the run is over and before its manifest is written.</summary>
    protected virtual void Summarize(ShotManifest manifest)
    {
    }

    /// <summary>Adds one named state and the action that drives it.</summary>
    protected void Shot(string name, Action drive) => _shots.Add((name, drive));

    public override void _Ready()
    {
        // Pause-immune. Both harnesses photograph states that pause the tree — an open menu for the
        // HUD, an open modal panel for the map — and a paused harness can neither capture them nor
        // advance past them (CLAUDE.md §7's pause deadlock, from the other side).
        ProcessMode = ProcessModeEnum.Always;
        _startedMsec = Time.GetTicksMsec();

        string[]? only = OnlyOverride;
        OnlyOverride = null;
        IReadOnlyList<string> patterns = only ?? (HeadlessArgs.User.Has(OnlyArgument)
            ? HeadlessArgs.User.List(OnlyArgument)
            : OS.GetEnvironment(OnlyVariable).Split(','));
        _filter = new ShotFilter(patterns);
        _thumbs = !HeadlessArgs.User.Has(NoThumbsArgument);
        _verbose = HeadlessArgs.User.Has(VerboseArgument);
        bool list = HeadlessArgs.User.Has(ListArgument);

        if (!list)
        {
            if (DisplayServer.GetName() == "headless")
            {
                Abort("rendering-capable display required; do not use --headless");
                return;
            }

            DisplayServer.WindowSetSize(ShotSize);
            if (float.TryParse(OS.GetEnvironment("EMBERVALE_SHOT_UISCALE"), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float uiScale))
            {
                GetTree().Root.ContentScaleFactor = Mathf.Clamp(uiScale, 0.75f, 1.5f); // the setting's own range
            }

            if (DirAccess.MakeDirRecursiveAbsolute(CaptureDirectory) != Error.Ok)
            {
                Abort($"could not create output directory {ProjectSettings.GlobalizePath(CaptureDirectory)}");
                return;
            }
        }

        BuildShotList();
        if (_shots.Count == 0)
        {
            Abort("no capture states were registered");
            return;
        }

        List<string> names = _shots.ConvertAll(shot => shot.Name);
        if (list)
        {
            _shots.Clear();
            GetTree().Quit(new HeadlessReport("shots-list")
                .Fact("suite", Flag).Fact("count", names.Count).Fact("shots", names).Finish());
            return;
        }

        IReadOnlyList<string> unmatched = _filter.Unmatched(names);
        if (unmatched.Count > 0)
        {
            Abort($"{OnlyArgument} matched no shot: {string.Join(", ", unmatched)} (run with {ListArgument} for the names)");
            return;
        }

        _manifest = new ShotManifest(Flag)
        {
            Width = ShotSize.X,
            Height = ShotSize.Y,
            UiScale = Math.Round(GetTree().Root.ContentScaleFactor, 3),
            Only = _filter.Patterns,
        };
        Log.Info($"{Flag}: {_shots.Count} state(s) queued" +
                 (_filter.Active ? $", {names.FindAll(IsSelected).Count} selected" : string.Empty) +
                 $"; output -> {ProjectSettings.GlobalizePath(CaptureDirectory)}");
    }

    public override void _Process(double delta)
    {
        if (_shots.Count == 0 || _finished || !WindowReady())
        {
            return;
        }

        if (_index < 0 && SessionHolds())
        {
            return;
        }

        if (--_countdown > 0)
        {
            return;
        }

        // ⚠️ THE CAPTURE COMES AFTER THE HOLD, NOT ON THE FRAME AFTER THE DRIVE. See the class note.
        if (_capturePending)
        {
            _capturePending = false;
            string shotName = _shots[_index].Name;
            ShotRecord record = _record!;

            // Always asked, selected or not: a subclass may keep its own count of finished shots here.
            string? invalid = ValidateShotState(shotName);
            if (record.Selected)
            {
                if (invalid is not null)
                {
                    FailShot(record, $"'{shotName}' prerequisite failed: {invalid}");
                }
                else if (WritesImage(shotName))
                {
                    Capture(record);
                }
            }

            if (_index + 1 >= _shots.Count)
            {
                Finish();
                return;
            }

            _countdown = 1; // drive the next state on the following frame
            return;
        }

        _index++;
        (string name, Action drive) = _shots[_index];
        _record = _manifest!.Add(name, IsSelected(name));
        _countdown = _record.Selected ? HoldFrames : SkipHoldFrames;
        _capturePending = true;

        drive();
        if (_verbose)
        {
            Log.Info($"{Flag}: [{_index + 1}/{_shots.Count}] {name}{(_record.Selected ? string.Empty : " (driven, not captured)")}");
        }
    }

    /// <summary>
    /// True once the window is windowed, at the capture size, and has been for <see cref="SteadyFrames"/>.
    ///
    /// ⚠️ At <c>EMBERVALE_RES=1280x800</c> with <c>EMBERVALE_SHOT_UISCALE=1.5</c> the panel harness returned
    /// 2880x1659 frames for its first three shots and a flat one for the fourth, then captured
    /// correctly. The viewport texture is the window's size, so the WINDOW was not 1280x800 for the
    /// first seconds of the run and was put right part-way through: the flat frame is the one read as
    /// the resize landed. A size set once, in <c>_Ready</c>, is ignored by a window that is maximized
    /// or still being placed.
    ///
    /// So the size is asserted for as long as the run lasts rather than once: every frame, before the
    /// loop may drive or capture. When it is wrong, this says what it found (mode, size, screen and
    /// screen scale, which is the evidence the cause needs) and restores it. It was written for the
    /// panel harness and every harness shares it now. The answer is worked out once a frame.
    /// </summary>
    protected bool WindowReady()
    {
        ulong frame = Engine.GetProcessFrames();
        if (_windowFrame != frame)
        {
            _windowFrame = frame;
            _windowReady = WindowHoldsCaptureSize();
        }

        return _windowReady;
    }

    private bool WindowHoldsCaptureSize()
    {
        if (DisplayServer.GetName() == "headless" || _restoreFrames > MaxRestoreFrames)
        {
            return true;
        }

        Vector2I want = ShotSize;
        DisplayServer.WindowMode mode = DisplayServer.WindowGetMode();
        Vector2I size = DisplayServer.WindowGetSize();
        if (mode == DisplayServer.WindowMode.Windowed && size == want)
        {
            if (_steadyFrames < SteadyFrames)
            {
                _steadyFrames++;
                return false;
            }

            return true;
        }

        if (!_reportedWindow)
        {
            _reportedWindow = true;
            Log.Warn($"{Flag}: window is {mode} {size.X}x{size.Y}, capture size is {want.X}x{want.Y} " +
                     $"(screen {DisplayServer.ScreenGetSize().X}x{DisplayServer.ScreenGetSize().Y}, " +
                     $"scale {DisplayServer.ScreenGetScale():0.##}); restoring before the next shot.");
        }

        _steadyFrames = 0;
        _restoreFrames++;
        if (mode != DisplayServer.WindowMode.Windowed)
        {
            DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
        }

        DisplayServer.WindowSetSize(want);
        return false;
    }

    /// <summary>
    /// Holds the first drive until the session can be driven: not while the world is still loading,
    /// and not under a new game's prologue. A run started with <c>--new-game</c> dismisses the
    /// prologue the way an unattended run always has, with the interact action; if that has not
    /// taken it down in three seconds it is taken down directly. Bounded, so a session that never
    /// settles is photographed as it stands rather than waited on for ever.
    /// </summary>
    private bool SessionHolds()
    {
        if (_sessionReady)
        {
            return false;
        }

        bool newGame = HeadlessArgs.User.Has(GameShellController.NewGameArgument);
        bool busy = GameManager.Instance?.State is GameState.Loading or GameState.Boot;
        if (!busy && newGame)
        {
            if ((_opening == null || !IsInstanceValid(_opening)) && _sessionHoldFrames % 15 == 0)
            {
                _opening = QuestShotFixtures.FindFirst<UI.OpeningSequence>(GetTree().Root);
            }

            if (_opening != null && IsInstanceValid(_opening) && _opening.IsPlaying)
            {
                busy = true;
                _openingFrames++;
                if (_openingFrames > 180)
                {
                    Log.Warn($"{Flag}: the prologue did not end on the interact action; taking it down directly.");
                    _opening.EndForCapture();
                }
                else if (_openingFrames % 20 == 1)
                {
                    PressInteract(true);
                }
                else if (_openingFrames % 20 == 4)
                {
                    PressInteract(false);
                }
            }
        }

        if (!busy && newGame && WaitsForOpeningNotices && OpeningNoticesShowing())
        {
            // New Game follows the prologue with the act card and the first discovery toasts. A state
            // photographed under them is not the state that was asked for.
            busy = true;
        }

        _sessionHoldFrames++;
        _quietFrames = busy ? 0 : _quietFrames + 1;
        if ((busy || _quietFrames < (newGame ? 45 : 1)) && _sessionHoldFrames < MaxSessionHoldFrames)
        {
            return true;
        }

        if (busy)
        {
            Log.Warn($"{Flag}: the session was still loading after {MaxSessionHoldFrames} frames; starting anyway.");
        }

        PressInteract(false);
        _sessionReady = true;
        return false;
    }

    /// <summary>False for a harness that hides the interface anyway and need not wait the notices out.</summary>
    protected virtual bool WaitsForOpeningNotices => true;

    private bool OpeningNoticesShowing()
    {
        if (_sessionHoldFrames % 15 == 0)
        {
            _banner ??= QuestShotFixtures.FindFirst<UI.ChapterBanner>(GetTree().Root);
            _notices ??= QuestShotFixtures.FindFirst<UI.Notifications>(GetTree().Root);
        }

        return (_banner != null && IsInstanceValid(_banner) && _banner.Showing != null) ||
               (_notices != null && IsInstanceValid(_notices) && _notices.LiveToastsForCapture > 0);
    }

    private void PressInteract(bool pressed)
    {
        if (_interactHeld == pressed)
        {
            return;
        }

        _interactHeld = pressed;
        Godot.Input.ParseInputEvent(new InputEventAction
        {
            Action = UI.UiLive.Interact,
            Pressed = pressed,
            Strength = pressed ? 1f : 0f,
        });
    }

    /// <summary>Renders the current frame to <c>&lt;name&gt;.png</c>, with a thumbnail and a
    /// <c>.png.json</c> sidecar, and measures it.</summary>
    private void Capture(ShotRecord record)
    {
        string name = record.Name;
        if (GetViewport()?.GetTexture()?.GetImage() is not { } image)
        {
            FailShot(record, $"no viewport image for '{name}'");
            return;
        }

        Vector2I size = ShotSize;
        if (image.IsEmpty() || image.GetWidth() != size.X || image.GetHeight() != size.Y)
        {
            FailShot(record, $"'{name}' returned {image.GetWidth()}x{image.GetHeight()}, expected {size.X}x{size.Y}");
            return;
        }
        if (IsBlank(image))
        {
            FailShot(record, $"'{name}' is blank/flat-colour; it is not valid visual evidence");
            return;
        }

        string path = $"{CaptureDirectory}/{name}.png";
        Error error = image.SavePng(path);
        if (error != Error.Ok)
        {
            FailShot(record, $"could not write '{path}' ({error})");
            return;
        }

        if (!FileAccess.FileExists(path))
        {
            FailShot(record, $"SavePng reported success but '{path}' is missing");
            return;
        }

        record.File = name + ".png";
        record.Frame = (long)Engine.GetProcessFrames();
        Measure(record, image);

        var metadata = new Dictionary<string, object?>
        {
            ["name"] = name,
            ["suite"] = Flag,
            ["frame"] = record.Frame,
            ["seed"] = OS.GetEnvironment("EMBERVALE_SEED"),
            ["resolution"] = new[] { image.GetWidth(), image.GetHeight() },
            ["ui_scale"] = Math.Round(GetTree().Root.ContentScaleFactor, 3),
            ["godot"] = Engine.GetVersionInfo()["string"].AsString(),
            ["flags"] = record.Flags,
        };
        if (GetViewport()?.GetCamera3D() is { } camera)
        {
            metadata["camera"] = new Dictionary<string, object?>
            {
                ["position"] = Triple(camera.GlobalPosition),
                ["rotation_degrees"] = Triple(camera.GlobalRotationDegrees),
                ["fov"] = Math.Round(camera.Fov, 2),
            };
        }

        if (ServiceLocator.Instance is { } locator)
        {
            if (locator.TryGet(out WorldClock clock))
            {
                metadata["hour"] = Math.Round(clock.TimeOfDay, 2);
            }

            if (locator.TryGet(out WeatherDirector weather))
            {
                metadata["weather"] = weather.Current?.Id;
            }
        }

        try
        {
            Describe(name, metadata);
            using FileAccess? sidecar = FileAccess.Open(path + ".json", FileAccess.ModeFlags.Write);
            sidecar?.StoreString(System.Text.Json.JsonSerializer.Serialize(metadata));
        }
        catch (Exception e)
        {
            FailShot(record, $"could not describe '{name}': {e.GetType().Name}: {e.Message}");
            return;
        }

        if (_verbose)
        {
            Log.Info($"{Flag}: wrote {ProjectSettings.GlobalizePath(path)} ({image.GetWidth()}x{image.GetHeight()})");
        }
    }

    /// <summary>Thumbnail, pixel measurements and the flags they raise. A flag is a note in the
    /// manifest, not a failure: a loading screen is black on purpose.</summary>
    private void Measure(ShotRecord record, Image image)
    {
        var thumb = (Image)image.Duplicate();
        if (thumb.GetWidth() > ThumbWidth)
        {
            thumb.Resize(ThumbWidth, Math.Max(1, thumb.GetHeight() * ThumbWidth / thumb.GetWidth()), Image.Interpolation.Bilinear);
        }

        thumb.Convert(Image.Format.Rgb8);
        ShotStats stats = ShotStats.Compute(thumb.GetData(), thumb.GetWidth(), thumb.GetHeight());
        record.Stats = stats;
        record.Flags.AddRange(stats.Flags(_previousStats, _previousName));
        _previousStats = stats;
        _previousName = record.Name;

        if (_thumbs && thumb.SaveJpg($"{CaptureDirectory}/{record.Name}.thumb.jpg", 0.8f) == Error.Ok)
        {
            record.Thumb = record.Name + ".thumb.jpg";
        }
    }

    /// <summary>A vector as three rounded numbers, for JSON.</summary>
    protected static double[] Triple(Vector3 value) => new[] { Finite(value.X), Finite(value.Y), Finite(value.Z) };

    private static double Finite(float value) => float.IsFinite(value) ? Math.Round(value, 3) : 0.0;

    private static bool IsBlank(Image image)
    {
        float min = 1f;
        float max = 0f;
        for (int y = 0; y < image.GetHeight(); y += 36)
        for (int x = 0; x < image.GetWidth(); x += 40)
        {
            Color c = image.GetPixel(x, y);
            float value = (c.R + c.G + c.B) / 3f;
            min = Mathf.Min(min, value);
            max = Mathf.Max(max, value);
        }
        return max - min < 0.01f;
    }

    /// <summary>Writes the manifest, prints the one result line and quits with the run's exit code.</summary>
    private void Finish()
    {
        _finished = true;
        ShotManifest manifest = _manifest!;
        try
        {
            Summarize(manifest);
        }
        catch (Exception e)
        {
            Fail($"the run summary threw {e.GetType().Name}: {e.Message}");
        }

        string path = $"{CaptureDirectory}/{ShotManifest.FileName}";
        using (FileAccess? file = FileAccess.Open(path, FileAccess.ModeFlags.Write))
        {
            if (file == null)
            {
                Fail($"could not write '{path}' ({FileAccess.GetOpenError()})");
            }
            else
            {
                manifest.Failures.AddRange(_failures.FindAll(failure => !manifest.Shots.Exists(shot => shot.Problem == failure)));
                file.StoreString(manifest.ToJson((Time.GetTicksMsec() - _startedMsec) / 1000.0));
            }
        }

        HeadlessReport report = new HeadlessReport("shots")
            .Fact("suite", Flag)
            .Fact("registered", manifest.Shots.Count)
            .Fact("captured", manifest.Captured)
            .Fact("failed", manifest.Failed)
            .Fact("flagged", manifest.Flagged)
            .Fact("focus_lost", manifest.FocusLost)
            .Fact("dir", ProjectSettings.GlobalizePath(CaptureDirectory))
            .Fact("manifest", ProjectSettings.GlobalizePath(path));
        foreach (string failure in _failures)
        {
            report.Fail(failure);
        }

        if (_failures.Count > 0)
        {
            Log.Error($"{Flag}: FAILED with {_failures.Count} capture error(s):\n  " + string.Join("\n  ", _failures));
        }
        else
        {
            Log.Info($"{Flag}: wrote {manifest.Captured} verified image(s) to {ProjectSettings.GlobalizePath(CaptureDirectory)}");
        }

        GetTree().Quit(report.Finish());
    }

    /// <summary>The run cannot start: say why, print the result line and exit 2.</summary>
    private void Abort(string message)
    {
        _shots.Clear();
        Fail(message);
        HeadlessReport report = new HeadlessReport("shots").Fact("suite", Flag);
        report.Fail(message);
        report.Finish();
        GetTree().Quit(2);
    }

    private void FailShot(ShotRecord record, string message)
    {
        record.Problem = message;
        Fail(message);
    }

    private void Fail(string message)
    {
        _failures.Add(message);
        Log.Error($"{Flag}: {message}");
    }
}
