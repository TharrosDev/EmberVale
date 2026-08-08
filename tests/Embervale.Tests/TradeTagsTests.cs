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
        Assert.True(TradeTags.IsKnown(TradeTags.Contraband)); // arrived in 38O, with five goods wearing it
        Assert.False(TradeTags.IsKnown("map"));               // still a promise: nothing wears it
    }

    // --- contraband (38O) ---------------------------------------------------
    //
    // The one tag that fails CLOSED. Every assertion below is the mirror of one above it, and that
    // symmetry is the test: the general-store rule and the untagged-item rule are exactly what a
    // prohibition must not inherit.

    [Fact]
    public void AGeneralStoreRefusesContrabandEvenThoughItBuysAnythingElse()
    {
        // ⚠️ The whole reason the exception exists. Aldreth authors an empty accepted list, so without
        // this branch the town's most respectable counter is also its fence.
        Assert.False(TradeTags.Accepts(Tags(TradeTags.Contraband), Tags()));
        Assert.True(TradeTags.Accepts(Tags(TradeTags.Herb), Tags()));
    }

    [Fact]
    public void ContrabandDominatesTheItemsOtherTags()
    {
        // A stolen signet is contraband AND jewelry. The jeweller takes jewelry and still refuses it —
        // acceptance is any-overlap for every other tag, and this one overrides that.
        Assert.False(TradeTags.Accepts(
            Tags(TradeTags.Contraband, TradeTags.Jewelry), Tags(TradeTags.Jewelry)));

        // And the fence takes it on the strength of the contraband tag alone, without dealing in jewelry.
        Assert.True(TradeTags.Accepts(
            Tags(TradeTags.Contraband, TradeTags.Jewelry), Tags(TradeTags.Contraband)));
    }

    [Fact]
    public void AFenceStillRefusesWhatIsNotHerTrade()
    {
        // The opt-in opens one door, it does not turn a fence into a general store. Her other tags are
        // read exactly as before for anything that is not contraband.
        Assert.False(TradeTags.Accepts(Tags(TradeTags.Herb), Tags(TradeTags.Contraband, TradeTags.Luxury)));
        Assert.True(TradeTags.Accepts(Tags(TradeTags.Luxury), Tags(TradeTags.Contraband, TradeTags.Luxury)));
    }

    [Fact]
    public void ContrabandIsSoldToNobodyByDefault()
    {
        // Against every shop authored before 38O — a non-empty list that simply does not name it.
        Assert.False(TradeTags.Accepts(Tags(TradeTags.Contraband), Tags(TradeTags.Metal, TradeTags.Weapon)));
    }

    [Fact]
    public void TheTwoQuestionsTheRefusalTextAsks()
    {
        Assert.True(TradeTags.IsContraband(Tags(TradeTags.Contraband, TradeTags.Pelt)));
        Assert.False(TradeTags.IsContraband(Tags(TradeTags.Pelt)));
        Assert.False(TradeTags.IsContraband(Tags()));

        Assert.True(TradeTags.IsFence(Tags(TradeTags.Contraband)));
        Assert.False(TradeTags.IsFence(Tags(TradeTags.Luxury)));

        // ⚠️ An empty accepted list is a general store, NOT a fence — the same asymmetry as above, and
        // the one a caller reaching for "does this shop take everything?" would get wrong.
        Assert.False(TradeTags.IsFence(Tags()));
    }

    [Fact]
    public void ASpecialtyIsUnchangedByTheProhibition()
    {
        // Deliberately untouched: the opt-in is about acceptance, never about price. A fence who
        // specialises in contraband pays the ordinary 38F premium for it, by the ordinary rule.
        Assert.True(TradeTags.IsSpecialty(Tags(TradeTags.Contraband), Tags(TradeTags.Contraband)));
        Assert.False(TradeTags.IsSpecialty(Tags(TradeTags.Contraband), Tags()));
    }
}
