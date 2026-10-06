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

/// <summary>The contextual readouts of <see cref="GameHud"/>: clock and weather, the world-event banner,
/// the interaction prompt and its keycaps, and the lock-on reticle.</summary>
public partial class GameHud
{
    private Label _context = null!;
    private TextureRect _phaseGlyph = null!;
    private Label _phaseText = null!;
    private Label _weatherText = null!;

    private PanelContainer _bannerPanel = null!;
    private Label _bannerText = null!;
    private Label _bannerTimer = null!;

    private PanelContainer _promptPanel = null!;
    private Label _promptText = null!;
    private Label _promptCap = null!;

    private TextureRect _lockReticle = null!;

    // -1 = nothing shown yet, -2 = the "no clock" empty state, otherwise the DayPhase.
    private int _phaseShown = -1;
    private int _hourShown = -1;
    private bool _weatherKnown;
    private WeatherResource? _weatherShown;

    private WorldEvent? _bannerShown;
    private int _bannerProgress = int.MinValue;
    private int _bannerRequired = int.MinValue;
    private float _bannerSecondsShown = float.NaN;
    private int _bannerHot = -1;

    private string? _promptShown;

    private void InvalidateContextShown()
    {
        _phaseShown = -1;
        _hourShown = -1;
        _weatherKnown = false;
        _bannerShown = null;
        _bannerSecondsShown = float.NaN;
        _bannerHot = -1;
        _promptShown = null;
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
        PanelContainer panel = Ignore(UiTheme.Band());
        _layout.TopLeft.AddChild(panel);

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", UiTheme.SpaceSm);

        // Shape AND colour carry the phase, so it survives ColorVision (§40) — and it is never the
        // only channel, because the phase name is on the same row.
        _phaseGlyph = UiIcon.Create(UiIcon.Kind.Sun, 18f, UiTheme.Accent);
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
    private static (UiIcon.Kind Icon, Color Tint) PhaseMark(DayPhase phase) => phase switch
    {
        DayPhase.Dawn => (UiIcon.Kind.Sun, UiTheme.Accent),
        DayPhase.Day => (UiIcon.Kind.Sun, UiTheme.Accent),
        DayPhase.Dusk => (UiIcon.Kind.Moon, UiTheme.AccentHot),
        _ => (UiIcon.Kind.Moon, UiTheme.ArcaneSilver),
    };

    private void BuildBanner()
    {
        _bannerPanel = Ignore(UiTheme.Band(UiTheme.AccentHot));
        _bannerPanel.Visible = false;
        _bannerPanel.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
        _layout.TopCenter.AddChild(_bannerPanel);

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", UiTheme.SpaceSm);
        row.AddChild(UiIcon.Create(UiIcon.Kind.Warning, 18f, UiTheme.Accent));
        _bannerText = UiTheme.Body("", UiTheme.Accent);
        row.AddChild(_bannerText);
        _bannerTimer = UiTheme.Body("", UiTheme.Dim);
        row.AddChild(_bannerTimer);
        WrapPadded(_bannerPanel, row);
    }

    /// <summary>A diamond marker (Phase 29H) tracked onto the locked-on target's screen position.</summary>
    private void BuildLockReticle()
    {
        _lockReticle = UiIcon.Create(UiIcon.Kind.Waypoint, 28f, UiTheme.AccentHot);
        _lockReticle.Visible = false;
        _lockReticle.Size = new Vector2(28f, 28f);
        _layout.Overlay.AddChild(_lockReticle);
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

    private void BuildPrompt()
    {
        _promptPanel = Ignore(UiTheme.Band(UiTheme.Accent));
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

    private void UpdateContext()
    {
        if (_clock is not { } clock || !IsInstanceValid(clock))
        {
            // Graceful empty state (§53): the widget goes away rather than showing an empty frame or
            // a placeholder time the player might believe.
            if (_phaseShown != -2)
            {
                _phaseShown = -2;
                _hourShown = -1;
                _weatherKnown = false;
                _phaseGlyph.Visible = false;
                _context.Text = string.Empty;
                _phaseText.Text = string.Empty;
                _weatherText.Text = string.Empty;
            }

            return;
        }

        // The phase changes four times a day and the hour twenty-four: neither is restated per frame.
        DayPhase phase = clock.Phase;
        if ((int)phase != _phaseShown)
        {
            _phaseShown = (int)phase;
            (UiIcon.Kind icon, Color tint) = PhaseMark(phase);
            _phaseGlyph.Visible = true;
            _phaseGlyph.Texture = UiIcon.Texture(icon);
            _phaseGlyph.Modulate = UiTheme.Adapt(tint);
            _phaseText.Text = Loc.T(DayPhases.NameKey(phase));
        }

        int hour = clock.Hour;
        if (hour != _hourShown)
        {
            _hourShown = hour;
            _context.Text = clock.Clock();
        }

        WeatherResource? current = _weather is { } weather && IsInstanceValid(weather) ? weather.Current : null;
        if (!_weatherKnown || !ReferenceEquals(current, _weatherShown))
        {
            _weatherKnown = true;
            _weatherShown = current;
            _weatherText.Visible = current != null;
            if (current != null)
            {
                _weatherText.Text = $"· {current.DisplayName}";
            }
        }
    }

    private void UpdateBanner()
    {
        if (_worldEvents is { } director && IsInstanceValid(director) && director.Active is { } worldEvent)
        {
            // The line is the event's name and its progress count; it is rebuilt when either moves.
            if (!ReferenceEquals(worldEvent, _bannerShown) || worldEvent.Progress != _bannerProgress ||
                worldEvent.Required != _bannerRequired)
            {
                _bannerShown = worldEvent;
                _bannerProgress = worldEvent.Progress;
                _bannerRequired = worldEvent.Required;
                _bannerSecondsShown = float.NaN;
                _bannerHot = -1;
                _bannerText.Text = $"{worldEvent.Name} — {worldEvent.ObjectiveLabel()}";
            }

            // Separate countdown that heats to ember orange in the final seconds (urgency read).
            _bannerTimer.Visible = worldEvent.IsTimed;
            if (worldEvent.IsTimed)
            {
                float seconds = (float)System.Math.Round(worldEvent.TimeLeft, System.MidpointRounding.AwayFromZero);
                if (seconds != _bannerSecondsShown)
                {
                    _bannerSecondsShown = seconds;
                    _bannerTimer.Text = $"{worldEvent.TimeLeft:0}s";
                }

                int hot = worldEvent.TimeLeft <= 10f ? 1 : 0;
                if (hot != _bannerHot)
                {
                    _bannerHot = hot;
                    UiLive.FontColor(_bannerTimer, hot == 1 ? UiTheme.AccentHot : UiTheme.Dim);
                }
            }

            _bannerPanel.Visible = true;
        }
        else
        {
            _bannerShown = null;
            _bannerPanel.Visible = false;
        }
    }

    private void UpdateFocus()
    {
        InteractionSensor? focusSensor = _player?.GetComponent<InteractionSensor>();

        // The locked-on target (Phase 29H) takes nameplate priority over the aimed-at focus, and is
        // reticled at its projected screen position.
        IEntity? locked = _player?.GetComponent<Combat.LockOnComponent>()?.Target;
        UpdateLockReticle(locked);
        IEntity? focus = (locked is Node lockNode && IsInstanceValid(lockNode)) ? locked : focusSensor?.FocusedEntity;

        // Nameplate for an aimed-at damageable that isn't the player. The widget owns the
        // validity guard, the snap-on-target-change and the disposition tint (37.5B).
        _nameplate.Show(Shows(HudElement.TargetPlate) ? focus : null, _player);

        // Interaction prompt for an aimed-at interactable.
        string? prompt = focusSensor?.FocusPrompt;
        if (!string.IsNullOrEmpty(prompt) && Shows(HudElement.Prompts))
        {
            if (prompt != _promptShown)
            {
                _promptShown = prompt;
                _promptText.Text = prompt;
            }

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
}
