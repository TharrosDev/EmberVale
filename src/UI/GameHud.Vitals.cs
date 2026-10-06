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

/// <summary>The vitals group of <see cref="GameHud"/>: health, stamina and mana bars, the corruption
/// gauge, the level line with its XP pop and level-up flourish, the prepared spell, and the status and
/// control chips. It has no plate: the bars are keylined and the text inked, and they sit on the world
/// with the bars on the bottom edge, level with the hotbar and the minimap.</summary>
public partial class GameHud
{
    private JuicedBar _hpBar = null!;
    private JuicedBar _staBar = null!;
    private JuicedBar _mpBar = null!;
    private Label _hpText = null!;
    private Label _staText = null!;
    private Label _mpText = null!;
    private TextureRect _hpIcon = null!;
    private TextureRect _staIcon = null!;

    // The corruption gauge: hidden until the player carries any. Violet is its colour everywhere, so
    // here it also gets a hatched fill, a mark of its own and the tier as a word.
    private HBoxContainer _corruptionRow = null!;
    private JuicedBar _corruptionBar = null!;
    private Label _corruptionText = null!;
    private int _corruptionShown = int.MinValue;
    private int _corruptionTierShown = -1;

    private Control _spellGlyph = null!;
    private VBoxContainer _spellGroup = null!;
    private float _staTickShown = float.NaN;
    private float _mpTickShown = float.NaN;

    // Set by an invalidation and cleared by the tick that answers it: that tick rewrites every cache,
    // and none of those rewrites is news for a Dynamic element (GameHud.MarkChanged).
    private bool _vitalsQuiet = true;
    private bool _vitalsSnap = true;

    /// <summary>Forgets when anything last changed and when the last blow landed, so the screenshot
    /// harness can photograph a Dynamic HUD at rest without waiting out
    /// <see cref="HudDynamicRules.ChangeLingerSeconds"/> and <see cref="HudDynamicRules.CombatLingerSeconds"/>.
    /// The rules and the signals are the real ones; this only ages the clocks they read.</summary>
    public void SettleDynamicForCapture()
    {
        System.Array.Fill(_elementChangedAt, double.NegativeInfinity);
        _combatAt = double.NegativeInfinity;
    }

    /// <summary>The HUD a widget sits in, found by walking up from it. For the widgets that are
    /// their own nodes (compass, minimap, party strip, hotbar) and need <see cref="MarkChanged"/> or
    /// <see cref="LayoutWidth"/>; null outside a HUD (a tool, a test scene).</summary>
    internal static GameHud? Of(Node? node)
    {
        for (Node? at = node; at != null; at = at.GetParent())
        {
            if (at is GameHud hud)
            {
                return hud;
            }
        }

        return null;
    }
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
    private ProgressBar _cooldownBar = null!;

    // Status-effect chips (30.5C): one tinted chip per active effect. The row is rebuilt only
    // when the effect set changes (signature compare); timers update in place per frame.
    private HFlowContainer _statusRow = null!;

    // Control and Weave chips (magic upgrade): silenced / rooted / stunned state and the region's fading
    // Weave, in a wrapping row above the status chips. Rebuilt only when the signature changes.
    private HFlowContainer _controlRow = null!;

    /// <summary>Statuses shown in the strip before the rest fold into a "+N" chip.</summary>
    private const int MaxStatusChips = 6;
    private readonly System.Collections.Generic.List<StatusChip> _statusChips = new();

    // Phase of the low-health breath, in radians. Reset when health recovers so the pulse always
    // starts from full rather than wherever it happened to be (a stateful widget turning one bad
    // frame into a permanent fault — invariant 7).
    private float _criticalPhase;

    private struct VitalShown
    {
        public float Current;
        public float Max;
    }

    private sealed class StatusChip
    {
        public StatusEffect Effect = null!;
        public Label Time = null!;
        public int Stacks = int.MinValue;
        public float Tenths = float.NaN;
    }

    private static readonly VitalShown NothingShown = new() { Current = float.NaN, Max = float.NaN };

    private VitalShown _hpShown = NothingShown;
    private VitalShown _staShown = NothingShown;
    private VitalShown _mpShown = NothingShown;
    private int _windedShown = -1;
    private int _healthStateShown = -1;
    private bool _hpPulsed;
    private int _levelShown = int.MinValue;

    private SpellResource? _spellShown;
    private float _spellCostShown = float.NaN;
    private float _spellHealthCostShown = float.NaN;
    private int _spellAffordShown = -1;
    private int _spellStateShown = -1;
    private float _spellStateValueShown = float.NaN;
    private int _spellStateColorShown = -1;

    private int _controlKey = int.MinValue;
    private bool _statusStale;
    private readonly System.Collections.Generic.List<string> _statusIds = new();

    private void InvalidateVitalsShown()
    {
        _hpShown = _staShown = _mpShown = NothingShown;
        _windedShown = -1;
        _healthStateShown = -1;
        _hpPulsed = true;
        _levelShown = int.MinValue;
        _spellShown = null;
        _controlKey = int.MinValue;
        _statusStale = true;
        _corruptionShown = int.MinValue;
        _corruptionTierShown = -1;
        _staTickShown = float.NaN;
        _mpTickShown = float.NaN;
        _vitalsQuiet = true;
        _vitalsSnap = true;
        RefreshContrast();
    }

    // The high-contrast setting the HUD's grounds and ink were last made for.
    private bool _contrastShown;

    /// <summary>High contrast is not a colour a cache holds: it decides whether the bare groups have
    /// a ground at all. Every settings change comes through the invalidation above, so this is where
    /// a change of it mid-session is caught and the built HUD restyled (<see cref="UiTheme.RefreshHud"/>).</summary>
    private void RefreshContrast()
    {
        if (UiTheme.HighContrast != _contrastShown)
        {
            _contrastShown = UiTheme.HighContrast;
            UiTheme.RefreshHud(_layout);
        }
    }

    private void InvalidateSpellShown()
    {
        _spellCostShown = float.NaN;
        _spellHealthCostShown = float.NaN;
        _spellAffordShown = -1;
        _spellStateShown = -1;
        _spellStateValueShown = float.NaN;
        _spellStateColorShown = -1;
    }

    private void BuildVitals()
    {
        // No ground at all. The vitals were a Band, and before that a Panel: a box round three bars
        // and a number, on the corner of the screen the player looks at most. The bars carry their own
        // keyline and the text its own ink (UiTheme.HudInk), which is all either needs to read over a
        // bright sky or a dark cave, and it leaves the world visible between them.
        _contrastShown = UiTheme.HighContrast;
        PanelContainer panel = Ignore(UiTheme.HudBare());
        panel.CustomMinimumSize = new Vector2(HudMetrics.VitalsMin, 0);
        _layout.BottomLeft.AddChild(panel);
        _vitalsPanel = panel;

        // Bottom-up: the bars are the last rows, so they sit on the HUD's bottom edge and never move.
        // What comes and goes (status chips, the spell, an XP pop) stacks above them and grows upward.
        // Groups sit SpaceSm apart and the bars within their group SpaceXs apart.
        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", UiTheme.SpaceSm);
        panel.AddChild(col);

        // A flow row, not a box: six status chips in a box are wider than the whole group and stretch it.
        // Hidden while empty, so an empty row does not hold a gap open above the bars.
        _statusRow = new HFlowContainer { Visible = false };
        _statusRow.AddThemeConstantOverride("h_separation", UiTheme.ChipGap);
        _statusRow.AddThemeConstantOverride("v_separation", UiTheme.SpaceXs);
        col.AddChild(_statusRow);

        _controlRow = new HFlowContainer { Visible = false };
        _controlRow.AddThemeConstantOverride("h_separation", UiTheme.ChipGap);
        _controlRow.AddThemeConstantOverride("v_separation", UiTheme.SpaceXs);
        col.AddChild(_controlRow);

        // Prepared spell: its key, its name in the school's colour, what it costs, the state readout,
        // and a thin recovery bar that fills while the spell cools down (hidden when ready).
        // The glyph resolves from the InputMap like the interaction prompt's does, so a rebind or a
        // pad flip keeps it honest (§44, §45), and affordability has a number to be shown with (§12).
        // Hidden with nothing prepared, so an empty group does not hold a gap open in the column.
        VBoxContainer spell = _spellGroup = new VBoxContainer { Visible = false };
        spell.AddThemeConstantOverride("separation", UiTheme.SpaceXs);
        col.AddChild(spell);

        _spellRow = new HBoxContainer { Visible = false };
        _spellRow.AddThemeConstantOverride("separation", UiTheme.SpaceSm);

        _spellGlyph = new CenterContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        _spellRow.AddChild(_spellGlyph);

        _spellName = UiTheme.HudInk(UiTheme.Body(""));
        _spellName.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _spellName.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        _spellName.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        _spellRow.AddChild(_spellName);

        _spellCost = UiTheme.HudInk(UiTheme.Caption("", UiTheme.Mana));
        _spellCost.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        _spellRow.AddChild(_spellCost);

        _spellState = UiTheme.HudInk(UiTheme.Caption(""));
        _spellState.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        _spellRow.AddChild(_spellState);
        spell.AddChild(_spellRow);

        _cooldownBar = UiTheme.Bar(UiTheme.Dim, 0f);
        _cooldownBar.CustomMinimumSize = new Vector2(0f, HudCoreMetrics.BarThinHeight);
        _cooldownBar.Visible = false;
        spell.AddChild(_cooldownBar);

        // Charge/channel meter (29.5G): fills while a charged cast is held, pinned full while
        // channeling, hidden otherwise. Modulated to the active spell's school colour.
        _castBar = UiTheme.Bar(UiTheme.ArcaneSilver, 0f);
        _castBar.CustomMinimumSize = new Vector2(0f, HudCoreMetrics.BarThinHeight);
        _castBar.Visible = false;
        spell.AddChild(_castBar);

        // The level and the XP it just gained, on one line.
        var level = new HBoxContainer();
        level.AddThemeConstantOverride("separation", UiTheme.SpaceSm);
        _footer = UiTheme.HudInk(UiTheme.Body("", UiTheme.Dim));
        level.AddChild(_footer);
        _xpPop = UiTheme.HudInk(UiTheme.Caption("", UiTheme.Accent));
        _xpPop.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        _xpPop.Visible = false;
        level.AddChild(_xpPop);
        col.AddChild(level);

        var bars = new VBoxContainer();
        bars.AddThemeConstantOverride("separation", UiTheme.SpaceXs);
        col.AddChild(bars);
        BuildCorruptionRow(bars);
        (_hpBar, _hpText, _hpIcon) = AddVital(bars, UiIcon.Kind.Health, UiTheme.Health, primary: true);
        (_staBar, _staText, _staIcon) = AddVital(bars, UiIcon.Kind.Stamina, UiTheme.Stamina, primary: false);
        (_mpBar, _mpText, _) = AddVital(bars, UiIcon.Kind.Mana, UiTheme.Mana, primary: false);

        // Where "low" and "critical" begin, marked on the bar itself: the player can see the line
        // coming instead of learning where it was from the colour change.
        _hpBar.SetTicks(VitalsRules.CriticalHealth, VitalsRules.LowHealth);
    }

    /// <summary>
    /// The corruption gauge, above the three vitals and absent until the player carries any.
    ///
    /// Violet means corruption everywhere in this UI, which is exactly why it cannot be the only thing
    /// saying so here: the fill is hatched, the row is led by a mark no other row has, the tier is a
    /// word, and the notches are the tier boundaries, read from <see cref="CorruptionTiers"/> so the
    /// bar cannot disagree with the rule.
    /// </summary>
    private void BuildCorruptionRow(VBoxContainer bars)
    {
        _corruptionRow = new HBoxContainer { Visible = false };
        _corruptionRow.AddThemeConstantOverride("separation", UiTheme.SpaceSm);

        var mark = new Control
        {
            CustomMinimumSize = new Vector2(HudCoreMetrics.IconSize, HudCoreMetrics.IconMinor),
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        mark.Draw += () => DrawCorruptionMark(mark);
        _corruptionRow.AddChild(mark);

        _corruptionBar = JuicedBar.Create(UiTheme.Corruption, 0f);
        _corruptionBar.Keylined = true;
        _corruptionBar.Hatched = true;
        _corruptionBar.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _corruptionBar.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        _corruptionBar.CustomMinimumSize = new Vector2(0f, HudCoreMetrics.BarMinorHeight);
        _corruptionBar.Snap(0d);

        var boundaries = new System.Collections.Generic.List<float>();
        for (int value = CorruptionTiers.Min + 1; value <= CorruptionTiers.Max; value++)
        {
            if (CorruptionTiers.Of(value) != CorruptionTiers.Of(value - 1))
            {
                boundaries.Add(value / (float)CorruptionTiers.Max);
            }
        }

        _corruptionBar.SetTicks(boundaries.ToArray());
        _corruptionRow.AddChild(_corruptionBar);

        _corruptionText = UiTheme.HudInk(UiTheme.Caption("", UiTheme.CorruptionText));
        _corruptionText.CustomMinimumSize = new Vector2(ReadingWidth(), 0f);
        _corruptionText.HorizontalAlignment = HorizontalAlignment.Right;
        _corruptionText.VerticalAlignment = VerticalAlignment.Center;
        _corruptionText.ClipText = true;
        _corruptionRow.AddChild(_corruptionText);

        bars.AddChild(_corruptionRow);
    }

    /// <summary>A hollow diamond with a struck centre: the corruption row's own mark.</summary>
    private static void DrawCorruptionMark(Control mark)
    {
        Vector2 centre = mark.Size / 2f;
        float r = (Mathf.Min(mark.Size.X, mark.Size.Y) / 2f) - 1f;
        var points = new[]
        {
            centre + new Vector2(0f, -r), centre + new Vector2(r, 0f),
            centre + new Vector2(0f, r), centre + new Vector2(-r, 0f), centre + new Vector2(0f, -r),
        };
        mark.DrawPolyline(points, UiTheme.Keyline, 4f);
        mark.DrawPolyline(points, UiTheme.CorruptionText, 2f);
        mark.DrawLine(centre + new Vector2(-r / 2f, r / 2f), centre + new Vector2(r / 2f, -r / 2f), UiTheme.CorruptionText, 2f);
    }

    /// <summary>The width every reading on the right of the vitals takes, so the bars all end on one line.</summary>
    private static float ReadingWidth() => HudCoreMetrics.ReadingWidth(UiTheme.FontSize(UiTheme.BodyFontSize));

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
        UiTheme.HudInk(_levelUp);
        _levelUp.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _layout.Overlay.AddChild(_levelUp);

        // The last widget built, so everything FitWidgetsToLayout reaches exists by now.
        _layout.Scaled.Resized += FitWidgetsToLayout;
        FitWidgetsToLayout();
    }

    /// <summary>
    /// Hands the layout width to the widgets that size themselves from it: the compass strip, the
    /// minimap and the party strip. The vitals and the tracker are sized by <c>ApplyMetrics</c>; these
    /// three are their own nodes, and are told on the same signal, so a window resize, a HUD scale or a
    /// safe zone moves all five on one frame and none of them polls for it.
    /// </summary>
    private void FitWidgetsToLayout()
    {
        float width = LayoutWidth;
        if (width <= 0f)
        {
            return; // not laid out yet; Resized calls back when it is
        }

        _compass?.FitToLayout(width);
        _minimap?.FitToLayout(width);
        _party?.FitToLayout(width);
        LayoutFitted?.Invoke();
    }

    /// <summary>Raised when the layout width has changed and the widgets above have been refitted,
    /// for a widget docked into the HUD from outside it (the hotbar), which sizes its cells from
    /// <see cref="LayoutWidth"/> too.</summary>
    public event System.Action? LayoutFitted;

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
        MarkChanged(HudElement.Vitals);
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

    private void UpdateVitals(double delta)
    {
        if (_player is not Node node || !IsInstanceValid(node) ||
            !_player.TryGetComponent(out StatsComponent stats))
        {
            return;
        }

        // A new player, a load or a settings change: the bars take the new value at once. A chunk
        // sliding off a bar because the save had less health than the last one did is not a hit.
        if (_vitalsSnap)
        {
            _vitalsSnap = false;
            _hpBar.Snap(stats.GetNormalized(StatType.Health));
            _staBar.Snap(stats.GetNormalized(StatType.Stamina));
            _mpBar.Snap(stats.GetNormalized(StatType.Mana));
        }

        SetVital(_hpBar, _hpText, stats, StatType.Health, ref _hpShown);
        SetVital(_staBar, _staText, stats, StatType.Stamina, ref _staShown);

        // The notch on the stamina bar is where being winded ends.
        float recover = stats.WindedRecoverFraction;
        if (recover != _staTickShown)
        {
            _staTickShown = recover;
            _staBar.SetTicks(recover);
        }

        // Winded (stamina hit zero; dodge and sprint locked until it refills - StatsComponent.IsWinded): the
        // bar dims, the reading turns Bad and the row's mark becomes a padlock, so the state is a shape as
        // well as a colour and needs no motion.
        int winded = stats.IsWinded ? 1 : 0;
        if (winded != _windedShown)
        {
            _windedShown = winded;
            _staBar.SelfModulate = winded == 1 ? new Color(1f, 1f, 1f, 0.45f) : Colors.White;
            _staIcon.Texture = UiIcon.Texture(winded == 1 ? UiIcon.Kind.Lock : UiIcon.Kind.Stamina);
            UiLive.FontColor(_staText, winded == 1 ? UiTheme.Bad : UiTheme.Text);
        }

        SetVital(_mpBar, _mpText, stats, StatType.Mana, ref _mpShown);
        UpdateCriticalHealth(stats, delta);
        UpdateCorruption();

        const int NoLevel = int.MinValue + 1;
        int level = _player.TryGetComponent(out ProgressionComponent prog) ? prog.Level : NoLevel;
        if (level != _levelShown)
        {
            _levelShown = level;
            _footer.Text = level != NoLevel ? Loc.TF("hud.level", level) : string.Empty;
            NoteVitalsChanged();
        }

        UpdateSpellWidget(stats);
        UpdateStatusChips();
        UpdateControlChips();
        _vitalsQuiet = false;
    }

    /// <summary>Brings a Dynamic vitals group up for something other than a bar moving (the bars are
    /// covered by the below-max signal): a level, a status, the prepared spell, corruption.</summary>
    private void NoteVitalsChanged()
    {
        if (!_vitalsQuiet)
        {
            MarkChanged(HudElement.Vitals);
        }
    }

    private void UpdateCorruption()
    {
        int value = _player!.GetComponent<CorruptionComponent>()?.Value ?? 0;
        if (value == _corruptionShown)
        {
            return;
        }

        bool first = _corruptionShown == int.MinValue;
        _corruptionShown = value;
        _corruptionRow.Visible = value > CorruptionTiers.Min;
        double fraction = value / (double)CorruptionTiers.Max;
        if (first)
        {
            _corruptionBar.Snap(fraction);
        }
        else
        {
            _corruptionBar.SetTarget(fraction);
        }

        int tier = (int)CorruptionTiers.Of(value);
        if (tier != _corruptionTierShown)
        {
            _corruptionTierShown = tier;
            _corruptionText.Text = CorruptionTiers.DisplayName((CorruptionTier)tier);
        }

        NoteVitalsChanged();
    }

    /// <summary>
    /// The low- and critical-health treatment (§5).
    ///
    /// ⚠️ <b>There was none.</b> A health bar at 5% looked exactly like a health bar at 95%, only
    /// shorter - the single most important state in the game had no presentation at all, which is the
    /// clearest example of the brief's "already built" not meaning "finished".
    ///
    /// Restrained on purpose, per §5's "do NOT permanently flash the entire screen": the bar breathes
    /// and the reading heats toward ember orange. **The colour is never alone** (§40): the row's heart
    /// becomes a warning mark, and the two notches on the bar show where low and critical begin.
    /// Reduced motion drops the breath and keeps the rest, because the motion is only the emphasis.
    /// </summary>
    private void UpdateCriticalHealth(StatsComponent stats, double delta)
    {
        int state = VitalsRules.HealthBand(stats.GetNormalized(StatType.Health));
        if (state != _healthStateShown)
        {
            _healthStateShown = state;
            _hpIcon.Texture = UiIcon.Texture(state == 0 ? UiIcon.Kind.Health : UiIcon.Kind.Warning);
            UiLive.FontColor(_hpText, state == 0 ? UiTheme.Text : state == 2 ? UiTheme.AccentHot : UiTheme.Accent);
        }

        if (state == 0 || !UiTheme.MotionEnabled)
        {
            if (state == 0)
            {
                _criticalPhase = 0f;
            }

            // Back to plain once, not every frame: the modulate only moves while the bar breathes.
            if (_hpPulsed)
            {
                _hpPulsed = false;
                _hpBar.SelfModulate = Colors.White;
            }

            return;
        }

        // Faster and deeper the worse it gets, so "bad" and "very bad" are distinguishable without
        // reading anything.
        bool critical = state == 2;
        _criticalPhase += (float)delta * (critical ? 6.4f : 3.4f);
        float swing = critical ? 0.32f : 0.16f;
        float pulse = 1f - (swing * 0.5f * (1f - Mathf.Cos(_criticalPhase)));
        _hpBar.SelfModulate = new Color(1f, pulse, pulse);
        _hpPulsed = true;
    }

    /// <summary>The prepared-spell widget (30.5C): school-tinted name, state readout, and the
    /// cooldown recovery bar (visible only while cooling down).</summary>
    private void UpdateSpellWidget(StatsComponent stats)
    {
        bool casting = false;
        float costTick = -1f;
        if (_player!.TryGetComponent(out SpellcastingComponent spells) && spells.Selected is { } spell)
        {
            Color tint = SpellSchools.Color(spell.School);
            if (!ReferenceEquals(spell, _spellShown))
            {
                _spellShown = spell;
                _spellName.Text = spell.DisplayName;

                // Font colour, not Modulate: modulate would tint the ink round the letters as well.
                UiLive.FontColor(_spellName, UiTheme.SchoolColor(spell.School));
                InvalidateSpellShown();
                NoteVitalsChanged();
            }

            if (_spellGlyphStale || _spellGlyph.GetChildCount() == 0)
            {
                _spellGlyphStale = false;
                SetGlyph(_spellGlyph, GameInput.Cast);
            }

            float cd = spells.CooldownOf(spell);

            // The cost the cast will actually charge: the region's Weave bends it (corrupted spells get
            // cheaper as the Weave fades, ordinary ones dearer) and the caster's perks shave it, so
            // showing the sheet cost would lie.
            float cost = spells.EffectiveManaCost(spell);

            // ⚠️ Affordability is ASKED, not decided (§48). The HUD compares against the live mana
            // reading purely to colour the number; whether the cast is allowed remains
            // SpellcastingComponent's call, and this never gates anything.
            float mana = stats.GetCurrent(StatType.Mana);
            bool affordable = mana >= cost;

            // The notch on the mana bar is what this spell costs: below it, the cast is refused.
            float maxMana = stats.GetMax(StatType.Mana);
            costTick = maxMana > 0f && cost > 0f && cost < maxMana
                ? Mathf.Round(cost / maxMana * 200f) / 200f
                : -1f;
            bool silenced = _player.GetComponent<StatusEffectsComponent>() is { IsSilenced: true };
            _spellCost.Visible = cost > 0f || spell.HealthCost > 0f;

            // The reading is whole mana, so it is reformatted when the whole number moves.
            float costShown = System.MathF.Round(cost, System.MidpointRounding.AwayFromZero);
            if (costShown != _spellCostShown || spell.HealthCost != _spellHealthCostShown)
            {
                _spellCostShown = costShown;
                _spellHealthCostShown = spell.HealthCost;
                _spellCost.Text = spell.HealthCost > 0f
                    ? $"{cost:0} + {Loc.TF("magic.book.hud_health_cost", spell.HealthCost.ToString("0"))}"
                    : $"{cost:0}";
            }

            int afford = affordable ? 1 : 0;
            if (afford != _spellAffordShown)
            {
                _spellAffordShown = afford;
                UiLive.FontColor(_spellCost, affordable ? UiTheme.Mana : UiTheme.Bad);
            }

            // Which line the state readout is on, and the one number two of them carry. The text is
            // rebuilt only when either moves: a tenth of a second of cooldown, a whole point of mana.
            int state = silenced ? 0
                : spells.PendingSpell != null ? 1
                : spells.IsCharging ? 2
                : spells.IsChanneling ? 3
                : cd > 0f ? 4
                : !affordable ? 5
                : 6;
            float stateValue = state == 4 ? System.MathF.Round(cd * 10f, System.MidpointRounding.AwayFromZero)
                : state == 5 ? Mathf.Ceil(cost - mana)
                : 0f;
            if (state != _spellStateShown || stateValue != _spellStateValueShown)
            {
                _spellStateShown = state;
                _spellStateValueShown = stateValue;
                _spellState.Text = state switch
                {
                    0 => Loc.T("magic.book.hud_silenced"),
                    1 => Loc.T("hud.casting"),
                    2 => Loc.T("hud.charging"),
                    3 => Loc.T("hud.channeling"),
                    4 => $"{cd:0.0}s",
                    5 => Loc.TF("magic.book.hud_mana_short", Mathf.Ceil(cost - mana).ToString("0")),
                    _ => Loc.T("hud.ready"),
                };
            }

            // Font colour, not Modulate — modulating multiplies the caption's own Dim down
            // below readable contrast (30.5K audit).
            int stateColor = silenced || !affordable ? 0 : cd > 0f ? 1 : 2;
            if (stateColor != _spellStateColorShown)
            {
                _spellStateColorShown = stateColor;
                UiLive.FontColor(
                    _spellState, stateColor == 0 ? UiTheme.Bad : stateColor == 1 ? UiTheme.Dim : UiTheme.Accent);
            }

            _spellRow.Visible = true;

            // The recovery bar runs off the cooldown the cast actually set, which mastery shortens.
            float total = spell.Cooldown
                * (_player.GetComponent<SchoolMasteryComponent>()?.CooldownMultiplier(spell.School) ?? 1f);
            bool coolingDown = cd > 0f && total > 0f;
            _cooldownBar.Visible = coolingDown;
            if (coolingDown)
            {
                _cooldownBar.Value = Mathf.Clamp(1d - (cd / total), 0d, 1d);
                _cooldownBar.Modulate = tint;
            }

            casting = spells.PendingSpell != null || spells.IsCharging || spells.IsChanneling;
            if (casting)
            {
                _castBar.Value = spells.PendingSpell != null ? spells.WindupProgress
                    : spells.IsCharging ? spells.ChargeProgress : 1d;
                _castBar.Modulate = tint;
            }
        }
        else
        {
            if (_spellRow.Visible)
            {
                NoteVitalsChanged();
            }

            _spellShown = null;
            _spellRow.Visible = false;
            _cooldownBar.Visible = false;
        }

        _castBar.Visible = casting;
        _spellGroup.Visible = _spellRow.Visible;

        if (costTick != _mpTickShown)
        {
            _mpTickShown = costTick;
            if (costTick > 0f)
            {
                _mpBar.SetTicks(costTick);
            }
            else
            {
                _mpBar.SetTicks();
            }
        }
    }

    /// <summary>The status-effect chip row (30.5C): rebuilt only when the active set changes;
    /// per-chip countdowns update in place as their shown tenth of a second moves.</summary>
    private void UpdateStatusChips()
    {
        StatusEffectsComponent? effects = _player!.GetComponent<StatusEffectsComponent>();
        int count = effects?.ActiveEffects.Count ?? 0;

        // The common frame has no effect at all: nothing to compare, nothing to walk.
        if (count == 0 && _statusIds.Count == 0 && !_statusStale)
        {
            return;
        }

        // The active set, compared id by id against the one the chips were built from. This is the
        // same question the old joined-id signature asked, without building a string to ask it.
        bool changed = _statusStale || count != _statusIds.Count;
        if (!changed)
        {
            int index = 0;
            foreach (StatusEffect effect in effects!.ActiveEffects)
            {
                if (_statusIds[index++] != effect.Definition.Id)
                {
                    changed = true;
                    break;
                }
            }
        }

        if (changed)
        {
            _statusStale = false;
            _statusIds.Clear();
            if (effects != null)
            {
                foreach (StatusEffect effect in effects.ActiveEffects)
                {
                    _statusIds.Add(effect.Definition.Id);
                }
            }

            RebuildStatusChips(effects);
            _statusRow.Visible = count > 0;
            NoteVitalsChanged();
        }

        foreach (StatusChip chip in _statusChips)
        {
            // One decimal is shown, so the text moves ten times a second, not sixty.
            int stacks = chip.Effect.Stacks;
            float tenths = (float)System.Math.Round(chip.Effect.Remaining * 10d, System.MidpointRounding.AwayFromZero);
            if (stacks == chip.Stacks && tenths == chip.Tenths)
            {
                continue;
            }

            chip.Stacks = stacks;
            chip.Tenths = tenths;
            chip.Time.Text = stacks > 1
                ? $"x{stacks} {chip.Effect.Remaining:0.0}s"
                : $"{chip.Effect.Remaining:0.0}s";
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

        int shown = 0;
        foreach (StatusEffect effect in effects.ActiveEffects)
        {
            if (shown++ >= MaxStatusChips)
            {
                _statusRow.AddChild(UiTheme.Chip(
                    $"+{effects.ActiveEffects.Count - MaxStatusChips}", UiTheme.Dim));
                break;
            }

            // Buffs read as dead-green, afflictions in their school's colour.
            Color tint = effect.Definition.IsBeneficial ? UiTheme.Good : SpellSchools.Color(effect.Definition.School);

            // A Chip, not a Panel (37.5B). These were full framed panels, which after 37.5A gave
            // every status effect a 2 px brass rule and its own grain ShaderMaterial — a five-chip
            // row was five framed screens' worth of chrome for five words of text.
            PanelContainer chip = UiTheme.Chip(SpellText.Name(effect.Definition), tint, out Label time);
            time.Visible = true;
            chip.TooltipText = SpellText.Description(effect.Definition);
            chip.MouseFilter = Control.MouseFilterEnum.Pass;
            _statusChips.Add(new StatusChip { Effect = effect, Time = time });
            _statusRow.AddChild(chip);
        }
    }

    /// <summary>The state chips: what the player cannot currently do (silenced, rooted, stunned) and the
    /// region's Weave while it is not strong. Rebuilt only when what they say changes.</summary>
    private void UpdateControlChips()
    {
        StatusEffectsComponent? effects = _player!.GetComponent<StatusEffectsComponent>();
        bool silenced = effects is { IsSilenced: true };
        bool rooted = effects is { IsRooted: true };
        bool stunned = effects is { IsStunned: true };
        bool weave = WeaveMath.ShowsIndicator(Weave.Potency);

        // The three flags and the Weave's whole percentage, packed: the same key the old
        // interpolated signature held, without allocating a string per frame to compare it.
        int key = (silenced ? 1 : 0) | (rooted ? 2 : 0) | (stunned ? 4 : 0) |
                  ((weave ? (int)Mathf.Round(Weave.Potency * 100f) : -1) << 3);
        if (key == _controlKey)
        {
            return;
        }

        _controlKey = key;
        UiTheme.ClearChildren(_controlRow);
        _controlRow.Visible = weave || silenced || rooted || stunned;
        NoteVitalsChanged();

        if (weave)
        {
            _controlRow.AddChild(UiTheme.Chip(SpellbookPanel.WeaveChipText(), SpellbookPanel.WeaveTint(Weave.Band)));
        }

        if (silenced)
        {
            _controlRow.AddChild(UiTheme.Chip(Loc.T("magic.book.hud_silenced"), UiTheme.Bad));
        }

        if (rooted)
        {
            _controlRow.AddChild(UiTheme.Chip(Loc.T("magic.book.hud_rooted"), UiTheme.Bad));
        }

        if (stunned)
        {
            _controlRow.AddChild(UiTheme.Chip(Loc.T("magic.book.hud_stunned"), UiTheme.Bad));
        }
    }

    /// <summary>
    /// One resource row: its mark, its bar, its reading.
    ///
    /// ⚠️ <b>The three used to be pixel-identical, and that was the §3 failure.</b> Health, mana and
    /// endurance sat in three bars with the same label and the same type, so the group read as a table
    /// of numbers and the player had to *read* the row labels to find their health, at exactly the
    /// moment they have no attention to spare. Health is the tallest bar and the largest number; the
    /// other two are subordinate. The "HP / STA / MP" captions are gone: the mark already names the
    /// row by shape, and the width they took is bar now.
    ///
    /// Shape, position and size carry the distinction as well as colour, so the group survives
    /// <see cref="ColorVision"/> (§40). Every row has the same mark column and the same reading
    /// column, so the three bars start and end on the same two lines.
    /// </summary>
    private static (JuicedBar Bar, Label Value, TextureRect Icon) AddVital(
        VBoxContainer col, UiIcon.Kind icon, Color fill, bool primary)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", UiTheme.SpaceSm);

        float markSize = primary ? HudCoreMetrics.IconSize : HudCoreMetrics.IconMinor;
        TextureRect mark = UiIcon.Create(icon, markSize, fill);
        mark.CustomMinimumSize = new Vector2(HudCoreMetrics.IconSize, markSize);
        mark.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        row.AddChild(mark);

        JuicedBar bar = JuicedBar.Create(fill, 0f);
        bar.Keylined = true;
        bar.LagChunk = true;
        bar.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        bar.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        bar.CustomMinimumSize = new Vector2(
            0f, primary ? HudCoreMetrics.BarHeight : HudCoreMetrics.BarMinorHeight);
        row.AddChild(bar);

        // The reading, in Inter, in a column of fixed width and right-aligned: a value that moves
        // while you watch it is one you have to re-find every time.
        Label value = UiTheme.HudInk(primary ? UiTheme.Body("", UiTheme.Text) : UiTheme.Caption("", UiTheme.Text));
        value.CustomMinimumSize = new Vector2(ReadingWidth(), 0);
        value.HorizontalAlignment = HorizontalAlignment.Right;
        value.VerticalAlignment = VerticalAlignment.Center;
        row.AddChild(value);

        col.AddChild(row);
        return (bar, value, mark);
    }

    private static void SetVital(
        JuicedBar bar, Label value, StatsComponent stats, StatType type, ref VitalShown shown)
    {
        bar.SetTarget(stats.GetNormalized(type));

        // The reading is two whole numbers. Regeneration moves the stat every frame and the text
        // perhaps once a second, so the string is built only when a whole number changes.
        float current = stats.GetCurrent(type);
        float max = stats.GetMax(type);
        float currentShown = System.MathF.Round(current, System.MidpointRounding.AwayFromZero);
        float maxShown = System.MathF.Round(max, System.MidpointRounding.AwayFromZero);
        if (currentShown == shown.Current && maxShown == shown.Max)
        {
            return;
        }

        shown.Current = currentShown;
        shown.Max = maxShown;
        value.Text = $"{current:0}/{max:0}";
    }
}
