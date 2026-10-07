using System.Collections.Generic;
using Godot;

namespace Embervale.Bootstrap;

/// <summary>
/// The process's command line, parsed once. Every gate, harness and session flag reads it through
/// here, so a flag spelt <c>--key=value</c> or <c>--key value</c> works the same way for all of them.
///
/// <para>User arguments (after <c>--</c>) are asked first, then the raw engine arguments, which is
/// the fallback <c>HeadlessValidation.HasFlag</c> always had. The two lists are parsed separately so
/// a bare flag at the end of one never takes the first argument of the other as its value. The
/// parsing itself is <see cref="CommandLineArgs"/>.</para>
/// </summary>
public static class HeadlessArgs
{
    private static CommandLineArgs? _user;
    private static CommandLineArgs? _engine;

    /// <summary>The arguments after <c>--</c>.</summary>
    public static CommandLineArgs User => _user ??= new CommandLineArgs(OS.GetCmdlineUserArgs());

    /// <summary>The raw engine arguments.</summary>
    public static CommandLineArgs Engine => _engine ??= new CommandLineArgs(OS.GetCmdlineArgs());

    public static bool Has(string flag) => User.Has(flag) || Engine.Has(flag);

    public static string? Value(string flag) => User.Has(flag) ? User.Value(flag) : Engine.Value(flag);

    public static int Int(string flag, int fallback) => Source(flag).Int(flag, fallback);

    public static float Float(string flag, float fallback) => Source(flag).Float(flag, fallback);

    public static IReadOnlyList<string> List(string flag) => Source(flag).List(flag);

    private static CommandLineArgs Source(string flag) => User.Has(flag) ? User : Engine;
}
