using System.Collections.Generic;
using Embervale.Economy;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// Pins the two questions a merchant's identity hangs off (Phase 38F): will she take this, and is it her
/// trade? Both answers reach the player as a refusal or a price, so both are worth holding still.
///
/// The rule most worth protecting here is that <b>both empties mean yes</b>. Failing closed instead would
/// turn every authoring gap into unsellable loot, and the symptom — one merchant refusing everything —
/// looks exactly like the feature being broken rather than like data being unfinished.
/// </summary>
public class TradeTagsTests
{
    private static List<string> Tags(params string[] tags) => new(tags);

    [Fact]
    public void AMerchantWithNoAcceptedListBuysAnything()
    {
        // This is how a general store is authored, and it is also what every shop written before 38F
        // says — so the field arrived without changing a single existing merchant.
        Assert.True(TradeTags.Accepts(Tags(TradeTags.Herb), Tags()));
        Assert.True(TradeTags.Accepts(Tags(TradeTags.Relic, TradeTags.Luxury), Tags()));
    }

    [Fact]
    public void AnUntaggedItemIsAcceptedEverywhere()
    {
        // A new item is never silently unsellable while its trade is still being decided.
        Assert.True(TradeTags.Accepts(Tags(), Tags(TradeTags.Metal, TradeTags.Weapon)));
    }

    [Fact]
    public void AcceptanceIsAnyOverlapNotEveryTag()
    {
        // A leather cap is armor AND leather; a smith who takes armour takes it, even though he has no
        // use for leather on its own. Requiring every tag to match would make multi-tag items homeless.
        Assert.True(TradeTags.Accepts(Tags(TradeTags.Armor, TradeTags.Leather), Tags(TradeTags.Armor)));
        Assert.False(TradeTags.Accepts(Tags(TradeTags.Herb, TradeTags.Reagent), Tags(TradeTags.Metal)));
    }

    [Fact]
    public void SpecialtyNeedsBothSidesToSaySomething()
    {
        Assert.True(TradeTags.IsSpecialty(Tags(TradeTags.Metal), Tags(TradeTags.Metal, TradeTags.Weapon)));

        // Unlike acceptance, an empty specialty list is NOT a yes — a general store that specialised in
        // everything would pay the premium on every item in the game.
        Assert.False(TradeTags.IsSpecialty(Tags(TradeTags.Metal), Tags()));
        Assert.False(TradeTags.IsSpecialty(Tags(), Tags(TradeTags.Metal)));
    }

    [Fact]
    public void TheVocabularyIsClosed()
    {
        Assert.True(TradeTags.IsKnown(TradeTags.Potion));
        Assert.False(TradeTags.IsKnown("potions"));   // the typo the validator exists to catch
        Assert.False(TradeTags.IsKnown(""));
        Assert.False(TradeTags.IsKnown("contraband")); // arrives with 38O, when something wears it
    }
}
