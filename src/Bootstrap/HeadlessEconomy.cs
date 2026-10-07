using System.Collections.Generic;
using Embervale.Economy;
using Embervale.Localization;
using Godot;

namespace Embervale.Bootstrap;

/// <summary>
/// Headless economy report (Phase 38N1). Launching with <c>--economy</c> loads every database,
/// reports <see cref="EconomyReport.Arbitrage"/> and quits, without entering gameplay:
/// <code>godot --headless --path . -- --economy</code>
///
/// It exists for the same reason <see cref="HeadlessValidation"/> does, and for one more: the
/// <c>F1</c> console <b>cannot be driven from a remote session at all</b> (CLAUDE.md §3 — no CLI
/// equivalent, and the MCP cannot inject input). A report that could only be reached through the
/// console would ship having never once been run, which is exactly how <c>CraftingComponent.Learn</c>
/// sat with no callers from Phase 15 to Phase 35. Both entry points call the same function, so what
/// the console prints and what this prints cannot drift.
///
/// <para><b>Output.</b> The <c>EMBERVALE_RESULT</c> line carries the route count, how many pay, and
/// the best <c>--top=N</c> routes (default 5) as one string each; <c>--json</c> carries every route
/// as an object (<c>item, from, buy, to, sell, margin</c>), which is what to diff before and after a
/// tuning change. <c>--verbose</c> prints the prose table.</para>
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

    /// <summary>Loads the content databases, reports the arbitrage table, and quits. Call from a node
    /// already in the tree.</summary>
    public static void Run(SceneTree tree)
    {
        ContentDatabases.InitializeAll();
        Loc.Initialize(); // display names come through the catalogue; idempotent

        HeadlessReport report = HeadlessGate.Begin("economy");
        List<ArbitrageRow> rows = EconomyReport.ArbitrageRows();
        int paying = 0;
        foreach (ArbitrageRow row in rows)
        {
            if (row.Margin > 0)
            {
                paying++;
            }
        }

        report.Fact("routes", rows.Count).Fact("paying", paying).Fact("shops", ShopDatabase.All.Count)
            .Fact("best_margin", rows.Count > 0 ? rows[0].Margin : 0);

        if (HeadlessGate.Json)
        {
            var all = new List<object?>();
            foreach (ArbitrageRow row in rows)
            {
                all.Add(new Dictionary<string, object?>
                {
                    ["item"] = row.Item, ["from"] = row.From, ["buy"] = row.Buy, ["to"] = row.To,
                    ["sell"] = row.Sell, ["margin"] = row.Margin,
                });
            }

            report.Fact("rows", all);
        }
        else
        {
            int top = HeadlessArgs.Int("--top", 5);
            var best = new List<string>();
            for (int i = 0; i < rows.Count && i < top; i++)
            {
                ArbitrageRow row = rows[i];
                best.Add($"{row.Margin:+0;-0;0} {row.Item}: buy {row.Buy} at {row.From}, sell {row.Sell} to {row.To}");
            }

            report.Fact("top", best);
        }

        HeadlessGate.Say(EconomyReport.Arbitrage());
        HeadlessGate.Finish(tree, report, legacyLine: false);
    }
}
