using System.Collections.Generic;
using Embervale.Dialogue;
using Embervale.Narrative;
using Xunit;

namespace Embervale.Tests;

/// <summary>Pure rules behind the campaign dialogue extensions: argument formats, the two-pair
/// choice gate, start-variant selection and story-card key resolution.</summary>
public class DialogueRulesTests
{
    [Theory]
    [InlineData("faction.iron_syndicate:20", 0, true, "faction.iron_syndicate", 20)]
    [InlineData("faction.dawnwardens:-5", 0, true, "faction.dawnwardens", -5)]
    [InlineData("item.potion.health:3", 1, true, "item.potion.health", 3)]
    [InlineData("item.potion.health", 1, true, "item.potion.health", 1)]
    [InlineData("item.currency.gold: 50 ", 1, true, "item.currency.gold", 50)]
    [InlineData("item.x:abc", 1, false, "item.x", 1)]
    [InlineData(":4", 1, false, "", 4)]
    [InlineData("", 1, false, "", 1)]
    public void ParsesIdAmountArguments(string arg, int fallback, bool ok, string id, int amount)
    {
        bool parsed = DialogueRules.TryParseIdAmount(arg, fallback, out string gotId, out int gotAmount);

        Assert.Equal(ok, parsed);
        if (ok)
        {
            Assert.Equal(id, gotId);
            Assert.Equal(amount, gotAmount);
        }
    }

    [Fact]
    public void ChoiceNeedsBothConditions()
    {
        bool Eval(DialogueCondition c, string arg) => c == DialogueCondition.Always || arg == "yes";

        Assert.True(DialogueRules.ChoiceVisible(DialogueCondition.Always, "", DialogueCondition.Always, "", Eval));
        Assert.True(DialogueRules.ChoiceVisible(DialogueCondition.HasFlag, "yes", DialogueCondition.HasItem, "yes", Eval));
        Assert.False(DialogueRules.ChoiceVisible(DialogueCondition.HasFlag, "yes", DialogueCondition.HasItem, "no", Eval));
        Assert.False(DialogueRules.ChoiceVisible(DialogueCondition.HasFlag, "no", DialogueCondition.Always, "", Eval));
    }

    [Fact]
    public void StartVariantsAreFirstMatchWinsAndFallBackToDefault()
    {
        var variants = new List<(DialogueCondition, string, string)>
        {
            (DialogueCondition.HasFlag, "flag.a", "after_a"),
            (DialogueCondition.HasFlag, "flag.b", "after_b"),
        };
        var held = new HashSet<string>();
        bool Eval(DialogueCondition c, string arg) => held.Contains(arg);
        bool Exists(string node) => true;

        Assert.Equal("root", DialogueRules.SelectStartNode(variants, Eval, Exists, "root"));

        held.Add("flag.b");
        Assert.Equal("after_b", DialogueRules.SelectStartNode(variants, Eval, Exists, "root"));

        held.Add("flag.a");
        Assert.Equal("after_a", DialogueRules.SelectStartNode(variants, Eval, Exists, "root"));
    }

    [Fact]
    public void StartVariantNamingAMissingNodeIsSkipped()
    {
        var variants = new List<(DialogueCondition, string, string)>
        {
            (DialogueCondition.Always, "", "ghost"),
            (DialogueCondition.Always, "", "real"),
        };

        string start = DialogueRules.SelectStartNode(variants, (_, _) => true, n => n == "real", "root");

        Assert.Equal("real", start);
    }

    [Fact]
    public void OnlyShopAndServiceOpenAPanel()
    {
        Assert.True(DialogueRules.OpensPanel(DialogueEffect.OpenShop));
        Assert.True(DialogueRules.OpensPanel(DialogueEffect.OpenService));
        Assert.False(DialogueRules.OpensPanel(DialogueEffect.PlayCards));
        Assert.False(DialogueRules.OpensPanel(DialogueEffect.None));
    }

    [Fact]
    public void StoryCardsStopAtTheFirstMissingKey()
    {
        var keys = new HashSet<string> { "cards.fork.1", "cards.fork.2", "cards.fork.4" };

        Assert.Equal(new[] { "cards.fork.1", "cards.fork.2" }, StoryCards.Keys("cards.fork", keys.Contains));
        Assert.Empty(StoryCards.Keys("cards.none", keys.Contains));
        Assert.Empty(StoryCards.Keys("", _ => true));
    }

    [Fact]
    public void StoryCardsAreCapped()
    {
        Assert.Equal(StoryCards.MaxCards, StoryCards.Keys("any", _ => true).Length);
    }
}
