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

    /// <summary>0 Low, 1 Medium, 2 High, 3 Ultra, 4 Performance (the lowest; appended so the saved
    /// ints keep their meaning — see <see cref="GraphicsMath"/>). Palette is identical on every tier.</summary>
    [Export(PropertyHint.Range, "0,4,1")] public int RenderQuality { get; set; } = 1;

    // --- Graphics: per-control departures from the preset -------------------
    // ⚠️ Each default is the "follow the preset" sentinel. ResourceSaver omits a property that equals
    // its default, so a file saved before these existed loads as a pure preset, and these defaults
    // can never be changed without silently changing what every such file means.

    /// <summary>3D resolution scale, 0.5..1; 0 = the preset's.</summary>
    [Export(PropertyHint.Range, "0,1,0.05")] public float RenderScale { get; set; } = 0f;

    /// <summary>-1 preset, 0 bilinear, 1 FSR 1.0, 2 FSR 2.2.</summary>
    [Export] public int ScalingMode { get; set; } = -1;

    /// <summary>-1 preset, 0 off, 1 FXAA, 2 MSAA 2x, 3 MSAA 4x, 4 TAA.</summary>
    [Export] public int AntiAliasing { get; set; } = -1;

    /// <summary>-1 preset, 0 off, 1..5 the shadow bundle of that preset (cheapest first).</summary>
    [Export] public int ShadowQuality { get; set; } = -1;

    /// <summary>-1 preset, 0 off, 1 on.</summary>
    [Export] public int AmbientOcclusion { get; set; } = -1;

    /// <summary>-1 preset, 0 off, 1 on.</summary>
    [Export] public int VolumetricFog { get; set; } = -1;

    /// <summary>-1 preset, 0 off, 1 on.</summary>
    [Export] public int Glow { get; set; } = -1;

    public GraphicsOverrides Overrides() =>
        new(RenderScale, ScalingMode, AntiAliasing, ShadowQuality, AmbientOcclusion, VolumetricFog, Glow);

    /// <summary>Back to the pure preset: what choosing a preset in the options menu means.</summary>
    public void ClearGraphicsOverrides()
    {
        RenderScale = 0f;
        ScalingMode = -1;
        AntiAliasing = -1;
        ShadowQuality = -1;
        AmbientOcclusion = -1;
        VolumetricFog = -1;
        Glow = -1;
    }

    // --- Audio (linear 0..1 per bus; ready for the Phase 31 mixer to consume) ----

    [Export(PropertyHint.Range, "0,1")] public float MasterVolume { get; set; } = 1f;
    [Export(PropertyHint.Range, "0,1")] public float MusicVolume { get; set; } = 0.8f;
    [Export(PropertyHint.Range, "0,1")] public float SfxVolume { get; set; } = 1f;
    [Export(PropertyHint.Range, "0,1")] public float AmbienceVolume { get; set; } = 0.8f;
    [Export(PropertyHint.Range, "0,1")] public float UiVolume { get; set; } = 0.9f;
    [Export(PropertyHint.Range, "0,1")] public float VoiceVolume { get; set; } = 1f;

    /// <summary>Vertical field of view, in degrees. Applied to the player camera by
    /// <c>PlayerCameraRig</c> (it is a property of that camera, not of the engine, so the
    /// service's graphics pass cannot reach it). The first-person viewmodel
    /// is drawn by the same camera as the world, so nothing rescales to match it.</summary>
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

    // --- Camera comfort ---------------------------------------------------
    // Read through Player.CameraComfort so Reduced Motion (below) scales all of them at once.

    /// <summary>How hard hits, landings and blasts shake the camera. 0 = none, 1 = full.</summary>
    [Export(PropertyHint.Range, "0,1")] public float CameraShakeIntensity { get; set; } = 1f;

    /// <summary>First-person head bob and sway while moving. 0 = a rock-steady eye.</summary>
    [Export(PropertyHint.Range, "0,1")] public float HeadBob { get; set; } = 0.6f;

    /// <summary>How far sprinting, dodging and landing punch the field of view. 0 = never.</summary>
    [Export(PropertyHint.Range, "0,1")] public float FovKick { get; set; } = 0.6f;

    /// <summary>Third person only: swing to the other shoulder instead of pulling in when the
    /// chosen one is against a wall.</summary>
    [Export] public bool AutoShoulderSwap { get; set; } = true;

    /// <summary>Lock-on and aim frame the camera toward the target instead of leaving it fixed
    /// behind the player.</summary>
    [Export] public bool LockOnFraming { get; set; } = true;

    /// <summary>Props between the camera and the player thin out rather than blocking the view.</summary>
    [Export] public bool ObstructionFade { get; set; } = true;

    // --- Combat comfort ---------------------------------------------------
    // Read through Combat.CombatComfort so Reduced Motion (below) scales them at once.

    /// <summary>How long a landed blow freezes the frame. 0 = never, 1 = full.</summary>
    [Export(PropertyHint.Range, "0,1")] public float HitStopIntensity { get; set; } = 1f;

    /// <summary>Strength of the full-screen flash on crits, blocks, staggers and parries.</summary>
    [Export(PropertyHint.Range, "0,1")] public float CombatFlashIntensity { get; set; } = 1f;

    /// <summary>Floating damage numbers over what you hit.</summary>
    [Export] public bool DamageNumbers { get; set; } = true;

    /// <summary>Lock-on picks and cycles targets for you, and holds through brief occlusion.</summary>
    [Export] public bool LockOnAssist { get; set; } = true;

    /// <summary>How far a bow's aim is pulled toward a target near the crosshair. 0 = none.</summary>
    [Export(PropertyHint.Range, "0,1")] public float AimAssistStrength { get; set; } = 0.5f;

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

    // --- UI upgrade (appended; every default is the behaviour before the field existed) ----

    /// <summary>Sets every title, header and prose line in the interface face instead of the carved
    /// capitals and the book serif (<c>UiTheme.ResolveRole</c>). Sizes and layout are unchanged.</summary>
    [Export] public bool ReadableFont { get; set; } = false;

    /// <summary>Hold-to-confirm prompts (delete a save, reset settings, respec) fire on a single
    /// press instead (<c>UiFx.HoldRing</c>), for a player who cannot hold a button down.</summary>
    [Export] public bool HoldsToPresses { get; set; } = false;

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
