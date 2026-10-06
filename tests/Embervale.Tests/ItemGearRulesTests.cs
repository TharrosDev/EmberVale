using System.Collections.Generic;
using Embervale.Items;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// The pure rules behind gear: set counting, pack room for a swap, the equip gate, arrow damage and
/// the arithmetic of each unique effect kind.
/// </summary>
public class ItemGearRulesTests
{
    // --- SetRules ---

    [Fact]
    public void CountPieces_CountsPerSet_AndIgnoresItemsWithNoSet()
    {
        Dictionary<string, int> counts = SetRules.CountPieces(new[]
        {
            ("item.armor.emberguard_helm", "set.emberguard"),
            ("item.armor.emberguard_chest", "set.emberguard"),
            ("item.weapon.steel_sword", ""),
            ("item.ring.starfall", "set.starfall"),
        });

        Assert.Equal(2, counts["set.emberguard"]);
        Assert.Equal(1, counts["set.starfall"]);
        Assert.Equal(2, counts.Count);
    }

    [Fact]
    public void CountPieces_TheSamePieceTwiceCountsOnce()
    {
        Dictionary<string, int> counts = SetRules.CountPieces(new[]
        {
            ("item.ring.starfall", "set.starfall"),
            ("item.ring.starfall", "set.starfall"),
        });

        Assert.Equal(1, counts["set.starfall"]);
    }

    [Theory]
    [InlineData(2, 1, false)]
    [InlineData(2, 2, true)]
    [InlineData(2, 5, true)]
    [InlineData(4, 3, false)]
    [InlineData(0, 0, false)]
    [InlineData(-1, 3, false)]
    public void IsActive_NeedsTheThreshold_AndNeverGrantsToNothingWorn(int required, int worn, bool expected)
    {
        Assert.Equal(expected, SetRules.IsActive(required, worn));
    }

    [Fact]
    public void NextThreshold_IsTheLowestOneNotYetMet()
    {
        int[] thresholds = { 2, 4, 5 };
        Assert.Equal(2, SetRules.NextThreshold(thresholds, 1));
        Assert.Equal(4, SetRules.NextThreshold(thresholds, 2));
        Assert.Equal(5, SetRules.NextThreshold(thresholds, 4));
        Assert.Equal(0, SetRules.NextThreshold(thresholds, 5));
    }

    // --- InventoryRules ---

    [Theory]
    [InlineData(1, 1, 0, 1)]     // a sword takes a slot
    [InlineData(3, 1, 99, 3)]    // merge space means nothing to a non-stackable
    [InlineData(20, 60, 0, 1)]   // a wrap of arrows, no stack to join
    [InlineData(20, 60, 25, 0)]  // joins the stack already there
    [InlineData(70, 60, 5, 2)]   // 65 left over two slots
    [InlineData(60, 60, 0, 1)]
    [InlineData(61, 60, 0, 2)]
    [InlineData(0, 60, 0, 0)]
    public void SlotsNeeded_CountsNewSlotsAfterToppingUp(int quantity, int maxStack, int mergeSpace, int expected)
    {
        Assert.Equal(expected, InventoryRules.SlotsNeeded(quantity, maxStack, mergeSpace));
    }

    [Fact]
    public void CheckEquip_LevelIsJudgedFirst()
    {
        Assert.Equal(EquipRefusal.LevelTooLow, InventoryRules.CheckEquip(18, 17, true, true, 5, 0));
        Assert.Equal(EquipRefusal.None, InventoryRules.CheckEquip(18, 18, false, false, 0, 0));
        Assert.Equal(EquipRefusal.None, InventoryRules.CheckEquip(0, 1, false, false, 0, 0));
    }

    [Fact]
    public void CheckEquip_AnOffHandItemCannotSitBesideATwoHandedWeapon()
    {
        Assert.Equal(EquipRefusal.HandsFull, InventoryRules.CheckEquip(0, 1, true, true, 0, 3));
        Assert.Equal(EquipRefusal.None, InventoryRules.CheckEquip(0, 1, true, false, 0, 3));
        Assert.Equal(EquipRefusal.None, InventoryRules.CheckEquip(0, 1, false, true, 1, 1));
    }

    [Fact]
    public void CheckEquip_ASwapNeedsRoomForEverythingItTakesOff()
    {
        // A two-handed weapon displacing a sword and a shield frees one slot and needs two.
        Assert.Equal(EquipRefusal.PackFull, InventoryRules.CheckEquip(0, 1, false, false, 2, 1));
        Assert.Equal(EquipRefusal.None, InventoryRules.CheckEquip(0, 1, false, false, 2, 2));
    }

    [Fact]
    public void EquipReasonKey_IsEmptyOnlyWhereThereIsNothingToSay()
    {
        Assert.Equal(InventoryRules.LevelReasonKey, InventoryRules.ReasonKey(EquipRefusal.LevelTooLow));
        Assert.Equal(InventoryRules.HandsReasonKey, InventoryRules.ReasonKey(EquipRefusal.HandsFull));
        Assert.Equal(InventoryRules.PackFullReasonKey, InventoryRules.ReasonKey(EquipRefusal.PackFull));
        Assert.Equal(string.Empty, InventoryRules.ReasonKey(EquipRefusal.None));
        Assert.Equal(string.Empty, InventoryRules.ReasonKey(EquipRefusal.NotHeld));
    }

    // --- AmmoRules ---

    [Theory]
    [InlineData(0, 0f)]
    [InlineData(1, 0f)]
    [InlineData(2, 4f)]
    [InlineData(6, 20f)]
    [InlineData(-3, 0f)]
    public void ArrowTier_AddsFlatDamageAboveTheFirst(int tier, float expected)
    {
        Assert.Equal(expected, AmmoRules.BonusDamage(tier));
    }

    // --- UniqueEffectRules ---

    [Fact]
    public void Rolls_RespectsTheChance()
    {
        Assert.True(UniqueEffectRules.Rolls(1f, 0.999f));
        Assert.True(UniqueEffectRules.Rolls(0.25f, 0.2f));
        Assert.False(UniqueEffectRules.Rolls(0.25f, 0.25f));
        Assert.False(UniqueEffectRules.Rolls(0f, 0f));
    }

    [Fact]
    public void Below_IsStrict_AndAZeroThresholdNeverTriggers()
    {
        Assert.True(UniqueEffectRules.Below(0.29f, 0.3f));
        Assert.False(UniqueEffectRules.Below(0.3f, 0.3f));
        Assert.False(UniqueEffectRules.Below(0f, 0f));
    }

    [Fact]
    public void ExecuteIsJudgedOnTheHealthTheBlowFound()
    {
        // 100 max, left at 10 by a hit of 30: it found the target at 40%, which is not below 30%.
        float before = UniqueEffectRules.FractionBeforeHit(10f, 30f, 100f);
        Assert.Equal(0.4f, before, 3);
        Assert.False(UniqueEffectRules.Below(before, 0.3f));
    }

    [Fact]
    public void BonusDamageAndKillHeal_ScaleByTheMagnitude()
    {
        Assert.Equal(15f, UniqueEffectRules.BonusDamage(30f, 0.5f));
        Assert.Equal(0f, UniqueEffectRules.BonusDamage(30f, -1f));
        Assert.Equal(16f, UniqueEffectRules.KillHeal(200f, 0.08f), 3);
        Assert.Equal(200f, UniqueEffectRules.KillHeal(200f, 3f));
    }

    [Fact]
    public void Reflected_RecoversWhatTheGuardStoppedFromTheChip()
    {
        // 70% mitigation: a chip of 30 means 70 was stopped; 40% of that is 28.
        Assert.Equal(28f, UniqueEffectRules.Reflected(30f, 0.7f, 0.4f), 3);
        Assert.Equal(0f, UniqueEffectRules.Reflected(30f, 0f, 0.4f));
        Assert.Equal(0f, UniqueEffectRules.Reflected(30f, 1f, 0.4f));
        Assert.Equal(0f, UniqueEffectRules.Reflected(0f, 0.7f, 0.4f));
    }

    [Fact]
    public void GoldBonus_CarriesTheFraction_SoSmallPickupsAddUp()
    {
        float carry = 0f;
        int total = 0;
        for (int i = 0; i < 10; i++)
        {
            total += UniqueEffectRules.GoldBonus(2, 0.25f, carry, out carry);
        }

        // Ten pickups of 2 at +25% is 5 coins, the same as one pickup of 20. No single pickup of 2
        // is worth a whole coin, so without the carry the bonus would be nothing.
        Assert.Equal(5, total);
        Assert.Equal(5, UniqueEffectRules.GoldBonus(20, 0.25f, 0f, out _));
        Assert.Equal(0, UniqueEffectRules.GoldBonus(2, 0.25f, 0f, out _));
    }

    [Fact]
    public void ManaShield_PaysItsShare_UpToTheManaThereIs()
    {
        Assert.Equal(10f, UniqueEffectRules.ManaShield(40f, 0.25f, 100f));
        Assert.Equal(4f, UniqueEffectRules.ManaShield(40f, 0.25f, 4f));
        Assert.Equal(0f, UniqueEffectRules.ManaShield(40f, 0.25f, 0f));
    }

    [Fact]
    public void MeleeRange_IsMeasuredOnTheSquaredDistance()
    {
        Assert.True(UniqueEffectRules.InMeleeRange(4f * 4f));
        Assert.False(UniqueEffectRules.InMeleeRange(5f * 5f));
    }
}
