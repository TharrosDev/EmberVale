using System.Collections.Generic;
using Embervale.UI;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// Ranked map search (Phase 39.5A). Matching is the easy half; the ordering is the feature, so most
/// of these pin which of two hits comes first rather than merely that both were found.
/// </summary>
public class MapSearchTests
{
    private static readonly MapSearchEntry[] Realm =
    {
        new("location.ember_crown.smith", "The Iron Anvil", "smith blacksmith forge garrick"),
        new("location.embermarket.ironmonger", "Ironmonger's Row", "trade merchant embermarket"),
        new("location.ember_crown.inn", "The Ember Rest", "inn bed rest service"),
        new("location.emberdeep.mine", "The Emberdeep", "mine ore exploration"),
        new("location.tarn.chandler", "Tarn Chandlery", "outfitter rope sail"),
    };

    private static IReadOnlyList<MapSearchHit> Search(string q) => MapSearch.Rank(q, Realm);

    [Fact]
    public void EmptyQuery_ReturnsNothing()
    {
        Assert.Empty(MapSearch.Rank(string.Empty, Realm));
        Assert.Empty(MapSearch.Rank("   ", Realm));
        Assert.Empty(MapSearch.Rank(null!, Realm));
    }

    [Fact]
    public void NullEntries_ReturnsNothingRatherThanThrowing()
    {
        Assert.Empty(MapSearch.Rank("anvil", null!));
    }

    [Fact]
    public void MatchesATermThatIsNotInTheName()
    {
        // The whole point of searching for a trade: "blacksmith" appears nowhere in "The Iron Anvil".
        IReadOnlyList<MapSearchHit> hits = Search("blacksmith");

        Assert.Single(hits);
        Assert.Equal("location.ember_crown.smith", hits[0].Id);
    }

    [Fact]
    public void NameMatchOutranksTermMatch()
    {
        // "iron" is a word in The Iron Anvil's NAME and a prefix of Ironmonger's Row's name;
        // both beat a mere term hit, and the name-prefix beats the mid-name word.
        IReadOnlyList<MapSearchHit> hits = Search("iron");

        Assert.Equal("Ironmonger's Row", hits[0].Name);
        Assert.Equal("The Iron Anvil", hits[1].Name);
    }

    [Fact]
    public void WordPrefix_BeatsBareSubstring()
    {
        IReadOnlyList<MapSearchHit> hits = Search("anvil");

        Assert.Single(hits);
        Assert.Equal("The Iron Anvil", hits[0].Name);
    }

    [Fact]
    public void SubstringInsideAWord_DoesNotCountAsAWordPrefix()
    {
        // "nvi" sits inside "Anvil" — a contains-hit, not a word-prefix hit.
        IReadOnlyList<MapSearchHit> hits = Search("nvi");

        Assert.Single(hits);
        Assert.True(hits[0].Score < 60);
    }

    [Fact]
    public void ExactName_ScoresHighestOfAll()
    {
        IReadOnlyList<MapSearchHit> hits = Search("the emberdeep");

        Assert.Equal("The Emberdeep", hits[0].Name);
        Assert.Equal(100, hits[0].Score);
    }

    [Fact]
    public void SearchIsCaseInsensitive()
    {
        Assert.Equal(Search("IRON ANVIL").Count, Search("iron anvil").Count);
        Assert.Equal("The Iron Anvil", Search("iRoN aNvIl")[0].Name);
    }

    [Fact]
    public void QueryIsTrimmed()
    {
        Assert.Equal("The Iron Anvil", Search("  anvil  ")[0].Name);
    }

    [Fact]
    public void NoMatch_ReturnsEmpty()
    {
        Assert.Empty(Search("dragon"));
    }

    [Fact]
    public void TiedScores_OrderStablyByName()
    {
        // Two entries both matching only on a shared term must come back in a fixed order, or the
        // result list reshuffles under the cursor between two identical searches.
        var tied = new[]
        {
            new MapSearchEntry("b", "Beta Hall", "shared"),
            new MapSearchEntry("a", "Alpha Hall", "shared"),
        };

        IReadOnlyList<MapSearchHit> first = MapSearch.Rank("shared", tied);
        IReadOnlyList<MapSearchHit> second = MapSearch.Rank("shared", tied);

        Assert.Equal("Alpha Hall", first[0].Name);
        Assert.Equal(first[0].Id, second[0].Id);
        Assert.Equal(first[1].Id, second[1].Id);
    }
}
