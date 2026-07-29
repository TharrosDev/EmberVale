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
/// Content integrity for <b>every</b> authored conversation (generalised from KaelContentTests in
/// Phase 33D). The in-engine <c>ContentValidator</c> is the real gate, but it needs Godot; these
/// checks catch the failure modes a text-authored <c>.tres</c> is most prone to — a <c>Goto</c>
/// naming a node that does not exist, an unreachable node no choice leads to, a string key that was
/// never added to the catalogue, a condition or effect ordinal typed one off — in a second rather
/// than a play session.
///
/// The slice (33D) leans hard on gated dialogue across four conversations, so a graph regression is
/// the most likely way to silently break the arc. This is the net under that.
/// </summary>
public class DialogueContentTests
{
    private static readonly string Root = FindRepositoryRoot();

    public static TheoryData<string> DialogueFiles()
    {
        var data = new TheoryData<string>();
        foreach (string path in Directory.EnumerateFiles(Path.Combine(Root, "data/dialogue"), "*.tres"))
        {
            data.Add(Path.GetFileName(path));
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(DialogueFiles))]
    public void EveryGotoNamesARealNode(string file)
    {
        string source = Read(file);
        HashSet<string> nodes = NodeIds(source);

        foreach (string target in Captures(source, @"^Goto = ""([^""]+)""").Where(g => g.Length > 0))
        {
            Assert.True(nodes.Contains(target), $"{file}: choice targets unknown node '{target}'");
        }
    }

    [Theory]
    [MemberData(nameof(DialogueFiles))]
    public void StartNodeExists(string file)
    {
        string source = Read(file);
        Match start = Regex.Match(source, @"^StartNodeId = ""([^""]+)""", RegexOptions.Multiline);

        Assert.True(start.Success, $"{file}: no StartNodeId");
        Assert.True(
            NodeIds(source).Contains(start.Groups[1].Value),
            $"{file}: StartNodeId '{start.Groups[1].Value}' is not a node in this graph");
    }

    [Theory]
    [MemberData(nameof(DialogueFiles))]
    public void EveryNodeIsReachable(string file)
    {
        // A node no choice leads to is authored content the player can never see.
        string source = Read(file);
        HashSet<string> reachable = Captures(source, @"^Goto = ""([^""]+)""");
        Match start = Regex.Match(source, @"^StartNodeId = ""([^""]+)""", RegexOptions.Multiline);
        if (start.Success)
        {
            reachable.Add(start.Groups[1].Value);
        }

        foreach (string node in NodeIds(source))
        {
            Assert.True(reachable.Contains(node), $"{file}: node '{node}' is unreachable");
        }
    }

    [Theory]
    [MemberData(nameof(DialogueFiles))]
    public void EveryNodeOffersAWayOut(string file)
    {
        // A node with no choices ends the conversation abruptly with no player input — always a
        // mistake in this project's graphs, where "(Leave.)" is the explicit exit.
        string source = Read(file);
        foreach (Match node in Regex.Matches(
            source, @"^Id = ""([^""]+)""\r?\nText = [^\r\n]+\r?\n(?<choices>Choices = [^\r\n]*)?", RegexOptions.Multiline))
        {
            string choices = node.Groups["choices"].Value;
            Assert.False(
                string.IsNullOrEmpty(choices) || choices.Contains("([])"),
                $"{file}: node '{node.Groups[1].Value}' has no choices");
        }
    }

    [Theory]
    [MemberData(nameof(DialogueFiles))]
    public void OrdinalsAreInRange(string file)
    {
        string source = Read(file);
        int conditions = Enum.GetValues<DialogueCondition>().Length;
        int effects = Enum.GetValues<DialogueEffect>().Length;

        foreach (int value in Ints(source, @"^Condition = (\d+)"))
        {
            Assert.True(value < conditions, $"{file}: condition ordinal {value} is out of range");
        }

        foreach (int value in Ints(source, @"^Effect = (\d+)"))
        {
            Assert.True(value < effects, $"{file}: effect ordinal {value} is out of range");
        }
    }

    [Theory]
    [MemberData(nameof(DialogueFiles))]
    public void LocKeysResolve(string file)
    {
        // Older conversations carry literal English (a pre-33D inconsistency), so only text that
        // *looks* like a key — dotted, lowercase, no spaces — is required to be in the catalogue.
        // That is exactly the population where a typo is invisible until a player sees the key.
        HashSet<string> catalogue = CatalogueKeys();

        foreach (string text in Captures(Read(file), @"^(?:Text|SpeakerName) = ""([^""]+)"""))
        {
            if (Regex.IsMatch(text, @"^[a-z][a-z0-9_]*(\.[a-z0-9_]+)+$"))
            {
                Assert.True(catalogue.Contains(text), $"{file}: string key '{text}' is not in strings.csv");
            }
        }
    }

    [Theory]
    [MemberData(nameof(DialogueFiles))]
    public void CompanionArgumentsParse(string file)
    {
        foreach (string arg in Captures(Read(file), @"^(?:Effect|Condition)Arg = ""(companion\.[^""]+)"""))
        {
            Assert.True(CompanionArg.TryParse(arg, out string id, out _), $"{file}: unparseable arg '{arg}'");
            Assert.StartsWith("companion.", id);
        }
    }

    [Theory]
    [MemberData(nameof(DialogueFiles))]
    public void NumericEffectArgumentsAreNumeric(string file)
    {
        // AddCorruption's argument is parsed as an int at runtime and silently becomes 0 if it
        // isn't one — a corruption beat that quietly does nothing.
        string source = Read(file);
        foreach (Match match in Regex.Matches(
            source, @"^Effect = (\d+)\r?\nEffectArg = ""([^""]*)""", RegexOptions.Multiline))
        {
            if (int.Parse(match.Groups[1].Value) == (int)DialogueEffect.AddCorruption)
            {
                Assert.True(
                    int.TryParse(match.Groups[2].Value, out _),
                    $"{file}: AddCorruption argument '{match.Groups[2].Value}' is not a number");
            }
        }
    }

    // --- helpers -------------------------------------------------------------

    private static string Read(string file) => File.ReadAllText(Path.Combine(Root, "data/dialogue", file));

    /// <summary>Node ids — the <c>Id</c> fields immediately followed by a <c>Text</c> field.
    /// Choices carry no <c>Id</c>, so this cleanly separates the two.</summary>
    private static HashSet<string> NodeIds(string source)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match match in Regex.Matches(source, @"^Id = ""([^""]+)""\r?\nText = ", RegexOptions.Multiline))
        {
            ids.Add(match.Groups[1].Value);
        }

        return ids;
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
