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

	private static int _warnCount;
	private static int _errorCount;

	/// <summary>Warnings written through <see cref="Warn"/> since the process started.</summary>
	public static int WarnCount => System.Threading.Volatile.Read(ref _warnCount);

	/// <summary>Errors written through <see cref="Error"/> since the process started. A run report
	/// reads this instead of scanning the log.</summary>
	public static int ErrorCount => System.Threading.Volatile.Read(ref _errorCount);

	/// <summary>Raised for every warning and error after it is printed, on the thread that logged
	/// it. The flight recorder listens; a handler must not log.</summary>
	public static event System.Action<Level, string>? Written;

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
		System.Threading.Interlocked.Increment(ref _warnCount);
		Written?.Invoke(Level.Warn, message);
	}

	public static void Error(string message, [CallerMemberName] string caller = "")
	{
		GD.PushError($"[ERROR] ({caller}) {message}");
		GD.PrintErr($"[ERROR] ({caller}) {message}");
		System.Threading.Interlocked.Increment(ref _errorCount);
		Written?.Invoke(Level.Error, message);
	}
}
