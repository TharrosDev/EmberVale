using Embervale.Core.Diagnostics;
using Embervale.Debugging;
using Godot;

namespace Embervale.Bootstrap;

/// <summary>
/// Headless content-validation entry point. Launching the game with the <c>--validate</c>
/// command-line argument runs the full <see cref="ContentValidator"/> battery (cross-references,
/// well-formedness, and graph reachability) and quits — without entering gameplay — exiting
/// non-zero if anything is broken. This lets the maintainer (and, later, CI) check content from
/// the command line:
/// <code>godot --headless --path . -- --validate</code>
/// (the <c>--</c> forwards <c>--validate</c> as a user argument). <see cref="GameBootstrap"/>
/// defers to this before building the sandbox.
/// </summary>
public static class HeadlessValidation
{
    /// <summary>The command-line argument that triggers headless validation.</summary>
    public const string FlagArgument = "--validate";

    /// <summary>True when <see cref="FlagArgument"/> was passed on the command line (whether as a
    /// user argument after <c>--</c> or as a raw engine argument).</summary>
    public static bool Requested()
    {
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg == FlagArgument)
            {
                return true;
            }
        }

        foreach (string arg in OS.GetCmdlineArgs())
        {
            if (arg == FlagArgument)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Loads the content databases, runs the full validator, prints the report, and quits
    /// the tree with exit code 0 (OK) or 1 (issues found). Call from a node already in the tree.</summary>
    public static void Run(SceneTree tree)
    {
        Log.Info("=== Embervale content validation (--validate) ===");
        ContentDatabases.InitializeAll();

        bool ok = ContentValidator.RunAll(out string report);
        GD.Print(report);
        GD.Print(ok ? "validate: PASS" : "validate: FAIL");

        tree.Quit(ok ? 0 : 1);
    }
}
