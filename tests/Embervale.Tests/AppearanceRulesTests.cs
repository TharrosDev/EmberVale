using System.Collections.Generic;
using Embervale.Appearance;
using Embervale.Races;
using Xunit;

namespace Embervale.Tests;

public class AppearanceRulesTests
{
    private static readonly Dictionary<string, AppearanceSlot> Slots = new()
    {
        ["appearance.skin.fair"] = AppearanceSlot.Skin,
        ["appearance.skin.tan"] = AppearanceSlot.Skin,
        ["appearance.skin.deep"] = AppearanceSlot.Skin,
        ["appearance.hair.brown"] = AppearanceSlot.Hair,
        ["appearance.hair.blonde"] = AppearanceSlot.Hair,
        ["appearance.build.average"] = AppearanceSlot.Build,
        ["appearance.build.broad"] = AppearanceSlot.Build,
    };

    private static readonly HashSet<string> Allowed = new() { "appearance.skin.tan", "appearance.hair.blonde", "appearance.build.broad" };

    private static AppearanceSlot? SlotOf(string id) => Slots.TryGetValue(id, out AppearanceSlot slot) ? slot : null;

    private static string? DefaultOf(AppearanceSlot slot) => slot switch
    {
        AppearanceSlot.Skin => "appearance.skin.fair",
        AppearanceSlot.Hair => "appearance.hair.brown",
        AppearanceSlot.Build => "appearance.build.average",
        _ => null,
    };

    private static string[] Resolve(params string[]? saved) =>
        AppearanceRules.Resolve(saved, SlotOf, Allowed.Contains, DefaultOf);

    [Fact]
    public void NothingSaved_ResolvesToEachSlotsDefault()
    {
        string[] picks = Resolve();

        Assert.Equal(AppearanceRules.SlotCount, picks.Length);
        Assert.Equal("appearance.skin.fair", picks[(int)AppearanceSlot.Skin]);
        Assert.Equal("appearance.hair.brown", picks[(int)AppearanceSlot.Hair]);
        Assert.Equal(string.Empty, picks[(int)AppearanceSlot.Eyes]);
        Assert.Equal("appearance.build.average", picks[(int)AppearanceSlot.Build]);
    }

    [Fact]
    public void NullSaved_IsTheSameAsEmpty()
    {
        Assert.Equal(Resolve(), AppearanceRules.Resolve(null, SlotOf, Allowed.Contains, DefaultOf));
    }

    [Fact]
    public void NullAndEmptyIdsAreSkipped()
    {
        Assert.Equal(Resolve(), Resolve("", null!));
        Assert.Equal("appearance.skin.tan", Resolve(null!, "", "appearance.skin.tan")[(int)AppearanceSlot.Skin]);
    }

    [Fact]
    public void AllowedSavedIds_ReplaceTheirSlotsDefaultOnly()
    {
        string[] picks = Resolve("appearance.skin.tan", "appearance.build.broad");

        Assert.Equal("appearance.skin.tan", picks[(int)AppearanceSlot.Skin]);
        Assert.Equal("appearance.hair.brown", picks[(int)AppearanceSlot.Hair]);
        Assert.Equal("appearance.build.broad", picks[(int)AppearanceSlot.Build]);
    }

    [Fact]
    public void DisallowedAndUnknownIds_FallBackToTheDefault()
    {
        // deep exists but the race does not offer it; "appearance.hair.gone" no longer exists at all.
        string[] picks = Resolve("appearance.skin.deep", "appearance.hair.gone");

        Assert.Equal("appearance.skin.fair", picks[(int)AppearanceSlot.Skin]);
        Assert.Equal("appearance.hair.brown", picks[(int)AppearanceSlot.Hair]);
    }

    [Fact]
    public void LastAllowedIdInASlotWins()
    {
        string[] picks = Resolve("appearance.skin.tan", "appearance.skin.deep");

        Assert.Equal("appearance.skin.tan", picks[(int)AppearanceSlot.Skin]);
    }

    [Fact]
    public void ToProfileIds_DropsEmptySlotsAndKeepsSlotOrder()
    {
        string[] ids = AppearanceRules.ToProfileIds(Resolve("appearance.build.broad", "appearance.skin.tan"));

        Assert.Equal(new[] { "appearance.skin.tan", "appearance.hair.brown", "appearance.build.broad" }, ids);
    }

    [Fact]
    public void ProfileIds_SurviveTheHeaderRoundTripAndResolveTheSame()
    {
        string[] chosen = AppearanceRules.ToProfileIds(Resolve("appearance.skin.tan", "appearance.hair.blonde", "appearance.build.broad"));
        var profile = new CharacterProfile { RaceId = "race.valari", AppearanceOptionIds = chosen };

        CharacterProfile restored = CharacterProfile.FromHeaderFields(profile.ToHeaderFields());

        Assert.Equal(chosen, restored.AppearanceOptionIds);
        Assert.Equal(Resolve(chosen), Resolve(restored.AppearanceOptionIds));
    }

    [Fact]
    public void HumanDefaultProfile_ResolvesToDefaults()
    {
        Assert.Equal(Resolve(), Resolve(CharacterProfile.Human.AppearanceOptionIds));
    }

    [Theory]
    [InlineData(0.92f, true)]
    [InlineData(1.0f, true)]
    [InlineData(1.1f, true)]
    [InlineData(1.15f, true)]
    [InlineData(1.3f, false)]
    [InlineData(0.7f, false)]
    public void BuildWithinCap_AllowsOnlyModestWidths(float scale, bool expected)
    {
        Assert.Equal(expected, AppearanceRules.BuildWithinCap(scale));
    }

    [Fact]
    public void MatchesReference_AcceptsAuthoredPrecisionAndRejectsRealChanges()
    {
        (float R, float G, float B) reference = AppearanceRules.SkinReference;

        Assert.True(AppearanceRules.MatchesReference((0.676f, 0.506f, 0.439f), reference));
        Assert.False(AppearanceRules.MatchesReference((0.60f, 0.43f, 0.30f), reference));
    }

    [Theory]
    [InlineData("appearance.skin.tan", true)]
    [InlineData("appearance.", false)]
    [InlineData("background.soldier", false)]
    [InlineData(null, false)]
    public void IsAppearanceId_ChecksShape(string? id, bool expected)
    {
        Assert.Equal(expected, AppearanceRules.IsAppearanceId(id));
    }
}
