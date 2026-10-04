using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Embervale.Companions;
using Xunit;

namespace Embervale.Tests;

/// <summary>Parsing and selection of data/story/reactions companion barks.</summary>
public class CompanionReactionTests
{
    private const string Good = """
        { "reactions": [
          { "id": "bark.kael_pass", "flag": "flag.main.the_pass_kept_done", "companion": "companion.kael",
            "textKey": "bark.kael_pass", "loyaltyDelta": 2 },
          { "id": "bark.mira_pass", "flag": "flag.main.the_pass_kept_done", "companion": "companion.mira",
            "textKey": "bark.mira_pass" }
        ] }
        """;

    [Fact]
    public void ParsesAWellFormedFile()
    {
        var errors = new List<string>();

        List<CompanionReaction> reactions = CompanionReactionData.Parse(Good, "t.json", errors);

        Assert.Empty(errors);
        Assert.Equal(2, reactions.Count);
        Assert.Equal(2, reactions[0].LoyaltyDelta);
        Assert.Equal(0, reactions[1].LoyaltyDelta);
        Assert.Equal("flag.bark.kael_pass", reactions[0].BarkFlag);
    }

    [Theory]
    [InlineData("{ nope", "not valid JSON")]
    [InlineData("{}", "\"reactions\" array")]
    [InlineData("{\"reactions\":[{\"id\":\"kael\",\"flag\":\"flag.a\",\"companion\":\"companion.k\",\"textKey\":\"t\"}]}", "must start with 'bark.'")]
    [InlineData("{\"reactions\":[{\"id\":\"bark.a\",\"flag\":\"a\",\"companion\":\"companion.k\",\"textKey\":\"t\"}]}", "must start with 'flag.'")]
    [InlineData("{\"reactions\":[{\"id\":\"bark.a\",\"flag\":\"flag.a\",\"companion\":\"kael\",\"textKey\":\"t\"}]}", "must start with 'companion.'")]
    [InlineData("{\"reactions\":[{\"id\":\"bark.a\",\"flag\":\"flag.a\",\"companion\":\"companion.k\"}]}", "textKey is required")]
    [InlineData("{\"reactions\":[{\"id\":\"bark.a\",\"flag\":\"flag.a\",\"companion\":\"companion.k\",\"textKey\":\"t\",\"loyaltyDelta\":21}]}", "outside")]
    [InlineData("{\"reactions\":[{\"id\":\"bark.a\",\"flag\":\"flag.a\",\"companion\":\"companion.k\",\"textKey\":\"t\",\"loyaltyDelta\":-21}]}", "outside")]
    [InlineData("{\"reactions\":[{\"id\":\"bark.a\",\"flag\":\"flag.a\",\"companion\":\"companion.k\",\"textKey\":\"t\",\"loyaltyDelta\":1.5}]}", "must be an integer")]
    [InlineData("{\"reactions\":[{\"id\":\"bark.a\",\"flag\":\"flag.a\",\"companion\":\"companion.k\",\"textKey\":\"t\"},{\"id\":\"bark.a\",\"flag\":\"flag.b\",\"companion\":\"companion.k\",\"textKey\":\"t\"}]}", "duplicate id")]
    public void ParserReportsMistakes(string json, string expected)
    {
        var errors = new List<string>();

        CompanionReactionData.Parse(json, "t.json", errors);

        Assert.Contains(errors, e => e.Contains(expected, StringComparison.Ordinal));
    }

    [Fact]
    public void LoyaltyDeltaBoundsAreInclusive()
    {
        var errors = new List<string>();
        string json = "{\"reactions\":[" +
                      "{\"id\":\"bark.hi\",\"flag\":\"flag.a\",\"companion\":\"companion.k\",\"textKey\":\"t\",\"loyaltyDelta\":20}," +
                      "{\"id\":\"bark.lo\",\"flag\":\"flag.a\",\"companion\":\"companion.k\",\"textKey\":\"t\",\"loyaltyDelta\":-20}]}";

        Assert.Equal(2, CompanionReactionData.Parse(json, "t.json", errors).Count);
        Assert.Empty(errors);
    }

    [Fact]
    public void OnlyPartyMembersWhoHaveNotBarkedAreDue()
    {
        List<CompanionReaction> reactions = CompanionReactionData.Parse(Good, "t.json", new List<string>());
        var party = new HashSet<string> { "companion.kael" };
        var held = new HashSet<string>();

        List<CompanionReaction> due = CompanionReactionData.Due(reactions, "flag.main.the_pass_kept_done", party.Contains, held.Contains);
        Assert.Equal(new[] { "bark.kael_pass" }, due.Select(r => r.Id));

        held.Add("flag.bark.kael_pass");
        Assert.Empty(CompanionReactionData.Due(reactions, "flag.main.the_pass_kept_done", party.Contains, held.Contains));

        party.Add("companion.mira");
        Assert.Equal(new[] { "bark.mira_pass" },
            CompanionReactionData.Due(reactions, "flag.main.the_pass_kept_done", party.Contains, held.Contains).Select(r => r.Id));

        Assert.Empty(CompanionReactionData.Due(reactions, "flag.other", party.Contains, held.Contains));
    }

    [Fact]
    public void EveryReactionFileInTheRepositoryParsesCleanlyWithUniqueIds()
    {
        string directory = Path.Combine(RepositoryRoot(), "data", "story", "reactions");
        if (!Directory.Exists(directory))
        {
            return;
        }

        var errors = new List<string>();
        var ids = new HashSet<string>();
        foreach (string path in Directory.EnumerateFiles(directory, "*.json").OrderBy(p => p, StringComparer.Ordinal))
        {
            foreach (CompanionReaction reaction in CompanionReactionData.Parse(File.ReadAllText(path), Path.GetFileName(path), errors))
            {
                Assert.True(ids.Add(reaction.Id), $"{Path.GetFileName(path)}: duplicate reaction id '{reaction.Id}'");
            }
        }

        Assert.Empty(errors);
    }

    private static string RepositoryRoot()
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
