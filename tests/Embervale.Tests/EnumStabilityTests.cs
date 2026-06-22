using Embervale.Combat;
using Embervale.Corruption;
using Embervale.Crafting;
using Embervale.Dialogue;
using Embervale.Factions;
using Embervale.Items;
using Embervale.Magic;
using Embervale.Quests;
using Embervale.Stats;
using Embervale.World;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// Pins the ordinal value of every member of each enum that is authored into <c>.tres</c>
/// resources and/or written into save files. Those enums serialize as their integer ordinal, so
/// reordering, inserting, or removing a member silently re-maps existing content and saves
/// (a saved <c>Rare</c> item would load as <c>Epic</c>). These tests fail the build the moment
/// that happens — the guard for the "append only" rule documented at each enum and in
/// docs/ARCHITECTURE.md §4.
///
/// Appending new members at the end is safe and intentionally NOT caught here. Runtime-only enums
/// (GameState, EnemyState, DayPhase, WorldEventStatus) are not persisted and are deliberately
/// omitted — reordering them is harmless.
/// </summary>
public class EnumStabilityTests
{
    [Fact]
    public void ItemType_Ordinals()
    {
        Assert.Equal(0, (int)ItemType.Misc);
        Assert.Equal(1, (int)ItemType.Consumable);
        Assert.Equal(2, (int)ItemType.Weapon);
        Assert.Equal(3, (int)ItemType.Armor);
        Assert.Equal(4, (int)ItemType.Material);
        Assert.Equal(5, (int)ItemType.Quest);
    }

    [Fact]
    public void ItemRarity_Ordinals()
    {
        Assert.Equal(0, (int)ItemRarity.Common);
        Assert.Equal(1, (int)ItemRarity.Uncommon);
        Assert.Equal(2, (int)ItemRarity.Rare);
        Assert.Equal(3, (int)ItemRarity.Epic);
        Assert.Equal(4, (int)ItemRarity.Legendary);
    }

    [Fact]
    public void EquipmentSlot_Ordinals()
    {
        Assert.Equal(0, (int)EquipmentSlot.None);
        Assert.Equal(1, (int)EquipmentSlot.MainHand);
        Assert.Equal(2, (int)EquipmentSlot.OffHand);
        Assert.Equal(3, (int)EquipmentSlot.Head);
        Assert.Equal(4, (int)EquipmentSlot.Chest);
        Assert.Equal(5, (int)EquipmentSlot.Hands);
        Assert.Equal(6, (int)EquipmentSlot.Legs);
        Assert.Equal(7, (int)EquipmentSlot.Feet);
        Assert.Equal(8, (int)EquipmentSlot.Ring);
        Assert.Equal(9, (int)EquipmentSlot.Amulet);
    }

    [Fact]
    public void GearFamily_Ordinals()
    {
        Assert.Equal(0, (int)GearFamily.None);
        Assert.Equal(1, (int)GearFamily.Weapon);
        Assert.Equal(2, (int)GearFamily.Armor);
        Assert.Equal(3, (int)GearFamily.Accessory);
    }

    [Fact]
    public void AffixKind_Ordinals()
    {
        Assert.Equal(0, (int)AffixKind.Prefix);
        Assert.Equal(1, (int)AffixKind.Suffix);
    }

    [Fact]
    public void ModifierType_Ordinals()
    {
        Assert.Equal(0, (int)ModifierType.Flat);
        Assert.Equal(1, (int)ModifierType.PercentAdd);
        Assert.Equal(2, (int)ModifierType.PercentMult);
    }

    [Fact]
    public void StatType_Ordinals()
    {
        Assert.Equal(0, (int)StatType.Health);
        Assert.Equal(1, (int)StatType.Stamina);
        Assert.Equal(2, (int)StatType.Mana);
        Assert.Equal(3, (int)StatType.Strength);
        Assert.Equal(4, (int)StatType.Dexterity);
        Assert.Equal(5, (int)StatType.Intelligence);
        Assert.Equal(6, (int)StatType.Vitality);
        Assert.Equal(7, (int)StatType.Endurance);
        Assert.Equal(8, (int)StatType.Armor);
        Assert.Equal(9, (int)StatType.PhysicalPower);
        Assert.Equal(10, (int)StatType.SpellPower);
        Assert.Equal(11, (int)StatType.MoveSpeed);
        Assert.Equal(12, (int)StatType.AttackSpeed);
        Assert.Equal(13, (int)StatType.CritChance);
        Assert.Equal(14, (int)StatType.CritDamage);
    }

    [Fact]
    public void DamageType_Ordinals()
    {
        Assert.Equal(0, (int)DamageType.Physical);
        Assert.Equal(1, (int)DamageType.Fire);
        Assert.Equal(2, (int)DamageType.Frost);
        Assert.Equal(3, (int)DamageType.Lightning);
        Assert.Equal(4, (int)DamageType.Arcane);
        Assert.Equal(5, (int)DamageType.Nature);
        Assert.Equal(6, (int)DamageType.Necrotic);
        Assert.Equal(7, (int)DamageType.True);
    }

    [Fact]
    public void SpellDelivery_Ordinals()
    {
        Assert.Equal(0, (int)SpellDelivery.Projectile);
        Assert.Equal(1, (int)SpellDelivery.Area);
        Assert.Equal(2, (int)SpellDelivery.Self);
    }

    [Fact]
    public void ObjectiveType_Ordinals()
    {
        Assert.Equal(0, (int)ObjectiveType.Kill);
        Assert.Equal(1, (int)ObjectiveType.Collect);
    }

    [Fact]
    public void QuestStatus_Ordinals()
    {
        Assert.Equal(0, (int)QuestStatus.Active);
        Assert.Equal(1, (int)QuestStatus.Completed);
    }

    [Fact]
    public void CraftingStationType_Ordinals()
    {
        Assert.Equal(0, (int)CraftingStationType.Hand);
        Assert.Equal(1, (int)CraftingStationType.Forge);
        Assert.Equal(2, (int)CraftingStationType.Workbench);
        Assert.Equal(3, (int)CraftingStationType.Alchemy);
        Assert.Equal(4, (int)CraftingStationType.Cooking);
    }

    [Fact]
    public void WorldEventKind_Ordinals()
    {
        Assert.Equal(0, (int)WorldEventKind.Raid);
        Assert.Equal(1, (int)WorldEventKind.Cache);
        Assert.Equal(2, (int)WorldEventKind.Hunt);
    }

    [Fact]
    public void ReputationTier_Ordinals()
    {
        Assert.Equal(0, (int)ReputationTier.Hated);
        Assert.Equal(1, (int)ReputationTier.Hostile);
        Assert.Equal(2, (int)ReputationTier.Unfriendly);
        Assert.Equal(3, (int)ReputationTier.Neutral);
        Assert.Equal(4, (int)ReputationTier.Friendly);
        Assert.Equal(5, (int)ReputationTier.Honored);
        Assert.Equal(6, (int)ReputationTier.Allied);
    }

    [Fact]
    public void CorruptionTier_Ordinals()
    {
        Assert.Equal(0, (int)CorruptionTier.Untainted);
        Assert.Equal(1, (int)CorruptionTier.Touched);
        Assert.Equal(2, (int)CorruptionTier.Marked);
        Assert.Equal(3, (int)CorruptionTier.Ashbound);
        Assert.Equal(4, (int)CorruptionTier.Embers);
    }

    [Fact]
    public void DialogueEffect_Ordinals()
    {
        Assert.Equal(0, (int)DialogueEffect.None);
        Assert.Equal(1, (int)DialogueEffect.StartQuest);
        Assert.Equal(2, (int)DialogueEffect.SetFlag);
        Assert.Equal(3, (int)DialogueEffect.ClearFlag);
    }

    [Fact]
    public void DialogueCondition_Ordinals()
    {
        Assert.Equal(0, (int)DialogueCondition.Always);
        Assert.Equal(1, (int)DialogueCondition.QuestAvailable);
        Assert.Equal(2, (int)DialogueCondition.QuestActive);
        Assert.Equal(3, (int)DialogueCondition.QuestCompleted);
        Assert.Equal(4, (int)DialogueCondition.QuestNotStarted);
        Assert.Equal(5, (int)DialogueCondition.HasFlag);
        Assert.Equal(6, (int)DialogueCondition.MissingFlag);
    }

    [Fact]
    public void WeatherType_Ordinals()
    {
        Assert.Equal(0, (int)WeatherType.Clear);
        Assert.Equal(1, (int)WeatherType.Cloudy);
        Assert.Equal(2, (int)WeatherType.Rain);
        Assert.Equal(3, (int)WeatherType.Storm);
        Assert.Equal(4, (int)WeatherType.Fog);
    }
}
