using Embervale.Core;
using Embervale.Core.Diagnostics;
using Embervale.Core.Services;
using Embervale.Localization;
using Embervale.Settings;
using Godot;

namespace Embervale.UI;

/// <summary>The Graphics section of <see cref="SettingsPanel"/>: window, preset and the per-control overrides.</summary>
public partial class SettingsPanel
{
    private void BuildGraphics(VBoxContainer body)
    {
        var s = _settings.Current;

        Section(body, Loc.T("settings.section.graphics"));
        body.AddChild(DropdownRow(Loc.T("settings.window_mode"),
            new[] { Loc.T("settings.window_mode.windowed"), Loc.T("settings.window_mode.fullscreen"), Loc.T("settings.window_mode.borderless") },
            s.WindowMode, i => { s.WindowMode = i; Persist(); }));
        // Presets read cheapest first; the saved int behind each is GraphicsMath's, not this order.
        _quality = UiTheme.Dropdown(
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
        body.AddChild(Row(Loc.T("settings.render_quality"), _quality));
        body.AddChild(ToggleRow(Loc.T("settings.vsync"), s.VSync, v => { s.VSync = v; Persist(); }));
        // Applies live: FOV is only judgeable by watching the world move under it.
        body.AddChild(SliderRow(Loc.T("settings.fov"), 60.0, 110.0, 1.0, s.FieldOfView,
            v => s.FieldOfView = (float)v));
        int[] fpsPresets = { 0, 30, 40, 60, 120, 144 };
        body.AddChild(DropdownRow(Loc.T("settings.max_fps"),
            new[] { Loc.T("settings.max_fps.uncapped"), "30", "40", "60", "120", "144" },
            System.Array.IndexOf(fpsPresets, s.MaxFps) is var fi && fi >= 0 ? fi : 0,
            i => { s.MaxFps = fpsPresets[i]; Persist(); }));

        BuildAdvancedGraphics(body);
    }

    // --- Advanced graphics --------------------------------------------------

    private OptionButton _quality = null!;

    /// <summary>One per advanced control: shows what the given preset plus the saved overrides
    /// resolve to, without raising the control's own change signal.</summary>
    private readonly System.Collections.Generic.List<System.Action<World.RenderQualityResource>> _graphicsSync = new();

    private World.RenderQualityResource? Preset() =>
        World.RenderQualityResource.ForTier(_settings.Current.RenderQuality);

    /// <summary>
    /// The individual controls a preset drives. Each shows the preset's value until the player moves
    /// it; a moved control is stored as an override and the preset then reads "Custom". Setting it
    /// back to the preset's value, or picking any preset, returns to the plain preset.
    /// </summary>
    private void BuildAdvancedGraphics(VBoxContainer body)
    {
        var s = _settings.Current;
        Section(body, Loc.T("settings.section.graphics_advanced"));

        // Render scale. Live while dragging, like every slider here, and written on release.
        var scaleBox = new HBoxContainer();
        scaleBox.AddThemeConstantOverride("separation", UiTheme.SpaceSm);
        HSlider scale = UiTheme.Slider(GraphicsMath.MinRenderScale, GraphicsMath.MaxRenderScale, 0.05, 1.0, 150f);
        Label scaleReadout = UiTheme.Body(string.Empty, UiTheme.Dim);
        scaleReadout.CustomMinimumSize = new Vector2(48, 0);
        scaleReadout.HorizontalAlignment = HorizontalAlignment.Right;
        scale.ValueChanged += v =>
        {
            if (Preset() is not { } preset)
            {
                return;
            }

            s.RenderScale = GraphicsMath.OverrideScale((float)v, preset.RenderScale);
            scaleReadout.Text = $"{Mathf.RoundToInt((float)v * 100f)}%";
            _settings.Apply();
            SyncQualityLabel();
        };
        scale.DragEnded += _ => Persist();
        scaleBox.AddChild(scale);
        scaleBox.AddChild(scaleReadout);
        body.AddChild(Row(Loc.T("settings.render_scale"), scaleBox, Loc.T("settings.render_scale.note")));
        _graphicsSync.Add(preset =>
        {
            float value = GraphicsMath.ResolveScale(s.RenderScale, preset.RenderScale);
            scale.SetValueNoSignal(value);
            scaleReadout.Text = $"{Mathf.RoundToInt(value * 100f)}%";
        });

        // FSR 2.2 is offered because Forward+ supports it, and never chosen by a preset: it is the
        // most expensive of the three and an integrated GPU is exactly where that shows.
        AdvancedChoice(body, Loc.T("settings.scaling_mode"),
            new[] { Loc.T("settings.scaling_mode.bilinear"), Loc.T("settings.scaling_mode.fsr1"), Loc.T("settings.scaling_mode.fsr2") },
            preset => GraphicsMath.Resolve(s.ScalingMode, preset.ScalingMode),
            (value, preset) => s.ScalingMode = GraphicsMath.Override(value, preset.ScalingMode),
            Loc.T("settings.scaling_mode.note"));
        AdvancedChoice(body, Loc.T("settings.anti_aliasing"),
            new[]
            {
                Loc.T("settings.anti_aliasing.off"), Loc.T("settings.anti_aliasing.fxaa"),
                Loc.T("settings.anti_aliasing.msaa2"), Loc.T("settings.anti_aliasing.msaa4"),
                Loc.T("settings.anti_aliasing.taa"),
            },
            preset => GraphicsMath.Resolve(s.AntiAliasing, preset.AntiAliasing),
            (value, preset) => s.AntiAliasing = GraphicsMath.Override(value, preset.AntiAliasing));
        AdvancedChoice(body, Loc.T("settings.shadow_quality"),
            new[]
            {
                Loc.T("settings.shadow_quality.off"), Loc.T("settings.shadow_quality.lowest"),
                Loc.T("settings.shadow_quality.low"), Loc.T("settings.shadow_quality.medium"),
                Loc.T("settings.shadow_quality.high"), Loc.T("settings.shadow_quality.ultra"),
            },
            _ => GraphicsMath.Resolve(s.ShadowQuality, GraphicsMath.PresetShadowChoice(s.RenderQuality)),
            (value, _) => s.ShadowQuality = GraphicsMath.Override(value, GraphicsMath.PresetShadowChoice(s.RenderQuality)));

        AdvancedToggle(body, Loc.T("settings.ambient_occlusion"),
            preset => GraphicsMath.Resolve(s.AmbientOcclusion, preset.AmbientOcclusion),
            (value, preset) => s.AmbientOcclusion = GraphicsMath.Override(value, preset.AmbientOcclusion));
        AdvancedToggle(body, Loc.T("settings.volumetric_fog"),
            preset => GraphicsMath.Resolve(s.VolumetricFog, preset.VolumetricFog),
            (value, preset) => s.VolumetricFog = GraphicsMath.Override(value, preset.VolumetricFog));
        AdvancedToggle(body, Loc.T("settings.glow"),
            preset => GraphicsMath.Resolve(s.Glow, preset.Glow),
            (value, preset) => s.Glow = GraphicsMath.Override(value, preset.Glow));

        RefreshGraphics();
    }

    private void AdvancedChoice(VBoxContainer body, string label, string[] options,
        System.Func<World.RenderQualityResource, int> read,
        System.Action<int, World.RenderQualityResource> write, string? explanation = null)
    {
        OptionButton dropdown = UiTheme.Dropdown(options, 0);
        dropdown.ItemSelected += index =>
        {
            if (Preset() is not { } preset)
            {
                return;
            }

            write((int)index, preset);
            Persist();
            SyncQualityLabel();
        };
        body.AddChild(Row(label, dropdown, explanation));
        _graphicsSync.Add(preset => dropdown.Select(System.Math.Clamp(read(preset), 0, options.Length - 1)));
    }

    private void AdvancedToggle(VBoxContainer body, string label,
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
        };
        body.AddChild(Row(label, toggle));
        _graphicsSync.Add(preset => toggle.SetPressedNoSignal(read(preset)));
    }

    /// <summary>Re-reads every advanced control from the preset and overrides now in force.</summary>
    private void RefreshGraphics()
    {
        SyncQualityLabel();
        if (Preset() is not { } preset)
        {
            return;
        }

        foreach (System.Action<World.RenderQualityResource> sync in _graphicsSync)
        {
            sync(preset);
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
    }
}
