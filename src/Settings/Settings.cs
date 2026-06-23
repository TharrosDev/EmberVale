using Godot;

namespace Embervale.Settings;

/// <summary>
/// The persisted player options (Phase 24E): graphics, audio bus volumes, controls, gameplay, and
/// accessibility. A plain data <see cref="Resource"/> saved to <c>user://settings.tres</c> by
/// <see cref="SettingsService"/> and applied to the engine on boot. Fields are deliberately flat and
/// data-only — the service owns load/save/apply, and later phases consume the fields they need
/// (audio buses in Phase 31, input remap in Phase 54, the reduced-motion guard already in the UI).
/// </summary>
[GlobalClass]
public partial class Settings : Resource
{
    // --- Graphics -----------------------------------------------------------

    /// <summary>0 = Windowed, 1 = Fullscreen, 2 = Borderless windowed. Applied via DisplayServer.</summary>
    [Export] public int WindowMode { get; set; } = 0;

    [Export] public bool VSync { get; set; } = true;

    /// <summary>Frame cap; 0 = uncapped. Applied via <c>Engine.MaxFps</c>.</summary>
    [Export] public int MaxFps { get; set; } = 0;

    // --- Audio (linear 0..1 per bus; ready for the Phase 31 mixer to consume) ----

    [Export(PropertyHint.Range, "0,1")] public float MasterVolume { get; set; } = 1f;
    [Export(PropertyHint.Range, "0,1")] public float MusicVolume { get; set; } = 0.8f;
    [Export(PropertyHint.Range, "0,1")] public float SfxVolume { get; set; } = 1f;
    [Export(PropertyHint.Range, "0,1")] public float AmbienceVolume { get; set; } = 0.8f;
    [Export(PropertyHint.Range, "0,1")] public float UiVolume { get; set; } = 0.9f;
    [Export(PropertyHint.Range, "0,1")] public float VoiceVolume { get; set; } = 1f;

    // --- Controls / gameplay ------------------------------------------------

    [Export(PropertyHint.Range, "0.05,2")] public float MouseSensitivity { get; set; } = 1f;

    [Export] public bool InvertY { get; set; } = false;

    /// <summary>0 = Story, 1 = Normal, 2 = Hard. A placeholder dial; difficulty curves land in Phase 56.</summary>
    [Export] public int Difficulty { get; set; } = 1;

    // --- Accessibility (placeholders completed in Phase 54) -----------------

    [Export] public bool ReducedMotion { get; set; } = false;

    [Export] public bool SubtitlesEnabled { get; set; } = true;

    [Export(PropertyHint.Range, "0.75,1.5")] public float UiScale { get; set; } = 1f;

    /// <summary>Pairs each audio setting with its mixer bus name (Phase 31 creates these buses; the
    /// default <c>Master</c> bus always exists, so master volume applies immediately).</summary>
    public (string Bus, float Linear)[] BusVolumes() => new[]
    {
        (AudioBuses.Master, MasterVolume),
        (AudioBuses.Music, MusicVolume),
        (AudioBuses.Sfx, SfxVolume),
        (AudioBuses.Ambience, AmbienceVolume),
        (AudioBuses.Ui, UiVolume),
        (AudioBuses.Voice, VoiceVolume),
    };
}

/// <summary>Canonical mixer bus names shared between <see cref="Settings"/> and the Phase 31 audio
/// system, so the volume fields and the buses they drive never drift apart.</summary>
public static class AudioBuses
{
    public const string Master = "Master";
    public const string Music = "Music";
    public const string Sfx = "SFX";
    public const string Ambience = "Ambience";
    public const string Ui = "UI";
    public const string Voice = "Voice";
}
