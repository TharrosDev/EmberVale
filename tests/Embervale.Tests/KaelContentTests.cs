using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Embervale.Companions;
using Embervale.Dialogue;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// Content integrity for Kael's arc (Phase 32E). The in-engine <c>ContentValidator</c> is the real
/// gate, but it needs Godot to run — so the failure modes that a text-authored <c>.tres</c> is most
/// prone to (a <c>Goto</c> pointing at a node that doesn't exist, a string key that was never added
/// to the catalogue, a condition/effect ordinal typed one off) are checked here, where they cost a
/// second instead of a play session.
///
/// These read the repository's authored files directly, so a rename that breaks the arc fails the
/// suite rather than surfacing as a conversation dead-ending mid-playthrough.
/// </summary>
public class KaelContentTests
{
    private static readonly string Root = FindRepositoryRoot();

    private static string DialogueSource => File.ReadAllText(Path.Combine(Root, "data/dialogue/Kael.tres"));

    [Fact]
    public void EveryChoiceLeadsSomewhereReal()
    {
        string source = DialogueSource;
        HashSet<string> nodes = Captures(source, @"^Id = ""([^""]+)""");
        HashSet<string> gotos = Captures(source, @"^Goto = ""([^""]+)""");

        // An empty Goto legitimately ends the conversation; anything else must name a real node.
        foreach (string target in gotos.Where(g => g.Length > 0))
        {
            Assert.True(nodes.Contains(target), $"dialogue.kael: choice targets unknown node '{target}'");
        }
    }

    [Fact]
    public void StartNodeExists()
    {
        string source = DialogueSource;
        Match start = Regex.Match(source, @"^StartNodeId = ""([^""]+)""", RegexOptions.Multiline);

        Assert.True(start.Success, "dialogue.kael: no StartNodeId");
        Assert.Contains(start.Groups[1].Value, Captures(source, @"^Id = ""([^""]+)"""));
    }

    [Fact]
    public void EveryNodeIsReachableFromTheStart()
    {
        // A node no choice reaches is authored content the player can never see.
        string source = DialogueSource;
        HashSet<string> reachable = Captures(source, @"^Goto = ""([^""]+)""");
        reachable.Add("root");

        foreach (string node in NodeIds(source))
        {
            Assert.True(reachable.Contains(node), $"dialogue.kael: node '{node}' is unreachable");
        }
    }

    [Fact]
    public void EveryStringKeyIsInTheCatalogue()
    {
        HashSet<string> catalogue = CatalogueKeys();
        HashSet<string> used = Captures(DialogueSource, @"^(?:Text|SpeakerName) = ""([^""]+)""");

        foreach (string key in used)
        {
            Assert.True(catalogue.Contains(key), $"dialogue.kael: string key '{key}' is not in strings.csv");
        }
    }

    [Fact]
    public void QuestStringKeysAreInTheCatalogue()
    {
        HashSet<string> catalogue = CatalogueKeys();

        foreach (string file in new[] { "data/quests/KaelOath.tres", "data/quests/KaelBrother.tres" })
        {
            string source = File.ReadAllText(Path.Combine(Root, file));
            foreach (string key in Captures(source, @"^(?:Title|Summary|Description) = ""([^""]+)"""))
            {
                Assert.True(catalogue.Contains(key), $"{file}: string key '{key}' is not in strings.csv");
            }
        }
    }

    [Fact]
    public void ConditionAndEffectOrdinalsAreInRange()
    {
        string source = DialogueSource;
        int conditions = Enum.GetValues<DialogueCondition>().Length;
        int effects = Enum.GetValues<DialogueEffect>().Length;

        foreach (int value in Ints(source, @"^Condition = (\d+)"))
        {
            Assert.True(value < conditions, $"dialogue.kael: condition ordinal {value} is out of range");
        }

        foreach (int value in Ints(source, @"^Effect = (\d+)"))
        {
            Assert.True(value < effects, $"dialogue.kael: effect ordinal {value} is out of range");
        }
    }

    [Fact]
    public void TheArcUsesTheCompanionHooksItNeeds()
    {
        string source = DialogueSource;

        // The three beats that make Kael a companion rather than a quest-giver.
        Assert.Contains($"Effect = {(int)DialogueEffect.RecruitCompanion}", source);
        Assert.Contains($"Effect = {(int)DialogueEffect.DismissCompanion}", source);
        Assert.Contains($"Effect = {(int)DialogueEffect.AddCompanionLoyalty}", source);
        Assert.Contains($"Condition = {(int)DialogueCondition.CompanionLoyaltyAtLeast}", source);
    }

    [Fact]
    public void CompanionArgumentsParse()
    {
        // Every <companionId>:<amount> argument in the graph must survive the parser the session
        // uses — a malformed one silently becomes a no-op effect at runtime.
        foreach (string arg in Captures(DialogueSource, @"^(?:Effect|Condition)Arg = ""(companion\.[^""]+)"""))
        {
            Assert.True(CompanionArg.TryParse(arg, out string id, out _), $"unparseable companion arg '{arg}'");
            Assert.StartsWith("companion.", id);
        }
    }

    [Fact]
    public void CompanionResourcePointsAtTheAuthoredArc()
    {
        string companion = File.ReadAllText(Path.Combine(Root, "data/companions/Kael.tres"));

        Assert.Contains(@"LoyaltyQuestId = ""quest.kael.brother""", companion);
        Assert.Contains(@"DialogueId = ""dialogue.kael""", companion);
        Assert.Contains(@"NameKey = ""companion.kael.name""", companion);
    }

    [Fact]
    public void TheLoyaltyQuestFollowsTheRecruitQuest()
    {
        string brother = File.ReadAllText(Path.Combine(Root, "data/quests/KaelBrother.tres"));

        // Personal content that can be taken before the companion is even recruited reads as a bug.
        Assert.Contains(@"PrerequisiteQuestId = ""quest.kael.oath""", brother);
    }

    // --- helpers -------------------------------------------------------------

    private static IEnumerable<string> NodeIds(string source)
    {
        // Node ids are the Id fields that carry a Text field with them; choices have no Id.
        foreach (Match match in Regex.Matches(source, @"^Id = ""([^""]+)""\r?\nText = ", RegexOptions.Multiline))
        {
            yield return match.Groups[1].Value;
        }
    }

    private static HashSet<string> Captures(string source, string pattern)
    {
        var found = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match match in Regex.Matches(source, pattern, RegexOptions.Multiline))
        {
            found.Add(match.Groups[1].Value);
        }

        return found;
    }

    private static IEnumerable<int> Ints(string source, string pattern)
    {
        foreach (Match match in Regex.Matches(source, pattern, RegexOptions.Multiline))
        {
            yield return int.Parse(match.Groups[1].Value);
        }
    }

    private static HashSet<string> CatalogueKeys()
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (string line in File.ReadAllLines(Path.Combine(Root, "data/locale/strings.csv")))
        {
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            int comma = line.IndexOf(',');
            if (comma > 0)
            {
                keys.Add(line[..comma]);
            }
        }

        return keys;
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "project.godot")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory!.FullName;
    }
}
