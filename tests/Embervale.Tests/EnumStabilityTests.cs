using Embervale.Combat;
using Embervale.Animation;
using Embervale.Combat.Actions;
using Embervale.Player;
using Embervale.Corruption;
using Embervale.Crafting;
using Embervale.Dialogue;
using Embervale.Factions;
using Embervale.Items;
using Embervale.Magic;
using Embervale.Onboarding;
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
    public void EquipmentSocket_Ordinals()
    {
        Assert.Equal(0, (int)EquipmentSocket.HandR);
        Assert.Equal(1, (int)EquipmentSocket.HandL);
        Assert.Equal(2, (int)EquipmentSocket.BackPrimary);
        Assert.Equal(3, (int)EquipmentSocket.BackSecondary);
        Assert.Equal(4, (int)EquipmentSocket.HipR);
        Assert.Equal(5, (int)EquipmentSocket.HipL);
        Assert.Equal(6, (int)EquipmentSocket.Shield);
        Assert.Equal(7, (int)EquipmentSocket.Bow);
        Assert.Equal(8, (int)EquipmentSocket.Quiver);
        Assert.Equal(9, (int)EquipmentSocket.Head);
        Assert.Equal(10, (int)EquipmentSocket.Chest);
        Assert.Equal(11, (int)EquipmentSocket.Hips);
        Assert.Equal(12, (int)EquipmentSocket.ShoulderR);
        Assert.Equal(13, (int)EquipmentSocket.ShoulderL);
    }

    [Fact]
    public void SocketSpace_Ordinals()
    {
        Assert.Equal(0, (int)SocketSpace.BoneLocal);
        Assert.Equal(1, (int)SocketSpace.BodyAligned);
    }

    [Fact]
    public void ReactionClass_Ordinals()
    {
        Assert.Equal(0, (int)ReactionClass.Small);
        Assert.Equal(1, (int)ReactionClass.Humanoid);
        Assert.Equal(2, (int)ReactionClass.Armored);
        Assert.Equal(3, (int)ReactionClass.Large);
        Assert.Equal(4, (int)ReactionClass.Boss);
    }

    [Fact]
    public void StaggerResponse_Ordinals()
    {
        Assert.Equal(0, (int)StaggerResponse.None);
        Assert.Equal(1, (int)StaggerResponse.Flinch);
        Assert.Equal(2, (int)StaggerResponse.Stagger);
        Assert.Equal(3, (int)StaggerResponse.Heavy);
        Assert.Equal(4, (int)StaggerResponse.Knockdown);
    }

    [Fact]
    public void CameraContext_Ordinals()
    {
        Assert.Equal(0, (int)CameraContext.Exploration);
        Assert.Equal(1, (int)CameraContext.Sprint);
        Assert.Equal(2, (int)CameraContext.Combat);
        Assert.Equal(3, (int)CameraContext.TargetLock);
        Assert.Equal(4, (int)CameraContext.Aim);
    }

    [Fact]
    public void ActionKind_Ordinals()
    {
        Assert.Equal(0, (int)ActionKind.Attack);
        Assert.Equal(1, (int)ActionKind.HeavyAttack);
        Assert.Equal(2, (int)ActionKind.Block);
        Assert.Equal(3, (int)ActionKind.Parry);
        Assert.Equal(4, (int)ActionKind.Dodge);
        Assert.Equal(5, (int)ActionKind.Cast);
        Assert.Equal(6, (int)ActionKind.Ranged);
        Assert.Equal(7, (int)ActionKind.Equip);
        Assert.Equal(8, (int)ActionKind.UseItem);
        Assert.Equal(9, (int)ActionKind.Stagger);
        Assert.Equal(10, (int)ActionKind.Death);
        Assert.Equal(11, (int)ActionKind.Contextual);
    }

    [Fact]
    public void RootMotionMode_Ordinals()
    {
        Assert.Equal(0, (int)RootMotionMode.None);
        Assert.Equal(1, (int)RootMotionMode.Horizontal);
        Assert.Equal(2, (int)RootMotionMode.WarpToTarget);
    }

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

    /// <summary>Authored into every <c>data/regions/*.tres</c> as <c>RegionResource.Realm</c>, so
    /// the ordinals are load-bearing. The Pale Concord was appended last on purpose — it is a
    /// mortal realm sitting after the Celestial Realm because stability beats tidiness.</summary>
    [Fact]
    public void Realm_Ordinals()
    {
        Assert.Equal(0, (int)Realm.EmberCrown);
        Assert.Equal(1, (int)Realm.FrostfangReach);
        Assert.Equal(2, (int)Realm.AshenWilds);
        Assert.Equal(3, (int)Realm.SunspireDominion);
        Assert.Equal(4, (int)Realm.CelestialRealm);
        Assert.Equal(5, (int)Realm.PaleConcord);
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
        Assert.Equal(15, (int)StatType.FireResist);
        Assert.Equal(16, (int)StatType.FrostResist);
        Assert.Equal(17, (int)StatType.LightningResist);
        Assert.Equal(18, (int)StatType.ArcaneResist);
        Assert.Equal(19, (int)StatType.NatureResist);
        Assert.Equal(20, (int)StatType.NecroticResist);
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
        Assert.Equal(3, (int)SpellDelivery.Cone);
    }

    [Fact]
    public void CastMode_Ordinals()
    {
        Assert.Equal(0, (int)CastMode.Instant);
        Assert.Equal(1, (int)CastMode.Charged);
        Assert.Equal(2, (int)CastMode.Channeled);
    }

    [Fact]
    public void ObjectiveType_Ordinals()
    {
        Assert.Equal(0, (int)ObjectiveType.Kill);
        Assert.Equal(1, (int)ObjectiveType.Collect);

        // 41A. Authored as ints in data/quests/*.tres (Type = 2 / Type = 3) and persisted in the
        // quest log, so a reorder would turn a "walk to Hollowreach" into "talk to Hollowreach" —
        // an objective that can never advance, in a save the player already has.
        Assert.Equal(2, (int)ObjectiveType.Reach);
        Assert.Equal(3, (int)ObjectiveType.Talk);

        // 41B, for the same reason: Type = 4 and Type = 5 are authored in LedgerRun.tres and
        // HoldTheNorthRoad.tres. Escort and Defend also read DIFFERENT fields (Escort needs
        // LocationId, Defend reads RequiredCount as seconds), so a swap would not merely mistarget
        // an objective — it would reinterpret the numbers beside it.
        Assert.Equal(4, (int)ObjectiveType.Escort);
        Assert.Equal(5, (int)ObjectiveType.Defend);

        // 41C. Type = 6 and Type = 7 are authored in TheSealedTally.tres. Stealth is the one whose
        // ordinal carries a behaviour rather than a lookup: QuestProgress seeds it already met, so a
        // swap would silently pre-complete whichever objective took its place.
        Assert.Equal(6, (int)ObjectiveType.Interact);
        Assert.Equal(7, (int)ObjectiveType.Stealth);
    }

    [Fact]
    public void QuestStatus_Ordinals()
    {
        Assert.Equal(0, (int)QuestStatus.Active);
        Assert.Equal(1, (int)QuestStatus.Completed);

        // 41B. Persisted as an int in every save's quest log (QuestProgress.Save), so an inserted
        // member would silently turn a failed quest into a completed one on load.
        Assert.Equal(2, (int)QuestStatus.Failed);
    }

    [Fact]
    public void CraftingStationType_Ordinals()
    {
        Assert.Equal(0, (int)CraftingStationType.Hand);
        Assert.Equal(1, (int)CraftingStationType.Forge);
        Assert.Equal(2, (int)CraftingStationType.Workbench);
        Assert.Equal(3, (int)CraftingStationType.Alchemy);

        // Ordinal 4 was Cooking, removed in Phase 40 when survival needs were cut. It was the LAST
        // member and no recipe, station or scene ever named it, so nothing shifted. 4 stays retired;
        // the next station appends at 5. Deliberately not pinned as a count — an append is legal.
    }

    [Fact]
    public void WorldEventKind_Ordinals()
    {
        Assert.Equal(0, (int)WorldEventKind.Raid);
        Assert.Equal(1, (int)WorldEventKind.Cache);
        Assert.Equal(2, (int)WorldEventKind.Hunt);
    }

    [Fact]
    public void ServiceKind_Ordinals()
    {
        // Authored as ints in data/services/*.tres (Phase 38D), so a reorder would silently turn every
        // inn into a trainer. There is deliberately no Repair member — durability does not exist and
        // never will: Phase 40 struck survival needs outright (maintainer direction, 2026-08-12).
        Assert.Equal(0, (int)Embervale.Economy.ServiceKind.Trainer);
        Assert.Equal(1, (int)Embervale.Economy.ServiceKind.Bank);
        Assert.Equal(2, (int)Embervale.Economy.ServiceKind.Inn);
        Assert.Equal(3, (int)Embervale.Economy.ServiceKind.Stable);

        // 38M's Passage was never pinned here; 38O found the gap while appending its own two.
        Assert.Equal(4, (int)Embervale.Economy.ServiceKind.Passage);
        Assert.Equal(5, (int)Embervale.Economy.ServiceKind.Search);
        Assert.Equal(6, (int)Embervale.Economy.ServiceKind.Redeem);

        // ⚠️ 38P, 38Q and 38Q2 each appended a member and none of them extended this test, so the
        // pinning had drifted five members behind the enum by the time 38R needed ordinal 11. Every
        // one of these is written as a bare integer in a .tres — CrosswayMercenary.tres says
        // `Kind = 11` and nothing but this test stops that meaning something else next phase.
        Assert.Equal(7, (int)Embervale.Economy.ServiceKind.Collect);
        Assert.Equal(8, (int)Embervale.Economy.ServiceKind.Appraise);
        Assert.Equal(9, (int)Embervale.Economy.ServiceKind.Commission);
        Assert.Equal(10, (int)Embervale.Economy.ServiceKind.Contracts);
        Assert.Equal(11, (int)Embervale.Economy.ServiceKind.Mercenary);
        Assert.Equal(12, (int)Embervale.Economy.ServiceKind.Wager);
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
        Assert.Equal(4, (int)DialogueEffect.AddCorruption);
        Assert.Equal(5, (int)DialogueEffect.RecruitCompanion);
        Assert.Equal(6, (int)DialogueEffect.DismissCompanion);
        Assert.Equal(7, (int)DialogueEffect.AddCompanionLoyalty);
        Assert.Equal(8, (int)DialogueEffect.LearnSpell);
        Assert.Equal(9, (int)DialogueEffect.OpenShop);

        // 38R. Written as `Effect = 10` by tools/gen_merchant_dialogue.py --service, in every service
        // conversation it emits — the generator hard-codes the integer, so this is what keeps the
        // generator honest as much as the data.
        Assert.Equal(10, (int)DialogueEffect.OpenService);
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
        Assert.Equal(7, (int)DialogueCondition.CorruptionAtLeast);
        Assert.Equal(8, (int)DialogueCondition.CorruptionBelow);

        // Phase 42B appended these two; every guild-officer .tres on disk stores them as 14 and 15.
        Assert.Equal(14, (int)DialogueCondition.GuildRankAtLeast);
        Assert.Equal(15, (int)DialogueCondition.GuildNotMember);
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

    [Fact]
    public void TutorialStep_Ordinals()
    {
        // Persisted by TutorialDirector (Phase 33B): a re-map would resume onboarding on the wrong
        // verb, or re-teach one the player has already been shown.
        Assert.Equal(0, (int)TutorialStep.None);
        Assert.Equal(1, (int)TutorialStep.Look);
        Assert.Equal(2, (int)TutorialStep.Move);
        Assert.Equal(3, (int)TutorialStep.Sprint);
        Assert.Equal(4, (int)TutorialStep.Attack);
        Assert.Equal(5, (int)TutorialStep.Block);
        Assert.Equal(6, (int)TutorialStep.Dodge);
        Assert.Equal(7, (int)TutorialStep.Interact);
        Assert.Equal(8, (int)TutorialStep.Inventory);
        Assert.Equal(9, (int)TutorialStep.Journal);
        Assert.Equal(10, (int)TutorialStep.Cast);
    }
}
