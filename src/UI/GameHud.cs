using Embervale.Core;
using Embervale.Core.Events;
using Embervale.Corruption;
using Embervale.Enemies;
using Embervale.Entities;
using Embervale.Localization;
using Embervale.Magic;
using Embervale.Player;
using Embervale.Progression;
using Embervale.Quests;
using Embervale.Stats;
using Embervale.World;
using Godot;

namespace Embervale.UI;

/// <summary>
/// The purpose-built in-game HUD (Phase 18; laid out on the 30.5B slot system), the
/// player-facing overlay that replaces the old debug read-out as the default on-screen UI.
/// Widgets live in <see cref="HudLayout"/> slots: vitals bottom-left, a prepared-spell +
/// status line, a quest tracker top-right, time/weather top-left, the compass / boss bar /
/// world-event banner / aimed-target nameplate stacked top-centre (hidden widgets collapse,
/// so they never overlap), an interaction prompt bottom-centre, and the crosshair. Persistent
/// nodes updated each frame from the player and the world directors; built through
/// <see cref="UiTheme"/>.
/// </summary>
public partial class GameHud : CanvasLayer
{
    private readonly HudLayout _layout = new();

    /// <summary>The bottom-bar dock the quick-use hotbar parents into (see HudLayout.BottomDock).</summary>
    public Control BottomDock => _layout.BottomDock;
    private IEntity? _player;
    private WorldClock? _clock;
    private WeatherDirector? _weather;
    private WorldEventDirector? _worldEvents;

    private Nameplate _nameplate = null!;

    private CompassStrip _compass = null!;

    private MinimapHud _minimap = null!;

    private DamageDirectionOverlay _damageDirection = null!;

    // Starts Inactive so the first ApplyMode always runs — the HUD is built before the world is,
    // and a field defaulting to the mode we are about to enter would skip the initial layout pass.
    private HudMode _mode = HudMode.Inactive;

    // Corruption dread: a dark blood-red edge vignette that fades in at high tiers (23E).
    private TextureRect _vignette = null!;
    private float _vignetteAlpha;
    private float _targetVignetteAlpha;
    private const float VignetteFadeSpeed = 0.5f; // alpha units per second

    // Boss fight UI (Phase 28C): owns its own events and update loop since 37.5B — see BossFrame.
    private BossFrame _bossFrame = null!;

    private PartyWidget? _party;

    // --- Shown-value caches (performance pass) ---------------------------------
    //
    // Every cached label (the caches live beside their widgets, in the GameHud.*.cs partials) is
    // written only when the value it shows has changed. Assigning Label.Text
    // each frame formats and marshals a string even though the engine then discards it as identical,
    // and AddThemeColorOverride re-shapes the label whether or not the colour moved. Each cache is
    // keyed on the value the text is derived from; InvalidateShown drops them all when something a
    // key cannot see changes - the player, the locale, the accessibility settings.

    /// <summary>Forgets what every cached label is showing, so the next tick rewrites them all.</summary>
    private void InvalidateShown()
    {
        InvalidateVitalsShown();
        InvalidateContextShown();
        InvalidateTrackerShown();
        _nameplate?.InvalidateShown();
        _compass?.InvalidateText();
        _party?.MarkStale();
    }

    public void SetPlayer(IEntity? player)
    {
        _player = player;
        _compass?.SetPlayer(player);
        InvalidateShown();
    }

    /// <summary>A locale switch re-resolves every cached string (the caches hold translated text).</summary>
    public override void _Notification(int what)
    {
        if (what == NotificationTranslationChanged)
        {
            InvalidateShown();
        }
    }

    // Colour-vision and contrast settings change what the semantic colour tokens resolve to, and
    // the HUD options (scale, opacity, safe zone, element modes) are read here too.
    private void OnSettingsApplied(Settings.SettingsAppliedEvent e) => ApplyOptions();

    // A load can replace anything a cache was keyed on while leaving the objects in place.
    private void OnGameLoaded(GameLoadedEvent e) => InvalidateShown();

    // Both edges are global rects, which carry the HUD scale and the safe-zone offset of the
    // layout's scaled parent, so they stay screen coordinates whatever the HUD options are.

    /// <summary>Bottom edge of the top-right stack (the tracker), so the toast feed can start below it.</summary>
    public float TopRightBottom => _layout.TopRight.GetGlobalRect().End.Y;

    /// <summary>Top edge of the bottom-right stack (the minimap), so the toast feed can stop above it.</summary>
    public float BottomRightTop => _layout.BottomRight.GetGlobalRect().Position.Y;

    public void SetClock(WorldClock? clock) => _clock = clock;

    public void SetWeather(WeatherDirector? weather) => _weather = weather;

    public void SetWorldEvents(WorldEventDirector? worldEvents) => _worldEvents = worldEvents;

    public override void _Ready()
    {
        // ⚠️ PAUSE-IMMUNE, AND IT HAS TO BE SINCE 39.5B GAVE THIS THING A MODE (CLAUDE.md §7).
        //
        // A blocking menu pauses the tree, and a CanvasLayer inherits its process mode — so the frame
        // a menu opens is the last frame `_Process` runs, and `ApplyMode` never sees the Menu state.
        // The HUD then freezes *exactly as it was* and sits on top of the menu, which is precisely the
        // defect the mode table was added to fix, hiding behind the fix for it. `UiPanel` carries the
        // same line for the same reason; this class never needed it before because it had nothing to
        // do while paused. Caught by the first run of `--hudshots`, and invisible to every other check
        // this repo has.
        ProcessMode = ProcessModeEnum.Always;

        AddChild(_layout);

        BuildVignette(); // backmost overlay — built first so the HUD widgets draw over it
        _crosshair = new Crosshair();
        _layout.Overlay.AddChild(_crosshair);
        _damageDirection = new DamageDirectionOverlay { Name = "DamageDirection" };
        _layout.Overlay.AddChild(_damageDirection);
        BuildParty();
        BuildVitals();
        BuildTutorialHint();
        BuildContext();
        // Top-centre stack order (top to bottom): compass strip, boss bar, event banner, nameplate.
        BuildCompass();
        BuildBossBar();
        BuildBanner();
        BuildNameplate();
        BuildQuestTracker();
        BuildMinimap();
        BuildPrompt();
        BuildLockReticle();
        BuildLevelUp();
        ReadyOptions();

        EventBus.Instance?.Subscribe<XpGainedEvent>(OnXpGained);
        EventBus.Instance?.Subscribe<LeveledUpEvent>(OnLeveledUp);
        EventBus.Instance?.Subscribe<InputDeviceChangedEvent>(OnInputDeviceChanged);
        EventBus.Instance?.Subscribe<CorruptionTierChangedEvent>(OnCorruptionTierChanged);
        EventBus.Instance?.Subscribe<EntityDiedEvent>(OnEntityDied);
        EventBus.Instance?.Subscribe<Settings.SettingsAppliedEvent>(OnSettingsApplied);
        EventBus.Instance?.Subscribe<Embervale.Dialogue.StoryFlagChangedEvent>(OnStoryFlagChanged);
        EventBus.Instance?.Subscribe<GameLoadedEvent>(OnGameLoaded);
    }

    public override void _ExitTree()
    {
        EventBus.Instance?.Unsubscribe<XpGainedEvent>(OnXpGained);
        EventBus.Instance?.Unsubscribe<LeveledUpEvent>(OnLeveledUp);
        EventBus.Instance?.Unsubscribe<InputDeviceChangedEvent>(OnInputDeviceChanged);
        EventBus.Instance?.Unsubscribe<CorruptionTierChangedEvent>(OnCorruptionTierChanged);
        EventBus.Instance?.Unsubscribe<EntityDiedEvent>(OnEntityDied);
        EventBus.Instance?.Unsubscribe<Settings.SettingsAppliedEvent>(OnSettingsApplied);
        EventBus.Instance?.Unsubscribe<Embervale.Dialogue.StoryFlagChangedEvent>(OnStoryFlagChanged);
        EventBus.Instance?.Unsubscribe<GameLoadedEvent>(OnGameLoaded);
        ExitOptions();
    }

    /// <summary>
    /// Clears the transient combat overlays when the player dies (§54).
    ///
    /// ⚠️ Embervale respawns the player in the same frame they die, so there is no death state for
    /// the HUD to enter (see <see cref="HudVisibility"/>) — but there IS a teleport, and the two
    /// widgets that draw from live world state would carry the moment of death across it: the lock
    /// reticle would sit on a corpse the player is no longer near, and the damage arcs would keep
    /// fading in the direction of an attacker now on the other side of the map.
    /// </summary>
    private void OnEntityDied(EntityDiedEvent e)
    {
        if (!ReferenceEquals(e.Entity, _player))
        {
            return;
        }

        _lockReticle.Visible = false;
        _damageDirection.Clear();
        _nameplate.Show(null, _player);
        _promptPanel.Visible = false;
    }

    // --- Construction -------------------------------------------------------

    /// <summary>The onboarding hint (33B): one line above the hotbar naming the verb being taught.
    /// Self-hiding — it is absent whenever nothing is being taught.</summary>
    private void BuildTutorialHint()
    {
        _layout.BottomCenter.AddChild(new TutorialHint { Name = "TutorialHint" });
    }

    /// <summary>The party strip (32B): companion health + standing order, above the vitals panel.
    /// Self-hiding while the party is empty, so a solo run's HUD is untouched.</summary>
    private void BuildParty()
    {
        _party = new PartyWidget { Name = "Party" };
        _layout.BottomLeft.AddChild(_party);
    }

    /// <summary>The local minimap (39.5B), bottom-right. Self-contained — it resolves the map service
    /// and the player itself, so the HUD hands it nothing and cannot hand it something stale.</summary>
    private void BuildMinimap()
    {
        _minimap = new MinimapHud { Name = "Minimap" };
        _layout.BottomRight.AddChild(_minimap);
    }

    private void BuildCompass()
    {
        _compass = new CompassStrip { SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter };
        _compass.SetPlayer(_player);
        _layout.TopCenter.AddChild(_compass);
    }

    private void BuildNameplate()
    {
        _nameplate = new Nameplate { Name = "Nameplate" };
        _layout.TopCenter.AddChild(_nameplate);
    }

    /// <summary>A full-screen radial vignette (clear centre, dark blood-red edges) whose opacity
    /// rises with the corruption tier. Built once; only its modulate alpha animates.</summary>
    private void BuildVignette()
    {
        Color edge = UiTheme.Corruption;
        var gradient = new Gradient
        {
            // Inner ~55% stays clear, then ramps to the corruption colour at the rim.
            Offsets = new float[] { 0.55f, 1.0f },
            Colors = new Color[] { new Color(edge.R, edge.G, edge.B, 0f), edge },
        };

        var texture = new GradientTexture2D
        {
            Gradient = gradient,
            Fill = GradientTexture2D.FillEnum.Radial,
            FillFrom = new Vector2(0.5f, 0.5f),
            FillTo = new Vector2(0.5f, 0.0f),
            Width = 256,
            Height = 256,
        };

        _vignette = new TextureRect
        {
            Texture = texture,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            SelfModulate = new Color(1f, 1f, 1f, 0f),
        };
        _vignette.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _layout.Overlay.AddChild(_vignette);
    }

    // --- Per-frame update ---------------------------------------------------

    public override void _Process(double delta)
    {
        // 39.5B: the HUD is a view of a live session, so before reading anything from it, work out
        // whether there IS one. Resolved from the existing authorities every frame rather than cached
        // off events, because UiState has five owners and GameManager has its own lifecycle — the
        // version of this that subscribes to both and keeps a bool is the one that gets stuck showing
        // a HUD over a menu when the two disagree by a frame.
        ApplyMode(HudVisibility.ModeFor(
            GameManager.Instance is { IsPlaying: true }, UiState.MenuOpen, UiState.WorldPaused));
        UpdateElements();

        if (!HudVisibility.ShowsVitals(_mode))
        {
            return;
        }

        UpdateVitals(delta);

        if (!HudVisibility.ShowsHud(_mode))
        {
            return;
        }

        UpdateContext();
        UpdateQuest((float)delta);
        UpdateBanner();
        UpdateFocus();
        ResolveTopCentrePriority();
        UpdateVignette(delta);
        UpdateProgressionPops(delta);
    }

    /// <summary>Top-centre suppression contract: boss owns the region; an event banner outranks an
    /// aimed target; the compass is the quiet fallback. This prevents independent widgets from
    /// becoming a vertical alert stack during hostile combinations.</summary>
    private void ResolveTopCentrePriority()
    {
        bool boss = _bossFrame.Visible;
        bool eventBanner = _bannerPanel.Visible;

        _compass.Visible = !boss && Shows(HudElement.Compass);
        if (boss)
        {
            _bannerPanel.Visible = false;
            _nameplate.Visible = false;
            return;
        }

        if (eventBanner)
        {
            _nameplate.Visible = false;
        }
    }

    /// <summary>
    /// Shows and hides the widget groups for a mode, on the frame the mode changes.
    ///
    /// Works on the <see cref="HudLayout"/> slots rather than the individual widgets: a slot is
    /// exactly one group, so nothing can be added to the HUD later and quietly miss the rule — which
    /// is the failure mode a per-widget list has. The data-driven <c>Visible</c> flags inside a slot
    /// (the quest panel hiding itself with no quest, the prompt hiding itself with no focus) are
    /// untouched and still decide what shows WITHIN a visible slot. The slot writes themselves are in
    /// <see cref="ApplyElementVisibility"/>, where the mode table meets the player's element modes.
    /// </summary>
    private void ApplyMode(HudMode mode)
    {
        if (mode == _mode)
        {
            return;
        }

        _mode = mode;
        ApplyElementVisibility();

        // No stale UI survives a transition (§52). The lock reticle and the damage arcs are the two
        // that position themselves from live world state, so hiding their layer is not enough —
        // returning to exploration would flash the last frame's placement before the next update.
        if (!HudVisibility.ShowsCombat(mode))
        {
            _lockReticle.Visible = false;
            _damageDirection.Clear();
        }
    }

    private void UpdateVignette(double delta)
    {
        if (Mathf.IsEqualApprox(_vignetteAlpha, _targetVignetteAlpha))
        {
            return;
        }

        _vignetteAlpha = Mathf.MoveToward(_vignetteAlpha, _targetVignetteAlpha, (float)delta * VignetteFadeSpeed);
        _vignette.SelfModulate = new Color(1f, 1f, 1f, _vignetteAlpha);
    }

    private void OnCorruptionTierChanged(CorruptionTierChangedEvent e) =>
        _targetVignetteAlpha = VignetteTargetFor(e.Current);

    /// <summary>Per-tier vignette opacity — silent below Ashbound, rising into Embers.</summary>
    private static float VignetteTargetFor(CorruptionTier tier) => tier switch
    {
        CorruptionTier.Ashbound => 0.22f,
        CorruptionTier.Embers => 0.40f,
        _ => 0f,
    };

    // --- Boss fight UI (Phase 28C) ------------------------------------------

    /// <summary>Builds the boss frame and hands it the overlay slot its full-screen defeat fade
    /// needs. Everything else about the encounter — the events, the health poll, the fade curve —
    /// lives in <see cref="BossFrame"/> since 37.5B.</summary>
    private void BuildBossBar()
    {
        _bossFrame = new BossFrame { Name = "BossFrame" };
        _bossFrame.AttachFade(_layout.Overlay);
        _layout.TopCenter.AddChild(_bossFrame);
    }

    /// <summary>Parents <paramref name="content"/> under <paramref name="panel"/> with the compact HUD margins.
    /// The band's stylebox is the padding: a second <see cref="UiTheme.Padding"/> inside it used to stack on
    /// top and left every HUD card about 26 px of dead space top and bottom.</summary>
    private static void WrapPadded(PanelContainer panel, Control content)
    {
        UiTheme.Compact(panel);
        panel.AddChild(content);
    }

    private static T Ignore<T>(T control)
        where T : Control
    {
        control.MouseFilter = Control.MouseFilterEnum.Ignore;
        return control;
    }
}
