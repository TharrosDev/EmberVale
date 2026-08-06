using Embervale.Combat;
using Embervale.Core.Events;
using Embervale.Enemies;
using Embervale.Entities;
using Embervale.Localization;
using Embervale.Stats;
using Godot;

namespace Embervale.UI;

/// <summary>
/// The boss encounter frame (Phase 28C; lifted out of <c>GameHud</c> in 37.5B). Name, health,
/// phase pips, a transient intro/defeat line, and the full-screen fade for the defeat beat.
///
/// It owns its own event subscriptions and its own <c>_Process</c>, which is the point of the
/// split: <c>GameHud</c> no longer carries three boss subscriptions and an <c>UpdateBoss</c> it
/// only forwards to. It is also the one HUD element that earns ornament — corner brass and the
/// display face — because the ornament budget (see <see cref="UiOrnament"/>) spends on the rarity
/// of the moment, and a boss is the rarest moment the HUD has.
/// </summary>
public partial class BossFrame : PanelContainer
{
    private const ulong FadeMs = 1400;

    private Label _name = null!;
    private JuicedBar _bar = null!;
    private HBoxContainer _pips = null!;
    private Label _phaseText = null!;
    private Label _message = null!;

    /// <summary>The defeat fade. Full-screen, so it lives in the HUD's overlay slot rather than
    /// under this panel — handed over by <see cref="AttachFade"/>.</summary>
    private ColorRect _fade = null!;

    private IEntity? _boss;
    private int _totalPhases = 1;
    private ulong _messageUntil;
    private ulong _fadeUntil;

    public BossFrame()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        Visible = false;
        SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
        AddThemeStyleboxOverride("panel", UiTheme.PanelStyle());
        UiTheme.ApplyGrain(this);

        MarginContainer pad = UiTheme.Padding(UiTheme.SpaceMd);
        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", UiTheme.SpaceXs);

        _name = UiTheme.Title(Loc.T("boss.name"));
        _name.HorizontalAlignment = HorizontalAlignment.Center;
        col.AddChild(_name);

        _bar = JuicedBar.Create(UiTheme.Health, 360f);
        col.AddChild(_bar);

        // Pips carry the phase at a glance; the line below keeps it in words. Redundant on
        // purpose — a row of shapes is fast to read and impossible to read *precisely*, and
        // "phase 2 of 4" is the kind of thing a player checks when they are losing.
        _pips = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        _pips.AddThemeConstantOverride("separation", UiTheme.SpaceXs);
        col.AddChild(_pips);

        _phaseText = UiTheme.Caption("");
        _phaseText.HorizontalAlignment = HorizontalAlignment.Center;
        col.AddChild(_phaseText);

        _message = UiTheme.Body("", UiTheme.AccentHot);
        _message.HorizontalAlignment = HorizontalAlignment.Center;
        _message.Visible = false;
        col.AddChild(_message);

        pad.AddChild(col);
        AddChild(pad);

        // Last child, so the brackets draw over the content.
        AddChild(UiOrnament.CornerBrass(arm: 16f, thickness: 2f, inset: 3f));
    }

    /// <summary>Builds the defeat fade into the HUD's full-screen overlay slot. Separate from the
    /// constructor because the fade is not a child of this panel — it covers the screen, and this
    /// panel is a small top-centre widget.</summary>
    public void AttachFade(Control overlay)
    {
        _fade = new ColorRect
        {
            Color = new Color(0f, 0f, 0f),
            SelfModulate = new Color(1f, 1f, 1f, 0f),
            MouseFilter = MouseFilterEnum.Ignore,
            Visible = false,
        };
        _fade.SetAnchorsPreset(LayoutPreset.FullRect);
        overlay.AddChild(_fade);
    }

    public override void _Ready()
    {
        EventBus.Instance?.Subscribe<BossEncounterStartedEvent>(OnStarted);
        EventBus.Instance?.Subscribe<BossPhaseChangedEvent>(OnPhase);
        EventBus.Instance?.Subscribe<EntityDiedEvent>(OnDied);
    }

    public override void _ExitTree()
    {
        EventBus.Instance?.Unsubscribe<BossEncounterStartedEvent>(OnStarted);
        EventBus.Instance?.Unsubscribe<BossPhaseChangedEvent>(OnPhase);
        EventBus.Instance?.Unsubscribe<EntityDiedEvent>(OnDied);
    }

    private void OnStarted(BossEncounterStartedEvent e)
    {
        _boss = e.Boss;
        _totalPhases = Mathf.Max(1, e.TotalPhases);
        _bar.Snap(1d);
        _name.Text = e.DisplayName;
        BuildPips();
        SetPhase(1);

        _name.Visible = true;
        _bar.Visible = true;
        _pips.Visible = true;
        _phaseText.Visible = true;
        Visible = true;
        ShowMessage(Loc.T("boss.intro"), 2500);
    }

    private void OnPhase(BossPhaseChangedEvent e) => SetPhase(e.Phase);

    private void OnDied(EntityDiedEvent e)
    {
        if (!ReferenceEquals(e.Entity, _boss))
        {
            return;
        }

        StandDown();
        ShowMessage(Loc.T("boss.defeat"), 3000);

        if (_fade is not null)
        {
            _fade.Visible = true;
            _fadeUntil = Time.GetTicksMsec() + FadeMs;
        }
    }

    /// <summary>Clears the live-fight widgets but leaves the panel up, so a defeat message can
    /// still play over it.</summary>
    private void StandDown()
    {
        _boss = null;
        _bar.Visible = false;
        _name.Visible = false;
        _pips.Visible = false;
        _phaseText.Visible = false;
    }

    private void BuildPips()
    {
        UiTheme.ClearChildren(_pips);
        for (int i = 0; i < _totalPhases; i++)
        {
            _pips.AddChild(new ColorRect
            {
                Color = UiTheme.Engrave,
                CustomMinimumSize = new Vector2(18f, 4f),
                MouseFilter = MouseFilterEnum.Ignore,
            });
        }
    }

    /// <summary>Lights the pips up to <paramref name="phase"/>. The current phase burns hot; the
    /// ones behind it stay lit but cool, so the frame shows how far in you are and not just where
    /// you are.</summary>
    private void SetPhase(int phase)
    {
        _phaseText.Text = Loc.TF("boss.phase", phase, _totalPhases);

        for (int i = 0; i < _pips.GetChildCount(); i++)
        {
            if (_pips.GetChild(i) is not ColorRect pip)
            {
                continue;
            }

            pip.Color = i + 1 == phase ? UiTheme.AccentHot
                : i + 1 < phase ? UiTheme.Brass
                : UiTheme.Engrave;
        }
    }

    private void ShowMessage(string text, ulong durationMs)
    {
        _message.Text = text;
        _message.Visible = true;
        _messageUntil = Time.GetTicksMsec() + durationMs;
    }

    public override void _Process(double delta)
    {
        ulong now = Time.GetTicksMsec();

        if (_boss is Node node && !IsInstanceValid(node))
        {
            // The boss left the world without dying — a region transition or a mid-fight load frees
            // it outright and raises no EntityDiedEvent, which was the only thing that cleared this
            // frame. Before the guard, the bar sat on screen at its last value for the rest of the
            // session. No defeat beat: nothing was defeated, so the widget just stands down.
            StandDown();
        }
        else if (_boss != null && _boss.TryGetComponent(out StatsComponent stats))
        {
            _bar.SetTarget(stats.GetNormalized(StatType.Health));
        }

        if (_message.Visible && now >= _messageUntil)
        {
            _message.Visible = false;
        }

        // Defeat fade: ramp to black and back over the window (sine), then clear. Driven off the
        // wall clock rather than a Tween because the defeat beat dips Engine.TimeScale, which would
        // stretch a Tween along with the slow motion.
        if (_fade is { Visible: true })
        {
            float t = Mathf.Clamp(1f - ((float)(_fadeUntil - now) / FadeMs), 0f, 1f);
            _fade.SelfModulate = new Color(1f, 1f, 1f, Mathf.Sin(t * Mathf.Pi) * 0.7f);
            if (now >= _fadeUntil)
            {
                _fade.Visible = false;
            }
        }

        if (_boss == null && !_message.Visible && _fade is not { Visible: true })
        {
            Visible = false;
        }
    }
}
