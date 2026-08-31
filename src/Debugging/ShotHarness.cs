using System;
using System.Collections.Generic;
using Embervale.Core.Diagnostics;
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
/// ⚠️ <b>It needs a real window.</b> Run WITHOUT <c>--headless</c> — there is no framebuffer to read
/// back otherwise, and the failure is a silently empty run.
/// </summary>
public abstract partial class ShotHarness : Node
{
    /// <summary>Frames to let the world settle before the first capture. A region streams in over
    /// many frames and a shot taken too early photographs a half-loaded world.</summary>
    private const int SettleFrames = 90;

    /// <summary>Frames between driving a state and capturing it. UI updates in <c>_Process</c> and
    /// several widgets ease over <see cref="UI.UiTheme"/> durations, so a capture taken too soon
    /// catches the transition rather than the state.</summary>
    private const int HoldFrames = 30;

    private readonly List<(string Name, Action Drive)> _shots = new();
    private readonly List<string> _failures = new();
    private int _index = -1;
    private int _countdown = SettleFrames;
    private bool _capturePending;

    /// <summary>The command-line flag this harness answers to, for logging (e.g. <c>--hudshots</c>).</summary>
    protected abstract string Flag { get; }

    /// <summary>Where the PNGs land, as a <c>user://</c> path.</summary>
    protected abstract string OutputDir { get; }

    /// <summary>Registers the states to capture, in order, via <see cref="Shot"/>.</summary>
    protected abstract void BuildShotList();

    /// <summary>Subclasses prove the requested gameplay/UI state exists before its pixels become
    /// evidence. Return a diagnostic when a player, panel, camera, model or driven state is absent.</summary>
    protected virtual string? ValidateShotState(string name) => null;

    /// <summary>Adds one named state and the action that drives it.</summary>
    protected void Shot(string name, Action drive) => _shots.Add((name, drive));

    public override void _Ready()
    {
        // Pause-immune. Both harnesses photograph states that pause the tree — an open menu for the
        // HUD, an open modal panel for the map — and a paused harness can neither capture them nor
        // advance past them (CLAUDE.md §7's pause deadlock, from the other side).
        ProcessMode = ProcessModeEnum.Always;

        if (DisplayServer.GetName() == "headless")
        {
            Fail("rendering-capable display required; do not use --headless");
            GetTree().Quit(2);
            return;
        }
        DisplayServer.WindowSetSize(new Vector2I(1280, 720));
        if (DirAccess.MakeDirRecursiveAbsolute(OutputDir) != Error.Ok)
        {
            Fail($"could not create output directory {ProjectSettings.GlobalizePath(OutputDir)}");
            GetTree().Quit(2);
            return;
        }
        BuildShotList();
        if (_shots.Count == 0)
        {
            Fail("no capture states were registered");
            GetTree().Quit(2);
            return;
        }
        Log.Info($"{Flag}: {_shots.Count} state(s) queued; output -> {ProjectSettings.GlobalizePath(OutputDir)}");
    }

    public override void _Process(double delta)
    {
        if (_shots.Count == 0)
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
            string? invalid = ValidateShotState(shotName);
            if (invalid is not null)
            {
                Fail($"'{shotName}' prerequisite failed: {invalid}");
            }
            else
            {
                Capture(shotName);
            }

            if (_index + 1 >= _shots.Count)
            {
                if (_failures.Count > 0)
                {
                    Log.Error($"{Flag}: FAILED with {_failures.Count} capture error(s):\n  " +
                              string.Join("\n  ", _failures));
                    GetTree().Quit(1);
                }
                else
                {
                    Log.Info($"{Flag}: wrote {_shots.Count} verified image(s) to {ProjectSettings.GlobalizePath(OutputDir)}");
                    GetTree().Quit(0);
                }
                return;
            }

            _countdown = 1; // drive the next state on the following frame
            return;
        }

        _index++;
        _countdown = HoldFrames;
        _capturePending = true;

        (string name, Action drive) = _shots[_index];
        drive();
        Log.Info($"{Flag}: [{_index + 1}/{_shots.Count}] {name}");
    }

    /// <summary>Renders the current frame to <c>OutputDir/&lt;name&gt;.png</c>.</summary>
    private void Capture(string name)
    {
        if (GetViewport()?.GetTexture()?.GetImage() is not { } image)
        {
            Fail($"no viewport image for '{name}'");
            return;
        }

        if (image.IsEmpty() || image.GetWidth() != 1280 || image.GetHeight() != 720)
        {
            Fail($"'{name}' returned {image.GetWidth()}x{image.GetHeight()}, expected 1280x720");
            return;
        }
        if (IsBlank(image))
        {
            Fail($"'{name}' is blank/flat-colour; it is not valid visual evidence");
            return;
        }

        string path = $"{OutputDir}/{name}.png";
        Error error = image.SavePng(path);
        if (error != Error.Ok)
        {
            Fail($"could not write '{path}' ({error})");
            return;
        }

        if (!FileAccess.FileExists(path))
        {
            Fail($"SavePng reported success but '{path}' is missing");
            return;
        }

        Log.Info($"{Flag}: wrote {ProjectSettings.GlobalizePath(path)} ({image.GetWidth()}x{image.GetHeight()})");
    }

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

    private void Fail(string message)
    {
        _failures.Add(message);
        Log.Error($"{Flag}: {message}");
    }
}
