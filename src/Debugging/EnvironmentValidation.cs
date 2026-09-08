using System;
using System.Collections.Generic;
using Embervale.World;
using Godot;

namespace Embervale.Debugging;

/// <summary>Native authored-data validation; invalid values never silently become artist sliders.</summary>
public static class EnvironmentValidation
{
    public static void Validate(List<string> issues)
    {
        var cycle = GD.Load<EnvironmentCycleResource>("res://data/rendering/DayCycle.tres");
        if (cycle == null || cycle.Keys.Count < 2) { issues.Add("environment cycle requires at least two keys"); return; }
        float previous = -1f;
        foreach (var key in cycle.Keys)
        {
            if (key == null) { issues.Add("environment cycle contains a null key"); continue; }
            Range(issues, "key hour", key.Hour, 0, 23.999f);
            if (key.Hour <= previous) issues.Add("environment keys must be strictly increasing");
            previous = key.Hour;
            Range(issues, "sun energy", key.SunEnergy, 0, 3);
            Range(issues, "moon energy", key.MoonEnergy, 0, .3f);
            Range(issues, "ambient energy", key.AmbientEnergy, .05f, 1.5f);
            Range(issues, "sky energy", key.SkyEnergy, .02f, 2);
            Range(issues, "fog energy", key.FogEnergy, 0, 2);
            Range(issues, "exposure", key.Exposure, .6f, 1.5f);
        }
        Range(issues, "transition seconds", cycle.TransitionSeconds, .1f, 30);
        Range(issues, "clear fog", cycle.ClearFogDensity, 0, .008f);
        Range(issues, "contrast", cycle.Contrast, .8f, 1.2f);
        Range(issues, "saturation", cycle.Saturation, .7f, 1.1f);
        Range(issues, "glow", cycle.GlowIntensity, 0, .5f);
        foreach (string name in new[] { "Low", "Medium", "High", "Ultra" })
        {
            var q = GD.Load<RenderQualityResource>($"res://data/rendering/{name}.tres");
            if (q == null) { issues.Add($"missing render quality {name}"); continue; }
            Range(issues, $"{name} render scale", q.RenderScale, .5f, 1f);
            Range(issues, $"{name} mesh LOD", q.MeshLodThreshold, .5f, 4f);
            if (q.ShadowAtlasSize != 1024 && q.ShadowAtlasSize != 2048 && q.ShadowAtlasSize != 4096)
                issues.Add($"{name} shadow atlas must be 1024, 2048 or 4096");
            Range(issues, $"{name} shadow distance", q.ShadowDistance, 20, 250);
            Range(issues, $"{name} particles", q.ParticleScale, .1f, 1);
            Range(issues, $"{name} local lights", q.LocalShadowLights, 0, 8);
            Range(issues, $"{name} light distance", q.LocalLightDistance, 10, 100);
        }
        foreach (string name in new[] { "Interior", "Dungeon", "Underwater" })
        {
            var profile = GD.Load<EnvironmentSpaceProfileResource>($"res://data/rendering/{name}.tres");
            if (profile == null) { issues.Add($"missing space profile {name}"); continue; }
            Range(issues, $"{name} ambient", profile.AmbientScale, 0.1f, 1);
            Range(issues, $"{name} fog", profile.FogScale, 0, 20);
            Range(issues, $"{name} exposure", profile.ExposureScale, .6f, 1.5f);
            Range(issues, $"{name} shelter", profile.Shelter, 0, 1);
        }
        foreach (var weather in WeatherDatabase.All)
        {
            Range(issues, $"{weather.Id} wind", weather.WindStrength, 0, 8);
            Range(issues, $"{weather.Id} precipitation", weather.Precipitation, 0, 1);
            Range(issues, $"{weather.Id} fog", weather.FogDensity, 0, .15f);
        }
    }

    private static void Range(List<string> issues, string field, float value, float min, float max)
    {
        if (!float.IsFinite(value) || value < min || value > max)
            issues.Add($"environment {field}: {value} outside {min}..{max}");
    }
}
