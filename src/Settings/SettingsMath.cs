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

    // --- UI upgrade: the valid range of each appended Settings field ----------------------------
    // A settings file is hand-editable and outlives the build that wrote it, so a reader takes the
    // value through its clamp rather than trusting it. A value that is not a number is the default.

    public const float HudScaleMin = 0.75f;
    public const float HudScaleMax = 1.5f;
    public const float HudOpacityMin = 0.3f;
    public const float HudSafeZoneMax = 0.1f;
    public const float PadSensitivityMin = 0.25f;
    public const float PadSensitivityMax = 3f;
    public const float ToastDurationMin = 0.5f;
    public const float ToastDurationMax = 3f;
    public const int SubtitleSizeMax = 2;
    public const int DamageNumberModeMax = 3;

    public static float ClampHudScale(float scale) => ClampOr(scale, HudScaleMin, HudScaleMax, 1f);

    /// <summary>Floored well above zero: a HUD the player cannot see is the Hidden mode's job, and
    /// a slider that can reach it is how a health bar gets lost.</summary>
    public static float ClampHudOpacity(float opacity) => ClampOr(opacity, HudOpacityMin, 1f, 1f);

    public static float ClampHudSafeZone(float fraction) => ClampOr(fraction, 0f, HudSafeZoneMax, 0f);

    public static float ClampPadSensitivity(float multiplier) =>
        ClampOr(multiplier, PadSensitivityMin, PadSensitivityMax, 1f);

    public static float ClampToastDuration(float multiplier) =>
        ClampOr(multiplier, ToastDurationMin, ToastDurationMax, 1f);

    public static int ClampSubtitleSize(int size) => Math.Clamp(size, 0, SubtitleSizeMax);

    /// <summary>The damage-number mode in force: the saved mode, or what the older on/off toggle
    /// means (1 all, 0 none) while the mode is still -1 or is out of range.</summary>
    public static int DamageNumberMode(int mode, bool legacyEnabled) =>
        mode >= 0 && mode <= DamageNumberModeMax ? mode : legacyEnabled ? 1 : 0;

    /// <summary>The saved mode of HUD element <paramref name="element"/>: 0 (always) when the list
    /// is short or holds a number that is not a mode.</summary>
    public static int HudElementMode(int[]? modes, int element, int modeCount = 3) =>
        modes != null && element >= 0 && element < modes.Length && modes[element] >= 0 && modes[element] < modeCount
            ? modes[element]
            : 0;

    /// <summary>One saved binding line, <c>action=binding</c>.</summary>
    public static string BindingEntry(string action, string binding) => $"{action}={binding}";

    /// <summary>The saved binding for <paramref name="action"/>, or null when it was never remapped
    /// (or its line is malformed). The last line for an action wins.</summary>
    public static string? BindingFor(string[]? entries, string action)
    {
        if (entries == null)
        {
            return null;
        }

        for (int i = entries.Length - 1; i >= 0; i--)
        {
            string entry = entries[i] ?? string.Empty;
            if (action.Length > 0 && entry.Length > action.Length + 1 && entry[action.Length] == '='
                && entry.StartsWith(action, StringComparison.Ordinal))
            {
                return entry.Substring(action.Length + 1);
            }
        }

        return null;
    }

    private static float ClampOr(float value, float min, float max, float fallback) =>
        float.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;

    /// <summary>Per-frame look step from a raw mouse delta: the controller's base sensitivity scaled by
    /// the player's sensitivity multiplier setting (Phase 25.5D wires the 24F slider into the
    /// controller, which previously ignored it).</summary>
    public static float LookStep(float rawDelta, float baseSensitivity, float multiplier) =>
        rawDelta * baseSensitivity * multiplier;

    /// <summary>
    /// Per-frame look step from an analog stick axis. A stick is a held deflection, not a delta like
    /// a mouse, so the step is rate × time rather than raw movement — which is also why it has to be
    /// framerate-independent where <see cref="LookStep"/> does not.
    ///
    /// The deflection is squared (magnitude only, sign preserved): a stick has far less travel than a
    /// mouse mat, and a linear response makes fine aim near centre impossible while still feeling
    /// slow at full tilt. Input below <paramref name="deadzone"/> is dropped and the remainder is
    /// rescaled from zero, so there is no step at the deadzone edge.
    /// </summary>
    public static float StickLookStep(
        float axis, float deadzone, float radiansPerSecond, float delta, float multiplier)
    {
        float magnitude = Math.Abs(axis);
        if (magnitude <= deadzone || delta <= 0f)
        {
            return 0f;
        }

        float span = 1f - deadzone;
        float scaled = span <= 0f ? 1f : Math.Clamp((magnitude - deadzone) / span, 0f, 1f);
        float curved = scaled * scaled;
        return Math.Sign(axis) * curved * radiansPerSecond * delta * multiplier;
    }

    /// <summary>New pitch after a vertical look step, honouring Invert-Y and the look limit. Up is
    /// negative pitch (subtract the step); Invert-Y adds instead, flipping the vertical axis.</summary>
    public static float ApplyPitch(float pitch, float verticalStep, bool invertY, float limit)
    {
        float next = invertY ? pitch + verticalStep : pitch - verticalStep;
        return Math.Clamp(next, -limit, limit);
    }
}
