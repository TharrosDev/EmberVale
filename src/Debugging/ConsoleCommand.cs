using System;

namespace Embervale.Debugging;

/// <summary>
/// One dev-console command: its name, a usage hint, a one-line summary, and the handler.
/// The handler receives the console (for output/state) and the parsed argument tokens, and
/// returns a line (or lines) to print. Registered into <see cref="DevConsole"/> by
/// <see cref="DevCommands"/>.
/// </summary>
public sealed record ConsoleCommand(string Name, string Usage, string Summary, Func<DevConsole, string[], string> Handler);
