using Embervale.Core.Diagnostics;
using Godot;

namespace Embervale.Settings;

/// <summary>
/// Loads, persists, and applies the player <see cref="Settings"/> (Phase 24E). Created by the
/// bootstrap before the title menu and registered in the <c>ServiceLocator</c> so any screen
/// (the Phase 24F settings panel, the pause menu) can read/mutate the live settings and re-apply.
///
/// Persistence is a single <c>user://settings.tres</c> via <see cref="ResourceSaver"/> /
/// <see cref="ResourceLoader"/>; a missing or unreadable file falls back to defaults (and is written
/// on the first save). <see cref="Apply"/> pushes graphics options to the engine immediately and
/// audio-bus volumes to whatever buses exist — the default <c>Master</c> bus always, and the rest
/// once the Phase 31 mixer creates them (so the audio fields are "ready for Phase 31 to consume").
/// </summary>
public sealed class SettingsService
{
    private const string SettingsPath = "user://settings.tres";

    /// <summary>The live, mutable settings. Mutate fields then call <see cref="Save"/> + <see cref="Apply"/>.</summary>
    public Settings Current { get; private set; } = new();

    /// <summary>Loads settings from disk (or defaults) and applies them. Call once on boot.</summary>
    public void LoadAndApply()
    {
        Current = Load();
        Apply();
    }

    private static Settings Load()
    {
        if (!FileAccess.FileExists(SettingsPath))
        {
            return new Settings();
        }

        // Ignore the resource cache so a fresh launch (or a reload after an external edit) reads the
        // file rather than a stale in-memory copy.
        if (ResourceLoader.Load<Settings>(SettingsPath, cacheMode: ResourceLoader.CacheMode.Ignore) is { } loaded)
        {
            return loaded;
        }

        Log.Warn($"settings.tres exists but could not be read as Settings; using defaults.");
        return new Settings();
    }

    /// <summary>Writes the current settings to <c>user://settings.tres</c>. Returns success.</summary>
    public bool Save()
    {
        Error error = ResourceSaver.Save(Current, SettingsPath);
        if (error != Error.Ok)
        {
            Log.Error($"Could not save settings to {SettingsPath}: {error}.");
            return false;
        }

        Log.Info("Settings saved.");
        return true;
    }

    /// <summary>Pushes the current settings to the engine: window mode, vsync, frame cap, and every
    /// audio bus volume that currently exists.</summary>
    public void Apply()
    {
        ApplyGraphics();
        ApplyAudio();
    }

    private void ApplyGraphics()
    {
        DisplayServer.WindowMode mode = Current.WindowMode switch
        {
            1 => DisplayServer.WindowMode.Fullscreen,
            2 => DisplayServer.WindowMode.Windowed, // borderless = windowed + the borderless flag below
            _ => DisplayServer.WindowMode.Windowed,
        };
        DisplayServer.WindowSetMode(mode);
        DisplayServer.WindowSetFlag(DisplayServer.WindowFlags.Borderless, Current.WindowMode == 2);

        DisplayServer.WindowSetVsyncMode(Current.VSync
            ? DisplayServer.VSyncMode.Enabled
            : DisplayServer.VSyncMode.Disabled);

        Engine.MaxFps = Current.MaxFps < 0 ? 0 : Current.MaxFps;
    }

    private void ApplyAudio()
    {
        foreach ((string bus, float linear) in Current.BusVolumes())
        {
            int index = AudioServer.GetBusIndex(bus);
            if (index < 0)
            {
                continue; // bus not created yet (Phase 31) — the stored value still persists for later
            }

            AudioServer.SetBusVolumeDb(index, SettingsMath.LinearToDb(SettingsMath.ClampVolume(linear)));
        }
    }

    /// <summary>Resets to defaults in memory (callers then <see cref="Save"/>/<see cref="Apply"/>).</summary>
    public void ResetToDefaults() => Current = new Settings();
}
