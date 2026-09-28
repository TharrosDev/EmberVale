using System;

namespace Embervale.Combat;

/// <summary>
/// How much combat presentation the player has asked for. Everything that punctuates a blow for feel
/// rather than for rules (hit-stop, the screen flash, floating numbers, the lock-on and aim helpers)
/// reads its scale here, so the settings and Reduced Motion reach all of them without each system
/// reading <c>Settings</c>. Mirrors <see cref="Player.CameraComfort"/>.
/// <para><see cref="HitStop"/> and <see cref="ScreenFlash"/> are 0..1 multipliers; Reduced Motion caps
/// both at <see cref="ReducedScale"/> — a hit still lands with a beat, it just no longer freezes or
/// strobes the screen. <see cref="AimAssist"/> is the 0..1 strength of the bow's pull toward a
/// target; it is an aid, not motion, so Reduced Motion leaves it alone.</para>
/// </summary>
public readonly record struct CombatComfort(
    float HitStop, float ScreenFlash, bool DamageNumbers, bool LockOnAssist, float AimAssist)
{
    /// <summary>What hit-stop and the flash are held to when Reduced Motion is on.</summary>
    public const float ReducedScale = 0.25f;

    public static readonly CombatComfort Full = new(1f, 1f, true, true, 0.5f);

    public static CombatComfort From(
        float hitStop, float screenFlash, bool damageNumbers, bool lockOnAssist, float aimAssist,
        bool reducedMotion)
    {
        float cap = reducedMotion ? ReducedScale : 1f;
        return new CombatComfort(
            Math.Min(Math.Clamp(hitStop, 0f, 1f), cap),
            Math.Min(Math.Clamp(screenFlash, 0f, 1f), cap),
            damageNumbers,
            lockOnAssist,
            Math.Clamp(aimAssist, 0f, 1f));
    }

    /// <summary>The live comfort for the current settings; <see cref="Full"/> with no settings service.</summary>
    public static CombatComfort From(Settings.Settings? s) => s == null
        ? Full
        : From(s.HitStopIntensity, s.CombatFlashIntensity, s.DamageNumbers, s.LockOnAssist,
            s.AimAssistStrength, s.ReducedMotion);
}
