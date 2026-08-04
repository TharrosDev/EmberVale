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
