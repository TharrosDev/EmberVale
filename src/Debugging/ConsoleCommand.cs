using System;

namespace Embervale.Debugging;

/// <summary>
/// One dev-console command: its name, a usage hint, a one-line summary, and the handler.
/// The handler receives the console (for output/state) and the parsed argument tokens, and
/// returns a line (or lines) to print. Registered into <see cref="DevConsole"/> by
/// <see cref="DevCommands"/>.
/// </summary>
public sealed record ConsoleCommand(string Name, string Usage, string Summary, Func<DevConsole, string[], string> Handler);

/// <summary>
/// What one command line did. <paramref name="Ok"/> is false when the handler called
/// <see cref="DevConsole.Fail"/>, threw, named no command, or (for a handler that predates
/// <c>Fail</c>) replied with text that reads as a failure (<see cref="ConsoleText.LooksFailed"/>).
/// <paramref name="Json"/> is the reply as one JSON value when the handler supplied one
/// (<see cref="DevConsole.Reply(string, string)"/>), else null.
/// </summary>
public readonly record struct ConsoleResult(bool Ok, string Text, string? Json = null);
