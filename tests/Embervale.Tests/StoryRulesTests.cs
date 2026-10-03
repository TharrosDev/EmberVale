using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Embervale.Narrative;
using Xunit;

namespace Embervale.Tests;

/// <summary>The pure story-rule engine, its JSON parser, the built-in rules and the party mirror.</summary>
public class StoryRulesTests
{
    private sealed class Harness
    {
        public readonly HashSet<string> Flags = new();
        public readonly List<string> Beats = new();
        public readonly List<string> SetOrder = new();

        public bool Has(string flag) => Flags.Contains(flag);

        public void Set(string flag)
        {
            if (Flags.Add(flag))
            {
                SetOrder.Add(flag);
            }
        }
    }

    private static StoryRule Rule(string id, string[] all, string[] set, string[]? none = null, string beat = "", string on = "") =>
        new(id, all, none ?? Array.Empty<string>(), set, beat, on);

    private static int Evaluate(StoryRuleEngine engine, Harness h, bool silent = false) =>
        engine.Evaluate(h.Has, h.Set, h.Beats.Add, silent);

    [Fact]
    public void RuleSetsItsFlagsOnlyWhenAllHoldAndNoneDoes()
    {
        var engine = new StoryRuleEngine(new[]
        {
            Rule("rule.a", new[] { "flag.x", "flag.y" }, new[] { "flag.out" }, none: new[] { "flag.block" }),
        });
        var h = new Harness();

        h.Flags.Add("flag.x");
        Assert.Equal(0, Evaluate(engine, h));
        Assert.DoesNotContain("flag.out", h.Flags);

        h.Flags.Add("flag.y");
        h.Flags.Add("flag.block");
        Assert.Equal(0, Evaluate(engine, h));

        h.Flags.Remove("flag.block");
        Assert.Equal(1, Evaluate(engine, h));
        Assert.Contains("flag.out", h.Flags);
    }

    [Fact]
    public void EvaluationIsIdempotent()
    {
        var engine = new StoryRuleEngine(new[] { Rule("rule.a", new[] { "flag.x" }, new[] { "flag.out" }, beat: "go") });
        var h = new Harness();
        h.Flags.Add("flag.x");

        Assert.Equal(1, Evaluate(engine, h));
        Assert.Equal(0, Evaluate(engine, h));
        Assert.Equal(0, Evaluate(engine, h));

        Assert.Single(h.SetOrder, f => f == "flag.out");
        Assert.Equal(new[] { "go" }, h.Beats);
    }

    [Fact]
    public void ChainedRulesSettleInOneCall()
    {
        var engine = new StoryRuleEngine(new[]
        {
            Rule("rule.b", new[] { "flag.mid" }, new[] { "flag.end" }),
            Rule("rule.a", new[] { "flag.start" }, new[] { "flag.mid" }),
        });
        var h = new Harness();
        h.Flags.Add("flag.start");

        Assert.Equal(2, Evaluate(engine, h));
        Assert.Contains("flag.end", h.Flags);
    }

    [Fact]
    public void ARuleThatNegatesItselfSettlesInsteadOfLooping()
    {
        // Sets the very flag its own `none` guard reads: it must fire once and stop.
        var engine = new StoryRuleEngine(new[]
        {
            Rule("rule.self", new[] { "flag.x" }, new[] { "flag.done" }, none: new[] { "flag.done" }),
        });
        var h = new Harness();
        h.Flags.Add("flag.x");

        Assert.Equal(1, Evaluate(engine, h));
        Assert.Equal(0, Evaluate(engine, h));
    }

    [Fact]
    public void BeatFiresOncePerSessionAndNeverWhenSilent()
    {
        var loud = new StoryRuleEngine(new[] { Rule("rule.a", new[] { "flag.x" }, new[] { "flag.out" }, beat: "pale_reveal") });
        var h = new Harness();
        h.Flags.Add("flag.x");
        Evaluate(loud, h);
        h.Flags.Remove("flag.out");
        Evaluate(loud, h); // conditions still hold, flag re-derived, beat not repeated
        Assert.Equal(new[] { "pale_reveal" }, h.Beats);
        Assert.Contains("flag.out", h.Flags);

        var quiet = new StoryRuleEngine(new[] { Rule("rule.a", new[] { "flag.x" }, new[] { "flag.out" }, beat: "pale_reveal") });
        var h2 = new Harness();
        h2.Flags.Add("flag.x");
        Evaluate(quiet, h2, silent: true);
        Assert.Contains("flag.out", h2.Flags);
        Assert.Empty(h2.Beats);
        Evaluate(quiet, h2); // the catch-up counted as the firing
        Assert.Empty(h2.Beats);
    }

    [Fact]
    public void BeatOnlyRuleAnnouncesWhenConditionsFirstHold()
    {
        var engine = new StoryRuleEngine(new[] { Rule("rule.a", new[] { "flag.x" }, Array.Empty<string>(), beat: "lamp") });
        var h = new Harness();

        Evaluate(engine, h);
        Assert.Empty(h.Beats);

        h.Flags.Add("flag.x");
        Evaluate(engine, h);
        Evaluate(engine, h);
        Assert.Equal(new[] { "lamp" }, h.Beats);
    }

    [Fact]
    public void TriggerRulesOnlyFireOnTheirTriggerAndHonourGuards()
    {
        var engine = new StoryRuleEngine(new[]
        {
            Rule("rule.opening", Array.Empty<string>(), new[] { "flag.main.opening_done" },
                none: new[] { "flag.main.smoke_done" }, on: StoryRule.OpeningFinished),
        });
        var h = new Harness();

        Evaluate(engine, h);
        Assert.Empty(h.Flags);

        engine.Trigger(StoryRule.OpeningFinished, h.Has, h.Set, h.Beats.Add);
        Assert.Contains("flag.main.opening_done", h.Flags);

        var guarded = new Harness();
        guarded.Flags.Add("flag.main.smoke_done");
        engine.Trigger(StoryRule.OpeningFinished, guarded.Has, guarded.Set, guarded.Beats.Add);
        Assert.DoesNotContain("flag.main.opening_done", guarded.Flags);
    }

    [Fact]
    public void TriggerThenSettlesFlagRulesTheNewFlagsSatisfy()
    {
        var engine = new StoryRuleEngine(new[]
        {
            Rule("rule.opening", Array.Empty<string>(), new[] { "flag.a" }, on: StoryRule.OpeningFinished),
            Rule("rule.next", new[] { "flag.a" }, new[] { "flag.b" }),
        });
        var h = new Harness();

        engine.Trigger(StoryRule.OpeningFinished, h.Has, h.Set, h.Beats.Add);

        Assert.Contains("flag.b", h.Flags);
    }

    [Fact]
    public void AllFiveTestimoniesSetTheAggregateFlag()
    {
        var engine = new StoryRuleEngine(StoryRuleData.BuiltIn());
        var h = new Harness();

        foreach (string flag in StoryRuleData.TestimonyFlags.Take(4))
        {
            h.Flags.Add(flag);
        }

        Evaluate(engine, h);
        Assert.DoesNotContain(StoryRuleData.TestimoniesAllFlag, h.Flags);

        h.Flags.Add(StoryRuleData.TestimonyFlags[4]);
        Evaluate(engine, h);
        Assert.Contains(StoryRuleData.TestimoniesAllFlag, h.Flags);
        Assert.Equal(5, StoryRuleData.TestimonyFlags.Length);
        Assert.Equal(
            new[] { "flag.testimony.iron", "flag.testimony.storm", "flag.testimony.beast", "flag.testimony.prophet", "flag.testimony.queen" },
            StoryRuleData.TestimonyFlags);
    }

    [Fact]
    public void PartyMirrorSetsAndClearsOnlyWhatDiffers()
    {
        string[] all = { "companion.kael", "companion.mira", "companion.toren" };
        var held = new HashSet<string> { "flag.party.companion.mira", "flag.party.companion.toren" };

        (List<string> set, List<string> clear) = PartyFlagMirror.Plan(all, new[] { "companion.kael", "companion.mira" }, held.Contains);

        Assert.Equal(new[] { "flag.party.companion.kael" }, set);
        Assert.Equal(new[] { "flag.party.companion.toren" }, clear);

        foreach (string flag in set)
        {
            held.Add(flag);
        }

        foreach (string flag in clear)
        {
            held.Remove(flag);
        }

        (List<string> again, List<string> noClear) = PartyFlagMirror.Plan(all, new[] { "companion.kael", "companion.mira" }, held.Contains);
        Assert.Empty(again);
        Assert.Empty(noClear);
        Assert.Equal("flag.party.companion.kael", StoryRuleData.PartyFlag("companion.kael"));
    }

    [Fact]
    public void CodeWrittenFlagsListEverythingTheCodeSets()
    {
        List<string> flags = StoryRuleData.CodeWrittenFlags(new[] { "companion.kael" }).ToList();

        Assert.Contains("flag.testimonies_all", flags);
        Assert.Contains("flag.party.companion.kael", flags);
    }

    // --- parser --------------------------------------------------------------

    [Fact]
    public void ParsesAWellFormedFile()
    {
        const string json = """
            { "rules": [
              { "id": "rule.one", "all": ["flag.a"], "none": ["flag.b"], "set": ["flag.c"], "beat": "pale_reveal" },
              { "id": "rule.open", "on": "opening_finished", "set": ["flag.main.opening_done"] }
            ] }
            """;
        var errors = new List<string>();

        List<StoryRule> rules = StoryRuleData.Parse(json, "test.json", errors);

        Assert.Empty(errors);
        Assert.Equal(2, rules.Count);
        Assert.Equal(new[] { "flag.a" }, rules[0].All);
        Assert.Equal(new[] { "flag.b" }, rules[0].None);
        Assert.Equal("pale_reveal", rules[0].Beat);
        Assert.True(rules[1].IsTriggered);
    }

    [Theory]
    [InlineData("{ not json", "not valid JSON")]
    [InlineData("[]", "\"rules\" array")]
    [InlineData("{\"rules\":[{\"id\":\"bad\",\"all\":[\"flag.a\"],\"set\":[\"flag.b\"]}]}", "must start with 'rule.'")]
    [InlineData("{\"rules\":[{\"id\":\"rule.x\",\"all\":[\"a\"],\"set\":[\"flag.b\"]}]}", "must be a flag id")]
    [InlineData("{\"rules\":[{\"id\":\"rule.x\",\"all\":[\"flag.a\"],\"set\":[\"b\"]}]}", "must be a flag id")]
    [InlineData("{\"rules\":[{\"id\":\"rule.x\",\"set\":[\"flag.b\"]}]}", "non-empty \"all\"")]
    [InlineData("{\"rules\":[{\"id\":\"rule.x\",\"all\":[\"flag.a\"]}]}", "does nothing")]
    [InlineData("{\"rules\":[{\"id\":\"rule.x\",\"on\":\"later\",\"set\":[\"flag.b\"]}]}", "unknown trigger")]
    [InlineData("{\"rules\":[{\"id\":\"rule.x\",\"all\":[\"flag.a\"],\"set\":[\"flag.b\"],\"beat\":\"Bad Beat\"}]}", "beat")]
    [InlineData("{\"rules\":[{\"id\":\"rule.x\",\"all\":[\"flag.a\"],\"set\":[\"flag.b\"]},{\"id\":\"rule.x\",\"all\":[\"flag.a\"],\"set\":[\"flag.b\"]}]}", "duplicate id")]
    public void ParserReportsMistakes(string json, string expected)
    {
        var errors = new List<string>();

        StoryRuleData.Parse(json, "test.json", errors);

        Assert.Contains(errors, e => e.Contains(expected, StringComparison.Ordinal));
    }

    [Fact]
    public void EveryRuleFileInTheRepositoryParsesCleanlyWithUniqueIds()
    {
        string directory = Path.Combine(RepositoryRoot(), "data", "story", "rules");
        if (!Directory.Exists(directory))
        {
            return;
        }

        var errors = new List<string>();
        var ids = new HashSet<string>();
        foreach (string path in Directory.EnumerateFiles(directory, "*.json").OrderBy(p => p, StringComparer.Ordinal))
        {
            foreach (StoryRule rule in StoryRuleData.Parse(File.ReadAllText(path), Path.GetFileName(path), errors))
            {
                Assert.True(ids.Add(rule.Id), $"{Path.GetFileName(path)}: duplicate rule id '{rule.Id}'");
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
