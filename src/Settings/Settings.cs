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

    /// <summary>Vertical field of view, in degrees. Applied to the player camera by
    /// <c>PlayerController</c> (it is a property of that camera, not of the engine, so the
    /// service's graphics pass cannot reach it). The first-person viewmodel
    /// rescales itself to match — see <c>FirstPersonArmsComponent</c>.</summary>
    [Export(PropertyHint.Range, "60,110")] public float FieldOfView { get; set; } = 75f;

    // --- Controls / gameplay ------------------------------------------------

    [Export(PropertyHint.Range, "0.05,2")] public float MouseSensitivity { get; set; } = 1f;

    [Export] public bool InvertY { get; set; } = false;

    /// <summary>0 = Story, 1 = Normal, 2 = Hard. A placeholder dial; difficulty curves land in Phase 56.</summary>
    [Export] public int Difficulty { get; set; } = 1;

    /// <summary>Play over the shoulder instead of at the eye. Swappable at any time, here or with
    /// the <c>toggle_camera</c> key — both flip this one field, so they can never disagree.</summary>
    [Export] public bool ThirdPersonCamera { get; set; } = false;

    /// <summary>How far behind the player the third-person camera sits, in metres. The wall spring
    /// still shortens it against geometry; this is the distance it eases back out to.</summary>
    [Export(PropertyHint.Range, "2,6")]
    public float ThirdPersonDistance { get; set; } = Player.PlayerFactory.ThirdPersonBackDistance;

    /// <summary>Which shoulder the third-person camera looks over: 0 = right, 1 = left,
    /// 2 = centred (no lateral offset, the body sits under the crosshair).</summary>
    [Export] public int ThirdPersonShoulderSide { get; set; } = ShoulderRight;

    public const int ShoulderRight = 0;
    public const int ShoulderLeft = 1;
    public const int ShoulderCentre = 2;

    /// <summary>The lateral camera offset the chosen shoulder side means, in metres. Signed off
    /// <c>PlayerFactory.ThirdPersonShoulder</c> so there is one number to tune, not three.</summary>
    public float ShoulderOffset() =>
        Player.CameraRigMath.ShoulderOffset(ThirdPersonShoulderSide, Player.PlayerFactory.ThirdPersonShoulder);

    /// <summary>Whether the onboarding hints appear (Phase 33B). Off means a returning player is
    /// never taught a verb they already know.</summary>
    [Export] public bool ShowTutorials { get; set; } = true;

    // --- Accessibility (placeholders completed in Phase 54) -----------------

    [Export] public bool ReducedMotion { get; set; } = false;

    [Export] public bool SubtitlesEnabled { get; set; } = true;

    [Export(PropertyHint.Range, "0.75,1.5")] public float UiScale { get; set; } = 1f;

    /// <summary>
    /// Text size multiplier, applied to the type scale **independently of <see cref="UiScale"/>**
    /// (Phase 37.5G). UiScale is the window's content-scale factor and magnifies everything
    /// including panels and margins; this scales only glyphs, for a player who wants larger text
    /// without a larger UI. Lands in <c>UiTheme.FontSize</c>, the seam 37.5A left for it.
    /// </summary>
    [Export(PropertyHint.Range, "0.85,1.5")] public float TextScale { get; set; } = 1f;

    /// <summary>
    /// Colour-vision adaptation for the UI's semantic palette (rarity, school, standing, good/bad).
    /// Daltonizes rather than simulates — see <c>ColorVision</c>. World art is never touched.
    /// </summary>
    [Export] public UI.ColorVisionMode ColorVision { get; set; } = UI.ColorVisionMode.None;

    /// <summary>Raises surface opacity, drops the grain texture and thickens frames (37.5G). For
    /// glare, low-quality panels, and anyone who finds the parchment material noisy.</summary>
    [Export] public bool HighContrast { get; set; } = false;

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
