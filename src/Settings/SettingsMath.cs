using System;

namespace Embervale.Settings;

/// <summary>
/// Pure helpers behind <see cref="SettingsService"/>'s engine application. Kept Godot-free so the
/// load-bearing conversions (notably the linear-fader → decibel mapping that drives every audio
/// bus) are unit-testable without an engine.
/// </summary>
public static class SettingsMath
{
    /// <summary>Decibels treated as silence; an audio bus set here is effectively muted. Below this
    /// the log curve runs to -infinity, which the mixer dislikes — clamp instead.</summary>
    public const float SilenceDb = -80f;

    /// <summary>
    /// Converts a linear 0..1 fader value to bus decibels. 1 → 0 dB (unchanged), 0.5 → ~-6 dB, and
    /// anything at/below silence maps to <see cref="SilenceDb"/> rather than -infinity. Mirrors
    /// Godot's <c>Mathf.LinearToDb</c> with a hard floor so a muted bus is well-defined.
    /// </summary>
    public static float LinearToDb(float linear)
    {
        if (linear <= 0.0001f)
        {
            return SilenceDb;
        }

        float db = 20f * MathF.Log10(linear);
        return db < SilenceDb ? SilenceDb : db;
    }

    /// <summary>Clamps a linear volume into the valid 0..1 fader range.</summary>
    public static float ClampVolume(float linear) => Math.Clamp(linear, 0f, 1f);
}
