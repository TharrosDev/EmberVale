using Embervale.Crafting;
using Embervale.Items;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// The pure crafting-skill rules: experience to rank, the rank a recipe tier asks for, and the
/// workmanship a craft comes out at. Quality is derived from the saved craft serial, so the last
/// group pins the property a quickload depends on: the same inputs always give the same piece.
/// </summary>
public class CraftingSkillTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(99, 0)]
    [InlineData(100, 1)]
    [InlineData(299, 1)]
    [InlineData(300, 2)]
    [InlineData(1500, 5)]
    [InlineData(5500, 10)]
    [InlineData(int.MaxValue, 10)]
    [InlineData(-50, 0)]
    public void RankOf_FollowsTheThresholds(int xp, int expected)
    {
        Assert.Equal(expected, CraftingSkill.RankOf(xp));
    }

    [Fact]
    public void XpForRank_StrictlyRises_AndRoundTrips()
    {
        for (int rank = 1; rank <= CraftingSkill.MaxRank; rank++)
        {
            Assert.True(CraftingSkill.XpForRank(rank) > CraftingSkill.XpForRank(rank - 1));
            Assert.Equal(rank, CraftingSkill.RankOf(CraftingSkill.XpForRank(rank)));
            Assert.Equal(rank - 1, CraftingSkill.RankOf(CraftingSkill.XpForRank(rank) - 1));
        }
    }

    [Fact]
    public void RankProgress_StaysInRange_AndEndsFull()
    {
        Assert.Equal(0f, CraftingSkill.RankProgress(0));
        Assert.Equal(0.5f, CraftingSkill.RankProgress(50), 3);
        Assert.Equal(1f, CraftingSkill.RankProgress(CraftingSkill.XpForRank(CraftingSkill.MaxRank) + 999));
    }

    [Theory]
    [InlineData(0, 0)]   // a legacy recipe asks for nothing
    [InlineData(1, 0)]
    [InlineData(2, 1)]
    [InlineData(6, 5)]
    [InlineData(9, 5)]   // a tier past the last realm is clamped, never unreachable
    public void RequiredRank_IsOnePerRealmAboveTheFirst(int tier, int expected)
    {
        Assert.Equal(expected, CraftingSkill.RequiredRank(tier));
        Assert.True(CraftingSkill.RequiredRank(tier) <= CraftingSkill.MaxRank);
    }

    [Fact]
    public void EveryTierIsReachable_ByCraftingTheTierBelow()
    {
        // The climb must never stall: at the rank a tier asks for, that tier's own crafts pay in full.
        for (int tier = 1; tier <= CraftingSkill.MaxTier; tier++)
        {
            int rank = CraftingSkill.RequiredRank(tier);
            Assert.Equal(CraftingSkill.XpPerTier * tier, CraftingSkill.XpForCraft(tier, rank));
            Assert.Equal(CraftingSkill.XpPerTier * tier, CraftingSkill.XpForCraft(tier, rank + 1));
        }
    }

    [Fact]
    public void XpForCraft_IsQuartered_WellBelowTheCraftersRank_ButNeverZero()
    {
        Assert.Equal(10, CraftingSkill.XpForCraft(tier: 1, rank: 1));
        Assert.Equal(2, CraftingSkill.XpForCraft(tier: 1, rank: 2));
        Assert.Equal(2, CraftingSkill.XpForCraft(tier: 0, rank: 9));
        Assert.True(CraftingSkill.XpForCraft(tier: 1, rank: CraftingSkill.MaxRank) >= 1);
    }

    [Fact]
    public void Odds_NeverFallWithRank_AndAlwaysLeaveRoomForStandard()
    {
        for (int tier = 0; tier <= CraftingSkill.MaxTier; tier++)
        {
            (int Fine, int Superior, int Masterwork) previous = CraftingSkill.Odds(0, tier);
            for (int rank = 0; rank <= CraftingSkill.MaxRank; rank++)
            {
                (int Fine, int Superior, int Masterwork) odds = CraftingSkill.Odds(rank, tier);
                Assert.True(odds.Fine >= previous.Fine && odds.Superior >= previous.Superior && odds.Masterwork >= previous.Masterwork);
                Assert.True(odds.Fine >= 0 && odds.Superior >= 0 && odds.Masterwork >= 0);
                Assert.True(odds.Fine + odds.Superior + odds.Masterwork <= 90);
                previous = odds;
            }
        }
    }

    [Fact]
    public void Odds_AtTheBareRequirement_OfferOnlyFine()
    {
        (int fine, int superior, int masterwork) = CraftingSkill.Odds(CraftingSkill.RequiredRank(4), 4);
        Assert.Equal(15, fine);
        Assert.Equal(0, superior);
        Assert.Equal(0, masterwork);
    }

    [Theory]
    [InlineData(0u, CraftQuality.Masterwork)]
    [InlineData(4u, CraftQuality.Masterwork)]
    [InlineData(5u, CraftQuality.Superior)]
    [InlineData(24u, CraftQuality.Superior)]
    [InlineData(25u, CraftQuality.Fine)]
    [InlineData(64u, CraftQuality.Fine)]
    [InlineData(65u, CraftQuality.Standard)]
    [InlineData(99u, CraftQuality.Standard)]
    public void FromRoll_GivesTheBestTiersTheLowestRolls(uint roll, CraftQuality expected)
    {
        Assert.Equal(expected, CraftingSkill.FromRoll((Fine: 40, Superior: 20, Masterwork: 5), roll));
    }

    [Fact]
    public void Roll_IsAPureFunctionOfItsInputs()
    {
        for (int serial = 0; serial < 200; serial++)
        {
            Assert.Equal(
                CraftingSkill.Roll(6, 2, serial, "recipe.steel_cuirass"),
                CraftingSkill.Roll(6, 2, serial, "recipe.steel_cuirass"));
        }
    }

    [Fact]
    public void Roll_SpreadsAcrossSerials_AndAnUnskilledCrafterNeverMakesAMasterwork()
    {
        var seen = new System.Collections.Generic.HashSet<CraftQuality>();
        for (int serial = 0; serial < 2000; serial++)
        {
            seen.Add(CraftingSkill.Roll(CraftingSkill.MaxRank, 1, serial, "recipe.iron_sword"));
            Assert.NotEqual(CraftQuality.Masterwork, CraftingSkill.Roll(0, 1, serial, "recipe.iron_sword"));
            Assert.NotEqual(CraftQuality.Superior, CraftingSkill.Roll(0, 1, serial, "recipe.iron_sword"));
        }

        Assert.Equal(4, seen.Count);
    }

    [Fact]
    public void AffixLuck_IsCapped()
    {
        Assert.Equal(0f, CraftingSkill.AffixLuck(0, 1));
        Assert.True(CraftingSkill.AffixLuck(CraftingSkill.MaxRank, 0) <= 0.3f);
    }

    [Fact]
    public void Workmanship_RisesWithQuality_AndStatsMoveLessThanValue()
    {
        CraftQuality[] tiers = { CraftQuality.Standard, CraftQuality.Fine, CraftQuality.Superior, CraftQuality.Masterwork };
        Assert.Equal(1f, CraftQualities.StatMultiplier(CraftQuality.Standard));
        Assert.Equal(1f, CraftQualities.ValueMultiplier(CraftQuality.Standard));
        for (int i = 1; i < tiers.Length; i++)
        {
            Assert.True(CraftQualities.StatMultiplier(tiers[i]) > CraftQualities.StatMultiplier(tiers[i - 1]));
            Assert.True(CraftQualities.ValueMultiplier(tiers[i]) > CraftQualities.ValueMultiplier(tiers[i - 1]));
            Assert.True(CraftQualities.StatMultiplier(tiers[i]) <= CraftQualities.ValueMultiplier(tiers[i]));
        }
    }

    [Theory]
    [InlineData(CraftingStationType.Hand, "craft.station_hand")]
    [InlineData(CraftingStationType.Forge, "craft.station_forge")]
    [InlineData(CraftingStationType.Workbench, "craft.station_workbench")]
    [InlineData(CraftingStationType.Alchemy, "craft.station_alchemy")]
    public void StationLabelKey_IsStable(CraftingStationType station, string key)
    {
        Assert.Equal(key, CraftingStations.LabelKey(station));
    }
}
