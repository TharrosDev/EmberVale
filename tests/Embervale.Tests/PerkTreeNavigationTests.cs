using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Embervale.UI;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// The perk tree's 2026-10 rules: the four node visuals, d-pad steps that follow the prerequisite
/// lines, and the route lit to the focused perk. The last test walks every authored branch, because a
/// focus rule that follows lines can strand a perk no line and no grid step leads to, and nothing but
/// a controller would ever notice.
/// </summary>
public class PerkTreeNavigationTests
{
    [Theory]
    [InlineData(PerkNodeState.Maxed, 3, PerkNodeVisual.Maxed)]
    [InlineData(PerkNodeState.Learnable, 0, PerkNodeVisual.Available)]
    [InlineData(PerkNodeState.NeedsPoints, 0, PerkNodeVisual.Available)]
    [InlineData(PerkNodeState.Locked, 0, PerkNodeVisual.Locked)]
    [InlineData(PerkNodeState.CorruptionGated, 0, PerkNodeVisual.Locked)]
    [InlineData(PerkNodeState.Learnable, 2, PerkNodeVisual.Owned)]
    [InlineData(PerkNodeState.NeedsPoints, 1, PerkNodeVisual.Owned)]
    [InlineData(PerkNodeState.Locked, 1, PerkNodeVisual.Owned)]
    public void VisualOf_OneOfFourForEveryStateAndRank(PerkNodeState state, int rank, PerkNodeVisual expected)
    {
        Assert.Equal(expected, PerkTreeRules.VisualOf(state, rank));
    }

    // Warrior-shaped: (tier, column), and its lines as (prerequisite, dependent) indices.
    private static readonly (int Tier, int Column)[] Grid =
    {
        (1, 0), (1, 1), (1, 2), (1, 3), // 0 might, 1 toughness, 2 iron stance, 3 brawler
        (2, 0), (2, 1), (2, 2),         // 4 warding, 5 second wind, 6 riposte
        (3, 1), (3, 2),                 // 7 crushing blows, 8 shield wall
        (4, 1),                         // 9 unbroken
        (5, 1),                         // 10 warlord
    };

    private static readonly (int From, int To)[] Lines =
    {
        (1, 5), (2, 6), (0, 7), (6, 8), (1, 9), (7, 10),
    };

    [Theory]
    [InlineData(1, PerkTreeDirection.Down, 5)]   // the nearer of the two perks Toughness opens
    [InlineData(0, PerkTreeDirection.Down, 7)]   // along the line, past the unrelated perk straight below
    [InlineData(7, PerkTreeDirection.Up, 0)]     // and back up the same line
    [InlineData(9, PerkTreeDirection.Up, 1)]
    [InlineData(10, PerkTreeDirection.Up, 7)]
    [InlineData(6, PerkTreeDirection.Down, 8)]
    public void AlongEdges_VerticalStepsFollowTheLine(int from, PerkTreeDirection direction, int expected)
    {
        Assert.Equal(expected, PerkTreeRules.FocusTargetAlongEdges(Grid, Lines, from, direction));
    }

    [Theory]
    [InlineData(3, PerkTreeDirection.Down)]  // Brawler opens nothing
    [InlineData(4, PerkTreeDirection.Down)]  // Warding opens nothing
    [InlineData(4, PerkTreeDirection.Up)]    // and requires nothing
    [InlineData(10, PerkTreeDirection.Down)]
    [InlineData(0, PerkTreeDirection.Up)]
    public void AlongEdges_WithNoLineThatWayFallsBackToTheGridStep(int from, PerkTreeDirection direction)
    {
        Assert.Equal(
            PerkTreeRules.FocusTarget(Grid, from, direction),
            PerkTreeRules.FocusTargetAlongEdges(Grid, Lines, from, direction));
    }

    [Theory]
    [InlineData(0, PerkTreeDirection.Right)]
    [InlineData(3, PerkTreeDirection.Left)]
    [InlineData(7, PerkTreeDirection.Right)]
    [InlineData(0, PerkTreeDirection.Left)]
    public void AlongEdges_SidewaysIsTheGridStep(int from, PerkTreeDirection direction)
    {
        Assert.Equal(
            PerkTreeRules.FocusTarget(Grid, from, direction),
            PerkTreeRules.FocusTargetAlongEdges(Grid, Lines, from, direction));
    }

    [Fact]
    public void AlongEdges_IgnoresALineThatRunsTheWrongWayOrOffTheGrid()
    {
        // A prerequisite authored on the same row is not "up", and an index outside the cells is not a perk.
        var cells = new[] { (1, 0), (1, 1), (2, 0) };
        var lines = new[] { (1, 0), (0, 7) };
        Assert.Equal(-1, PerkTreeRules.FocusTargetAlongEdges(cells, lines, 0, PerkTreeDirection.Up));
        Assert.Equal(2, PerkTreeRules.FocusTargetAlongEdges(cells, lines, 0, PerkTreeDirection.Down));
    }

    [Fact]
    public void PathTo_RunsUpToTheFirstOwnedPerkAndNoFurther()
    {
        var requires = new Dictionary<string, string[]>
        {
            ["warlord"] = new[] { "crushing" },
            ["crushing"] = new[] { "might" },
            ["might"] = new[] { "root" },
        };
        IEnumerable<string> Of(string id) => requires.TryGetValue(id, out string[]? found) ? found : Array.Empty<string>();

        Assert.Equal(
            new[] { ("crushing", "warlord"), ("might", "crushing"), ("root", "might") },
            PerkTreeRules.PathTo("warlord", Of, _ => false));

        // Might is owned: the route to buy stops at it.
        Assert.Equal(
            new[] { ("crushing", "warlord"), ("might", "crushing") },
            PerkTreeRules.PathTo("warlord", Of, id => id == "might"));

        Assert.Empty(PerkTreeRules.PathTo("root", Of, _ => false));
    }

    [Fact]
    public void PathTo_TakesEveryPrerequisiteAndSurvivesALoop()
    {
        var requires = new Dictionary<string, string[]>
        {
            ["c"] = new[] { "a", "b" },
            ["a"] = new[] { "c" }, // a bad record; the walk must still end
        };
        IEnumerable<string> Of(string id) => requires.TryGetValue(id, out string[]? found) ? found : Array.Empty<string>();

        List<(string From, string To)> path = PerkTreeRules.PathTo("c", Of, _ => false);
        Assert.Contains(("a", "c"), path);
        Assert.Contains(("b", "c"), path);
        Assert.True(path.Count <= 4);
    }

    /// <summary>Every authored branch, read from <c>data/perks</c> as text (a <c>.tres</c> cannot be loaded
    /// here): starting on the branch's first perk, the four steps reach every perk in it.</summary>
    [Fact]
    public void EveryAuthoredPerkIsReachableWithTheDPad()
    {
        string folder = Path.Combine(FindRepositoryRoot(), "data", "perks");
        var perks = Directory.EnumerateFiles(folder, "*.tres").Select(ReadPerk).ToList();
        Assert.NotEmpty(perks);

        foreach (IGrouping<string, PerkRow> branch in perks.GroupBy(p => p.Branch))
        {
            List<PerkRow> rows = branch.OrderBy(p => p.Tier).ThenBy(p => p.Column).ThenBy(p => p.Id, StringComparer.Ordinal).ToList();
            var cells = rows.Select(r => (r.Tier, r.Column)).ToList();
            var index = rows.Select((r, i) => (r.Id, i)).ToDictionary(t => t.Id, t => t.i);
            var lines = new List<(int From, int To)>();
            foreach (PerkRow row in rows)
            {
                foreach (string prerequisite in row.Prerequisites)
                {
                    if (index.TryGetValue(prerequisite, out int from))
                    {
                        lines.Add((from, index[row.Id]));
                    }
                }
            }

            var reached = new HashSet<int> { 0 };
            var open = new Queue<int>();
            open.Enqueue(0);
            while (open.Count > 0)
            {
                int at = open.Dequeue();
                foreach (PerkTreeDirection direction in Enum.GetValues<PerkTreeDirection>())
                {
                    int next = PerkTreeRules.FocusTargetAlongEdges(cells, lines, at, direction);
                    if (next >= 0 && reached.Add(next))
                    {
                        open.Enqueue(next);
                    }
                }
            }

            string[] stranded = rows.Where((_, i) => !reached.Contains(i)).Select(r => r.Id).ToArray();
            Assert.True(stranded.Length == 0, $"branch {branch.Key}: no d-pad route to {string.Join(", ", stranded)}");
        }
    }

    private sealed record PerkRow(string Id, string Branch, int Tier, int Column, string[] Prerequisites);

    private static PerkRow ReadPerk(string path)
    {
        string text = File.ReadAllText(path);
        string Field(string name, string fallback)
        {
            Match match = Regex.Match(text, @"^" + name + @" = (.*)$", RegexOptions.Multiline);
            return match.Success ? match.Groups[1].Value.Trim() : fallback;
        }

        string[] prerequisites = Regex.Matches(Field("PrerequisiteIds", string.Empty), "\"([^\"]+)\"")
            .Select(m => m.Groups[1].Value)
            .ToArray();
        return new PerkRow(
            Field("Id", "\"\"").Trim('"'),
            Field("Branch", "0"),
            int.Parse(Field("Tier", "1")),
            int.Parse(Field("Column", "0")),
            prerequisites);
    }

    private static string FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "project.godot")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("project.godot not found");
    }
}
