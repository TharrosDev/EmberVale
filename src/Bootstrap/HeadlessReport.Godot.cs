using Godot;

namespace Embervale.Bootstrap;

/// <summary>The engine-facing half of <see cref="HeadlessReport"/>: where the line goes and where
/// <c>--report</c> comes from.</summary>
public sealed partial class HeadlessReport
{
    /// <summary>The command-line argument naming a file to write the JSON to.</summary>
    public const string ReportArgument = "--report";

    /// <summary>Prints the <c>EMBERVALE_RESULT</c> line to stdout, writes <c>--report=&lt;path&gt;</c>
    /// when given, and returns the exit code. Call once, as the gate's last act.</summary>
    public int Finish()
    {
        if (BuildFreshness.IsStale)
        {
            Fact("stale_binary", true);
            Warn(BuildFreshness.Detail);
        }

        // GD.Print rather than Log: the line is a protocol, and a level prefix would break it.
        return Emit(line => GD.Print(line), HeadlessArgs.Value(ReportArgument));
    }

    /// <summary><see cref="Finish"/>, then quits the tree with the exit code.</summary>
    public void FinishAndQuit(SceneTree tree) => tree.Quit(Finish());
}
