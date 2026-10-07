using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using Embervale.Bootstrap;
using Embervale.Core;
using Embervale.Core.Diagnostics;
using Embervale.Player;
using Godot;

namespace Embervale.Debugging;

/// <summary>
/// Runs a dev-console script against a live session from the command line, then quits:
/// <code>godot --headless --fixed-fps 60 --path . -- --new-game --exec "seed 7; tp out; spawn enemy.goblin 3; wait 2; assert enemies.count ge 3"</code>
/// <c>--exec "a; b"</c> takes the script inline, <c>--exec-file &lt;path&gt;</c> from a file
/// (statements split on <c>;</c> and newlines, <c>#</c> comments). It attaches like any session
/// harness, so it works with <c>--play</c>, <c>--slot</c> and <c>--new-game</c>. ⚠️ A script
/// changes the session it runs in, so the shell refuses it (exit 2) unless
/// <c>EMBERVALE_USER_DIR</c> is an absolute directory; <c>--exec-allow-real-save</c> runs it on the
/// real save folder with autosaves off and <c>save</c> held to the console slot
/// (<see cref="SessionEntryRules.ScriptRefusal"/>).
///
/// <para><b>Statements.</b> Anything the F1 console accepts, plus the runner's own verbs
/// (<see cref="ScriptVerb"/>): <c>wait &lt;seconds&gt;</c>, <c>frames &lt;n&gt;</c>,
/// <c>wait-until &lt;key&gt; &lt;op&gt; &lt;value&gt; [seconds]</c>, <c>assert &lt;key&gt; &lt;op&gt; &lt;value&gt;</c>
/// (keys are <c>get</c>'s), <c>expect &lt;text&gt;</c>, <c>shot &lt;name&gt;</c>,
/// <c>input &lt;action&gt; [frames]</c>, <c>quit</c>. The whole script is checked before the first
/// statement runs.</para>
///
/// <para><b>Pacing.</b> One statement a frame at most. Before each one the runner waits for the
/// world to be usable (<see cref="WorldReady"/>): a session with a player, not loading, and the
/// ground under the player streamed in. That is what makes <c>tp</c>, <c>region goto</c>,
/// <c>travel goto</c> and <c>load</c> safe to follow with another statement.</para>
///
/// <para><b>Output.</b> <c>&lt;artifacts&gt;/console/result.ndjson</c> (see
/// <see cref="DevCommands.OutputDirectory"/>): one line per statement,
/// <c>{"i","cmd","ok","frame","out","data"?}</c>, then <c>{"event":"result",…}</c>. Stdout gets
/// one <c>[CON] FAIL</c> line per failed statement (every statement with <c>--exec-verbose</c>)
/// and the final <c>EMBERVALE_RESULT</c> line (<see cref="HeadlessReport"/>, which also honours
/// <c>--report</c>). The exit code is 0 when every statement passed and no invariant violation
/// was recorded, 1 otherwise. The result file is flushed per statement. A failed
/// statement does not stop the script unless <c>--exec-stop-on-fail</c> is given.
/// <c>--exec-timeout=&lt;seconds&gt;</c> (real time, default 300) ends a script that hangs.</para>
///
/// <para>Log output below warnings is switched off for the run unless <c>--exec-verbose</c>.</para>
/// </summary>
public sealed partial class ConsoleScript : Node
{
    public const string ExecArgument = "--exec";
    public const string ExecFileArgument = "--exec-file";
    public const string TimeoutArgument = "--exec-timeout";
    public const string VerboseArgument = "--exec-verbose";
    public const string StopOnFailArgument = "--exec-stop-on-fail";
    public const string KeepNarrationArgument = "--exec-keep-narration";

    /// <summary>How long the runner waits for the world before a statement: 30 s at 60 fps, the
    /// loading gate's own limit.</summary>
    private const int BarrierFrames = 1800;

    /// <summary>A reply longer than this is cut in the result file; the full text of a long
    /// listing belongs in a <c>dump</c> file.</summary>
    private const int MaxOutCharacters = 2000;

    private readonly HeadlessReport _report = new("exec");
    private readonly List<ScriptStep> _steps = new();
    private StreamWriter? _results;
    private string _directory = string.Empty;
    private bool _verbose;
    private bool _stopOnFail;
    private bool _openingHandled;
    private double _timeoutSeconds;
    private ulong _startedMsec;

    private int _index;

    /// <summary>Statements that actually ran. Not <see cref="_index"/>: <c>quit</c> and
    /// <c>--exec-stop-on-fail</c> end a script by moving the index past the last statement.</summary>
    private int _ran;
    private int _failed;
    private bool _done;
    private bool _started;
    private int _barrier;
    private double _secondsLeft;
    private int _framesLeft;
    private string _lastOut = string.Empty;
    private string _firstFailure = string.Empty;

    /// <summary>Owns the session. Held instead of the session itself because <c>load</c> replaces
    /// the session, and with it the console, under a script that keeps running.</summary>
    public SessionLifecycleCoordinator Lifecycle { get; init; } = null!;

    public override void _Ready()
    {
        // The console works while the tree is paused (a command can open a blocking menu), and so
        // must the thing driving it.
        ProcessMode = ProcessModeEnum.Always;
        _startedMsec = Time.GetTicksMsec();
        _verbose = HeadlessArgs.User.Has(VerboseArgument);
        _stopOnFail = HeadlessArgs.User.Has(StopOnFailArgument);
        _timeoutSeconds = HeadlessArgs.User.Float(TimeoutArgument, 300f);

        string? problem = LoadScript(out string source);
        if (problem == null)
        {
            try
            {
                _directory = DevCommands.OutputDirectory();
                // Flushed per line: a statement that crashes the engine, or a hung run the caller
                // kills, is exactly when the lines before it are needed.
                _results = new StreamWriter(Path.Combine(_directory, "result.ndjson"), append: false, new UTF8Encoding(false))
                {
                    AutoFlush = true,
                };
            }
            catch (IOException error)
            {
                problem = $"could not open the result file: {error.Message}";
            }
            catch (System.UnauthorizedAccessException error)
            {
                problem = $"could not open the result file: {error.Message}";
            }
        }

        _report.Fact("source", source).Fact("steps", _steps.Count);
        if (problem != null)
        {
            _report.Fail(problem);
            Finish();
            return;
        }

        if (!_verbose)
        {
            Log.MinimumLevel = Log.Level.Warn;
        }

        GD.Print($"[CON] {_steps.Count} statement(s) from {source}; results -> {Path.Combine(_directory, "result.ndjson")}");
    }

    /// <summary>
    /// A script run's stdout is read by a shell caller: before anything loads, drop the Info log
    /// (about a hundred startup lines) and the engine's seven-line navigation edge warning that
    /// every region load prints. --exec-verbose keeps both.
    /// </summary>
    public static void QuietStartupIfRequested()
    {
        if ((HeadlessArgs.User.Has(ExecArgument) || HeadlessArgs.User.Has(ExecFileArgument))
            && !HeadlessArgs.User.Has(VerboseArgument))
        {
            Log.MinimumLevel = Log.Level.Warn;
            Log.MirrorWarningsToEngine = false; // one line per warning, not the engine's copy as well
            ProjectSettings.SetSetting("navigation/3d/warnings/navmesh_edge_merge_errors", false);
        }
    }

    /// <summary>Reads and checks the script. Returns what is wrong with it, or null.</summary>
    private string? LoadScript(out string source)
    {
        string text;
        if (HeadlessArgs.User.Has(ExecFileArgument))
        {
            string path = HeadlessArgs.User.Value(ExecFileArgument) ?? string.Empty;
            source = path;
            if (path.Length == 0)
            {
                return $"{ExecFileArgument} needs a path";
            }

            try
            {
                text = File.ReadAllText(path.StartsWith("res://") || path.StartsWith("user://")
                    ? ProjectSettings.GlobalizePath(path)
                    : path);
            }
            catch (IOException error)
            {
                return $"could not read {path}: {error.Message}";
            }
            catch (System.UnauthorizedAccessException error)
            {
                return $"could not read {path}: {error.Message}";
            }
        }
        else
        {
            source = ExecArgument;
            text = HeadlessArgs.User.Value(ExecArgument) ?? string.Empty;
        }

        foreach (string statement in ConsoleText.SplitScript(text))
        {
            _steps.Add(ConsoleText.Classify(statement));
        }

        if (_steps.Count == 0)
        {
            return "the script is empty (--exec \"cmd; cmd\" or --exec-file <path>)";
        }

        bool headless = DisplayServer.GetName() == "headless";
        for (int i = 0; i < _steps.Count; i++)
        {
            ScriptStep step = _steps[i];
            string? problem = ConsoleText.Problem(step);
            if (problem == null && step.Verb == ScriptVerb.Shot && headless)
            {
                problem = "shot needs a rendering display; run without --headless";
            }

            if (problem == null && step.Verb == ScriptVerb.Input && !InputMap.HasAction(step.Args[0]))
            {
                problem = $"unknown input action '{step.Args[0]}'";
            }

            if (problem != null)
            {
                return $"statement {i + 1} `{step.Raw}`: {problem}";
            }
        }

        return null;
    }

    public override void _Process(double delta)
    {
        if (_done)
        {
            return;
        }

        if ((Time.GetTicksMsec() - _startedMsec) / 1000d > _timeoutSeconds)
        {
            string where = _index < _steps.Count ? $"statement {_index + 1} `{_steps[_index].Raw}`" : "the end";
            _report.Fail($"timed out after {_timeoutSeconds:0} s at {where}");
            Finish();
            return;
        }

        if (_index >= _steps.Count)
        {
            Finish();
            return;
        }

        ScriptStep step = _steps[_index];
        if (!_started)
        {
            // Pure waits pass time and need nothing from the world; everything else does.
            if (step.Verb is not (ScriptVerb.Wait or ScriptVerb.Frames or ScriptVerb.Quit or ScriptVerb.Expect) &&
                !WorldReady(out string why))
            {
                if (++_barrier > BarrierFrames)
                {
                    Complete(step, false, $"the world was not ready after {BarrierFrames} frames: {why}");
                }

                return;
            }

            _barrier = 0;
            _started = true;
            if (!_openingHandled)
            {
                // A New Game opens under the prologue cards: controls are locked and a shot shows the
                // card, not the world. End it once, through its own skip path, before anything runs.
                _openingHandled = true;
                if (!HeadlessArgs.User.Has(KeepNarrationArgument) &&
                    DevCommands.SkipNarration(GetTree().Root) is { Count: > 0 } skipped)
                {
                    _report.Fact("narration_skipped", string.Join(",", skipped));
                }
            }

            Begin(step);
        }
        else
        {
            Continue(step, delta);
        }
    }

    /// <summary>True when a statement can run: there is a session with a player, the game is not
    /// loading or on the title, and while playing the ground under the player is streamed in.</summary>
    private bool WorldReady(out string why)
    {
        why = string.Empty;
        GameState? state = GameManager.Instance?.State;
        if (Lifecycle.Session is not { } session || !IsInstanceValid(session))
        {
            why = "no session";
            return false;
        }

        if (state is null or GameState.Boot or GameState.MainMenu or GameState.Loading)
        {
            why = $"the game is {state}";
            return false;
        }

        PlayerCharacter? player = session.Players.Player;
        if (player == null || !IsInstanceValid(player))
        {
            why = "no player";
            return false;
        }

        if (state == GameState.Playing && session.WorldDirector.Streamer is { } streamer &&
            !streamer.IsPositionReady(player.GlobalPosition))
        {
            why = "the ground under the player is not streamed in";
            return false;
        }

        return true;
    }

    /// <summary>The first frame of a statement. Instant ones complete here.</summary>
    private void Begin(ScriptStep step)
    {
        string[] a = step.Args;
        switch (step.Verb)
        {
            case ScriptVerb.Command:
            {
                if (Lifecycle.Session?.DevTools?.Console is not { } console)
                {
                    Complete(step, false, "no dev console in this session (a --capture or exported run has none)");
                    return;
                }

                ConsoleResult result = console.Run(step.Raw);
                Complete(step, result.Ok, result.Text, result.Json);
                return;
            }
            case ScriptVerb.Wait:
                ConsoleText.TryNumber(a[0].TrimEnd('s'), out _secondsLeft);
                return;
            case ScriptVerb.Frames:
                _framesLeft = int.Parse(a[0], System.Globalization.CultureInfo.InvariantCulture);
                return;
            case ScriptVerb.Assert:
            {
                bool known = DevCommands.TryQuery(a[0], out string actual);
                bool ok = known && ConsoleText.Compare(actual, a[1], a[2]);
                Complete(step, ok, known ? $"{a[0]}={actual}" : $"unknown or unavailable key '{a[0]}'");
                return;
            }
            case ScriptVerb.WaitUntil:
                _secondsLeft = 10d;
                if (a.Length == 4)
                {
                    ConsoleText.TryNumber(a[3], out _secondsLeft);
                }

                Continue(step, 0d);
                return;
            case ScriptVerb.Expect:
            {
                bool ok = _lastOut.Contains(a[0], System.StringComparison.OrdinalIgnoreCase);
                Complete(step, ok, ok ? "matched" : $"the previous reply was: {_lastOut}");
                return;
            }
            case ScriptVerb.Shot:
                // The image read back is the last frame DRAWN, so a capture on the frame a state
                // was driven photographs the state before it (ShotHarness's note). Two frames on.
                _framesLeft = 2;
                return;
            case ScriptVerb.Input:
                _framesLeft = a.Length > 1 ? int.Parse(a[1], System.Globalization.CultureInfo.InvariantCulture) : 2;
                Input.ParseInputEvent(new InputEventAction { Action = a[0], Pressed = true, Strength = 1f });
                return;
            case ScriptVerb.Quit:
                Complete(step, true, "quit");
                _index = _steps.Count;
                return;
        }
    }

    /// <summary>Every later frame of a statement that takes time.</summary>
    private void Continue(ScriptStep step, double delta)
    {
        string[] a = step.Args;
        switch (step.Verb)
        {
            case ScriptVerb.Wait:
                _secondsLeft -= delta;
                if (_secondsLeft <= 0d)
                {
                    Complete(step, true, string.Empty);
                }

                return;
            case ScriptVerb.Frames:
                if (--_framesLeft <= 0)
                {
                    Complete(step, true, string.Empty);
                }

                return;
            case ScriptVerb.WaitUntil:
            {
                bool known = DevCommands.TryQuery(a[0], out string actual);
                if (known && ConsoleText.Compare(actual, a[1], a[2]))
                {
                    Complete(step, true, $"{a[0]}={actual}");
                    return;
                }

                // Real seconds: a wait must end even while the tree is paused or time is scaled.
                _secondsLeft -= delta / System.Math.Max(Engine.TimeScale, 0.01d);
                if (_secondsLeft <= 0d)
                {
                    Complete(step, false, known ? $"timed out with {a[0]}={actual}" : $"timed out: key '{a[0]}' is unknown or unavailable");
                }

                return;
            }
            case ScriptVerb.Shot:
                if (--_framesLeft <= 0)
                {
                    string? problem = Capture(a[0], out string path);
                    Complete(step, problem == null, problem ?? path);
                }

                return;
            case ScriptVerb.Input:
                if (--_framesLeft <= 0)
                {
                    Input.ParseInputEvent(new InputEventAction { Action = a[0], Pressed = false });
                    Complete(step, true, string.Empty);
                }

                return;
        }
    }

    /// <summary>Writes the viewport to <c>&lt;output&gt;/&lt;name&gt;.png</c>. Returns what went
    /// wrong, or null. A flat-colour frame is refused: it is not evidence of anything.</summary>
    private string? Capture(string name, out string path)
    {
        path = Path.Combine(_directory, name + ".png");
        if (GetViewport()?.GetTexture()?.GetImage() is not { } image || image.IsEmpty())
        {
            return "no viewport image";
        }

        float min = 1f;
        float max = 0f;
        for (int y = 0; y < image.GetHeight(); y += 36)
        {
            for (int x = 0; x < image.GetWidth(); x += 40)
            {
                Color c = image.GetPixel(x, y);
                float value = (c.R + c.G + c.B) / 3f;
                min = Mathf.Min(min, value);
                max = Mathf.Max(max, value);
            }
        }

        if (max - min < 0.01f)
        {
            return "the frame is blank (one flat colour)";
        }

        Error error = image.SavePng(path);
        return error == Error.Ok ? null : $"could not write {path} ({error})";
    }

    /// <summary>Records a statement's outcome and moves on.</summary>
    private void Complete(ScriptStep step, bool ok, string output, string? json = null)
    {
        if (!ok)
        {
            _failed++;
            string failure = $"statement {_index + 1} `{step.Raw}`: {FirstLine(output)}";
            if (_firstFailure.Length == 0)
            {
                _firstFailure = failure;
            }

            _report.Fail(failure);
        }

        if (!ok || _verbose)
        {
            GD.Print($"[CON] {(ok ? "ok  " : "FAIL")} {_index + 1} {step.Raw}{(output.Length > 0 ? " -> " + FirstLine(output) : string.Empty)}");
        }

        using (var stream = new MemoryStream())
        {
            using (var line = new Utf8JsonWriter(stream))
            {
                line.WriteStartObject();
                line.WriteNumber("i", _index + 1);
                line.WriteString("cmd", step.Raw);
                line.WriteBoolean("ok", ok);
                line.WriteNumber("frame", Engine.GetProcessFrames());
                line.WriteString("out", output.Length > MaxOutCharacters ? output[..MaxOutCharacters] + "…" : output);
                if (json != null && json.Length <= MaxOutCharacters && IsJson(json))
                {
                    line.WritePropertyName("data");
                    line.WriteRawValue(json, skipInputValidation: true);
                }

                line.WriteEndObject();
            }

            _results?.WriteLine(Encoding.UTF8.GetString(stream.ToArray()));
        }

        // `expect` reads the most recent reply, whichever kind of statement gave it: after
        // `wait-until` or `assert` it must not compare against some earlier command's text. The
        // verbs that only pass time or press a key reply nothing and leave it alone, so
        // `spawn ...; wait 2; expect spawned` still reads the spawn.
        if (step.Verb is not (ScriptVerb.Expect or ScriptVerb.Wait or ScriptVerb.Frames or ScriptVerb.Input))
        {
            _lastOut = output;
        }

        _started = false;
        _ran++;
        _index++;
        if (!ok && _stopOnFail)
        {
            _index = _steps.Count;
        }
    }

    /// <summary>Whether a handler's JSON reply is JSON a strict reader accepts. The engine writes
    /// a non-finite number as a bare word, and one such value must not make the line unreadable.</summary>
    private static bool IsJson(string json)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string FirstLine(string text)
    {
        int end = text.IndexOf('\n');
        string line = end < 0 ? text : text[..end] + " …";
        return line.Length > 200 ? line[..200] + "…" : line;
    }

    /// <summary>Writes the summary line, prints the machine line and quits with its exit code.</summary>
    private void Finish()
    {
        _done = true;
        int ran = _ran;

        // The same rule --quit-after and the lifecycle gate apply: a session that broke an
        // invariant did not pass, whatever its statements replied.
        if (Invariant.Violations > 0)
        {
            string recent = string.Join(" | ", Invariant.Recent);
            _report.Fail($"{Invariant.Violations} invariant violation(s) were recorded during the script" +
                         (recent.Length > 0 ? $": {recent}" : string.Empty));
        }

        _report.Fact("ran", ran)
            .Fact("stopped_early", ran < _steps.Count)
            .Fact("failed", _failed)
            .Fact("first_failure", _firstFailure)
            .Fact("frames", (long)Engine.GetProcessFrames())
            .Fact("invariants", Invariant.Violations)
            .Fact("orphans", (long)Performance.GetMonitor(Performance.Monitor.ObjectOrphanNodeCount))
            .Fact("timescale", Engine.TimeScale)
            .Fact("output", _directory);
        if (_results != null)
        {
            _report.Fact("results", Path.Combine(_directory, "result.ndjson"));
            _results.WriteLine(
                $"{{\"event\":\"result\",\"ok\":{(_report.Passed ? "true" : "false")},\"steps\":{_steps.Count}," +
                $"\"ran\":{ran},\"failed\":{_failed},\"invariants\":{Invariant.Violations}}}");
            _results.Dispose();
            _results = null;
        }

        _report.FinishAndQuit(GetTree());
    }

    public override void _ExitTree()
    {
        _results?.Dispose();
        _results = null;
    }
}
