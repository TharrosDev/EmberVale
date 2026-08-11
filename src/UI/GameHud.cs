using System.Text;
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

    private JuicedBar _hpBar = null!;
    private JuicedBar _staBar = null!;
    private JuicedBar _mpBar = null!;
    private Label _hpText = null!;
    private Label _staText = null!;
    private Label _mpText = null!;
    private Label _footer = null!;
    private ProgressBar _castBar = null!;

    // XP-gain pop (30.5I): a transient "+N XP" caption under the level footer that accumulates
    // rapid gains, holds, then fades. Driven by XpGainedEvent; timings via the motion tokens.
    private Label _xpPop = null!;
    private double _xpPopAge = double.MaxValue;
    private int _xpPopAmount;
    private const double XpPopHold = 1.0;

    // Level-up flourish (30.5I): a centred Display-size "Level N" that fades in, holds, and
    // fades out with a slight rise — the type scale's "big moment" (UI_STYLE §3).
    private Label _levelUp = null!;
    private double _levelUpAge = double.MaxValue;
    private const float LevelUpHold = 1.4f;
    private const float LevelUpBaseLift = 100f;
    private const float LevelUpRise = 16f;

    // Prepared spell + cooldown widget (30.5C): name tinted by school, a recovery bar that
    // fills while the spell cools down, and a READY/charging/channeling state readout.
    private HBoxContainer _spellRow = null!;
    private Label _spellName = null!;
    private Label _spellState = null!;
    private Label _spellCost = null!;
    private Label _spellCap = null!;
    private ProgressBar _cooldownBar = null!;

    // Status-effect chips (30.5C): one tinted chip per active effect. The row is rebuilt only
    // when the effect set changes (signature compare); timers update in place per frame.
    private HBoxContainer _statusRow = null!;
    private readonly System.Collections.Generic.List<(StatusEffect Effect, Label Time)> _statusChips = new();
    private string _statusSignature = string.Empty;

    private Label _context = null!;
    private Label _phaseGlyph = null!;
    private Label _phaseText = null!;
    private Label _weatherText = null!;

    private PanelContainer _questPanel = null!;
    private VBoxContainer _questList = null!;
    private Label _questWhere = null!;
    private string _questSignature = string.Empty;

    private PanelContainer _bannerPanel = null!;
    private Label _bannerText = null!;
    private Label _bannerTimer = null!;

    private Nameplate _nameplate = null!;

    private PanelContainer _promptPanel = null!;
    private Label _promptText = null!;
    private Label _promptCap = null!;

    private Label _lockReticle = null!;

    private CompassStrip _compass = null!;

    private MinimapHud _minimap = null!;

    private DamageDirectionOverlay _damageDirection = null!;

    // Starts Inactive so the first ApplyMode always runs — the HUD is built before the world is,
    // and a field defaulting to the mode we are about to enter would skip the initial layout pass.
    private HudMode _mode = HudMode.Inactive;

    // Phase of the low-health breath, in radians. Reset when health recovers so the pulse always
    // starts from full rather than wherever it happened to be (a stateful widget turning one bad
    // frame into a permanent fault — invariant 7).
    private float _criticalPhase;

    // Corruption dread: a dark blood-red edge vignette that fades in at high tiers (23E).
    private TextureRect _vignette = null!;
    private float _vignetteAlpha;
    private float _targetVignetteAlpha;
    private const float VignetteFadeSpeed = 0.5f; // alpha units per second

    // Boss fight UI (Phase 28C): owns its own events and update loop since 37.5B — see BossFrame.
    private BossFrame _bossFrame = null!;

    public void SetPlayer(IEntity? player)
    {
        _player = player;
        _compass?.SetPlayer(player);
    }

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
        _layout.Overlay.AddChild(new Crosshair());
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

        EventBus.Instance?.Subscribe<XpGainedEvent>(OnXpGained);
        EventBus.Instance?.Subscribe<LeveledUpEvent>(OnLeveledUp);
        EventBus.Instance?.Subscribe<InputDeviceChangedEvent>(OnInputDeviceChanged);
        EventBus.Instance?.Subscribe<CorruptionTierChangedEvent>(OnCorruptionTierChanged);
        EventBus.Instance?.Subscribe<EntityDiedEvent>(OnEntityDied);
    }

    public override void _ExitTree()
    {
        EventBus.Instance?.Unsubscribe<XpGainedEvent>(OnXpGained);
        EventBus.Instance?.Unsubscribe<LeveledUpEvent>(OnLeveledUp);
        EventBus.Instance?.Unsubscribe<InputDeviceChangedEvent>(OnInputDeviceChanged);
        EventBus.Instance?.Unsubscribe<CorruptionTierChangedEvent>(OnCorruptionTierChanged);
        EventBus.Instance?.Unsubscribe<EntityDiedEvent>(OnEntityDied);
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

    private void BuildVitals()
    {
        // Cards, not Panels, for every HUD widget (37.5H).
        //
        // 37.5B named this trap and fixed the status chips, then left the five widgets around them
        // on `Panel()` — so the HUD carried five brass frames, five engraved shadows and **five
        // grain ShaderMaterials** simultaneously, which is more framing than the character screen
        // uses. The ornament budget says a HUD widget earns none of it; the boss frame is the sole
        // exception and it has its own class.
        PanelContainer panel = Ignore(UiTheme.Card());
        panel.CustomMinimumSize = new Vector2(250, 0);
        _layout.BottomLeft.AddChild(panel);

        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 4);
        WrapPadded(panel, col);

        (_hpBar, _hpText) = AddVital(col, Loc.T("hud.hp"), UiTheme.Health, primary: true);
        (_staBar, _staText) = AddVital(col, Loc.T("hud.sta"), UiTheme.Stamina, primary: false);
        (_mpBar, _mpText) = AddVital(col, Loc.T("hud.mp"), UiTheme.Mana, primary: false);

        _footer = UiTheme.Body("", UiTheme.Dim);
        col.AddChild(_footer);

        _xpPop = UiTheme.Caption("", UiTheme.Accent);
        _xpPop.Visible = false;
        col.AddChild(_xpPop);

        // Prepared spell: name in the school's colour, state readout, and a thin recovery bar
        // that fills while the spell cools down (hidden when ready).
        // ⚠️ The prepared spell was a bare name and the word "ready", with NO indication of which key
        // casts it and NO cost — so §11's "resource cost where useful" and §12's "insufficient mana"
        // state had nothing to render with, and a player could not tell whether the spell they were
        // looking at was affordable. The keycap resolves from the InputMap like the interaction
        // prompt's does, so a rebind or a pad flip keeps it honest (§44, §45).
        _spellRow = new HBoxContainer { Visible = false };
        _spellRow.AddThemeConstantOverride("separation", UiTheme.SpaceSm);

        PanelContainer castCap = UiTheme.KeyCap(GameInput.PromptLabel(GameInput.Cast), out _spellCap);
        castCap.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        _spellRow.AddChild(castCap);

        _spellName = UiTheme.Body("");
        _spellName.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _spellName.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        _spellRow.AddChild(_spellName);

        _spellCost = UiTheme.Caption("", UiTheme.Mana);
        _spellCost.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        _spellRow.AddChild(_spellCost);

        _spellState = UiTheme.Caption("");
        _spellState.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        _spellRow.AddChild(_spellState);
        col.AddChild(_spellRow);

        _cooldownBar = UiTheme.Bar(UiTheme.Dim);
        _cooldownBar.CustomMinimumSize = new Vector2(168f, 5f);
        _cooldownBar.Visible = false;
        col.AddChild(_cooldownBar);

        // Charge/channel meter (29.5G): fills while a charged cast is held, pinned full while
        // channeling, hidden otherwise. Modulated to the active spell's school colour.
        _castBar = UiTheme.Bar(UiTheme.ArcaneSilver);
        _castBar.Visible = false;
        col.AddChild(_castBar);

        _statusRow = new HBoxContainer();
        _statusRow.AddThemeConstantOverride("separation", UiTheme.SpaceXs);
        col.AddChild(_statusRow);
    }

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
        _layout.BottomLeft.AddChild(new PartyWidget { Name = "Party" });
    }

    /// <summary>
    /// The world clock, its phase and the weather (§25–§27).
    ///
    /// ⚠️ <b>This was one `Body` label reading "10:00  (Day)   ·   Fog".</b> Three unrelated facts in
    /// one string at one weight, so nothing in it could be read at a glance — and the phase came from
    /// a hard-coded English literal (see <see cref="DayPhases.NameKey"/>). §26 asks for the time to be
    /// communicated subtly rather than spelled out, so the phase is now a **glyph** carrying the
    /// dawn/day/dusk/night state, the hour is the thing you actually read, and the weather is
    /// subordinate — the hierarchy §3 asks for, inside one small widget.
    /// </summary>
    private void BuildContext()
    {
        PanelContainer panel = Ignore(UiTheme.Card());
        _layout.TopLeft.AddChild(panel);

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", UiTheme.SpaceSm);

        // Shape AND colour carry the phase, so it survives ColorVision (§40) — and it is never the
        // only channel, because the phase name is on the same row.
        _phaseGlyph = UiTheme.Body("", UiTheme.Accent);
        _phaseGlyph.VerticalAlignment = VerticalAlignment.Center;
        row.AddChild(_phaseGlyph);

        _context = UiTheme.Body("", UiTheme.Text);
        _context.VerticalAlignment = VerticalAlignment.Center;
        row.AddChild(_context);

        _phaseText = UiTheme.Caption("", UiTheme.Dim);
        _phaseText.VerticalAlignment = VerticalAlignment.Center;
        row.AddChild(_phaseText);

        _weatherText = UiTheme.Caption("", UiTheme.Dim);
        _weatherText.VerticalAlignment = VerticalAlignment.Center;
        row.AddChild(_weatherText);

        WrapPadded(panel, row);
    }

    /// <summary>The phase's glyph and tint. Warm at midday, cold at night, ember at the edges of the
    /// day — the same palette language the rest of the UI uses.</summary>
    private static (string Glyph, Color Tint) PhaseMark(DayPhase phase) => phase switch
    {
        DayPhase.Dawn => ("◑", UiTheme.Accent),
        DayPhase.Day => ("☀", UiTheme.Accent),
        DayPhase.Dusk => ("◐", UiTheme.AccentHot),
        _ => ("☾", UiTheme.ArcaneSilver),
    };

    private void BuildQuestTracker()
    {
        // The spine carries the tracked quest's priority, matching the journal.
        _questPanel = Ignore(UiTheme.Card(UiTheme.QuestMain));
        _questPanel.Visible = false;
        _questPanel.CustomMinimumSize = new Vector2(210, 0);
        _layout.TopRight.AddChild(_questPanel);

        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 2);
        col.AddChild(UiTheme.Header(Loc.T("hud.quest")));
        _questList = new VBoxContainer();
        _questList.AddThemeConstantOverride("separation", 2);
        col.AddChild(_questList);

        // Distance + bearing to the tracked objective. Its own label under the objective rows, so
        // the rows can stay on their rebuild-on-change signature while this updates as you walk.
        _questWhere = UiTheme.Caption("", UiTheme.Accent);
        _questWhere.Visible = false;
        col.AddChild(_questWhere);

        WrapPadded(_questPanel, col);
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

    private void BuildBanner()
    {
        _bannerPanel = Ignore(UiTheme.Card(UiTheme.AccentHot));
        _bannerPanel.Visible = false;
        _bannerPanel.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
        _layout.TopCenter.AddChild(_bannerPanel);

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", UiTheme.SpaceSm);
        _bannerText = UiTheme.Body("", UiTheme.Accent);
        row.AddChild(_bannerText);
        _bannerTimer = UiTheme.Body("", UiTheme.Dim);
        row.AddChild(_bannerTimer);
        WrapPadded(_bannerPanel, row);
    }

    private void BuildNameplate()
    {
        _nameplate = new Nameplate { Name = "Nameplate" };
        _layout.TopCenter.AddChild(_nameplate);
    }

    /// <summary>A diamond marker (Phase 29H) tracked onto the locked-on target's screen position.</summary>
    private void BuildLockReticle()
    {
        _lockReticle = new Label
        {
            Text = "◆",
            Visible = false,
            Size = new Vector2(28, 28),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        UiTheme.ApplyType(_lockReticle, UiTheme.FontRole.Interface, UiTheme.TitleFontSize);
        _lockReticle.AddThemeColorOverride("font_color", UiTheme.Accent);
        _layout.Overlay.AddChild(_lockReticle);
    }

    /// <summary>The level-up flourish label: full-rect, text centred, lifted above the
    /// crosshair; only its modulate alpha and vertical offset animate.</summary>
    private void BuildLevelUp()
    {
        _levelUp = new Label
        {
            Visible = false,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        UiTheme.ApplyType(_levelUp, UiTheme.FontRole.Display, UiTheme.DisplayFontSize);
        _levelUp.AddThemeColorOverride("font_color", UiTheme.Accent);
        _levelUp.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _layout.Overlay.AddChild(_levelUp);
    }

    /// <summary>Swap the interaction keycap glyph when the player switches between keyboard
    /// and gamepad (30.5J) — "E" ↔ "X" live, no rebuild.</summary>
    private void OnInputDeviceChanged(InputDeviceChangedEvent e)
    {
        _promptCap.Text = GameInput.PromptLabel(GameInput.Interact);

        // 39.5B: the spell row grew a keycap too, and a keycap that does not follow the device is
        // worse than none — it confidently names a key the player's controller does not have.
        _spellCap.Text = GameInput.PromptLabel(GameInput.Cast);
    }

    private void OnXpGained(XpGainedEvent e)
    {
        if (!ReferenceEquals(e.Entity, _player))
        {
            return;
        }

        // Rapid gains (a pack of kills) accumulate into one pop and restart the hold.
        _xpPopAmount = _xpPopAge <= XpPopHold + UiTheme.DurationBase ? _xpPopAmount + e.Amount : e.Amount;
        _xpPopAge = 0d;
        _xpPop.Text = Loc.TF("hud.xp_gain", _xpPopAmount);
        _xpPop.Visible = true;
    }

    private void OnLeveledUp(LeveledUpEvent e)
    {
        if (!ReferenceEquals(e.Entity, _player))
        {
            return;
        }

        _levelUpAge = 0d;
        _levelUp.Text = Loc.TF("hud.levelup", e.NewLevel);
        _levelUp.Visible = true;
    }

    /// <summary>Drives the XP pop and level-up flourish timelines: ease in, hold, ease out.
    /// Under reduced motion both snap on/off (durations collapse to 0).</summary>
    private void UpdateProgressionPops(double delta)
    {
        if (_xpPop.Visible)
        {
            _xpPopAge += delta;
            float fadeOut = UiTheme.Duration(UiTheme.DurationBase);
            if (_xpPopAge >= XpPopHold + fadeOut)
            {
                _xpPop.Visible = false;
            }
            else
            {
                float alpha = _xpPopAge < XpPopHold
                    ? 1f
                    : 1f - UiMotion.EaseIn(UiMotion.Progress((float)(_xpPopAge - XpPopHold), fadeOut));
                _xpPop.Modulate = new Color(1f, 1f, 1f, alpha);
            }
        }

        if (_levelUp.Visible)
        {
            _levelUpAge += delta;
            float fadeIn = UiTheme.Duration(UiTheme.DurationBase);
            float fadeOut = UiTheme.Duration(UiTheme.DurationSlow);
            if (_levelUpAge >= fadeIn + LevelUpHold + fadeOut)
            {
                _levelUp.Visible = false;
            }
            else
            {
                float alpha = _levelUpAge < fadeIn + LevelUpHold
                    ? UiMotion.EaseOut(UiMotion.Progress((float)_levelUpAge, fadeIn))
                    : 1f - UiMotion.EaseIn(UiMotion.Progress((float)(_levelUpAge - fadeIn - LevelUpHold), fadeOut));
                _levelUp.Modulate = new Color(1f, 1f, 1f, alpha);

                // A slight upward drift across the whole beat (0 under reduced motion).
                float rise = UiTheme.MotionEnabled
                    ? LevelUpRise * UiMotion.EaseOut(UiMotion.Progress(
                        (float)_levelUpAge, fadeIn + LevelUpHold + fadeOut))
                    : 0f;
                float lift = LevelUpBaseLift + rise;
                _levelUp.OffsetTop = -lift;
                _levelUp.OffsetBottom = -lift;
            }
        }
    }

    private void BuildPrompt()
    {
        _promptPanel = Ignore(UiTheme.Card());
        _promptPanel.Visible = false;
        _promptPanel.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
        _layout.BottomCenter.AddChild(_promptPanel);

        // A keycap chip + the prompt text ("[E] Loot" as a real glyph, not string brackets).
        // The cap's label resolves from the InputMap so a future rebind stays correct.
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", UiTheme.SpaceSm);
        PanelContainer cap = UiTheme.KeyCap(GameInput.PromptLabel(GameInput.Interact), out _promptCap);
        cap.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        row.AddChild(cap);
        _promptText = UiTheme.Body("", UiTheme.Accent);
        row.AddChild(_promptText);
        WrapPadded(_promptPanel, row);
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
            GameManager.Instance is { IsPlaying: true }, UiState.MenuOpen));

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
        UpdateQuest();
        UpdateBanner();
        UpdateFocus();
        UpdateVignette(delta);
        UpdateProgressionPops(delta);
    }

    /// <summary>
    /// Shows and hides the widget groups for a mode, on the frame the mode changes.
    ///
    /// Works on the <see cref="HudLayout"/> slots rather than the individual widgets: a slot is
    /// exactly one group, so nothing can be added to the HUD later and quietly miss the rule — which
    /// is the failure mode a per-widget list has. The data-driven <c>Visible</c> flags inside a slot
    /// (the quest panel hiding itself with no quest, the prompt hiding itself with no focus) are
    /// untouched and still decide what shows WITHIN a visible slot.
    /// </summary>
    private void ApplyMode(HudMode mode)
    {
        if (mode == _mode)
        {
            return;
        }

        _mode = mode;

        _layout.BottomLeft.Visible = HudVisibility.ShowsVitals(mode);   // vitals, spell, status, party
        // The hotbar rides with the vitals rather than the rest: assigning a quick-use slot is done
        // from inside the inventory (its own 1–5 buttons), and doing that with the bar you are
        // assigning to hidden is working blind.
        _layout.BottomDock.Visible = HudVisibility.ShowsVitals(mode);   // quick-use hotbar
        _layout.TopLeft.Visible = HudVisibility.ShowsNavigation(mode);  // clock + weather
        _layout.TopRight.Visible = HudVisibility.ShowsNavigation(mode); // quest tracker
        _layout.TopCenter.Visible = HudVisibility.ShowsNavigation(mode); // compass, boss, banner, nameplate
        _layout.BottomRight.Visible = HudVisibility.ShowsNavigation(mode); // minimap
        _layout.BottomCenter.Visible = HudVisibility.ShowsPrompt(mode); // interaction prompt, tutorial hint
        _layout.Overlay.Visible = HudVisibility.ShowsHud(mode);         // crosshair, vignette, reticle, arcs

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

    private void UpdateVitals(double delta)
    {
        if (_player is not Node node || !IsInstanceValid(node) ||
            !_player.TryGetComponent(out StatsComponent stats))
        {
            return;
        }

        SetVital(_hpBar, _hpText, stats, StatType.Health);
        SetVital(_staBar, _staText, stats, StatType.Stamina);
        SetVital(_mpBar, _mpText, stats, StatType.Mana);
        UpdateCriticalHealth(stats, delta);

        _footer.Text = _player.TryGetComponent(out ProgressionComponent prog)
            ? Loc.TF("hud.level", prog.Level)
            : string.Empty;

        UpdateSpellWidget();
        UpdateStatusChips();
    }

    /// <summary>Health fraction at or below which the bar starts asking for attention.</summary>
    private const float LowHealth = 0.30f;

    /// <summary>…and below which it insists.</summary>
    private const float CriticalHealth = 0.15f;

    /// <summary>
    /// The low- and critical-health treatment (§5).
    ///
    /// ⚠️ <b>There was none.</b> A health bar at 5% looked exactly like a health bar at 95%, only
    /// shorter — the single most important state in the game had no presentation at all, which is the
    /// clearest example of the brief's "already built" not meaning "finished".
    ///
    /// Restrained on purpose, per §5's "do NOT permanently flash the entire screen": the bar breathes
    /// and the reading heats toward ember orange. **The number changing colour is the second channel**
    /// (§40) — a player who cannot separate the red bar from its warm surround still sees the digits
    /// change. Reduced motion drops the breath and keeps the colour, because the colour is the
    /// information and the motion is the emphasis.
    /// </summary>
    private void UpdateCriticalHealth(StatsComponent stats, double delta)
    {
        float fraction = stats.GetNormalized(StatType.Health);
        if (fraction > LowHealth)
        {
            _criticalPhase = 0f;
            _hpBar.SelfModulate = Colors.White;
            _hpText.AddThemeColorOverride("font_color", UiTheme.Text);
            return;
        }

        bool critical = fraction <= CriticalHealth;
        _hpText.AddThemeColorOverride("font_color", critical ? UiTheme.AccentHot : UiTheme.Accent);

        if (!UiTheme.MotionEnabled)
        {
            _hpBar.SelfModulate = Colors.White;
            return;
        }

        // Faster and deeper the worse it gets, so "bad" and "very bad" are distinguishable without
        // reading anything.
        _criticalPhase += (float)delta * (critical ? 6.4f : 3.4f);
        float swing = critical ? 0.32f : 0.16f;
        float pulse = 1f - (swing * 0.5f * (1f - Mathf.Cos(_criticalPhase)));
        _hpBar.SelfModulate = new Color(1f, pulse, pulse);
    }

    /// <summary>The prepared-spell widget (30.5C): school-tinted name, state readout, and the
    /// cooldown recovery bar (visible only while cooling down).</summary>
    private void UpdateSpellWidget()
    {
        bool casting = false;
        if (_player!.TryGetComponent(out SpellcastingComponent spells) && spells.Selected is { } spell)
        {
            Color tint = SpellSchools.Color(spell.School);
            _spellName.Text = spell.DisplayName;
            _spellName.Modulate = tint;

            float cd = spells.CooldownOf(spell);

            // ⚠️ Affordability is ASKED, not decided (§48). The HUD compares against the live mana
            // reading purely to colour the number; whether the cast is allowed remains
            // SpellcastingComponent's call, and this never gates anything.
            bool affordable = _player.GetComponent<StatsComponent>() is not { } casterStats ||
                              casterStats.GetCurrent(StatType.Mana) >= spell.ManaCost;
            _spellCost.Visible = spell.ManaCost > 0f;
            _spellCost.Text = $"{spell.ManaCost:0}";
            _spellCost.AddThemeColorOverride(
                "font_color", affordable ? UiTheme.Mana : UiTheme.Bad);

            _spellState.Text = spells.IsCharging ? Loc.T("hud.charging")
                : spells.IsChanneling ? Loc.T("hud.channeling")
                : cd > 0f ? $"{cd:0.0}s"
                : !affordable ? Loc.T("hud.no_mana")
                : Loc.T("hud.ready");
            // Font colour, not Modulate — modulating multiplies the caption's own Dim down
            // below readable contrast (30.5K audit).
            _spellState.AddThemeColorOverride(
                "font_color", cd > 0f ? UiTheme.Dim : !affordable ? UiTheme.Bad : UiTheme.Accent);
            _spellRow.Visible = true;

            bool coolingDown = cd > 0f && spell.Cooldown > 0f;
            _cooldownBar.Visible = coolingDown;
            if (coolingDown)
            {
                _cooldownBar.Value = 1d - (cd / spell.Cooldown);
                _cooldownBar.Modulate = tint;
            }

            casting = spells.IsCharging || spells.IsChanneling;
            if (casting)
            {
                _castBar.Value = spells.IsCharging ? spells.ChargeProgress : 1d;
                _castBar.Modulate = tint;
            }
        }
        else
        {
            _spellRow.Visible = false;
            _cooldownBar.Visible = false;
        }

        _castBar.Visible = casting;
    }

    /// <summary>The status-effect chip row (30.5C): rebuilt only when the active set changes;
    /// per-chip countdowns update in place each frame.</summary>
    private void UpdateStatusChips()
    {
        StatusEffectsComponent? effects = _player!.GetComponent<StatusEffectsComponent>();

        var signature = new StringBuilder();
        if (effects != null)
        {
            foreach (StatusEffect effect in effects.ActiveEffects)
            {
                signature.Append(effect.Definition.Id).Append('|');
            }
        }

        string current = signature.ToString();
        if (current != _statusSignature)
        {
            _statusSignature = current;
            RebuildStatusChips(effects);
        }

        foreach ((StatusEffect effect, Label time) in _statusChips)
        {
            time.Text = $"{effect.Remaining:0.0}s";
        }
    }

    private void RebuildStatusChips(StatusEffectsComponent? effects)
    {
        _statusChips.Clear();
        foreach (Node child in _statusRow.GetChildren())
        {
            _statusRow.RemoveChild(child);
            child.QueueFree();
        }

        if (effects == null)
        {
            return;
        }

        foreach (StatusEffect effect in effects.ActiveEffects)
        {
            // Buffs read as dead-green, afflictions in their school's colour.
            Color tint = effect.Definition.IsBeneficial ? UiTheme.Good : SpellSchools.Color(effect.Definition.School);

            // A Chip, not a Panel (37.5B). These were full framed panels, which after 37.5A gave
            // every status effect a 2 px brass rule and its own grain ShaderMaterial — a five-chip
            // row was five framed screens' worth of chrome for five words of text.
            PanelContainer chip = UiTheme.Chip(effect.Definition.DisplayName, tint, out Label time);
            _statusChips.Add((effect, time));
            _statusRow.AddChild(chip);
        }
    }

    private void UpdateContext()
    {
        if (_clock is not { } clock || !IsInstanceValid(clock))
        {
            // Graceful empty state (§53): the widget goes away rather than showing an empty frame or
            // a placeholder time the player might believe.
            _phaseGlyph.Text = string.Empty;
            _context.Text = string.Empty;
            _phaseText.Text = string.Empty;
            _weatherText.Text = string.Empty;
            return;
        }

        (string glyph, Color tint) = PhaseMark(clock.Phase);
        _phaseGlyph.Text = glyph;
        _phaseGlyph.AddThemeColorOverride("font_color", UiTheme.Adapt(tint));

        _context.Text = clock.Clock();
        _phaseText.Text = Loc.T(DayPhases.NameKey(clock.Phase));

        bool hasWeather = _weather is { } weather && IsInstanceValid(weather) && weather.Current is not null;
        _weatherText.Visible = hasWeather;
        if (hasWeather)
        {
            _weatherText.Text = $"· {_weather!.Current!.DisplayName}";
        }
    }

    private void UpdateQuest()
    {
        // 39.5B: one authority for "which quest am I on" — see QuestLogComponent.Tracked.
        QuestProgress? active = _player?.GetComponent<QuestLogComponent>()?.Tracked;

        if (active == null)
        {
            _questPanel.Visible = false;
            _questSignature = string.Empty;
            return;
        }

        // Rebuild the tracker rows only when the tracked quest's shape/progress changes.
        var signature = new StringBuilder(active.Quest.Id);
        foreach (int count in active.Counts)
        {
            signature.Append(':').Append(count);
        }

        string current = signature.ToString();
        if (current != _questSignature)
        {
            _questSignature = current;
            RebuildQuestRows(active);
        }

        UpdateQuestDestination();
        _questPanel.Visible = true;
    }

    /// <summary>
    /// How far the tracked objective is and which way (§16, §21) — "320 m · NW" under the objectives.
    ///
    /// Reads the compass strip's already-resolved target rather than locating one itself, so the
    /// number under the tracker and the marker on the strip are the same point by construction. Lives
    /// outside the signature-driven rebuild because it changes every time the player takes a step,
    /// and rebuilding the objective rows at walking pace to update one label would be the "recreating
    /// nodes every frame" §50 forbids.
    /// </summary>
    private void UpdateQuestDestination()
    {
        if (_compass.ObjectiveTarget is not { } target ||
            _player?.Body is not { } body || !IsInstanceValid(body))
        {
            _questWhere.Visible = false;
            return;
        }

        Vector3 offset = target - body.GlobalPosition;
        (string value, string unitKey) = CompassMath.Distance(new Vector2(offset.X, offset.Z).Length());
        string cardinal = Loc.T(CompassMath.CardinalKey(CompassMath.BearingTo(offset.X, offset.Z)));

        _questWhere.Text = Loc.TF("hud.quest.destination", value, Loc.T(unitKey), cardinal);
        _questWhere.Visible = true;
    }

    /// <summary>Structured tracker rows (30.5D): accent title, then one line per objective —
    /// complete objectives tick over to dead-green so progress reads at a glance.</summary>
    private void RebuildQuestRows(QuestProgress progress)
    {
        foreach (Node child in _questList.GetChildren())
        {
            _questList.RemoveChild(child);
            child.QueueFree();
        }

        // Priority colour off the real field, added in 37.5E. 37.5B had this pinned to QuestMain
        // because `QuestResource` had no main/side flag and the available heuristic ("has a
        // prerequisite") was both invented and backwards — a prerequisite chains a quest, it does
        // not demote it.
        // The title is the thing you glance at, so it is Display-faced and wraps rather than
        // clipping — a truncated quest name is a quest you cannot identify.
        Label title = UiTheme.Body(
            Loc.T(progress.Quest.Title),
            progress.Quest.IsMainQuest ? UiTheme.QuestMain : UiTheme.QuestSide);
        UiTheme.ApplyType(title, UiTheme.FontRole.Display, UiTheme.BodyFontSize);
        title.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _questList.AddChild(title);

        var objectives = progress.Quest.ObjectiveList();
        for (int i = 0; i < objectives.Count; i++)
        {
            int required = Mathf.Max(1, objectives[i].RequiredCount);
            int have = progress.Counts[i];
            bool done = have >= objectives[i].RequiredCount;

            // ⚠️ The objective used to be a single Caption with the count glued on the end and two
            // leading spaces for indent — so a long objective wrapped its own progress count onto the
            // next line, and the count was the same weight as the words. It is a row now: the text
            // wraps, the count holds the right edge.
            var line = new HBoxContainer();
            line.AddThemeConstantOverride("separation", UiTheme.SpaceXs);

            Label bullet = UiTheme.Caption(done ? "✓" : "•", done ? UiTheme.QuestComplete : UiTheme.Dim);
            bullet.CustomMinimumSize = new Vector2(10f, 0f);
            line.AddChild(bullet);

            Label text = UiTheme.Caption(
                Loc.T(objectives[i].ShortLabel()),
                done ? UiTheme.QuestComplete : UiTheme.Text);
            text.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            text.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            line.AddChild(text);

            // A 1-of-1 objective's "0/1" is noise — the bullet already says done or not.
            if (objectives[i].RequiredCount > 1)
            {
                line.AddChild(UiTheme.Caption(
                    $"{have}/{objectives[i].RequiredCount}",
                    done ? UiTheme.QuestComplete : UiTheme.Dim));
            }

            _questList.AddChild(line);

            // A bar under any objective that counts to more than one (37.5B). "3/10 pelts" is a
            // number you have to read; a bar is a glance. Pointless for a 1-of-1 objective, so it
            // is not drawn there.
            if (objectives[i].RequiredCount > 1)
            {
                ProgressBar track = UiTheme.Bar(done ? UiTheme.QuestComplete : UiTheme.Accent, 186f);
                track.CustomMinimumSize = new Vector2(186f, 3f);
                track.Value = Mathf.Clamp(have / (double)required, 0d, 1d);
                _questList.AddChild(track);
            }
        }
    }

    private void UpdateBanner()
    {
        if (_worldEvents is { } director && IsInstanceValid(director) && director.Active is { } worldEvent)
        {
            _bannerText.Text = $"★ {worldEvent.Resource.DisplayName} — {worldEvent.ObjectiveLabel()}";

            // Separate countdown that heats to ember orange in the final seconds (urgency read).
            _bannerTimer.Visible = worldEvent.IsTimed;
            if (worldEvent.IsTimed)
            {
                _bannerTimer.Text = $"{worldEvent.TimeLeft:0}s";
                _bannerTimer.AddThemeColorOverride("font_color",
                    worldEvent.TimeLeft <= 10f ? UiTheme.AccentHot : UiTheme.Dim);
            }

            _bannerPanel.Visible = true;
        }
        else
        {
            _bannerPanel.Visible = false;
        }
    }

    private void UpdateFocus()
    {
        PlayerController? controller = _player?.GetComponent<PlayerController>();

        // The locked-on target (Phase 29H) takes nameplate priority over the aimed-at focus, and is
        // reticled at its projected screen position.
        IEntity? locked = controller?.LockedTarget;
        UpdateLockReticle(locked);
        IEntity? focus = (locked is Node lockNode && IsInstanceValid(lockNode)) ? locked : controller?.FocusedEntity;

        // Nameplate for an aimed-at damageable that isn't the player. The widget owns the
        // validity guard, the snap-on-target-change and the disposition tint (37.5B).
        _nameplate.Show(focus, _player);

        // Interaction prompt for an aimed-at interactable.
        string? prompt = controller?.FocusPrompt;
        if (!string.IsNullOrEmpty(prompt))
        {
            _promptText.Text = prompt;
            _promptPanel.Visible = true;
        }
        else
        {
            _promptPanel.Visible = false;
        }
    }

    /// <summary>Tracks the lock-on reticle onto the target's body, hiding it when there's no lock, the
    /// target is gone, or it's behind the camera.</summary>
    private void UpdateLockReticle(IEntity? locked)
    {
        if (locked is Node node && IsInstanceValid(node) && locked.Body is Node3D body &&
            GetViewport().GetCamera3D() is { } camera)
        {
            Vector3 head = body.GlobalPosition + Vector3.Up;
            if (!camera.IsPositionBehind(head))
            {
                _lockReticle.Position = camera.UnprojectPosition(head) - (_lockReticle.Size / 2f);

                // A slow breathe so the lock reads as live, not a painted marker (30.5E).
                float alpha = UiTheme.MotionEnabled
                    ? 0.8f + (0.2f * Mathf.Sin(Time.GetTicksMsec() / 250f))
                    : 1f;
                _lockReticle.Modulate = new Color(1f, 1f, 1f, alpha);
                _lockReticle.Visible = true;
                return;
            }
        }

        _lockReticle.Visible = false;
    }

    // --- Helpers ------------------------------------------------------------

    /// <summary>Bar heights, in the hierarchy §3 asks for: health is the one the player checks under
    /// pressure, so it is read first by being physically the largest thing in the group.</summary>
    private const float HealthBarHeight = 17f;
    private const float MinorBarHeight = 10f;

    /// <summary>
    /// One resource row.
    ///
    /// ⚠️ <b>The three used to be pixel-identical, and that was the §3 failure.</b> Health, mana and
    /// endurance sat in three 13 px bars with the same label width and the same type, so the group
    /// read as a table of numbers rather than as a hierarchy — the player had to *read* the row
    /// labels to find their health, at exactly the moment they have no attention to spare. Now health
    /// is visibly the largest and brightest thing in the group and the other two are subordinate,
    /// which is the whole of the "critical vs important" split.
    ///
    /// Shape, position and size carry the distinction as well as colour, so the group survives
    /// <see cref="ColorVision"/> (§40).
    /// </summary>
    private static (JuicedBar Bar, Label Value) AddVital(
        VBoxContainer col, string caption, Color fill, bool primary)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", UiTheme.SpaceSm);

        Label cap = UiTheme.Caption(caption, primary ? UiTheme.Text : UiTheme.Dim);
        cap.CustomMinimumSize = new Vector2(30, 0);
        cap.VerticalAlignment = VerticalAlignment.Center;
        row.AddChild(cap);

        JuicedBar bar = JuicedBar.Create(fill);
        bar.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        bar.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        bar.CustomMinimumSize = new Vector2(150f, primary ? HealthBarHeight : MinorBarHeight);
        row.AddChild(bar);

        // The reading, in the same weight as the bar it belongs to. Tabular-ish fixed width so the
        // numbers do not shuffle left and right as they change — a value that moves while you watch
        // it is one you have to re-find every time.
        Label value = primary ? UiTheme.Body("", UiTheme.Text) : UiTheme.Caption("", UiTheme.Dim);
        value.CustomMinimumSize = new Vector2(74, 0);
        value.HorizontalAlignment = HorizontalAlignment.Right;
        value.VerticalAlignment = VerticalAlignment.Center;
        row.AddChild(value);

        col.AddChild(row);
        return (bar, value);
    }

    private static void SetVital(JuicedBar bar, Label value, StatsComponent stats, StatType type)
    {
        bar.SetTarget(stats.GetNormalized(type));
        value.Text = $"{stats.GetCurrent(type):0}/{stats.GetMax(type):0}";
    }

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

    /// <summary>Wraps <paramref name="content"/> in the theme's padding and parents it under
    /// <paramref name="panel"/> (a single inner margin container).</summary>
    private static void WrapPadded(PanelContainer panel, Control content)
    {
        MarginContainer pad = UiTheme.Padding(10);
        pad.AddChild(content);
        panel.AddChild(pad);
    }

    private static T Ignore<T>(T control)
        where T : Control
    {
        control.MouseFilter = Control.MouseFilterEnum.Ignore;
        return control;
    }
}
