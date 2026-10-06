using Embervale.Core;
using Embervale.Core.Diagnostics;
using Embervale.Core.Events;
using Godot;

namespace Embervale.Settings;

/// <summary>Raised after <see cref="SettingsService.Apply"/> pushes the current settings, so
/// gameplay systems that read a setting (the camera-mode toggle, future ones) refresh live.</summary>
public readonly record struct SettingsAppliedEvent(Settings Current) : IGameEvent;

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
    private static string SettingsPath => Embervale.Core.UserDataPaths.Resolve("settings.tres");

    /// <summary>The live, mutable settings. Mutate fields then call <see cref="Save"/> + <see cref="Apply"/>.</summary>
    public Settings Current { get; private set; } = new();

    /// <summary>Loads settings from disk (or defaults) and applies them. Call once on boot.</summary>
    public void LoadAndApply()
    {
        bool firstRun = !FileAccess.FileExists(SettingsPath);
        Current = Load();
        if (firstRun && AutoDetectAllowed())
        {
            AutoDetectGraphics();
        }

        // The cap follows the game state so a menu never renders faster than it needs to. One
        // application-lifetime subscription: this service lives exactly as long as the bus does.
        EventBus.Instance?.Subscribe<GameStateChangedEvent>(OnGameStateChanged);
        Apply();
    }

    private void OnGameStateChanged(GameStateChangedEvent e) => ApplyFrameCap(e.Current);

    /// <summary>
    /// ⚠️ Automation keeps the class default. A probe or capture run has no settings file by
    /// construction (<c>EMBERVALE_USER_DIR</c> is a fresh folder each run), so detecting there would
    /// move every baseline to whatever tier the capturing machine earns.
    /// </summary>
    private static bool AutoDetectAllowed() =>
        DisplayServer.GetName() != "headless" &&
        string.IsNullOrWhiteSpace(OS.GetEnvironment("EMBERVALE_USER_DIR"));

    /// <summary>First run only: start on the preset the adapter can carry, and write it down so the
    /// pick is the player's from then on. A saved file is never re-detected over.</summary>
    private void AutoDetectGraphics()
    {
        string adapter = RenderingServer.GetVideoAdapterName();
        long memory = OS.GetMemoryInfo().TryGetValue("physical", out Variant physical) ? physical.AsInt64() : 0L;
        GraphicsRecommendation pick = GraphicsAutoDetect.Recommend(
            adapter, RenderingServer.GetVideoAdapterVendor(), (int)RenderingServer.GetVideoAdapterType(),
            memory, OS.GetProcessorCount());
        Current.RenderQuality = pick.Tier;
        Current.MaxFps = pick.MaxFps;
        Log.Info($"First run: graphics preset '{GraphicsMath.TierName(pick.Tier)}' chosen for '{adapter}' " +
                 $"({memory / (1024L * 1024L * 1024L)} GB, {OS.GetProcessorCount()} threads).");
        Save();
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
        EventBus.Instance?.Publish(new SettingsAppliedEvent(Current));
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

        ApplyFrameCap(GameManager.Instance?.State ?? GameState.Boot);

        // Global UI scale (30.5B): with the project's canvas_items stretch, the window's content
        // scale factor resizes every 2D surface (HUD, panels, menus) without touching 3D rendering.
        if (Engine.GetMainLoop() is SceneTree tree)
        {
            tree.Root.ContentScaleFactor = Mathf.Clamp(Current.UiScale, 0.75f, 1.5f);
        }
    }

    /// <summary>The saved cap in play; never above <see cref="GraphicsMath.MenuFpsCap"/> on the
    /// title or pause screen. A headless run has no GPU to spare and its gates count frames, so it
    /// is left exactly as saved.</summary>
    private void ApplyFrameCap(GameState state)
    {
        bool inMenu = state is GameState.Boot or GameState.MainMenu or GameState.Paused
            && DisplayServer.GetName() != "headless";
        Engine.MaxFps = GraphicsMath.FpsCap(Current.MaxFps, inMenu);
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
