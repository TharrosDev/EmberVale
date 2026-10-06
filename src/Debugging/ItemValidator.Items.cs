using System;
using System.Collections.Generic;
using Embervale.Animation;
using Embervale.Items;
using Embervale.Magic;
using Godot;

namespace Embervale.Debugging;

/// <summary>
/// <see cref="ItemValidator"/>: item schema, equipment and consumable rules. Owned by the items lane
/// alone; its rules go here and nowhere else. <c>RequireKey</c> in <c>ItemValidator.cs</c> is shared.
///
/// <para>What <c>ItemValidator.cs</c> already reports is not repeated here: a <c>SetId</c> or
/// <c>UniqueEffectId</c> that resolves to nothing, a set threshold outside 2..piece count, a tier
/// outside 0..6 and a negative level. These arms cover what the runtime in <c>src/Items</c> would
/// otherwise do silently: an item that equips into nothing, a weapon that hits with the old one's
/// numbers, a potion that is drunk and does nothing, a unique effect that can never trigger.</para>
/// </summary>
public static partial class ItemValidator
{
    private static void CollectItems(List<string> issues)
    {
        foreach (ItemResource item in ItemDatabase.All.Values)
        {
            string what = $"item '{item.Id}'";

            // The foundation reports a negative level; the cap is this lane's.
            if (item.RequiredLevel > InventoryRules.MaxRequiredLevel)
            {
                issues.Add($"{what} requires level {item.RequiredLevel}, above the level cap of {InventoryRules.MaxRequiredLevel}");
            }

            if (item is EquippableItemResource equippable)
            {
                CollectEquippable(equippable, what, issues);
            }
            else if (item.SetId.Length > 0)
            {
                issues.Add($"{what} names set '{item.SetId}' but is not equippable, so it can never count as a piece");
            }

            if (item is ConsumableItemResource consumable)
            {
                CollectConsumable(consumable, what, issues);
            }
        }

        foreach (UniqueEffectResource effect in UniqueEffectDatabase.All)
        {
            CollectUniqueEffect(effect, issues);
        }

        foreach (ItemSetResource set in ItemSetDatabase.All)
        {
            // One piece can never reach the lowest legal threshold (2).
            if (set.PieceIds.Count < 2)
            {
                issues.Add($"item set '{set.Id}' has {set.PieceIds.Count} piece(s); a set needs at least 2");
            }
        }

        // The reason keys travel as constants, so the locale-usage test cannot see them.
        RequireKey(ConsumableRules.CooldownReasonKey, "reason key", "consumable refusal", issues);
        RequireKey(ConsumableRules.FullReasonKey, "reason key", "consumable refusal", issues);
        RequireKey(ConsumableRules.NothingToCureReasonKey, "reason key", "consumable refusal", issues);
        RequireKey(InventoryRules.LevelReasonKey, "reason key", "equip refusal", issues);
        RequireKey(InventoryRules.HandsReasonKey, "reason key", "equip refusal", issues);
        RequireKey(InventoryRules.PackFullReasonKey, "reason key", "equip refusal", issues);
        RequireKey(AmmoRules.NoAmmoReasonKey, "reason key", "ammo refusal", issues);

        // The stand-in a modelless weapon is drawn with must itself exist.
        foreach (WeaponClass weaponClass in Enum.GetValues<WeaponClass>())
        {
            string path = EquipmentPresentationComponent.ModelFor(string.Empty, weaponClass).Path;
            if (path.Length > 0 && !ResourceLoader.Exists(path))
            {
                issues.Add($"weapon class {weaponClass} falls back to model '{path}', which does not resolve");
            }
        }
    }

    private static void CollectEquippable(EquippableItemResource item, string what, List<string> issues)
    {
        if (item.Slot == EquipmentSlot.None)
        {
            issues.Add($"{what} is equippable but its Slot is None, so it can never be equipped");
        }

        if (item.Slot == EquipmentSlot.MainHand && item.Weapon == null)
        {
            issues.Add($"{what} goes in the main hand but has no Weapon, so equipping it would leave the previous weapon's attacks in place");
        }

        if (item.TwoHanded && item.Slot != EquipmentSlot.MainHand)
        {
            issues.Add($"{what} is TwoHanded but its Slot is {item.Slot}; only a main-hand weapon can fill both hands");
        }

        if (item.WeaponClass == WeaponClass.Shield && item.Slot != EquipmentSlot.OffHand)
        {
            issues.Add($"{what} has WeaponClass Shield but its Slot is {item.Slot}; a shield is an off-hand item");
        }

        if (item.Weapon != null && item.Slot == EquipmentSlot.MainHand
            && (item.WeaponClass == WeaponClass.Bow) != item.Weapon.IsRanged && item.WeaponClass != WeaponClass.None)
        {
            issues.Add($"{what} has WeaponClass {item.WeaponClass} but its Weapon is {(item.Weapon.IsRanged ? "ranged" : "not ranged")}");
        }

        if (item.Slot == EquipmentSlot.Ammo && item.MaxStack <= 1)
        {
            issues.Add($"{what} is ammunition with MaxStack {item.MaxStack}; one shot would empty the slot");
        }
    }

    private static void CollectConsumable(ConsumableItemResource item, string what, List<string> issues)
    {
        if (item.Magnitude < 0f || item.HealAmount < 0f)
        {
            issues.Add($"{what} has a negative Magnitude or HealAmount");
        }

        if (item.DurationSeconds < 0f || item.CooldownSeconds < 0f)
        {
            issues.Add($"{what} has a negative DurationSeconds or CooldownSeconds");
        }

        switch (item.Effect)
        {
            case ConsumableEffectKind.RestoreStamina:
            case ConsumableEffectKind.RestoreMana:
                if (item.Magnitude <= 0f)
                {
                    issues.Add($"{what} has effect {item.Effect} but Magnitude 0, so using it restores nothing");
                }

                break;

            case ConsumableEffectKind.Buff:
                if (item.Magnitude == 0f)
                {
                    issues.Add($"{what} is a Buff with Magnitude 0, so using it changes nothing");
                }

                if (item.DurationSeconds <= 0f)
                {
                    issues.Add($"{what} is a Buff with no DurationSeconds; a buff has to last");
                }

                break;

            case ConsumableEffectKind.Cure:
                foreach (string statusId in item.CureStatusIds)
                {
                    if (StatusEffectDatabase.Get(statusId) == null)
                    {
                        issues.Add($"{what} cures unknown status '{statusId}'");
                    }
                }

                break;

            case ConsumableEffectKind.Heal:
                // A Heal with neither a Magnitude nor a legacy HealAmount is the pre-ics "inert
                // consumable" shape and stays legal, unless something else says it was meant to act.
                if (item.EffectiveHeal <= 0f && (item.DurationSeconds > 0f || item.CooldownSeconds > 0f))
                {
                    issues.Add($"{what} has a duration or cooldown but heals for 0");
                }

                break;
        }
    }

    private static void CollectUniqueEffect(UniqueEffectResource effect, List<string> issues)
    {
        string what = $"unique effect '{effect.Id}'";

        if (effect.Chance <= 0f || effect.Chance > 1f)
        {
            issues.Add($"{what} has Chance {effect.Chance}, outside (0, 1]; it would never trigger or the number is not a probability");
        }

        if (effect.CooldownSeconds < 0f || effect.DurationSeconds < 0f)
        {
            issues.Add($"{what} has a negative CooldownSeconds or DurationSeconds");
        }

        if (effect.Kind == UniqueEffectKind.OnHitStatus)
        {
            if (StatusEffectDatabase.Get(effect.StatusId) == null)
            {
                issues.Add($"{what} is OnHitStatus but its StatusId '{effect.StatusId}' is not a status effect");
            }

            return;
        }

        if (effect.Magnitude <= 0f)
        {
            issues.Add($"{what} is {effect.Kind} with Magnitude {effect.Magnitude}; it would do nothing");
        }

        bool fraction = effect.Kind is UniqueEffectKind.OnKillHeal or UniqueEffectKind.ManaShield;
        if (fraction && effect.Magnitude > 1f)
        {
            issues.Add($"{what} is {effect.Kind} with Magnitude {effect.Magnitude}; this kind's magnitude is a fraction of 0..1");
        }

        bool gated = effect.Kind is UniqueEffectKind.LowHealthPower or UniqueEffectKind.CritExecute;
        if (gated && (effect.Threshold <= 0f || effect.Threshold > 1f))
        {
            issues.Add($"{what} is {effect.Kind} with Threshold {effect.Threshold}, outside (0, 1]; it would never trigger");
        }
    }
}
