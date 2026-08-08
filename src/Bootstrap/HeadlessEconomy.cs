using Embervale.Core.Diagnostics;
using Embervale.Economy;
using Embervale.Localization;
using Godot;

namespace Embervale.Bootstrap;

/// <summary>
/// Headless economy report (Phase 38N1). Launching with <c>--economy</c> loads every database, prints
/// <see cref="EconomyReport.Arbitrage"/> and quits, without entering gameplay:
/// <code>godot --headless --path . -- --economy</code>
///
/// It exists for the same reason <see cref="HeadlessValidation"/> does, and for one more: the
/// <c>F1</c> console <b>cannot be driven from a remote session at all</b> (CLAUDE.md §3 — no CLI
/// equivalent, and the MCP cannot inject input). A report that could only be reached through the
/// console would ship having never once been run, which is exactly how <c>CraftingComponent.Learn</c>
/// sat with no callers from Phase 15 to Phase 35. Both entry points call the same function, so what
/// the console prints and what this prints cannot drift.
///
/// Always exits <b>0</b>: an arbitrage table is an observation, not a check. <c>--validate</c> is the
/// gate, and conflating the two would make a thin market fail a build.
/// </summary>
public static class HeadlessEconomy
{
    /// <summary>The command-line argument that triggers the headless report.</summary>
    public const string FlagArgument = "--economy";

    /// <summary>True when <see cref="FlagArgument"/> was passed on the command line.</summary>
    public static bool Requested() => HeadlessValidation.HasFlag(FlagArgument);

    /// <summary>Loads the content databases, prints the arbitrage table, and quits. Call from a node
    /// already in the tree.</summary>
    public static void Run(SceneTree tree)
    {
        Log.Info("=== Embervale economy report (--economy) ===");
        ContentDatabases.InitializeAll();
        Loc.Initialize(); // display names come through the catalogue; idempotent

        GD.Print(EconomyReport.Arbitrage());
        tree.Quit(0);
    }
}
