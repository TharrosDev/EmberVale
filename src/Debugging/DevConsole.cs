using System;
using System.Collections.Generic;
using Embervale.Core;
using Embervale.Core.Diagnostics;
using Embervale.UI;
using Godot;

namespace Embervale.Debugging;

/// <summary>
/// An in-game developer console (toggled with <c>F1</c>): a scrollback log + an input line
/// that dispatches text commands to a registry. The commands are registered by
/// <see cref="DevCommands"/> (<c>help</c> lists them, <c>--console-help</c> prints them without a
/// session) and reach the gameplay systems through the
/// <see cref="Embervale.Core.Services.ServiceLocator"/>.
///
/// <para><see cref="Run"/> is the typed entry: it returns whether the command worked and, for the
/// commands that have one, the reply as JSON. A <c>--json</c> token asks for that JSON as the text
/// too. <see cref="Execute"/> is the same call returning only the text, which is what the F1 prompt
/// prints. From a shell the console is driven by <c>--exec</c> (<c>ConsoleScript</c>).</para>
///
/// While open it frees the mouse, grabs keyboard focus and sets <see cref="UiState.MenuOpen"/>
/// so typing never drives the character. Runs with <see cref="Node.ProcessModeEnum.Always"/>
/// so it is usable while the game is paused. Built through <see cref="UiTheme"/>.
/// </summary>
public partial class DevConsole : CanvasLayer
{
    private readonly Dictionary<string, ConsoleCommand> _commands = new(StringComparer.OrdinalIgnoreCase);

    private PanelContainer _panel = null!;
    private RichTextLabel _log = null!;
    private LineEdit _input = null!;
    private bool _open;

    public IReadOnlyDictionary<string, ConsoleCommand> Commands => _commands;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        Layer = 9;
        Build();
        SetOpen(false);

        DevCommands.RegisterAll(this);
        Print("Embervale dev console. Type 'help'. F1 closes.");
    }

    public void Register(ConsoleCommand command)
    {
        if (!_commands.TryAdd(command.Name, command))
            throw new InvalidOperationException($"duplicate dev-console command '{command.Name}'");
    }

    public bool IsOpen => _open;

    /// <summary>Shows/hides the console (bound to F1 by the bootstrap).</summary>
    public void Toggle() => SetOpen(!_open);

    public void Print(string text)
    {
        if (!string.IsNullOrEmpty(text))
        {
            _log?.AppendText(text + "\n");
        }
    }

    public void ClearLog() => _log?.Clear();

    /// <summary>The token that asks a command for its JSON reply as the text.</summary>
    public const string JsonFlag = "--json";

    private bool _failed;
    private string? _json;

    /// <summary>True while a handler runs for a line that carried <see cref="JsonFlag"/>.</summary>
    public bool WantsJson { get; private set; }

    /// <summary>The session this console belongs to, or null (a detached console has none).</summary>
    public Embervale.Bootstrap.GameSession? Session =>
        (GetParent() as Embervale.Bootstrap.DeveloperToolsHost)?.Session;

    /// <summary>Marks the running command as failed and returns <paramref name="message"/>, so a
    /// handler writes <c>return console.Fail("unknown item ...")</c>.</summary>
    public string Fail(string message)
    {
        _failed = true;
        return message;
    }

    /// <summary>Attaches a JSON reply to the running command. <paramref name="json"/> is one JSON
    /// value, already serialised. Returns the JSON when the line asked for it with
    /// <see cref="JsonFlag"/>, else <paramref name="text"/>.</summary>
    public string Reply(string text, string json)
    {
        _json = json;
        return WantsJson ? json : text;
    }

    /// <summary><see cref="Reply(string, string)"/> for a Godot dictionary or array.</summary>
    public string Reply(string text, Variant data) => Reply(text, Json.Stringify(data));

    /// <summary>Parses and runs one command line, returning its output (does not print it).</summary>
    public string Execute(string line) => Run(line).Text;

    /// <summary><see cref="Run"/> as one JSON object, <c>{"ok":bool,"out":string,"data":any?}</c>,
    /// for a caller that cannot read a C# struct (the GDScript scenario driver).</summary>
    public string ExecuteJson(string line)
    {
        ConsoleResult result = Run(line);
        var reply = new Godot.Collections.Dictionary { ["ok"] = result.Ok, ["out"] = result.Text };
        if (result.Json != null)
        {
            reply["data"] = Json.ParseString(result.Json);
        }

        return Json.Stringify(reply);
    }

    /// <summary>Parses and runs one command line. Double quotes group a token; a <c>--json</c>
    /// token is removed and sets <see cref="WantsJson"/> for the handler.</summary>
    public ConsoleResult Run(string line)
    {
        var tokens = new List<string>(ConsoleText.Tokenize(line));
        if (tokens.Count == 0)
        {
            return new ConsoleResult(true, string.Empty);
        }

        if (!_commands.TryGetValue(tokens[0], out ConsoleCommand? command))
        {
            return new ConsoleResult(false, $"unknown command '{tokens[0]}' — try 'help'");
        }

        // A handler may run other lines (repro does): keep the outer command's state.
        (bool outerFailed, string? outerJson, bool outerWantsJson) = (_failed, _json, WantsJson);
        bool wantsJson = tokens.RemoveAll(token => token == JsonFlag) > 0;
        (_failed, _json, WantsJson) = (false, null, wantsJson);
        try
        {
            string text = command.Handler(this, tokens.GetRange(1, tokens.Count - 1).ToArray());
            bool ok = !_failed && (_json != null || !ConsoleText.LooksFailed(text));
            return new ConsoleResult(ok, text, _json);
        }
        catch (Exception e)
        {
            Log.Error($"dev command '{line}' threw: {e}");
            return new ConsoleResult(false, $"error: {e.Message}");
        }
        finally
        {
            (_failed, _json, WantsJson) = (outerFailed, outerJson, outerWantsJson);
        }
    }

    // --- Construction -------------------------------------------------------

    private void Build()
    {
        _panel = UiTheme.Panel();
        _panel.SetAnchorsPreset(Control.LayoutPreset.TopWide);
        _panel.OffsetLeft = 0;
        _panel.OffsetRight = 0;
        _panel.OffsetTop = 0;
        _panel.OffsetBottom = 320;
        AddChild(_panel);

        MarginContainer pad = UiTheme.Padding(8);
        _panel.AddChild(pad);

        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 6);
        pad.AddChild(col);

        _log = new RichTextLabel
        {
            BbcodeEnabled = false,
            ScrollActive = true,
            ScrollFollowing = true,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            FocusMode = Control.FocusModeEnum.None,
        };
        _log.AddThemeFontSizeOverride("normal_font_size", UiTheme.BodyFontSize);
        col.AddChild(_log);

        _input = new LineEdit { PlaceholderText = "command…" };
        _input.TextSubmitted += OnSubmit;
        col.AddChild(_input);
    }

    private void OnSubmit(string text)
    {
        text = text.Trim();
        _input.Clear();
        if (text.Length == 0)
        {
            return;
        }

        Print("> " + text);
        Print(Execute(text));
        _input.GrabFocus();
    }

    private void SetOpen(bool open)
    {
        _open = open;
        _panel.Visible = open;
        // Diagnostic overlay: freezing the simulation under it would change what is being
        // diagnosed, so it takes the controls without taking the clock.
        if (open) UiState.Open(this, pausesWorld: false); else UiState.Close(this);

        if (open)
        {
            Godot.Input.MouseMode = Godot.Input.MouseModeEnum.Visible;
            _input.GrabFocus();
        }
        else
        {
            _input.ReleaseFocus();
            // Only recapture if no other menu still owns the mouse (e.g. the console was opened
            // over the inventory) — otherwise the player would look around behind that open menu.
            bool playing = GameManager.Instance is { IsPlaying: true };
            Godot.Input.MouseMode = playing && !UiState.MenuOpen
                ? Godot.Input.MouseModeEnum.Captured
                : Godot.Input.MouseModeEnum.Visible;
        }
    }
}
