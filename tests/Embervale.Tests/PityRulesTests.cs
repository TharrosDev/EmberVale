using Embervale.Items;
using Embervale.Loot;
using Embervale.Stats;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// Bad-luck protection, the ledger counter behind it, the deterministic chest seed, the affix level
/// curve and the rarity presentation numbers: the pure rules of the loot lane.
/// </summary>
public class PityRulesTests
{
    [Fact]
    public void AnOrdinaryRunOfBadLuckCostsNothing()
    {
        for (int dry = 0; dry <= PityRules.GraceRolls; dry++)
        {
            Assert.Equal(0f, PityRules.BonusQuality(dry));
            Assert.False(PityRules.IsGuaranteed(dry));
        }
    }

    [Fact]
    public void TheBonusGrowsWithTheStreakAndIsCapped()
    {
        float previous = 0f;
        for (int dry = PityRules.GraceRolls + 1; dry <= 200; dry++)
        {
            float bonus = PityRules.BonusQuality(dry);
            Assert.True(bonus >= previous);
            Assert.InRange(bonus, 0f, PityRules.MaxBonusQuality);
            previous = bonus;
        }

        Assert.Equal(PityRules.QualityPerRoll, PityRules.BonusQuality(PityRules.GraceRolls + 1), 5);
        Assert.Equal(PityRules.MaxBonusQuality, PityRules.BonusQuality(200));
    }

    [Fact]
    public void ALongEnoughStreakForcesRare()
    {
        Assert.Equal(ItemRarity.Common, PityRules.Apply(ItemRarity.Common, PityRules.GuaranteeAt - 1));
        Assert.Equal(ItemRarity.Rare, PityRules.Apply(ItemRarity.Common, PityRules.GuaranteeAt));
        Assert.Equal(ItemRarity.Rare, PityRules.Apply(ItemRarity.Uncommon, PityRules.GuaranteeAt + 5));
        Assert.Equal(ItemRarity.Legendary, PityRules.Apply(ItemRarity.Legendary, PityRules.GuaranteeAt));
    }

    [Theory]
    [InlineData(ItemRarity.Common, 4)]
    [InlineData(ItemRarity.Uncommon, 4)]
    [InlineData(ItemRarity.Rare, 0)]
    [InlineData(ItemRarity.Epic, 0)]
    [InlineData(ItemRarity.Legendary, 0)]
    public void RareOrBetterResetsTheStreak(ItemRarity rarity, int expected)
    {
        Assert.Equal(expected, PityRules.Next(3, rarity));
    }

    [Fact]
    public void TheGuaranteeAlwaysEndsAStreak()
    {
        // Worst case: every natural roll is Common. The streak must reach the guarantee and reset.
        int dry = 0;
        int longest = 0;
        for (int i = 0; i < 200; i++)
        {
            ItemRarity rarity = PityRules.Apply(ItemRarity.Common, dry);
            dry = PityRules.Next(dry, rarity);
            longest = System.Math.Max(longest, dry);
        }

        Assert.Equal(PityRules.GuaranteeAt, longest);
    }

    [Fact]
    public void TheLedgerCountsTheStreakAndRemembersClaims()
    {
        var ledger = new LootLedger();
        ledger.RecordRoll(ItemRarity.Common);
        ledger.RecordRoll(ItemRarity.Uncommon);
        Assert.Equal(2, ledger.DryStreak);
        ledger.RecordRoll(ItemRarity.Epic);
        Assert.Equal(0, ledger.DryStreak);

        Assert.False(ledger.HasClaimed("item.weapon.crownbreaker"));
        Assert.True(ledger.Claim("item.weapon.crownbreaker"));
        Assert.False(ledger.Claim("item.weapon.crownbreaker"));
        Assert.True(ledger.HasClaimed("item.weapon.crownbreaker"));
        Assert.False(ledger.Claim(string.Empty));

        Assert.Equal(1, ledger.NextChestOrdinal());
        Assert.Equal(2, ledger.NextChestOrdinal());
        Assert.NotEqual(0L, ledger.Salt);
    }

    [Fact]
    public void AChestSeedIsStableAndDistinct()
    {
        Assert.Equal(LootSeeds.For("chest.reward.1", 42L), LootSeeds.For("chest.reward.1", 42L));
        Assert.NotEqual(LootSeeds.For("chest.reward.1", 42L), LootSeeds.For("chest.reward.2", 42L));
        Assert.NotEqual(LootSeeds.For("chest.reward.1", 42L), LootSeeds.For("chest.reward.1", 43L));
        Assert.NotEqual(0UL, LootSeeds.For(null, 0L));
    }

    [Fact]
    public void AnAffixRangeIsUnscaledAtLevelOneAndOnALevelLessRoll()
    {
        Assert.Equal(6f, AffixDefinition.ScaleForLevel(6f, 0, AffixDefinition.FlatGrowthPerLevel));
        Assert.Equal(6f, AffixDefinition.ScaleForLevel(6f, 1, AffixDefinition.FlatGrowthPerLevel));
        Assert.Equal(6f, AffixDefinition.ScaleForLevel(6f, 30, 0f));
    }

    [Fact]
    public void AnAffixRangeGrowsWithItemLevelUpToTheCap()
    {
        float previous = 6f;
        for (int level = 2; level <= AffixDefinition.MaxScaledLevel; level++)
        {
            float scaled = AffixDefinition.ScaleForLevel(6f, level, AffixDefinition.FlatGrowthPerLevel);
            Assert.True(scaled > previous);
            previous = scaled;
        }

        // 1 + 0.05 * 49 = 3.45 at the cap, and nothing past it.
        Assert.Equal(6f * 3.45f, AffixDefinition.ScaleForLevel(6f, AffixDefinition.MaxScaledLevel, AffixDefinition.FlatGrowthPerLevel), 3);
        Assert.Equal(
            AffixDefinition.ScaleForLevel(6f, AffixDefinition.MaxScaledLevel, AffixDefinition.FlatGrowthPerLevel),
            AffixDefinition.ScaleForLevel(6f, 90, AffixDefinition.FlatGrowthPerLevel));
    }

    [Fact]
    public void PercentagesGrowSlowerThanFlatAmounts()
    {
        float flat = AffixDefinition.ScaleForLevel(1f, 40, AffixDefinition.FlatGrowthPerLevel);
        float regen = AffixDefinition.ScaleForLevel(1f, 40, AffixDefinition.RegenGrowthPerLevel);
        float percent = AffixDefinition.ScaleForLevel(1f, 40, AffixDefinition.PercentGrowthPerLevel);
        Assert.True(flat > regen && regen > percent && percent > 1f);
    }

    [Fact]
    public void ARegenAffixContributesNoStatModifier()
    {
        var regen = new ItemAffix("affix.suffix.wellspring", "of the Wellspring", AffixKind.Suffix, StatType.Mana, 1.5f,
            ModifierType.Flat, AffixEffect.ManaRegen);
        Assert.Equal(0f, regen.Value);
        Assert.Equal(1.5f, regen.Magnitude);

        var stat = new ItemAffix("affix.prefix.sages", "Sage's", AffixKind.Prefix, StatType.Mana, 20f, ModifierType.Flat);
        Assert.Equal(AffixEffect.Stat, stat.Effect);
        Assert.Equal(20f, stat.Value);
        Assert.Equal(20f, stat.Magnitude);
    }

    [Fact]
    public void RarerLootIsTallerLouderAndHigher()
    {
        Assert.Equal(0f, LootPresentation.BeamHeight(ItemRarity.Common));
        Assert.False(LootPresentation.IsAnnounced(ItemRarity.Uncommon));
        Assert.True(LootPresentation.IsAnnounced(ItemRarity.Rare));

        for (ItemRarity rarity = ItemRarity.Uncommon; rarity <= ItemRarity.Legendary; rarity++)
        {
            ItemRarity below = rarity - 1;
            Assert.True(LootPresentation.BeamHeight(rarity) > LootPresentation.BeamHeight(below));
            Assert.True(LootPresentation.ChimePitch(rarity) > LootPresentation.ChimePitch(below));
            Assert.True(LootPresentation.ChimeVolumeDb(rarity) >= LootPresentation.ChimeVolumeDb(below));
        }
    }
}
