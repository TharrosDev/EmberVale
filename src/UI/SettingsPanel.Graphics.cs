using Embervale.Core;
using Embervale.Core.Diagnostics;
using Embervale.Core.Services;
using Embervale.Localization;
using Embervale.Settings;
using Godot;
using S = Embervale.Settings.Settings;

namespace Embervale.UI;

/// <summary>The Graphics tab of <see cref="SettingsPanel"/>: window, preset and the per-control overrides.</summary>
public partial class SettingsPanel
{
    private void BuildGraphics(VBoxContainer body)
    {
        var s = _settings.Current;

        Section(body, Loc.T("settings.section.display"), first: true);
        body.AddChild(DropdownRow(
            Info(Loc.T("settings.window_mode"), Loc.T("settings.window_mode.desc"), nameof(S.WindowMode)),
            new[] { Loc.T("settings.window_mode.windowed"), Loc.T("settings.window_mode.fullscreen"), Loc.T("settings.window_mode.borderless") },
            s.WindowMode, i => { s.WindowMode = i; Persist(); }));

        // Presets read cheapest first; the saved int behind each is GraphicsMath's, not this order.
        // Restoring the row puts back the default preset and drops every override with it.
        RowInfo quality = Info(Loc.T("settings.render_quality"), Loc.T("settings.render_quality.desc"),
            nameof(S.RenderQuality), nameof(S.RenderScale), nameof(S.ScalingMode), nameof(S.AntiAliasing),
            nameof(S.ShadowQuality), nameof(S.AmbientOcclusion), nameof(S.VolumetricFog), nameof(S.Glow));
        _qualityInfo = quality;
        _quality = Dropdown(
            new[]
            {
                Loc.T("settings.render_quality.performance"), Loc.T("settings.render_quality.low"),
                Loc.T("settings.render_quality.medium"), Loc.T("settings.render_quality.high"),
                Loc.T("settings.render_quality.ultra"),
            },
            GraphicsMath.UiIndexOfTier(s.RenderQuality));
        _quality.ItemSelected += index =>
        {
            if (index >= GraphicsMath.UiOrder.Length)
            {
                return; // the Custom entry names a state, it is not a choice
            }

            s.RenderQuality = GraphicsMath.TierFromUiIndex((int)index);
            s.ClearGraphicsOverrides();
            Persist();
            // Deferred: dropping the Custom entry edits this dropdown's own list mid-selection.
            Callable.From(RefreshGraphics).CallDeferred();
        };
        body.AddChild(Row(quality, _quality));
        body.AddChild(ToggleRow(
            Info(Loc.T("settings.vsync"), Loc.T("settings.vsync.desc"), nameof(S.VSync)),
            s.VSync, v => { s.VSync = v; Persist(); }));
        // Applies live: FOV is only judgeable by watching the world move under it.
        body.AddChild(SliderRow(
            Info(Loc.T("settings.fov"), Loc.T("settings.fov.desc"), nameof(S.FieldOfView)),
            60.0, 110.0, 1.0, s.FieldOfView, v => s.FieldOfView = v, Whole));
        int[] fpsPresets = { 0, 30, 40, 60, 120, 144 };
        body.AddChild(DropdownRow(
            Info(Loc.T("settings.max_fps"), Loc.T("settings.max_fps.desc"), nameof(S.MaxFps)),
            new[] { Loc.T("settings.max_fps.uncapped"), "30", "40", "60", "120", "144" },
            System.Array.IndexOf(fpsPresets, s.MaxFps) is var fi && fi >= 0 ? fi : 0,
            i => { s.MaxFps = fpsPresets[i]; Persist(); }));

        BuildAdvancedGraphics(body);
    }

    // --- Advanced graphics --------------------------------------------------

    private OptionButton _quality = null!;
    private RowInfo? _qualityInfo;

    /// <summary>One per advanced control: shows what the given preset plus the saved overrides
    /// resolve to, without raising the control's own change signal.</summary>
    private readonly System.Collections.Generic.List<System.Action<World.RenderQualityResource>> _graphicsSync = new();

    private World.RenderQualityResource? Preset() =>
        World.RenderQualityResource.ForTier(_settings.Current.RenderQuality);

    /// <summary>
    /// The individual controls a preset drives. Each shows the preset's value until the player moves
    /// it; a moved control is stored as an override and the preset then reads "Custom". Setting it
    /// back to the preset's value, restoring the row, or picking any preset, returns it to the
    /// preset: a control's default here is "what the preset says", not a fixed value.
    /// </summary>
    private void BuildAdvancedGraphics(VBoxContainer body)
    {
        var s = _settings.Current;
        Section(body, Loc.T("settings.section.advanced"));

        // Render scale. Live while dragging, like every slider here, and written on release.
        RowInfo scaleInfo = Info(Loc.T("settings.render_scale"), Loc.T("settings.render_scale.note"), nameof(S.RenderScale));
        var scaleBox = new HBoxContainer();
        scaleBox.AddThemeConstantOverride("separation", UiTheme.SpaceSm);
        HSlider scale = UiTheme.Slider(GraphicsMath.MinRenderScale, GraphicsMath.MaxRenderScale, 0.05, 1.0,
            UiTheme.SettingsControlColumn - UiTheme.SettingsReadout - UiTheme.SpaceSm);
        scale.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        Label scaleReadout = UiTheme.Body(string.Empty, UiTheme.Dim);
        scaleReadout.CustomMinimumSize = new Vector2(UiTheme.SettingsReadout, 0f);
        scaleReadout.HorizontalAlignment = HorizontalAlignment.Right;
        scale.ValueChanged += v =>
        {
            if (Preset() is not { } preset)
            {
                return;
            }

            s.RenderScale = GraphicsMath.OverrideScale((float)v, preset.RenderScale);
            scaleReadout.Text = Percent((float)v);
            _settings.Apply();
            SyncQualityLabel();
            Touched(scaleInfo);
        };
        scale.DragEnded += _ => Persist();
        scaleBox.AddChild(scale);
        scaleBox.AddChild(scaleReadout);
        body.AddChild(Row(scaleInfo, scaleBox));
        _graphicsSync.Add(preset =>
        {
            float value = GraphicsMath.ResolveScale(s.RenderScale, preset.RenderScale);
            scale.SetValueNoSignal(value);
            scaleReadout.Text = Percent(value);
        });

        // FSR 2.2 is offered because Forward+ supports it, and never chosen by a preset: it is the
        // most expensive of the three and an integrated GPU is exactly where that shows.
        AdvancedChoice(body,
            Info(Loc.T("settings.scaling_mode"), Loc.T("settings.scaling_mode.note"), nameof(S.ScalingMode)),
            new[] { Loc.T("settings.scaling_mode.bilinear"), Loc.T("settings.scaling_mode.fsr1"), Loc.T("settings.scaling_mode.fsr2") },
            preset => GraphicsMath.Resolve(s.ScalingMode, preset.ScalingMode),
            (value, preset) => s.ScalingMode = GraphicsMath.Override(value, preset.ScalingMode));
        AdvancedChoice(body,
            Info(Loc.T("settings.anti_aliasing"), Loc.T("settings.anti_aliasing.desc"), nameof(S.AntiAliasing)),
            new[]
            {
                Loc.T("settings.anti_aliasing.off"), Loc.T("settings.anti_aliasing.fxaa"),
                Loc.T("settings.anti_aliasing.msaa2"), Loc.T("settings.anti_aliasing.msaa4"),
                Loc.T("settings.anti_aliasing.taa"),
            },
            preset => GraphicsMath.Resolve(s.AntiAliasing, preset.AntiAliasing),
            (value, preset) => s.AntiAliasing = GraphicsMath.Override(value, preset.AntiAliasing));
        AdvancedChoice(body,
            Info(Loc.T("settings.shadow_quality"), Loc.T("settings.shadow_quality.desc"), nameof(S.ShadowQuality)),
            new[]
            {
                Loc.T("settings.shadow_quality.off"), Loc.T("settings.shadow_quality.lowest"),
                Loc.T("settings.shadow_quality.low"), Loc.T("settings.shadow_quality.medium"),
                Loc.T("settings.shadow_quality.high"), Loc.T("settings.shadow_quality.ultra"),
            },
            _ => GraphicsMath.Resolve(s.ShadowQuality, GraphicsMath.PresetShadowChoice(s.RenderQuality)),
            (value, _) => s.ShadowQuality = GraphicsMath.Override(value, GraphicsMath.PresetShadowChoice(s.RenderQuality)));

        AdvancedToggle(body,
            Info(Loc.T("settings.ambient_occlusion"), Loc.T("settings.ambient_occlusion.desc"), nameof(S.AmbientOcclusion)),
            preset => GraphicsMath.Resolve(s.AmbientOcclusion, preset.AmbientOcclusion),
            (value, preset) => s.AmbientOcclusion = GraphicsMath.Override(value, preset.AmbientOcclusion));
        AdvancedToggle(body,
            Info(Loc.T("settings.volumetric_fog"), Loc.T("settings.volumetric_fog.desc"), nameof(S.VolumetricFog)),
            preset => GraphicsMath.Resolve(s.VolumetricFog, preset.VolumetricFog),
            (value, preset) => s.VolumetricFog = GraphicsMath.Override(value, preset.VolumetricFog));
        AdvancedToggle(body,
            Info(Loc.T("settings.glow"), Loc.T("settings.glow.desc"), nameof(S.Glow)),
            preset => GraphicsMath.Resolve(s.Glow, preset.Glow),
            (value, preset) => s.Glow = GraphicsMath.Override(value, preset.Glow));

        RefreshGraphics();
    }

    private void AdvancedChoice(VBoxContainer body, RowInfo info, string[] options,
        System.Func<World.RenderQualityResource, int> read,
        System.Action<int, World.RenderQualityResource> write)
    {
        OptionButton dropdown = Dropdown(options, 0);
        dropdown.ItemSelected += index =>
        {
            if (Preset() is not { } preset)
            {
                return;
            }

            write((int)index, preset);
            Persist();
            SyncQualityLabel();
            Touched(info);
        };
        body.AddChild(Row(info, dropdown));
        _graphicsSync.Add(preset => dropdown.Select(System.Math.Clamp(read(preset), 0, options.Length - 1)));
    }

    private void AdvancedToggle(VBoxContainer body, RowInfo info,
        System.Func<World.RenderQualityResource, bool> read,
        System.Action<bool, World.RenderQualityResource> write)
    {
        CheckButton toggle = UiTheme.Toggle(false);
        toggle.Toggled += pressed =>
        {
            if (Preset() is not { } preset)
            {
                return;
            }

            write(pressed, preset);
            Persist();
            SyncQualityLabel();
            Touched(info);
        };
        body.AddChild(Row(info, toggle));
        _graphicsSync.Add(preset => toggle.SetPressedNoSignal(read(preset)));
    }

    /// <summary>Re-reads every advanced control from the preset and overrides now in force.</summary>
    private void RefreshGraphics()
    {
        if (_tab != SettingsTab.Graphics || !IsInstanceValid(_quality))
        {
            return; // deferred from a selection, and the tab was left before it ran
        }

        SyncQualityLabel();
        if (Preset() is not { } preset)
        {
            return;
        }

        foreach (System.Action<World.RenderQualityResource> sync in _graphicsSync)
        {
            sync(preset);
        }

        // Picking a preset cleared every override, so every row's restore button goes with them.
        foreach (RowInfo row in _rows)
        {
            if (row.RevertButton != null && row.Changed != null)
            {
                row.RevertButton.Visible = row.Changed();
            }
        }
    }

    /// <summary>Shows the saved preset, or a trailing "Custom" entry while any control departs from it.</summary>
    private void SyncQualityLabel()
    {
        int presets = GraphicsMath.UiOrder.Length;
        bool custom = _settings.Current.Overrides().IsCustom;
        if (custom && _quality.ItemCount == presets)
        {
            _quality.AddItem(Loc.T("settings.render_quality.custom"), presets);
        }
        else if (!custom && _quality.ItemCount > presets)
        {
            _quality.RemoveItem(presets);
        }

        _quality.Select(custom ? presets : GraphicsMath.UiIndexOfTier(_settings.Current.RenderQuality));
        if (_qualityInfo is { RevertButton: { } revert, Changed: { } changed })
        {
            revert.Visible = changed();
        }
    }
}
