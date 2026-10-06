using Embervale.Localization;
using Embervale.Settings;
using Godot;
using S = Embervale.Settings.Settings;

namespace Embervale.UI;

/// <summary>
/// The Interface tab of <see cref="SettingsPanel"/>: the HUD preset and each element's own mode,
/// the HUD's size, opacity and safe zone, and how notices and damage numbers behave. The modes and
/// presets are <see cref="HudPresets"/>; <c>GameHud</c> applies them on every settings apply.
/// </summary>
public partial class SettingsPanel
{
    private void BuildInterface(VBoxContainer body)
    {
        var s = _settings.Current;

        Section(body, Loc.T("settings.section.hud"), first: true);

        // The preset is not saved: it is whichever table the per-element list matches, and a list
        // that matches none reads as a trailing "Custom" entry that names the state.
        HudPreset preset = HudPresets.Detect(s.HudElementModes);
        var presets = new System.Collections.Generic.List<string>
        {
            Loc.T("settings.hud_preset.full"), Loc.T("settings.hud_preset.dynamic"), Loc.T("settings.hud_preset.minimal"),
        };
        if (preset == HudPreset.Custom)
        {
            presets.Add(Loc.T("settings.hud_preset.custom"));
        }

        body.AddChild(DropdownRow(
            Info(Loc.T("settings.hud_preset"), Loc.T("settings.hud_preset.desc"), nameof(S.HudElementModes)),
            presets.ToArray(), (int)preset, index =>
            {
                if (index < (int)HudPreset.Custom)
                {
                    s.HudElementModes = SavedModes(HudPresets.Modes((HudPreset)index));
                    Persist();
                }

                MarkDirty(); // the element rows below show the preset's modes
            }));

        RowInfo scale = Info(Loc.T("settings.hud_scale"), Loc.T("settings.hud_scale.desc"), nameof(S.HudScale));
        scale.Preview = () => HudDiagram(scale);
        body.AddChild(SliderRow(scale, SettingsMath.HudScaleMin, SettingsMath.HudScaleMax, 0.05,
            SettingsMath.ClampHudScale(s.HudScale), v => s.HudScale = v, Percent));

        RowInfo opacity = Info(Loc.T("settings.hud_opacity"), Loc.T("settings.hud_opacity.desc"), nameof(S.HudOpacity));
        opacity.Preview = () => HudDiagram(opacity);
        body.AddChild(SliderRow(opacity, SettingsMath.HudOpacityMin, 1.0, 0.05,
            SettingsMath.ClampHudOpacity(s.HudOpacity), v => s.HudOpacity = v, Percent));

        RowInfo safeZone = Info(Loc.T("settings.hud_safe_zone"), Loc.T("settings.hud_safe_zone.desc"), nameof(S.HudSafeZone));
        safeZone.Preview = () => HudDiagram(safeZone);
        body.AddChild(SliderRow(safeZone, 0.0, SettingsMath.HudSafeZoneMax, 0.01,
            SettingsMath.ClampHudSafeZone(s.HudSafeZone), v => s.HudSafeZone = v, Percent));

        Section(body, Loc.T("settings.section.hud_elements"));
        string[] modes =
        {
            Loc.T("settings.hud_mode.always"), Loc.T("settings.hud_mode.dynamic"), Loc.T("settings.hud_mode.hidden"),
        };
        HudElementMode[] current = HudPresets.FromSaved(s.HudElementModes);
        for (int i = 0; i < HudPresets.ElementCount; i++)
        {
            var element = (HudElement)i;
            var info = new RowInfo
            {
                Title = ElementName(element),
                Description = ElementDescription(element),
                Changed = () => HudPresets.FromSaved(s.HudElementModes)[(int)element] != HudElementMode.Always,
                Revert = () => SetElementMode(element, HudElementMode.Always),
            };
            body.AddChild(DropdownRow(info, modes, (int)current[i], index =>
            {
                SetElementMode(element, (HudElementMode)index);
                Persist();
                MarkDirty(); // the preset row above now names a different preset, or Custom
            }));
        }

        Section(body, Loc.T("settings.section.notices"));
        body.AddChild(SliderRow(
            Info(Loc.T("settings.toast_duration"), Loc.T("settings.toast_duration.desc"), nameof(S.ToastDuration)),
            SettingsMath.ToastDurationMin, SettingsMath.ToastDurationMax, 0.25,
            SettingsMath.ClampToastDuration(s.ToastDuration), v => s.ToastDuration = v, Times));

        // The mode replaced an on/off toggle that older code still reads, so both are written: off
        // is off for both, and anything else leaves the toggle on.
        body.AddChild(DropdownRow(
            Info(Loc.T("settings.damage_numbers"), Loc.T("settings.damage_numbers.desc"),
                nameof(S.DamageNumberMode), nameof(S.DamageNumbers)),
            new[]
            {
                Loc.T("settings.damage_numbers.off"), Loc.T("settings.damage_numbers.all"),
                Loc.T("settings.damage_numbers.own"), Loc.T("settings.damage_numbers.crits"),
            },
            SettingsMath.DamageNumberMode(s.DamageNumberMode, s.DamageNumbers),
            index =>
            {
                s.DamageNumberMode = index;
                s.DamageNumbers = index != 0;
                Persist();
            }));

        Section(body, Loc.T("settings.section.menus"));
        body.AddChild(ToggleRow(
            Info(Loc.T("settings.static_menu"), Loc.T("settings.static_menu.desc"), nameof(S.StaticMenuBackground)),
            s.StaticMenuBackground, v => { s.StaticMenuBackground = v; Persist(); }));
    }

    private static string Times(float value) => $"{value:0.00}x";

    private void SetElementMode(HudElement element, HudElementMode mode)
    {
        HudElementMode[] modes = HudPresets.FromSaved(_settings.Current.HudElementModes);
        modes[(int)element] = mode;
        _settings.Current.HudElementModes = SavedModes(modes);
    }

    /// <summary>The list to save. Everything on Always is saved as the empty list, which is the
    /// default, so going back to Full by hand leaves nothing marked as changed.</summary>
    private static int[] SavedModes(HudElementMode[] modes) =>
        HudPresets.Detect(modes) == HudPreset.Full ? System.Array.Empty<int>() : HudPresets.ToSaved(modes);

    private static string ElementName(HudElement element) => element switch
    {
        HudElement.Vitals => Loc.T("settings.hud_element.vitals"),
        HudElement.Hotbar => Loc.T("settings.hud_element.hotbar"),
        HudElement.Compass => Loc.T("settings.hud_element.compass"),
        HudElement.Minimap => Loc.T("settings.hud_element.minimap"),
        HudElement.Clock => Loc.T("settings.hud_element.clock"),
        HudElement.QuestTracker => Loc.T("settings.hud_element.tracker"),
        HudElement.Party => Loc.T("settings.hud_element.party"),
        HudElement.TargetPlate => Loc.T("settings.hud_element.target"),
        HudElement.EnemyPlates => Loc.T("settings.hud_element.enemy_plates"),
        HudElement.DamageNumbers => Loc.T("settings.hud_element.damage_numbers"),
        HudElement.Prompts => Loc.T("settings.hud_element.prompts"),
        HudElement.Crosshair => Loc.T("settings.hud_element.crosshair"),
        HudElement.Toasts => Loc.T("settings.hud_element.toasts"),
        HudElement.Subtitles => Loc.T("settings.hud_element.subtitles"),
        _ => element.ToString(),
    };

    /// <summary>What Dynamic means for the element, which is the one thing its name does not say.
    /// Grouped as <see cref="HudDynamicRules"/> groups them.</summary>
    private static string ElementDescription(HudElement element) => element switch
    {
        HudElement.Vitals => Loc.T("settings.hud_dynamic.vitals"),
        HudElement.Hotbar => Loc.T("settings.hud_dynamic.hotbar"),
        HudElement.Party => Loc.T("settings.hud_dynamic.party"),
        HudElement.Crosshair or HudElement.TargetPlate or HudElement.EnemyPlates => Loc.T("settings.hud_dynamic.combat"),
        HudElement.Compass or HudElement.Minimap or HudElement.Clock or HudElement.QuestTracker =>
            Loc.T("settings.hud_dynamic.steady"),
        _ => Loc.T("settings.hud_dynamic.transient"),
    };
}
