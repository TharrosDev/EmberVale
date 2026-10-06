using System;
using System.Collections;
using System.Collections.Generic;

namespace Embervale.Settings;

/// <summary>The tabs of the settings screen, in strip order.</summary>
public enum SettingsTab
{
    Graphics = 0,
    Audio = 1,
    Controls = 2,
    Gameplay = 3,
    Interface = 4,
    Accessibility = 5,
}

/// <summary>
/// Which <see cref="Settings"/> field belongs to which tab of the settings screen, and what each
/// kind of reset is allowed to touch. Pure (names only), so the two promises a reset makes are
/// tests and not habits: a tab's reset changes that tab and nothing else, and no reset but the
/// Controls tab's own ever takes the player's bindings.
/// </summary>
public static class SettingsTabRules
{
    public const int TabCount = 6;

    /// <summary>Below this logical width the description pane folds under the option list.</summary>
    public const float NarrowWidth = 1000f;

    /// <summary>The two saved binding lists.</summary>
    public static readonly string[] BindingFields = { nameof(Settings.KeyBindings), nameof(Settings.PadBindings) };

    private static readonly string[][] Table =
    {
        // Graphics
        new[]
        {
            nameof(Settings.WindowMode), nameof(Settings.VSync), nameof(Settings.MaxFps), nameof(Settings.RenderQuality),
            nameof(Settings.RenderScale), nameof(Settings.ScalingMode), nameof(Settings.AntiAliasing),
            nameof(Settings.ShadowQuality), nameof(Settings.AmbientOcclusion), nameof(Settings.VolumetricFog),
            nameof(Settings.Glow), nameof(Settings.FieldOfView),
        },

        // Audio
        new[]
        {
            nameof(Settings.MasterVolume), nameof(Settings.MusicVolume), nameof(Settings.SfxVolume),
            nameof(Settings.AmbienceVolume), nameof(Settings.UiVolume), nameof(Settings.VoiceVolume),
        },

        // Controls
        new[]
        {
            nameof(Settings.MouseSensitivity), nameof(Settings.InvertY), nameof(Settings.PadSensitivityX),
            nameof(Settings.PadSensitivityY), nameof(Settings.KeyBindings), nameof(Settings.PadBindings),
        },

        // Gameplay
        new[]
        {
            nameof(Settings.Difficulty), nameof(Settings.ThirdPersonCamera), nameof(Settings.ThirdPersonDistance),
            nameof(Settings.ThirdPersonShoulderSide), nameof(Settings.CameraShakeIntensity), nameof(Settings.HeadBob),
            nameof(Settings.FovKick), nameof(Settings.AutoShoulderSwap), nameof(Settings.LockOnFraming),
            nameof(Settings.ObstructionFade), nameof(Settings.HitStopIntensity), nameof(Settings.CombatFlashIntensity),
            nameof(Settings.LockOnAssist), nameof(Settings.AimAssistStrength), nameof(Settings.ShowTutorials),
        },

        // Interface
        new[]
        {
            nameof(Settings.HudElementModes), nameof(Settings.HudScale), nameof(Settings.HudOpacity),
            nameof(Settings.HudSafeZone), nameof(Settings.ToastDuration), nameof(Settings.DamageNumberMode),
            nameof(Settings.DamageNumbers), nameof(Settings.StaticMenuBackground),
        },

        // Accessibility
        new[]
        {
            nameof(Settings.ReducedMotion), nameof(Settings.SubtitlesEnabled), nameof(Settings.UiScale),
            nameof(Settings.TextScale), nameof(Settings.ColorVision), nameof(Settings.HighContrast),
            nameof(Settings.ReadableFont), nameof(Settings.HoldsToPresses), nameof(Settings.SubtitleSize),
            nameof(Settings.SubtitleBackground), nameof(Settings.SubtitleSpeakerNames),
        },
    };

    /// <summary>The fields the tab shows, which are the fields its reset puts back.</summary>
    public static IReadOnlyList<string> Fields(SettingsTab tab) =>
        (int)tab >= 0 && (int)tab < Table.Length ? Table[(int)tab] : Array.Empty<string>();

    /// <summary>
    /// What "reset all settings" puts back: every tab but Accessibility, and never the bindings.
    /// A player who needs large text or high contrast to read the screen at all must not lose them
    /// to a button on another page, and a layout that took ten minutes to build is reset from the
    /// Controls tab, by name.
    /// </summary>
    public static IReadOnlyList<string> ResetAllFields()
    {
        var fields = new List<string>();
        for (int tab = 0; tab < Table.Length; tab++)
        {
            if ((SettingsTab)tab == SettingsTab.Accessibility)
            {
                continue;
            }

            foreach (string field in Table[tab])
            {
                if (Array.IndexOf(BindingFields, field) < 0)
                {
                    fields.Add(field);
                }
            }
        }

        return fields;
    }

    /// <summary>The tab <paramref name="delta"/> steps along the strip, wrapping at both ends.</summary>
    public static SettingsTab Step(SettingsTab current, int delta) =>
        (SettingsTab)(((((int)current + delta) % TabCount) + TabCount) % TabCount);

    /// <summary>Whether the screen lays out in one column at this logical viewport width.</summary>
    public static bool Narrow(float logicalWidth) => logicalWidth < NarrowWidth;

    /// <summary>
    /// Whether two setting values are the same setting. Sliders hand back a double that was
    /// snapped to a step, so a float is compared with a little slack; a saved list is compared by
    /// what is in it, because an empty list and the default's empty list are different objects.
    /// </summary>
    public static bool SameValue(object? a, object? b)
    {
        if (a is float x && b is float y)
        {
            return MathF.Abs(x - y) < 0.0005f;
        }

        if (a is Array first && b is Array second)
        {
            return ((IStructuralEquatable)first).Equals(second, StructuralComparisons.StructuralEqualityComparer);
        }

        return Equals(a, b);
    }
}
