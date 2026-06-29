using System.Text;
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
/// The purpose-built in-game HUD (Phase 18), the player-facing overlay that replaces the
/// old debug read-out as the default on-screen UI. Anchored widgets rather than a text
/// dump: vitals bars bottom-left, a prepared-spell + status line, a quest tracker top-right,
/// time/weather top-left, a world-event banner and aimed-target nameplate up top, an
/// interaction prompt bottom-centre, and the crosshair. Persistent nodes updated each frame
/// from the player and the world directors; built through <see cref="UiTheme"/>.
/// </summary>
public partial class GameHud : CanvasLayer
{
    private IEntity? _player;
    private WorldClock? _clock;
    private WeatherDirector? _weather;
    private WorldEventDirector? _worldEvents;

    private ProgressBar _hpBar = null!;
    private ProgressBar _staBar = null!;
    private ProgressBar _mpBar = null!;
    private Label _hpText = null!;
    private Label _staText = null!;
    private Label _mpText = null!;
    private Label _footer = null!;
    private Label _statusLine = null!;

    private Label _context = null!;

    private PanelContainer _questPanel = null!;
    private Label _questText = null!;

    private PanelContainer _bannerPanel = null!;
    private Label _bannerText = null!;

    private PanelContainer _namePanel = null!;
    private Label _nameText = null!;
    private ProgressBar _nameBar = null!;

    private PanelContainer _promptPanel = null!;
    private Label _promptText = null!;

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
    private ProgressBar _bossBar = null!;
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
        BuildVignette(); // backmost — built first so the HUD widgets draw over it
        AddChild(new Crosshair());
        BuildVitals();
        BuildContext();
        BuildCompass();
        BuildQuestTracker();
        BuildBanner();
        BuildNameplate();
        BuildPrompt();
        BuildLockReticle();
        BuildBossBar();

        EventBus.Instance?.Subscribe<CorruptionTierChangedEvent>(OnCorruptionTierChanged);
        EventBus.Instance?.Subscribe<BossEncounterStartedEvent>(OnBossStarted);
        EventBus.Instance?.Subscribe<BossPhaseChangedEvent>(OnBossPhase);
        EventBus.Instance?.Subscribe<EntityDiedEvent>(OnBossDied);
    }

    public override void _ExitTree()
    {
        EventBus.Instance?.Unsubscribe<CorruptionTierChangedEvent>(OnCorruptionTierChanged);
        EventBus.Instance?.Unsubscribe<BossEncounterStartedEvent>(OnBossStarted);
        EventBus.Instance?.Unsubscribe<BossPhaseChangedEvent>(OnBossPhase);
        EventBus.Instance?.Unsubscribe<EntityDiedEvent>(OnBossDied);
    }

    // --- Construction -------------------------------------------------------

    private void BuildVitals()
    {
        PanelContainer panel = Ignore(UiTheme.Panel());
        panel.SetAnchorsPreset(Control.LayoutPreset.BottomLeft);
        panel.OffsetLeft = 16;
        panel.OffsetBottom = -16;
        panel.GrowVertical = Control.GrowDirection.Begin;
        panel.CustomMinimumSize = new Vector2(300, 0);
        AddChild(panel);

        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 4);
        WrapPadded(panel, col);

        (_hpBar, _hpText) = AddVital(col, Loc.T("hud.hp"), UiTheme.Health);
        (_staBar, _staText) = AddVital(col, Loc.T("hud.sta"), UiTheme.Stamina);
        (_mpBar, _mpText) = AddVital(col, Loc.T("hud.mp"), UiTheme.Mana);

        _footer = UiTheme.Body("", UiTheme.Dim);
        col.AddChild(_footer);
        _statusLine = UiTheme.Body("", UiTheme.Good);
        col.AddChild(_statusLine);
    }

    private void BuildContext()
    {
        PanelContainer panel = Ignore(UiTheme.Panel());
        panel.Position = new Vector2(16, 16);
        AddChild(panel);

        _context = UiTheme.Body("", UiTheme.Dim);
        var col = new VBoxContainer();
        col.AddChild(_context);
        WrapPadded(panel, col);
    }

    private void BuildQuestTracker()
    {
        _questPanel = Ignore(UiTheme.Panel());
        _questPanel.Visible = false;
        _questPanel.SetAnchorsPreset(Control.LayoutPreset.TopRight);
        _questPanel.OffsetRight = -16;
        _questPanel.OffsetTop = 16;
        _questPanel.GrowHorizontal = Control.GrowDirection.Begin;
        _questPanel.CustomMinimumSize = new Vector2(240, 0);
        AddChild(_questPanel);

        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 2);
        col.AddChild(UiTheme.Header(Loc.T("hud.quest")));
        _questText = UiTheme.Body("");
        col.AddChild(_questText);
        WrapPadded(_questPanel, col);
    }

    private void BuildCompass()
    {
        _compass = new CompassStrip();
        CenterTop(_compass, 6);
        _compass.SetPlayer(_player);
        AddChild(_compass);
    }

    private void BuildBanner()
    {
        _bannerPanel = Ignore(UiTheme.Panel());
        _bannerPanel.Visible = false;
        CenterTop(_bannerPanel, 40);
        AddChild(_bannerPanel);

        _bannerText = UiTheme.Body("", UiTheme.Accent);
        var col = new VBoxContainer();
        col.AddChild(_bannerText);
        WrapPadded(_bannerPanel, col);
    }

    private void BuildNameplate()
    {
        _namePanel = Ignore(UiTheme.Panel());
        _namePanel.Visible = false;
        CenterTop(_namePanel, 66);
        _namePanel.CustomMinimumSize = new Vector2(220, 0);
        AddChild(_namePanel);

        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 3);
        _nameText = UiTheme.Body("");
        _nameText.HorizontalAlignment = HorizontalAlignment.Center;
        col.AddChild(_nameText);
        _nameBar = UiTheme.Bar(UiTheme.Health, 200f);
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
        AddChild(_lockReticle);
    }

    private void BuildPrompt()
    {
        _promptPanel = Ignore(UiTheme.Panel());
        _promptPanel.Visible = false;
        CenterBottom(_promptPanel, 120);
        AddChild(_promptPanel);

        _promptText = UiTheme.Body("", UiTheme.Accent);
        var col = new VBoxContainer();
        col.AddChild(_promptText);
        WrapPadded(_promptPanel, col);
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
        AddChild(_vignette);
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

        var footer = new StringBuilder();
        if (_player.TryGetComponent(out ProgressionComponent prog))
        {
            footer.Append(Loc.TF("hud.level", prog.Level));
        }

        if (_player.TryGetComponent(out SpellcastingComponent spells) && spells.Selected is { } spell)
        {
            float cd = spells.CooldownOf(spell);
            string state = cd > 0f ? $"{cd:0.0}s" : Loc.T("hud.ready");
            footer.Append(footer.Length > 0 ? "    " : string.Empty);
            footer.Append(Loc.TF("hud.spell", spell.DisplayName, state));
        }

        _footer.Text = footer.ToString();
        _statusLine.Text = StatusText(_player);
        _statusLine.Visible = _statusLine.Text.Length > 0;
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
        if (_player is not { } player || player.GetComponent<QuestLogComponent>() is not { } log)
        {
            _questPanel.Visible = false;
            return;
        }

        foreach (QuestProgress progress in log.Quests)
        {
            if (progress.Status != QuestStatus.Active)
            {
                continue;
            }

            var sb = new StringBuilder();
            sb.Append(Loc.T(progress.Quest.Title));
            var objectives = progress.Quest.ObjectiveList();
            for (int i = 0; i < objectives.Count; i++)
            {
                sb.Append($"\n  {Loc.T(objectives[i].ShortLabel())}  {progress.Counts[i]}/{objectives[i].RequiredCount}");
            }

            _questText.Text = sb.ToString();
            _questPanel.Visible = true;
            return;
        }

        _questPanel.Visible = false;
    }

    private void UpdateBanner()
    {
        if (_worldEvents is { } director && IsInstanceValid(director) && director.Active is { } worldEvent)
        {
            var sb = new StringBuilder();
            sb.Append($"★ {worldEvent.Resource.DisplayName} — {worldEvent.ObjectiveLabel()}");
            if (worldEvent.IsTimed)
            {
                sb.Append($"  ·  {worldEvent.TimeLeft:0}s");
            }

            _bannerText.Text = sb.ToString();
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
            _nameBar.Value = stats.GetNormalized(StatType.Health);
            _namePanel.Visible = true;
        }
        else
        {
            _namePanel.Visible = false;
        }

        // Interaction prompt for an aimed-at interactable.
        string? prompt = controller?.FocusPrompt;
        if (!string.IsNullOrEmpty(prompt))
        {
            _promptText.Text = $"[E] {prompt}";
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
                _lockReticle.Visible = true;
                return;
            }
        }

        _lockReticle.Visible = false;
    }

    // --- Helpers ------------------------------------------------------------

    private static string StatusText(IEntity player)
    {
        if (player.GetComponent<StatusEffectsComponent>() is not { } effects || effects.ActiveEffects.Count == 0)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        foreach (StatusEffect effect in effects.ActiveEffects)
        {
            if (sb.Length > 0)
            {
                sb.Append("  ·  ");
            }

            sb.Append($"{effect.Definition.DisplayName} {effect.Remaining:0.0}s");
        }

        return sb.ToString();
    }

    private static (ProgressBar Bar, Label Value) AddVital(VBoxContainer col, string caption, Color fill)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);

        Label cap = UiTheme.Body(caption);
        cap.CustomMinimumSize = new Vector2(34, 0);
        row.AddChild(cap);

        ProgressBar bar = UiTheme.Bar(fill);
        bar.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        row.AddChild(bar);

        Label value = UiTheme.Body("", UiTheme.Dim);
        value.CustomMinimumSize = new Vector2(72, 0);
        value.HorizontalAlignment = HorizontalAlignment.Right;
        row.AddChild(value);

        col.AddChild(row);
        return (bar, value);
    }

    private static void SetVital(ProgressBar bar, Label value, StatsComponent stats, StatType type)
    {
        bar.Value = stats.GetNormalized(type);
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
        AddChild(_bossFade);

        _bossPanel = Ignore(UiTheme.Panel());
        _bossPanel.Visible = false;
        CenterTop(_bossPanel, 14);
        AddChild(_bossPanel);

        var col = new VBoxContainer();
        _bossName = UiTheme.Header(Loc.T("boss.name"));
        _bossName.HorizontalAlignment = HorizontalAlignment.Center;
        col.AddChild(_bossName);

        _bossBar = UiTheme.Bar(UiTheme.Health, 360f);
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
        _bossName.Text = Loc.T(e.NameKey);
        _bossPhase.Text = Loc.TF("boss.phase", 1, 3);
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

        if (_boss is Node node && IsInstanceValid(node) && _boss.TryGetComponent(out StatsComponent stats))
        {
            _bossBar.Value = stats.GetNormalized(StatType.Health);
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

    private static void CenterTop(Control control, float y)
    {
        control.AnchorLeft = 0.5f;
        control.AnchorRight = 0.5f;
        control.AnchorTop = 0f;
        control.AnchorBottom = 0f;
        control.GrowHorizontal = Control.GrowDirection.Both;
        control.GrowVertical = Control.GrowDirection.End;
        control.OffsetTop = y;
    }

    private static void CenterBottom(Control control, float y)
    {
        control.AnchorLeft = 0.5f;
        control.AnchorRight = 0.5f;
        control.AnchorTop = 1f;
        control.AnchorBottom = 1f;
        control.GrowHorizontal = Control.GrowDirection.Both;
        control.GrowVertical = Control.GrowDirection.Begin;
        control.OffsetBottom = -y;
    }

    private static T Ignore<T>(T control)
        where T : Control
    {
        control.MouseFilter = Control.MouseFilterEnum.Ignore;
        return control;
    }
}
