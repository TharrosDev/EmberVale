using System.Collections.Generic;
using Embervale.Debugging;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// The text half of the dev console and its script runner (<see cref="ConsoleText"/>): tokens,
/// script splitting, failure classification of legacy replies, comparisons, and the generated
/// command reference (<see cref="ConsoleHelp.Render"/>). Running a command needs the engine.
/// </summary>
public class ConsoleTextTests
{
    [Fact]
    public void Tokenize_SplitsOnWhitespaceAndKeepsQuotedRunsWhole()
    {
        Assert.Equal(new[] { "give", "item.x", "3" }, ConsoleText.Tokenize("  give   item.x\t3 "));
        Assert.Equal(new[] { "expect", "gave 2x Iron Sword" }, ConsoleText.Tokenize("expect \"gave 2x Iron Sword\""));
        Assert.Equal(new[] { "a", "", "b" }, ConsoleText.Tokenize("a \"\" b"));
        Assert.Equal(new[] { "pre fix" }, ConsoleText.Tokenize("pre\" \"fix"));
        Assert.Empty(ConsoleText.Tokenize("   "));
    }

    [Fact]
    public void SplitScript_SplitsOnSemicolonsAndNewlinesAndDropsComments()
    {
        List<string> statements = ConsoleText.SplitScript(
            "seed 7; tp out ;; # a comment; not a statement\n" +
            "  spawn enemy.goblin 3   # trailing\r\n" +
            "# whole line\n" +
            "expect \"a; b # c\"; quit");
        Assert.Equal(new[] { "seed 7", "tp out", "spawn enemy.goblin 3", "expect \"a; b # c\"", "quit" }, statements);
    }

    [Fact]
    public void SplitScript_HashInsideATokenIsNotAComment()
    {
        Assert.Equal(new[] { "flag set flag.a#b" }, ConsoleText.SplitScript("flag set flag.a#b"));
    }

    [Fact]
    public void Classify_NamesRunnerVerbsAndLeavesCommandsWhole()
    {
        ScriptStep wait = ConsoleText.Classify("wait-until enemies.count ge 3 20");
        Assert.Equal(ScriptVerb.WaitUntil, wait.Verb);
        Assert.Equal(new[] { "enemies.count", "ge", "3", "20" }, wait.Args);

        ScriptStep command = ConsoleText.Classify("spawn enemy.goblin 3");
        Assert.Equal(ScriptVerb.Command, command.Verb);
        Assert.Equal("spawn enemy.goblin 3", command.Raw);
        Assert.Equal(new[] { "spawn", "enemy.goblin", "3" }, command.Args);

        Assert.Equal(ScriptVerb.Frames, ConsoleText.Classify("FRAMES 10").Verb);
        Assert.Equal(ScriptVerb.Quit, ConsoleText.Classify("quit").Verb);
    }

    [Theory]
    [InlineData("wait 2", true)]
    [InlineData("wait 2.5s", true)]
    [InlineData("wait", false)]
    [InlineData("wait soon", false)]
    [InlineData("wait 601", false)]
    [InlineData("frames 120", true)]
    [InlineData("frames 1.5", false)]
    [InlineData("frames -1", false)]
    [InlineData("assert player.hp gt 0", true)]
    [InlineData("assert player.hp above 0", false)]
    [InlineData("assert player.hp gt", false)]
    [InlineData("wait-until state eq Playing", true)]
    [InlineData("wait-until state eq Playing 30", true)]
    [InlineData("wait-until state eq Playing 0", false)]
    [InlineData("wait-until state eq Playing soon", false)]
    [InlineData("expect gave", true)]
    [InlineData("expect gave 2x", false)]
    [InlineData("expect \"gave 2x\"", true)]
    [InlineData("shot after-spawn_1", true)]
    [InlineData("shot ../escape", false)]
    [InlineData("shot", false)]
    [InlineData("input attack", true)]
    [InlineData("input attack 30", true)]
    [InlineData("input attack 0", false)]
    [InlineData("input", false)]
    [InlineData("quit", true)]
    [InlineData("quit 3", false)]
    [InlineData("anything at all", true)]
    public void Problem_ChecksRunnerVerbArguments(string statement, bool valid)
    {
        string? problem = ConsoleText.Problem(ConsoleText.Classify(statement));
        Assert.Equal(valid, problem == null);
        if (!valid)
        {
            Assert.StartsWith("usage:", problem);
        }
    }

    [Theory]
    [InlineData("error: boom", true)]
    [InlineData("usage: give <itemId> [qty]", true)]
    [InlineData("unknown item 'item.x'", true)]
    [InlineData("Unknown/undiscovered travel node 'x'", true)]
    [InlineData("no player", true)]
    [InlineData("no clock", true)]
    [InlineData("cannot learn spell.x: corruption below Tainted", true)]
    [InlineData("could not start 'event.x' (already active / unknown)", true)]
    [InlineData("fast-travel service unavailable", true)]
    [InlineData("repro is unavailable in a shipping build", true)]
    [InlineData("no flags set", false)]
    [InlineData("no spells", false)]
    [InlineData("no travel nodes attuned yet", false)]
    [InlineData("no persistent actors", false)]
    [InlineData("gave 2x Iron Sword", false)]
    [InlineData("nothing was running there", false)]
    [InlineData("", false)]
    [InlineData("3 shop(s):\n  shop.a  — closed, merchant unavailable today", false)]
    public void LooksFailed_ClassifiesLegacyReplies(string output, bool failed)
    {
        Assert.Equal(failed, ConsoleText.LooksFailed(output));
    }

    [Theory]
    [InlineData("3", "ge", "3", true)]
    [InlineData("3", "gt", "3", false)]
    [InlineData("2.5", "lt", "3", true)]
    [InlineData("10", "gt", "9", true)] // numeric, not text: "10" < "9" as strings
    [InlineData("3.0000001", "eq", "3", true)]
    [InlineData("3", "ne", "4", true)]
    [InlineData("true", "eq", "1", true)]
    [InlineData("false", "eq", "true", false)]
    [InlineData("Playing", "eq", "playing", true)]
    [InlineData("Playing", "ne", "Loading", true)]
    [InlineData("Playing", "gt", "Loading", false)]
    [InlineData("ember_crown.waystone", "contains", "WAYSTONE", true)]
    [InlineData("ember_crown.waystone", "contains", "frost", false)]
    [InlineData("3", "between", "1", false)]
    public void Compare_ComparesNumbersAsNumbersAndTextWithoutCase(string actual, string op, string expected, bool result)
    {
        Assert.Equal(result, ConsoleText.Compare(actual, op, expected));
    }

    [Fact]
    public void Format_IsInvariantAndCompact()
    {
        Assert.Equal("true", ConsoleText.Format(true));
        Assert.Equal("false", ConsoleText.Format(false));
        Assert.Equal("12.346", ConsoleText.Format(12.34567f));
        Assert.Equal("0.5", ConsoleText.Format(0.5d));
        Assert.Equal("42", ConsoleText.Format(42));
        Assert.Equal("Playing", ConsoleText.Format("Playing"));
        Assert.Equal(string.Empty, ConsoleText.Format(null));
    }

    [Fact]
    public void IsFileName_AllowsOnlyPlainNames()
    {
        Assert.True(ConsoleText.IsFileName("dump-player_01"));
        Assert.False(ConsoleText.IsFileName(""));
        Assert.False(ConsoleText.IsFileName("a/b"));
        Assert.False(ConsoleText.IsFileName("a.png"));
        Assert.False(ConsoleText.IsFileName(new string('a', 65)));
    }

    [Fact]
    public void HelpRender_ListsCommandsRunnerVerbsAndKeys()
    {
        var commands = new[]
        {
            new ConsoleCommand("tp", "tp <x> <z>|out [metres]", "Teleport.", (_, _) => string.Empty),
        };

        string text = ConsoleHelp.Render(commands, string.Empty);
        Assert.Contains("tp <x> <z>|out [metres]  — Teleport.", text);
        Assert.Contains("wait-until <key> <op> <value> [seconds=10]", text);
        Assert.Contains("get keys: state ", text);

        string markdown = ConsoleHelp.Render(commands, "md");
        Assert.StartsWith("| Command | What it does |", markdown);
        Assert.Contains("| `tp <x> <z>\\|out [metres]` | Teleport. |", markdown);
        foreach (string line in markdown.Split('\n'))
        {
            Assert.True(line.Length < 2000, "a documentation line must stay under 2000 characters");
        }
    }

    [Fact]
    public void QueryKeys_AreUniqueAndEveryRunnerVerbIsDocumented()
    {
        Assert.Equal(DevCommands.QueryKeys.Length, new HashSet<string>(DevCommands.QueryKeys).Count);
        foreach (ScriptVerb verb in System.Enum.GetValues<ScriptVerb>())
        {
            if (verb == ScriptVerb.Command)
            {
                continue;
            }

            Assert.Contains(ConsoleHelp.RunnerVerbs, row => ConsoleText.Classify(row.Usage).Verb == verb);
        }
    }
}
