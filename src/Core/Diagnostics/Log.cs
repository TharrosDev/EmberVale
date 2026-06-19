using System.Runtime.CompilerServices;
using Godot;

namespace Embervale.Core.Diagnostics;

/// <summary>
/// Thin, centralized logging facade over Godot's print functions.
/// Keeping all logging behind one type means we can later route output to a
/// file, an in-game console, or a telemetry sink without touching call sites.
/// </summary>
public static class Log
{
    public enum Level
    {
        Trace,
        Info,
        Warn,
        Error,
    }

    /// <summary>Messages below this level are suppressed.</summary>
    public static Level MinimumLevel { get; set; } = Level.Trace;

    public static void Trace(string message, [CallerMemberName] string caller = "")
    {
        if (MinimumLevel > Level.Trace)
        {
            return;
        }

        GD.Print($"[TRACE] ({caller}) {message}");
    }

    public static void Info(string message, [CallerMemberName] string caller = "")
    {
        if (MinimumLevel > Level.Info)
        {
            return;
        }

        GD.Print($"[INFO]  {message}");
    }

    public static void Warn(string message, [CallerMemberName] string caller = "")
    {
        if (MinimumLevel > Level.Warn)
        {
            return;
        }

        GD.PushWarning($"[WARN]  ({caller}) {message}");
        GD.Print($"[WARN]  ({caller}) {message}");
    }

    public static void Error(string message, [CallerMemberName] string caller = "")
    {
        GD.PushError($"[ERROR] ({caller}) {message}");
        GD.PrintErr($"[ERROR] ({caller}) {message}");
    }
}
