using System;

namespace Embervale.Items;

/// <summary>Why a consumable could not be used right now. Runtime only, never saved.</summary>
public enum ConsumeRefusal
{
    None,

    /// <summary>Its cooldown group is still running.</summary>
    OnCooldown,

    /// <summary>A restore whose resource is already at its maximum.</summary>
    AlreadyFull,

    /// <summary>A cure with no harmful status it can remove.</summary>
    NothingToCure,
}

/// <summary>
/// The rules of using a <see cref="ConsumableItemResource"/>, free of the engine so they are
/// unit-testable: what a use restores, when it is refused, how a cooldown and an over-time restore
/// advance. <see cref="InventoryComponent.Consume"/> and <see cref="ConsumableEffectsComponent"/> are
/// the only callers that touch live state.
/// </summary>
public static class ConsumableRules
{
    public const string CooldownReasonKey = "item.use.refused.cooldown";
    public const string FullReasonKey = "item.use.refused.full";
    public const string NothingToCureReasonKey = "item.use.refused.nothing_to_cure";

    /// <summary>The cooldown key: the shared group, or the item's own id when it has none.</summary>
    public static string CooldownKey(string? group, string id) => string.IsNullOrEmpty(group) ? id : group;

    /// <summary>True for the three effects that refill a resource.</summary>
    public static bool IsRestore(ConsumableEffectKind effect) =>
        effect is ConsumableEffectKind.Heal or ConsumableEffectKind.RestoreStamina or ConsumableEffectKind.RestoreMana;

    /// <summary>
    /// How much a restore gives back in total. A heal with no magnitude is the legacy potion and uses
    /// <paramref name="legacyHeal"/>; the other restores have only their magnitude. 0 for a buff or cure.
    /// </summary>
    public static float RestoreAmount(ConsumableEffectKind effect, float magnitude, float legacyHeal)
    {
        return effect switch
        {
            ConsumableEffectKind.Heal => Math.Max(0f, magnitude > 0f ? magnitude : legacyHeal),
            ConsumableEffectKind.RestoreStamina or ConsumableEffectKind.RestoreMana => Math.Max(0f, magnitude),
            _ => 0f,
        };
    }

    /// <summary>A restore with a duration is spread evenly over it rather than given at once.</summary>
    public static bool IsOverTime(ConsumableEffectKind effect, float durationSeconds) =>
        IsRestore(effect) && durationSeconds > 0f;

    /// <summary>
    /// Whether a use is allowed. A cooldown refuses everything; a restore is refused at a full
    /// resource (so a potion is never wasted), unless it restores nothing at all, which is the legacy
    /// "consumable with no effect" and stays usable; a cure is refused when it would remove nothing.
    /// </summary>
    public static ConsumeRefusal Check(
        ConsumableEffectKind effect, float restoreAmount, float cooldownRemaining,
        float resourceCurrent, float resourceMax, int curableStatuses)
    {
        if (cooldownRemaining > 0f)
        {
            return ConsumeRefusal.OnCooldown;
        }

        if (IsRestore(effect) && restoreAmount > 0f && resourceCurrent >= resourceMax)
        {
            return ConsumeRefusal.AlreadyFull;
        }

        if (effect == ConsumableEffectKind.Cure && curableStatuses <= 0)
        {
            return ConsumeRefusal.NothingToCure;
        }

        return ConsumeRefusal.None;
    }

    /// <summary>The locale key that tells the player why; empty for <see cref="ConsumeRefusal.None"/>.</summary>
    public static string ReasonKey(ConsumeRefusal refusal)
    {
        return refusal switch
        {
            ConsumeRefusal.OnCooldown => CooldownReasonKey,
            ConsumeRefusal.AlreadyFull => FullReasonKey,
            ConsumeRefusal.NothingToCure => NothingToCureReasonKey,
            _ => string.Empty,
        };
    }

    /// <summary>A cooldown after <paramref name="delta"/> seconds, never below zero.</summary>
    public static float TickCooldown(float remaining, float delta) => Math.Max(0f, remaining - Math.Max(0f, delta));

    /// <summary>How much of a cooldown is left, 1 (just used) down to 0 (ready), for a hotbar sweep.</summary>
    public static float CooldownFraction(float remaining, float total) =>
        total <= 0f || remaining <= 0f ? 0f : Math.Min(1f, remaining / total);

    /// <summary>
    /// One tick of an over-time restore: the amount to give this frame and the seconds left after
    /// it. The last tick is cut to the time remaining, so the total over the whole duration is
    /// exactly rate times duration however the frames fall.
    /// </summary>
    public static float RestoreStep(float perSecond, float remainingSeconds, float delta, out float secondsLeft)
    {
        float elapsed = Math.Min(Math.Max(0f, delta), Math.Max(0f, remainingSeconds));
        secondsLeft = Math.Max(0f, remainingSeconds - elapsed);
        return Math.Max(0f, perSecond) * elapsed;
    }
}
