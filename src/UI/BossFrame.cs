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
/// only forwards to. It sits bare on the world like the vitals do: the name carved above one
/// keylined bar as wide as <see cref="HudMetrics.BossBarWidth"/> allows, with the length a blow
/// just removed held behind the fill for a beat. The display face is all the ornament it takes.
///
/// A boss's authored intro line is something it says, so with subtitles on it is captioned by
/// <see cref="SubtitleLayer"/> under the boss's name; the frame's own line carries it otherwise.
/// </summary>
public partial class BossFrame : PanelContainer
{
    private const ulong FadeMs = 1400;

    /// <summary>Taller than a vitals bar: it is read from across a fight.</summary>
    private const float BarHeight = HudCoreMetrics.BarHeight + UiTheme.Space2xs;

    /// <summary>How long an authored intro line stays, as the frame's line or as a caption.</summary>
    private const float IntroSeconds = 4.5f;

    private static readonly Vector2 PipSize = new(18f, 4f);

    private Label _name = null!;
    private Label _epithet = null!;
    private JuicedBar _bar = null!;
    private HBoxContainer _pips = null!;
    private Label _phaseText = null!;
    private Label _message = null!;

    /// <summary>The defeat fade. Full-screen, so it lives in the HUD's overlay slot rather than
    /// under this panel — handed over by <see cref="AttachFade"/>.</summary>
    private ColorRect _fade = null!;

    private HudLayout? _layout;
    private IEntity? _boss;
    private int _totalPhases = 1;
    private ulong _messageUntil;
    private ulong _fadeUntil;

    /// <summary>The bar's width, for a harness to hold against <see cref="HudMetrics.BossBarWidth"/>.</summary>
    public float BarWidthForCapture => _bar.CustomMinimumSize.X;

    public BossFrame()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        Visible = false;
        SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
        AddThemeStyleboxOverride("panel", new StyleBoxEmpty());

        PanelContainer ground = UiTheme.HudBare(); // an opaque plate under high contrast only
        var col = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        col.AddThemeConstantOverride("separation", UiTheme.SpaceXs);

        _name = UiTheme.HudInk(UiTheme.Display(Loc.T("boss.name"), UiTheme.Text));
        _name.HorizontalAlignment = HorizontalAlignment.Center;
        col.AddChild(_name);

        // The boss's epithet card ("The Black-Iron King"), set in the book italic under the name. Hidden for
        // a boss that authors none, so the frame is unchanged for them.
        _epithet = UiTheme.HudInk(UiTheme.Flavour(string.Empty, UiTheme.Dim));
        UiTheme.ApplyType(_epithet, UiTheme.FontRole.SerifItalic, UiTheme.BodyFontSize);
        _epithet.HorizontalAlignment = HorizontalAlignment.Center;
        _epithet.Visible = false;
        col.AddChild(_epithet);

        float width = HudMetrics.BossBarWidth(HudMetrics.ReferenceWidth);
        _bar = JuicedBar.Create(UiTheme.Health, width);
        _bar.CustomMinimumSize = new Vector2(width, BarHeight);
        _bar.Keylined = true;
        _bar.LagChunk = true;
        _bar.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
        col.AddChild(_bar);

        // Pips carry the phase at a glance; the line beside them keeps it in words. Redundant on
        // purpose — a row of shapes is fast to read and impossible to read *precisely*, and
        // "phase 2 of 4" is the kind of thing a player checks when they are losing.
        var phase = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center, MouseFilter = MouseFilterEnum.Ignore };
        phase.AddThemeConstantOverride("separation", UiTheme.SpaceSm);
        _pips = new HBoxContainer
        {
            Alignment = BoxContainer.AlignmentMode.Center,
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _pips.AddThemeConstantOverride("separation", UiTheme.SpaceXs);
        phase.AddChild(_pips);

        _phaseText = UiTheme.HudInk(UiTheme.Caption(""));
        phase.AddChild(_phaseText);
        col.AddChild(phase);

        _message = UiTheme.HudInk(UiTheme.Body("", UiTheme.AccentHot));
        _message.HorizontalAlignment = HorizontalAlignment.Center;
        _message.Visible = false;
        col.AddChild(_message);

        ground.AddChild(col);
        AddChild(ground);
    }

    /// <summary>Builds the defeat fade into the HUD's full-screen overlay slot. Separate from the
    /// constructor because the fade is not a child of this panel — it covers the screen, and this
    /// panel is a small top-centre widget.</summary>
    public void AttachFade(Control overlay)
    {
        _fade = new ColorRect
        {
            Color = UiTheme.ScrimBg,
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
        EventBus.Instance?.Subscribe<BossWithdrewEvent>(OnWithdrew);

        // The bar is a share of the width the scaled HUD lays out in, so it follows that rect.
        for (Node? node = GetParent(); node != null && _layout == null; node = node.GetParent())
        {
            _layout = node as HudLayout;
        }

        if (_layout != null)
        {
            _layout.Scaled.Resized += ApplyWidth;
            ApplyWidth();
        }

        // Asleep between fights: Present and ShowMessage wake it, and the tick puts it back to
        // sleep on the frame it hides itself.
        SetProcess(Visible);
    }

    private void ApplyWidth()
    {
        float layoutWidth = _layout?.Scaled.Size.X ?? 0f;
        if (layoutWidth <= 0f)
        {
            return; // not laid out yet; Resized calls back when it is
        }

        float width = HudMetrics.BossBarWidth(layoutWidth);
        if (_bar.CustomMinimumSize.X != width)
        {
            _bar.CustomMinimumSize = new Vector2(width, BarHeight);
        }
    }

    public override void _ExitTree()
    {
        EventBus.Instance?.Unsubscribe<BossEncounterStartedEvent>(OnStarted);
        EventBus.Instance?.Unsubscribe<BossPhaseChangedEvent>(OnPhase);
        EventBus.Instance?.Unsubscribe<EntityDiedEvent>(OnDied);
        EventBus.Instance?.Unsubscribe<BossWithdrewEvent>(OnWithdrew);
        if (_layout != null && IsInstanceValid(_layout))
        {
            _layout.Scaled.Resized -= ApplyWidth;
        }
    }

    private void OnStarted(BossEncounterStartedEvent e)
    {
        // The boss's own data carries the epithet and intro line. A boss with no controller (or authored
        // text that does not resolve) falls back to the generic "bars your path" line.
        BossResource? fight = e.Boss.GetComponent<BossController>()?.Fight;
        Present(e.Boss, e.DisplayName, e.TotalPhases, fight?.EpithetKey ?? string.Empty, fight?.IntroLineKey ?? string.Empty);
    }

    /// <summary>Opens the frame for a fight: name, epithet under it, phase pips and the intro line. The event
    /// handler calls this with the boss's data; it is public so a harness can stage the same frame
    /// through the same path.</summary>
    public void Present(IEntity boss, string displayName, int totalPhases, string epithetKey, string introLineKey)
    {
        _boss = boss;
        SetProcess(true);
        _totalPhases = Mathf.Max(1, totalPhases);
        _bar.Snap(1d);
        _name.Text = displayName;
        BuildPips();
        SetPhase(1);

        string? epithet = BossIntroText.Epithet(epithetKey, Loc.Has);
        _epithet.Visible = epithet != null;
        _epithet.Text = epithet != null ? Loc.T(epithet) : string.Empty;

        _name.Visible = true;
        _bar.Visible = true;
        _pips.Visible = true;
        _phaseText.Visible = true;
        _message.Visible = false;
        UiFx.Rise(this, seconds: UiTheme.DurationSlow);

        // An authored line is the boss speaking: captioned under its name when subtitles are on,
        // and carried by the frame when they are not. The generic line is the game's, not the boss's.
        string introKey = BossIntroText.IntroKey(introLineKey, Loc.Has, out bool usesName);
        if (usesName)
        {
            ShowMessage(Loc.TF(introKey, displayName), 2500UL);
        }
        else if (!SubtitleLayer.TryShow(displayName, Loc.T(introKey), IntroSeconds))
        {
            ShowMessage(Loc.T(introKey), (ulong)(IntroSeconds * 1000f));
        }
    }

    private void OnPhase(BossPhaseChangedEvent e) => SetPhase(e.Phase);

    private void OnDied(EntityDiedEvent e)
    {
        if (ReferenceEquals(e.Entity, _boss))
        {
            EndFight("boss.defeat");
        }
    }

    /// <summary>A boss that yields (Phase 47.5) ends the fight with its own line, not "defeated".</summary>
    private void OnWithdrew(BossWithdrewEvent e)
    {
        if (ReferenceEquals(e.Boss, _boss))
        {
            EndFight("boss.withdraw");
        }
    }

    private void EndFight(string messageKey)
    {
        StandDown();
        ShowMessage(Loc.TF(messageKey, _name.Text), 3000);

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
        _epithet.Visible = false;
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
                Color = UiTheme.Keyline,
                CustomMinimumSize = PipSize,
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
                : UiTheme.Keyline;
        }
    }

    private void ShowMessage(string text, ulong durationMs)
    {
        _message.Text = text;
        _message.Visible = true;
        SetProcess(true);
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
            // Leaves on its own, so it may fade; Present takes over a fade in flight.
            UiFx.FadeOut(this, seconds: UiTheme.DurationBase);
            SetProcess(false); // nothing left to drive until the next fight
        }
    }
}
