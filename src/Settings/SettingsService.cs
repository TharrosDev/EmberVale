using System.Collections.Generic;
using System.Reflection;
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

        // The saved bindings go into the input map before any screen can draw a prompt. The actions
        // themselves were registered a moment ago (GameInput.EnsureActions); with nothing remapped
        // this changes nothing.
        ApplyBindings();
        if (firstRun && AutoDetectAllowed())
        {
            if (HasExistingSaves())
            {
                // Not a fresh install: this player has been running the class default without ever
                // opening the options panel. Keep it, and write it down so it is never re-detected.
                Save();
            }
            else
            {
                AutoDetectGraphics();
            }
        }

        // The cap follows the game state so an unpaced menu does not run flat out. One
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

    private static bool HasExistingSaves() =>
        Embervale.Save.SaveManager.Instance is { } saves && saves.ListSlots().Count > 0;

    /// <summary>A fresh install only (no settings file and no saves): start on the preset the adapter can carry, and write it down so the
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

        // Only when a saved list was replaced (or an earlier attempt was refused): settings are
        // applied on every tick of a dragged slider, and the input map has no reason to hear that.
        if (!BindingsApplied || !ReferenceEquals(_appliedKeys, Current.KeyBindings)
                             || !ReferenceEquals(_appliedPads, Current.PadBindings))
        {
            ApplyBindings();
        }

        EventBus.Instance?.Publish(new SettingsAppliedEvent(Current));
    }

    private string[]? _appliedKeys;
    private string[]? _appliedPads;

    /// <summary>False while the saved bindings have not reached the input map, because it was
    /// lent out when they were applied (see <see cref="GameInput.ApplyBindings"/>).</summary>
    public bool BindingsApplied { get; private set; }

    /// <summary>Pushes the saved bindings into the input map. Returns whether they went in; a
    /// caller that got false asks again once the keyboard or d-pad is handed back.</summary>
    public bool ApplyBindings()
    {
        BindingsApplied = GameInput.ApplyBindings(Current);
        if (BindingsApplied)
        {
            _appliedKeys = Current.KeyBindings;
            _appliedPads = Current.PadBindings;
        }

        return BindingsApplied;
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

    /// <summary>The saved cap, always. Only when nothing paces the frame at all (V-Sync off and no
    /// saved cap) does the title or pause screen fall back to <see cref="GraphicsMath.MenuFpsCap"/>.
    /// A headless run's gates count frames, so it is left exactly as saved.</summary>
    private void ApplyFrameCap(GameState state)
    {
        bool unpacedMenu = state is GameState.Boot or GameState.MainMenu or GameState.Paused
            && !Current.VSync
            && DisplayServer.GetName() != "headless";
        Engine.MaxFps = GraphicsMath.FpsCap(Current.MaxFps, unpacedMenu);
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

    // --- Scoped resets (the settings screen) ------------------------------------------------------

    private static Settings? _defaults;

    /// <summary>A settings object nobody has touched: what every field is before the player
    /// changes it. Read-only by convention.</summary>
    public static Settings Defaults => _defaults ??= new Settings();

    /// <summary>Whether any of the named <see cref="Settings"/> fields differs from its default.</summary>
    public bool Differs(IEnumerable<string> fields)
    {
        foreach (string field in fields)
        {
            if (Field(field) is { } property &&
                !SettingsTabRules.SameValue(property.GetValue(Current), property.GetValue(Defaults)))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Puts the named fields back to their defaults in memory, and leaves every other
    /// field alone (callers then <see cref="Save"/>/<see cref="Apply"/>).</summary>
    public void ResetFields(IEnumerable<string> fields)
    {
        foreach (string field in fields)
        {
            Field(field)?.SetValue(Current, Field(field)!.GetValue(Defaults));
        }
    }

    /// <summary>Puts one tab of the settings screen back to its defaults. Only the Controls tab
    /// holds the bindings, so only its reset takes them.</summary>
    public void ResetTab(SettingsTab tab) => ResetFields(SettingsTabRules.Fields(tab));

    /// <summary>Puts everything back but the bindings and the accessibility options
    /// (<see cref="SettingsTabRules.ResetAllFields"/> says why).</summary>
    public void ResetAllButBindingsAndAccessibility() => ResetFields(SettingsTabRules.ResetAllFields());

    private static PropertyInfo? Field(string name) =>
        typeof(Settings).GetProperty(name, BindingFlags.Public | BindingFlags.Instance);

    /// <summary>
    /// The multiplier the difficulty setting puts on a blow landing on <paramref name="defender"/>:
    /// <see cref="DifficultyRules"/> for the player, exactly 1 for everyone else and before
    /// settings exist. For the one place incoming damage is resolved.
    /// </summary>
    public static float IncomingDamageScale(Embervale.Entities.IEntity? defender) =>
        defender is Embervale.Player.PlayerCharacter &&
        Embervale.Core.Services.ServiceLocator.Instance is { } locator && locator.TryGet(out SettingsService settings)
            ? DifficultyRules.IncomingPlayerDamage(settings.Current.Difficulty)
            : 1f;
}
