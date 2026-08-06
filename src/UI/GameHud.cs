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
    private ProgressBar _cooldownBar = null!;

    // Status-effect chips (30.5C): one tinted chip per active effect. The row is rebuilt only
    // when the effect set changes (signature compare); timers update in place per frame.
    private HBoxContainer _statusRow = null!;
    private readonly System.Collections.Generic.List<(StatusEffect Effect, Label Time)> _statusChips = new();
    private string _statusSignature = string.Empty;

    private Label _context = null!;

    private PanelContainer _questPanel = null!;
    private VBoxContainer _questList = null!;
    private string _questSignature = string.Empty;

    private PanelContainer _bannerPanel = null!;
    private Label _bannerText = null!;
    private Label _bannerTimer = null!;

    private PanelContainer _namePanel = null!;
    private Label _nameText = null!;
    private JuicedBar _nameBar = null!;
    private IEntity? _lastFocus;

    private PanelContainer _promptPanel = null!;
    private Label _promptText = null!;
    private Label _promptCap = null!;

    private Label _lockReticle = null!;

    private CompassStrip _compass = null!;

    // Corruption dread: a dark blood-red edge vignette that fades in at high tiers (23E).
    private TextureRect _vignette = null!;
    private float _vignetteAlpha;
    private float _targetVignetteAlpha;
    private const float VignetteFadeSpeed = 0.5f; // alpha units per second

    // Boss fight UI (Phase 28C): a top-centre healthbar + a transient title/defeat message, plus a
    // screen fade for the defeat beat. Driven by the boss encounter events; HP polled each frame.
    private PanelContainer _bossPanel = null!;
    private Label _bossName = null!;
    private JuicedBar _bossBar = null!;
    private Label _bossPhase = null!;
    private Label _bossMsg = null!;
    private ColorRect _bossFade = null!;
    private IEntity? _boss;
    private ulong _bossMsgUntil;
    private ulong _bossFadeUntil;
    private const ulong BossFadeMs = 1400;

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
        AddChild(_layout);

        BuildVignette(); // backmost overlay — built first so the HUD widgets draw over it
        _layout.Overlay.AddChild(new Crosshair());
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
        BuildPrompt();
        BuildLockReticle();
        BuildLevelUp();

        EventBus.Instance?.Subscribe<XpGainedEvent>(OnXpGained);
        EventBus.Instance?.Subscribe<LeveledUpEvent>(OnLeveledUp);
        EventBus.Instance?.Subscribe<InputDeviceChangedEvent>(OnInputDeviceChanged);
        EventBus.Instance?.Subscribe<CorruptionTierChangedEvent>(OnCorruptionTierChanged);
        EventBus.Instance?.Subscribe<BossEncounterStartedEvent>(OnBossStarted);
        EventBus.Instance?.Subscribe<BossPhaseChangedEvent>(OnBossPhase);
        EventBus.Instance?.Subscribe<EntityDiedEvent>(OnBossDied);
    }

    public override void _ExitTree()
    {
        EventBus.Instance?.Unsubscribe<XpGainedEvent>(OnXpGained);
        EventBus.Instance?.Unsubscribe<LeveledUpEvent>(OnLeveledUp);
        EventBus.Instance?.Unsubscribe<InputDeviceChangedEvent>(OnInputDeviceChanged);
        EventBus.Instance?.Unsubscribe<CorruptionTierChangedEvent>(OnCorruptionTierChanged);
        EventBus.Instance?.Unsubscribe<BossEncounterStartedEvent>(OnBossStarted);
        EventBus.Instance?.Unsubscribe<BossPhaseChangedEvent>(OnBossPhase);
        EventBus.Instance?.Unsubscribe<EntityDiedEvent>(OnBossDied);
    }

    // --- Construction -------------------------------------------------------

    private void BuildVitals()
    {
        PanelContainer panel = Ignore(UiTheme.Panel());
        panel.CustomMinimumSize = new Vector2(250, 0);
        _layout.BottomLeft.AddChild(panel);

        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 4);
        WrapPadded(panel, col);

        (_hpBar, _hpText) = AddVital(col, Loc.T("hud.hp"), UiTheme.Health);
        (_staBar, _staText) = AddVital(col, Loc.T("hud.sta"), UiTheme.Stamina);
        (_mpBar, _mpText) = AddVital(col, Loc.T("hud.mp"), UiTheme.Mana);

        _footer = UiTheme.Body("", UiTheme.Dim);
        col.AddChild(_footer);

        _xpPop = UiTheme.Caption("", UiTheme.Accent);
        _xpPop.Visible = false;
        col.AddChild(_xpPop);

        // Prepared spell: name in the school's colour, state readout, and a thin recovery bar
        // that fills while the spell cools down (hidden when ready).
        _spellRow = new HBoxContainer { Visible = false };
        _spellRow.AddThemeConstantOverride("separation", UiTheme.SpaceSm);
        _spellName = UiTheme.Body("");
        _spellName.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _spellRow.AddChild(_spellName);
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
        _castBar = UiTheme.Bar(new Color(0.9f, 0.9f, 0.9f));
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

    private void BuildContext()
    {
        PanelContainer panel = Ignore(UiTheme.Panel());
        _layout.TopLeft.AddChild(panel);

        _context = UiTheme.Body("", UiTheme.Dim);
        var col = new VBoxContainer();
        col.AddChild(_context);
        WrapPadded(panel, col);
    }

    private void BuildQuestTracker()
    {
        _questPanel = Ignore(UiTheme.Panel());
        _questPanel.Visible = false;
        _questPanel.CustomMinimumSize = new Vector2(210, 0);
        _layout.TopRight.AddChild(_questPanel);

        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 2);
        col.AddChild(UiTheme.Header(Loc.T("hud.quest")));
        _questList = new VBoxContainer();
        _questList.AddThemeConstantOverride("separation", 2);
        col.AddChild(_questList);
        WrapPadded(_questPanel, col);
    }

    private void BuildCompass()
    {
        _compass = new CompassStrip { SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter };
        _compass.SetPlayer(_player);
        _layout.TopCenter.AddChild(_compass);
    }

    private void BuildBanner()
    {
        _bannerPanel = Ignore(UiTheme.Panel());
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
        _namePanel = Ignore(UiTheme.Panel());
        _namePanel.Visible = false;
        _namePanel.CustomMinimumSize = new Vector2(190, 0);
        _namePanel.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
        _layout.TopCenter.AddChild(_namePanel);

        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 3);
        _nameText = UiTheme.Body("");
        _nameText.HorizontalAlignment = HorizontalAlignment.Center;
        col.AddChild(_nameText);
        _nameBar = JuicedBar.Create(UiTheme.Health, 170f);
        col.AddChild(_nameBar);
        WrapPadded(_namePanel, col);
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
        _lockReticle.AddThemeFontSizeOverride("font_size", 22);
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
        _levelUp.AddThemeFontSizeOverride("font_size", UiTheme.DisplayFontSize);
        _levelUp.AddThemeColorOverride("font_color", UiTheme.Accent);
        _levelUp.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _layout.Overlay.AddChild(_levelUp);
    }

    /// <summary>Swap the interaction keycap glyph when the player switches between keyboard
    /// and gamepad (30.5J) — "E" ↔ "X" live, no rebuild.</summary>
    private void OnInputDeviceChanged(InputDeviceChangedEvent e) =>
        _promptCap.Text = GameInput.PromptLabel(GameInput.Interact);

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
        _promptPanel = Ignore(UiTheme.Panel());
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
        UpdateVitals();
        UpdateContext();
        UpdateQuest();
        UpdateBanner();
        UpdateFocus();
        UpdateVignette(delta);
        UpdateProgressionPops(delta);
        UpdateBoss();
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

    private void UpdateVitals()
    {
        if (_player is not Node node || !IsInstanceValid(node) ||
            !_player.TryGetComponent(out StatsComponent stats))
        {
            return;
        }

        SetVital(_hpBar, _hpText, stats, StatType.Health);
        SetVital(_staBar, _staText, stats, StatType.Stamina);
        SetVital(_mpBar, _mpText, stats, StatType.Mana);

        _footer.Text = _player.TryGetComponent(out ProgressionComponent prog)
            ? Loc.TF("hud.level", prog.Level)
            : string.Empty;

        UpdateSpellWidget();
        UpdateStatusChips();
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
            _spellState.Text = spells.IsCharging ? Loc.T("hud.charging")
                : spells.IsChanneling ? Loc.T("hud.channeling")
                : cd > 0f ? $"{cd:0.0}s"
                : Loc.T("hud.ready");
            // Font colour, not Modulate — modulating multiplies the caption's own Dim down
            // below readable contrast (30.5K audit).
            _spellState.AddThemeColorOverride("font_color", cd > 0f ? UiTheme.Dim : UiTheme.Accent);
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
            PanelContainer chip = Ignore(UiTheme.Panel());
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", UiTheme.SpaceXs);

            // Buffs read as dead-green, afflictions in their school's colour.
            Color tint = effect.Definition.IsBeneficial ? UiTheme.Good : SpellSchools.Color(effect.Definition.School);
            row.AddChild(UiTheme.Caption(effect.Definition.DisplayName, tint));

            Label time = UiTheme.Caption("");
            row.AddChild(time);
            _statusChips.Add((effect, time));

            MarginContainer pad = UiTheme.Padding(UiTheme.SpaceXs);
            pad.AddChild(row);
            chip.AddChild(pad);
            _statusRow.AddChild(chip);
        }
    }

    private void UpdateContext()
    {
        if (_clock is not { } clock || !IsInstanceValid(clock))
        {
            _context.Text = string.Empty;
            return;
        }

        var sb = new StringBuilder();
        sb.Append($"{clock.Clock()}  ({DayPhases.Label(clock.Phase)})");
        if (_weather is { } weather && IsInstanceValid(weather) && weather.Current is { } w)
        {
            sb.Append($"   ·   {w.DisplayName}");
        }

        _context.Text = sb.ToString();
    }

    private void UpdateQuest()
    {
        QuestProgress? active = null;
        if (_player is { } player && player.GetComponent<QuestLogComponent>() is { } log)
        {
            foreach (QuestProgress progress in log.Quests)
            {
                if (progress.Status == QuestStatus.Active)
                {
                    active = progress;
                    break;
                }
            }
        }

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

        _questPanel.Visible = true;
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

        _questList.AddChild(UiTheme.Body(Loc.T(progress.Quest.Title), UiTheme.Accent));

        var objectives = progress.Quest.ObjectiveList();
        for (int i = 0; i < objectives.Count; i++)
        {
            bool done = progress.Counts[i] >= objectives[i].RequiredCount;
            string mark = done ? "✓" : "•";
            _questList.AddChild(UiTheme.Caption(
                $"  {mark} {Loc.T(objectives[i].ShortLabel())}  {progress.Counts[i]}/{objectives[i].RequiredCount}",
                done ? UiTheme.Good : UiTheme.Text));
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

        // Nameplate for an aimed-at damageable that isn't the player. Guard instance validity
        // first: a focused target can be freed (despawn, save/load rebuild) while its reference
        // lingers, and dereferencing the disposed node would throw every frame.
        if (focus is Node focusNode && IsInstanceValid(focusNode) && !ReferenceEquals(focus, _player) &&
            focus.GetComponent<StatsComponent>() is { } stats)
        {
            _nameText.Text = focus.DisplayName;

            // Snap when the aimed-at subject changes so the drain lag never animates across targets.
            double health = stats.GetNormalized(StatType.Health);
            if (!ReferenceEquals(focus, _lastFocus))
            {
                _lastFocus = focus;
                _nameBar.Snap(health);
            }
            else
            {
                _nameBar.SetTarget(health);
            }

            _namePanel.Visible = true;
        }
        else
        {
            _lastFocus = null;
            _namePanel.Visible = false;
        }

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

    private static (JuicedBar Bar, Label Value) AddVital(VBoxContainer col, string caption, Color fill)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);

        Label cap = UiTheme.Body(caption);
        cap.CustomMinimumSize = new Vector2(34, 0);
        row.AddChild(cap);

        JuicedBar bar = JuicedBar.Create(fill);
        bar.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        row.AddChild(bar);

        Label value = UiTheme.Body("", UiTheme.Dim);
        value.CustomMinimumSize = new Vector2(72, 0);
        value.HorizontalAlignment = HorizontalAlignment.Right;
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

    private void BuildBossBar()
    {
        // Full-screen black fade for the defeat beat — built before the panel so the boss text draws
        // over it; alpha-pulsed manually (a Tween would be slowed by the defeat's Engine.TimeScale dip).
        _bossFade = Ignore(new ColorRect { Color = new Color(0f, 0f, 0f), SelfModulate = new Color(1f, 1f, 1f, 0f) });
        _bossFade.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _bossFade.Visible = false;
        _layout.Overlay.AddChild(_bossFade);

        _bossPanel = Ignore(UiTheme.Panel());
        _bossPanel.Visible = false;
        _bossPanel.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
        _layout.TopCenter.AddChild(_bossPanel);

        var col = new VBoxContainer();
        _bossName = UiTheme.Header(Loc.T("boss.name"));
        _bossName.HorizontalAlignment = HorizontalAlignment.Center;
        col.AddChild(_bossName);

        _bossBar = JuicedBar.Create(UiTheme.Health, 320f);
        col.AddChild(_bossBar);

        _bossPhase = UiTheme.Body("", UiTheme.Dim);
        _bossPhase.HorizontalAlignment = HorizontalAlignment.Center;
        col.AddChild(_bossPhase);

        _bossMsg = UiTheme.Body("", UiTheme.Accent);
        _bossMsg.HorizontalAlignment = HorizontalAlignment.Center;
        _bossMsg.Visible = false;
        col.AddChild(_bossMsg);

        WrapPadded(_bossPanel, col);
    }

    private void OnBossStarted(BossEncounterStartedEvent e)
    {
        _boss = e.Boss;
        _bossBar.Snap(1d);
        _bossName.Text = e.DisplayName;
        _bossPhase.Text = Loc.TF("boss.phase", 1, e.TotalPhases);
        _bossName.Visible = true;
        _bossBar.Visible = true;
        _bossPhase.Visible = true;
        _bossPanel.Visible = true;
        ShowBossMessage(Loc.T("boss.intro"), 2500);
    }

    private void OnBossPhase(BossPhaseChangedEvent e) =>
        _bossPhase.Text = Loc.TF("boss.phase", e.Phase, e.TotalPhases);

    private void OnBossDied(EntityDiedEvent e)
    {
        if (!ReferenceEquals(e.Entity, _boss))
        {
            return;
        }

        _boss = null;
        _bossBar.Visible = false;
        _bossName.Visible = false;
        _bossPhase.Visible = false;
        ShowBossMessage(Loc.T("boss.defeat"), 3000);
        _bossFade.Visible = true;
        _bossFadeUntil = Time.GetTicksMsec() + BossFadeMs;
    }

    private void ShowBossMessage(string text, ulong durationMs)
    {
        _bossMsg.Text = text;
        _bossMsg.Visible = true;
        _bossMsgUntil = Time.GetTicksMsec() + durationMs;
    }

    private void UpdateBoss()
    {
        ulong now = Time.GetTicksMsec();

        if (_boss is Node node && !IsInstanceValid(node))
        {
            // The boss left the world without dying — a region transition or a load mid-fight frees
            // it outright and raises no EntityDiedEvent, which is the only thing that cleared this
            // panel. The bar stayed on screen at its last value for the rest of the session. No
            // defeat beat here: nothing was defeated, so the widget simply stands down.
            _boss = null;
            _bossBar.Visible = false;
            _bossName.Visible = false;
            _bossPhase.Visible = false;
        }
        else if (_boss != null && _boss.TryGetComponent(out StatsComponent stats))
        {
            _bossBar.SetTarget(stats.GetNormalized(StatType.Health));
        }

        if (_bossMsg.Visible && now >= _bossMsgUntil)
        {
            _bossMsg.Visible = false;
        }

        // Defeat fade: ramp to black and back over the window (sin curve), then clear.
        if (_bossFade.Visible)
        {
            float t = Mathf.Clamp(1f - (float)(_bossFadeUntil - now) / BossFadeMs, 0f, 1f);
            _bossFade.SelfModulate = new Color(1f, 1f, 1f, Mathf.Sin(t * Mathf.Pi) * 0.7f);
            if (now >= _bossFadeUntil)
            {
                _bossFade.Visible = false;
            }
        }

        if (_boss == null && !_bossMsg.Visible && !_bossFade.Visible)
        {
            _bossPanel.Visible = false;
        }
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
