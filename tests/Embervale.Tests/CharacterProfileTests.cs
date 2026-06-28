using System.Collections.Generic;
using Embervale.Races;
using Xunit;

namespace Embervale.Tests;

public class CharacterProfileTests
{
    [Fact]
    public void RoundTrip_PreservesAllFields()
    {
        var original = new CharacterProfile
        {
            RaceId = "race.umbral",
            CharacterName = "Shade",
            AppearanceOptionIds = new[] { "skin.ash", "hair.black" },
            Background = "Exile of the deep wood.",
        };

        CharacterProfile restored = CharacterProfile.FromHeaderFields(original.ToHeaderFields());

        Assert.Equal("race.umbral", restored.RaceId);
        Assert.Equal("Shade", restored.CharacterName);
        Assert.Equal(new[] { "skin.ash", "hair.black" }, restored.AppearanceOptionIds);
        Assert.Equal("Exile of the deep wood.", restored.Background);
    }

    [Fact]
    public void RoundTrip_EmptyAppearance_StaysEmpty()
    {
        var original = new CharacterProfile { RaceId = "race.valari", CharacterName = "Lyra" };

        CharacterProfile restored = CharacterProfile.FromHeaderFields(original.ToHeaderFields());

        Assert.Empty(restored.AppearanceOptionIds);
        Assert.Equal("race.valari", restored.RaceId);
    }

    [Fact]
    public void FromHeaderFields_MissingKeys_FallBackToHumanDefaults()
    {
        CharacterProfile restored = CharacterProfile.FromHeaderFields(new Dictionary<string, string>());

        Assert.Equal("race.human", restored.RaceId);
        Assert.Equal("Wanderer", restored.CharacterName);
        Assert.Empty(restored.AppearanceOptionIds);
        Assert.Equal(string.Empty, restored.Background);
    }

    [Fact]
    public void FromHeaderFields_BlankRaceId_FallsBackToHuman()
    {
        var fields = new Dictionary<string, string> { ["race_id"] = "", ["char_name"] = "" };

        CharacterProfile restored = CharacterProfile.FromHeaderFields(fields);

        Assert.Equal("race.human", restored.RaceId);
        Assert.Equal("Wanderer", restored.CharacterName);
    }
}
