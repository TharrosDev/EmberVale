using System;
using System.Collections.Generic;
using Embervale.Core;
using Embervale.UI;
using Godot;

namespace Embervale.Debugging;

/// <summary>
/// An in-game developer console (toggled with <c>F1</c>): a scrollback log + an input line
/// that dispatches text commands to a registry. Commands (spawn, give, xp, time, weather,
/// event, rep, repro, invariants…) are registered by <see cref="DevCommands"/> and reach the
/// gameplay systems through the <see cref="Embervale.Core.Services.ServiceLocator"/>.
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

    public void Register(ConsoleCommand command) => _commands[command.Name] = command;

    public bool IsOpen => _open;

    /// <summary>Shows/hides the console (bound to F1 by the bootstrap).</summary>
    public void Toggle() => SetOpen(!_open);

    public void Print(string text)
    {
        if (!string.IsNullOrEmpty(text))
        {
            _log.AppendText(text + "\n");
        }
    }

    public void ClearLog() => _log.Clear();

    /// <summary>Parses and runs one command line, returning its output (does not print it).</summary>
    public string Execute(string line)
    {
        string[] tokens = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0)
        {
            return string.Empty;
        }

        if (!_commands.TryGetValue(tokens[0], out ConsoleCommand? command))
        {
            return $"unknown command '{tokens[0]}' — try 'help'";
        }

        string[] args = tokens.Length > 1 ? tokens[1..] : Array.Empty<string>();
        try
        {
            return command.Handler(this, args);
        }
        catch (Exception e)
        {
            return $"error: {e.Message}";
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
        UiState.MenuOpen = open;

        if (open)
        {
            Godot.Input.MouseMode = Godot.Input.MouseModeEnum.Visible;
            _input.GrabFocus();
        }
        else
        {
            _input.ReleaseFocus();
            bool playing = GameManager.Instance is { IsPlaying: true };
            Godot.Input.MouseMode = playing
                ? Godot.Input.MouseModeEnum.Captured
                : Godot.Input.MouseModeEnum.Visible;
        }
    }
}
