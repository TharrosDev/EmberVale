using System;
using System.Collections.Generic;
using Godot;

namespace Embervale.Bootstrap;

/// <summary>One command-line flag that attaches a node to a live session.</summary>
/// <param name="Flag">The user argument, e.g. <c>--hudshots</c>.</param>
/// <param name="Create">Builds the node (named) for the session; the shell adds it as its child.</param>
/// <param name="Capture">True for a screenshot harness: the world's performance sampling is turned
/// off for the run, because viewport readback and PNG compression would dominate it. False for a
/// harness that is there to measure.</param>
/// <param name="Alias">A second spelling of the flag, if it has one.</param>
internal sealed record SessionHarness(
    string Flag, Func<GameSession, Node> Create, bool Capture = true, string? Alias = null);

/// <summary>
/// The table of session harnesses: every flag that needs a live session with a real player in it,
/// and the node it attaches. <see cref="GameShellController"/> starts the session (continuing a
/// save, or a fresh one with <c>--new-game</c>) and adds each requested harness.
///
/// <para><b>Adding a harness is one line in <see cref="All"/>.</b> A tooling-only class goes inside
/// the <c>#if EMBERVALE_TOOLING</c> block here, in the <c>Compile Remove</c> list in
/// <c>Embervale.csproj</c> (unless its file name already matches a pattern there, as
/// <c>src/Debugging/*Shots.cs</c> does) and in <c>tools/check_shipping_assembly.py</c>.</para>
///
/// <para>The order is the order the harnesses are attached in, and the first requested one names
/// the run in the log.</para>
/// </summary>
internal static class SessionHarnesses
{
    public static readonly SessionHarness[] All =
    {
#if EMBERVALE_TOOLING
        new("--hudshots", _ => new Debugging.HudShots { Name = "HudShots" }),
        new("--panelshots", session => new Debugging.PanelShots
        {
            Name = "PanelShots",
            Map = session.Ui.Map,
            Journal = session.Ui.QuestLog,
            Character = session.Ui.Inventory,
            Vendor = session.Ui.Vendor,
            Dialogue = session.Ui.Dialogue,
        }),
        new("--uishots", _ => new Debugging.UiAuditShots { Name = "UiAuditShots" }),
        new("--shrine-shots", _ => new Debugging.ShrineShots { Name = "ShrineShots" }),
        new("--guild-shots", session => new Debugging.GuildShots { Name = "GuildShots", Dialogue = session.Ui.Dialogue }),
        new("--enemy-shots", _ => new Debugging.EnemyShots { Name = "EnemyShots" }),
        new("--combat-shots", _ => new Debugging.CombatShots { Name = "CombatShots" }, Alias: "--combatshots"),
        new("--look-shots", _ => new Debugging.LookShots { Name = "LookShots" }),
        new("--metashots", _ => new Debugging.MetaShots { Name = "MetaShots" }),
        new("--tradeshots", _ => new Debugging.TradeShots { Name = "TradeShots" }),
        new("--spellshots", _ => new Debugging.SpellShots { Name = "SpellShots" }),
        new("--camshots", _ => new Debugging.CamShots { Name = "CamShots" }),
        new("--shot", session => Debugging.OneShots.Create(session)),

        // There to measure frame times, so the world's performance sampling stays on for it.
        new("--vfxperf", _ => new Debugging.VfxPerfScenario { Name = "VfxPerf" }, Capture: false),
        new("--exec", session => new Debugging.ConsoleScript { Name = "ConsoleScript", Lifecycle = session.Lifecycle }, Capture: false, Alias: "--exec-file"),
        new("--repro", session => new Debugging.ReproRun { Name = "ReproRun", Console = session.DevTools?.Console }, Capture: false),
        new("--perf-report", _ => new Debugging.SessionPerfReport { Name = "SessionPerfReport" }, Capture: false),
#endif
    };

    /// <summary>The harnesses named in the user arguments (after <c>--</c>), in table order, each with the spelling
    /// that was used.</summary>
    public static List<(SessionHarness Harness, string Flag)> Requested()
    {
        var requested = new List<(SessionHarness, string)>();
        foreach (SessionHarness harness in All)
        {
            if (harness.Alias != null && HeadlessArgs.User.Has(harness.Alias))
            {
                requested.Add((harness, harness.Alias));
            }
            else if (HeadlessArgs.User.Has(harness.Flag))
            {
                requested.Add((harness, harness.Flag));
            }
        }

        return requested;
    }
}
