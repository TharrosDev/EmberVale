using Godot;

namespace Embervale.Core;

/// <summary>
/// What kind of run this is (Phase 33E). The project grew up as a sandbox: a training dummy, a debug
/// goblin camp, loose loot, a spell tome on a plinth, and a row of single-key cheats all live in the
/// bootstrap because they made the systems testable. None of them belong in front of a stranger
/// playing the vertical slice, and a capture with a dummy standing in the town square is a capture
/// nobody can use.
///
/// Rather than deleting that scaffolding — it is still the fastest way to exercise the systems — it
/// is gated here:
///
/// <list type="bullet">
/// <item>an <b>exported</b> build is the slice, automatically (<see cref="OS.IsDebugBuild"/> is
/// false), with no flags to remember;</item>
/// <item>an editor/dev run keeps everything, exactly as before;</item>
/// <item><c>--capture</c> gives a dev run the clean experience, so the capture build can be checked
/// without exporting first.</item>
/// </list>
/// </summary>
public static class BuildProfile
{
    /// <summary>Command-line flag that makes a development run behave like an exported one.</summary>
    public const string CaptureFlag = "--capture";

    private static bool? _capture;

    /// <summary>True when this run should present as a finished build: no sandbox props, no
    /// developer overlays, no debug hotkeys.</summary>
    public static bool IsCapture => !OS.IsDebugBuild() || CaptureRequested;

    /// <summary>True when the sandbox's test props (dummy, debug camp, loose loot, spell tome)
    /// should be placed.</summary>
    public static bool SpawnSandboxContent => !IsCapture;

    /// <summary>True when the developer tools (F1 console, F3 debug HUD, F4 profiler) and the
    /// single-key cheats should be available.</summary>
    public static bool ShowDeveloperTools => !IsCapture;

    private static bool CaptureRequested => _capture ??= HasFlag(CaptureFlag);

    private static bool HasFlag(string flag)
    {
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg == flag)
            {
                return true;
            }
        }

        foreach (string arg in OS.GetCmdlineArgs())
        {
            if (arg == flag)
            {
                return true;
            }
        }

        return false;
    }
}
