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

/// <summary>The vitals card of <see cref="GameHud"/>: health, stamina and mana bars, the level line with
/// its XP pop and level-up flourish, the prepared spell, and the status and control chips.</summary>
public partial class GameHud
{
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
        // Cards, not Panels, for every HUD widget (37.5H).
        //
        // 37.5B named this trap and fixed the status chips, then left the five widgets around them
        // on `Panel()` — so the HUD carried five brass frames, five engraved shadows and **five
        // grain ShaderMaterials** simultaneously, which is more framing than the character screen
        // uses. The ornament budget says a HUD widget earns none of it; the boss frame is the sole
        // exception and it has its own class.
        PanelContainer panel = Ignore(UiTheme.Band());
        panel.CustomMinimumSize = new Vector2(286, 0);
        _layout.BottomLeft.AddChild(panel);

        // Groups (the three bars, the level line, the spell, the status chips) sit SpaceSm apart; the
        // bars within their group sit SpaceXs apart, so the card reads as clusters and not as one stack.
        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", UiTheme.SpaceSm);
        WrapPadded(panel, col);

        var bars = new VBoxContainer();
        bars.AddThemeConstantOverride("separation", UiTheme.SpaceXs);
        col.AddChild(bars);
        (_hpBar, _hpText) = AddVital(bars, UiIcon.Kind.Health, Loc.T("hud.hp"), UiTheme.Health, primary: true);
        (_staBar, _staText) = AddVital(bars, UiIcon.Kind.Stamina, Loc.T("hud.sta"), UiTheme.Stamina, primary: false);
        (_mpBar, _mpText) = AddVital(bars, UiIcon.Kind.Mana, Loc.T("hud.mp"), UiTheme.Mana, primary: false);

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

        _controlRow = new HFlowContainer { CustomMinimumSize = new Vector2(168f, 0f) };
        _controlRow.AddThemeConstantOverride("h_separation", UiTheme.ChipGap);
        _controlRow.AddThemeConstantOverride("v_separation", UiTheme.SpaceXs);
        col.AddChild(_controlRow);

        // A flow row, not a box: six status chips in a box are wider than the whole card and stretch it.
        _statusRow = new HFlowContainer { CustomMinimumSize = new Vector2(168f, 0f) };
        _statusRow.AddThemeConstantOverride("h_separation", UiTheme.ChipGap);
        _statusRow.AddThemeConstantOverride("v_separation", UiTheme.SpaceXs);
        col.AddChild(_statusRow);
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

    private void UpdateVitals(double delta)
    {
        if (_player is not Node node || !IsInstanceValid(node) ||
            !_player.TryGetComponent(out StatsComponent stats))
        {
            return;
        }

        SetVital(_hpBar, _hpText, stats, StatType.Health, ref _hpShown);
        SetVital(_staBar, _staText, stats, StatType.Stamina, ref _staShown);

        // Winded (stamina hit zero; dodge and sprint locked until it refills — StatsComponent.IsWinded): the
        // bar dims and the reading turns Bad, two channels as with low health, no motion needed.
        int winded = stats.IsWinded ? 1 : 0;
        if (winded != _windedShown)
        {
            _windedShown = winded;
            _staBar.SelfModulate = winded == 1 ? new Color(1f, 1f, 1f, 0.45f) : Colors.White;
            UiLive.FontColor(_staText, winded == 1 ? UiTheme.Bad : UiTheme.Text);
        }

        SetVital(_mpBar, _mpText, stats, StatType.Mana, ref _mpShown);
        UpdateCriticalHealth(stats, delta);

        const int NoLevel = int.MinValue + 1;
        int level = _player.TryGetComponent(out ProgressionComponent prog) ? prog.Level : NoLevel;
        if (level != _levelShown)
        {
            _levelShown = level;
            _footer.Text = level != NoLevel ? Loc.TF("hud.level", level) : string.Empty;
        }

        UpdateSpellWidget();
        UpdateStatusChips();
        UpdateControlChips();
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
        int state = fraction > LowHealth ? 0 : fraction <= CriticalHealth ? 2 : 1;
        if (state != _healthStateShown)
        {
            _healthStateShown = state;
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
    private void UpdateSpellWidget()
    {
        bool casting = false;
        if (_player!.TryGetComponent(out SpellcastingComponent spells) && spells.Selected is { } spell)
        {
            Color tint = SpellSchools.Color(spell.School);
            if (!ReferenceEquals(spell, _spellShown))
            {
                _spellShown = spell;
                _spellName.Text = spell.DisplayName;
                _spellName.Modulate = tint;
                InvalidateSpellShown();
            }

            float cd = spells.CooldownOf(spell);

            // The cost the cast will actually charge: the region's Weave bends it (corrupted spells get
            // cheaper as the Weave fades, ordinary ones dearer) and the caster's perks shave it, so
            // showing the sheet cost would lie.
            float cost = spells.EffectiveManaCost(spell);

            // ⚠️ Affordability is ASKED, not decided (§48). The HUD compares against the live mana
            // reading purely to colour the number; whether the cast is allowed remains
            // SpellcastingComponent's call, and this never gates anything.
            float mana = _player.GetComponent<StatsComponent>() is { } casterStats
                ? casterStats.GetCurrent(StatType.Mana)
                : float.MaxValue;
            bool affordable = mana >= cost;
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
            _spellShown = null;
            _spellRow.Visible = false;
            _cooldownBar.Visible = false;
        }

        _castBar.Visible = casting;
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
        VBoxContainer col, UiIcon.Kind icon, string caption, Color fill, bool primary)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", UiTheme.SpaceSm);

        row.AddChild(UiIcon.Create(icon, primary ? 20f : 17f, fill));

        Label cap = UiTheme.Caption(caption, primary ? UiTheme.Text : UiTheme.Dim);
        cap.CustomMinimumSize = new Vector2(34, 0);
        cap.VerticalAlignment = VerticalAlignment.Center;
        row.AddChild(cap);

        JuicedBar bar = JuicedBar.Create(fill);
        bar.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        bar.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        bar.CustomMinimumSize = new Vector2(168f, primary ? HealthBarHeight : MinorBarHeight);
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
