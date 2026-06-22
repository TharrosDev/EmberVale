using System.Collections.Generic;
using Embervale.Debugging;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// Exercises the pure dialogue-graph reachability analysis behind <c>validate-all</c>
/// (Phase 22E). The analyzer is deliberately Godot-free so it runs under <c>dotnet test</c>
/// without the engine; these are the "intentionally-broken test graphs" the Done-when bar
/// calls for, plus a well-formed control proving no false positives.
/// </summary>
public class DialogueGraphAnalysisTests
{
    private static DialogueGraphAnalysis.Node Node(string id, bool terminal, params string[] gotos)
        => new(id, gotos, terminal);

    [Fact]
    public void WellFormedGraph_ReportsNothing()
    {
        // root → offer → accepted(end); root → bye(end). Mirrors the shape of the Elder graph.
        var nodes = new List<DialogueGraphAnalysis.Node>
        {
            Node("root", terminal: true, "offer", "bye"), // a "Farewell" choice makes root terminal too
            Node("offer", terminal: true, "accepted"),    // a "decline" choice ends here
            Node("accepted", terminal: true),
            Node("bye", terminal: true),
        };

        DialogueGraphAnalysis.Result result = DialogueGraphAnalysis.Analyze("root", nodes);

        Assert.Empty(result.Unreachable);
        Assert.Empty(result.DeadEnds);
    }

    [Fact]
    public void OrphanNode_IsReportedUnreachable()
    {
        var nodes = new List<DialogueGraphAnalysis.Node>
        {
            Node("root", terminal: true, "offer"),
            Node("offer", terminal: true),
            Node("orphan", terminal: true), // never targeted by any goto
        };

        DialogueGraphAnalysis.Result result = DialogueGraphAnalysis.Analyze("root", nodes);

        Assert.Contains("orphan", result.Unreachable);
        Assert.DoesNotContain("root", result.Unreachable);
        Assert.DoesNotContain("offer", result.Unreachable);
        Assert.Empty(result.DeadEnds);
    }

    [Fact]
    public void DeadEndLoop_IsReported()
    {
        // root → a ⇄ b, no node terminal: reachable but no path ever reaches a conversation end.
        var nodes = new List<DialogueGraphAnalysis.Node>
        {
            Node("root", terminal: false, "a"),
            Node("a", terminal: false, "b"),
            Node("b", terminal: false, "a"),
        };

        DialogueGraphAnalysis.Result result = DialogueGraphAnalysis.Analyze("root", nodes);

        Assert.Empty(result.Unreachable);
        Assert.Contains("root", result.DeadEnds);
        Assert.Contains("a", result.DeadEnds);
        Assert.Contains("b", result.DeadEnds);
    }

    [Fact]
    public void LoopWithAnExit_IsNotADeadEnd()
    {
        // root → a ⇄ b, but b can also end: the whole reachable set can reach an end.
        var nodes = new List<DialogueGraphAnalysis.Node>
        {
            Node("root", terminal: false, "a"),
            Node("a", terminal: false, "b"),
            Node("b", terminal: true, "a"), // b has both a loop-back and a terminating choice
        };

        DialogueGraphAnalysis.Result result = DialogueGraphAnalysis.Analyze("root", nodes);

        Assert.Empty(result.Unreachable);
        Assert.Empty(result.DeadEnds);
    }
}
