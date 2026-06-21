using System.Text;
using Embervale.Combat;
using Embervale.Core;
using Embervale.Core.Events;
using Embervale.Entities;
using Embervale.Magic;
using Embervale.Progression;
using Embervale.Quests;
using Embervale.Stats;
using Embervale.World;
using Godot;

namespace Embervale.UI;

/// <summary>
/// On-screen diagnostics overlay. It keeps the otherwise-invisible core systems (game
/// state, stats, combat, time/weather) observable while running — a stand-in until the
/// full game UI arrives. Built entirely in code through <see cref="UiTheme"/> so it
/// stays consistent with the other panels: a framed vitals panel (with live coloured
/// resource bars) top-left, a target panel beneath it, a controls hint bottom-left, and
/// a screen-centre crosshair.
/// </summary>
public partial class DebugHud : CanvasLayer
{
    private IEntity? _target;
    private IEntity? _player;
    private WorldClock? _clock;
    private WeatherDirector? _weather;
    private string _lastHit = "—";

    private Label _diag = null!;
    private Label _info = null!;
    private ProgressBar _hpBar = null!;
    private ProgressBar _staBar = null!;
    private ProgressBar _mpBar = null!;
    private Label _hpText = null!;
    private Label _staText = null!;
    private Label _mpText = null!;

    private PanelContainer _targetPanel = null!;
    private Label _targetTitle = null!;
    private ProgressBar _targetHpBar = null!;
    private Label _targetHpText = null!;
    private Label _targetInfo = null!;

    public override void _Ready()
    {
        AddChild(new Crosshair());
        BuildVitalsPanel();
        BuildTargetPanel();
        BuildControlsHint();

        EventBus.Instance?.Subscribe<DamageDealtEvent>(OnDamageDealt);
    }

    public override void _ExitTree()
    {
        EventBus.Instance?.Unsubscribe<DamageDealtEvent>(OnDamageDealt);
    }

    public void SetTarget(IEntity? target) => _target = target;

    public void SetPlayer(IEntity? player) => _player = player;

    public void SetClock(WorldClock? clock) => _clock = clock;

    public void SetWeather(WeatherDirector? weather) => _weather = weather;

    // --- Construction -------------------------------------------------------

    private void BuildVitalsPanel()
    {
        PanelContainer panel = Ignore(UiTheme.Panel());
        panel.Position = new Vector2(16, 16);
        panel.CustomMinimumSize = new Vector2(320, 0);
        AddChild(panel);

        MarginContainer pad = UiTheme.Padding();
        panel.AddChild(pad);

        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 5);
        pad.AddChild(col);

        col.AddChild(UiTheme.Header("EMBERVALE — Sandbox"));
        _diag = UiTheme.Body("", UiTheme.Dim);
        col.AddChild(_diag);

        col.AddChild(new HSeparator());

        (_hpBar, _hpText) = AddVital(col, "HP", UiTheme.Health);
        (_staBar, _staText) = AddVital(col, "STA", UiTheme.Stamina);
        (_mpBar, _mpText) = AddVital(col, "MP", UiTheme.Mana);

        _info = UiTheme.Body("");
        col.AddChild(_info);
    }

    private void BuildTargetPanel()
    {
        _targetPanel = Ignore(UiTheme.Panel());
        _targetPanel.Visible = false;
        _targetPanel.Position = new Vector2(16, 250);
        _targetPanel.CustomMinimumSize = new Vector2(320, 0);
        AddChild(_targetPanel);

        MarginContainer pad = UiTheme.Padding();
        _targetPanel.AddChild(pad);

        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 5);
        pad.AddChild(col);

        _targetTitle = UiTheme.Header("Target");
        col.AddChild(_targetTitle);
        (_targetHpBar, _targetHpText) = AddVital(col, "HP", UiTheme.Health);
        _targetInfo = UiTheme.Body("", UiTheme.Dim);
        col.AddChild(_targetInfo);
    }

    private void BuildControlsHint()
    {
        PanelContainer panel = Ignore(UiTheme.Panel());
        panel.SetAnchorsPreset(Control.LayoutPreset.BottomLeft);
        panel.Position = new Vector2(16, -96);
        panel.GrowVertical = Control.GrowDirection.Begin;
        AddChild(panel);

        MarginContainer pad = UiTheme.Padding(8);
        panel.AddChild(pad);

        Label hint = UiTheme.Body(
            "WASD move · Mouse look · LMB attack · RMB block · Q cast · F cycle spell\n" +
            "E interact · I character · J journal · [H] heal · [R] respawn · [X] +XP\n" +
            "[F5/F9] save/load · [Esc] pause",
            UiTheme.Dim);
        pad.AddChild(hint);
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
        value.CustomMinimumSize = new Vector2(70, 0);
        value.HorizontalAlignment = HorizontalAlignment.Right;
        row.AddChild(value);

        col.AddChild(row);
        return (bar, value);
    }

    // --- Per-frame update ---------------------------------------------------

    public override void _Process(double delta)
    {
        UpdateDiagnostics();
        UpdatePlayer();
        UpdateTarget();
    }

    private void UpdateDiagnostics()
    {
        var sb = new StringBuilder();
        sb.Append($"FPS {Engine.GetFramesPerSecond()}    {GameManager.Instance?.State.ToString() ?? "?"}");

        if (_clock is { } clock && IsInstanceValid(clock))
        {
            sb.Append($"\n{clock.Clock()}  ({DayPhases.Label(clock.Phase)})");
            if (_weather is { } weather && IsInstanceValid(weather) && weather.Current is { } w)
            {
                sb.Append($"   ·   {w.DisplayName}");
            }
        }

        _diag.Text = sb.ToString();
    }

    private void UpdatePlayer()
    {
        if (_player is not Node node || !IsInstanceValid(node) ||
            !_player.TryGetComponent(out StatsComponent stats))
        {
            return;
        }

        SetVital(_hpBar, _hpText, stats, StatType.Health);
        SetVital(_staBar, _staText, stats, StatType.Stamina);
        SetVital(_mpBar, _mpText, stats, StatType.Mana);

        var sb = new StringBuilder();
        if (_player.TryGetComponent(out ProgressionComponent prog))
        {
            string xp = prog.IsMaxLevel ? "MAX" : $"{prog.CurrentXp}/{prog.XpToNext}";
            sb.Append($"Level {prog.Level}   XP {xp}   SP {prog.SkillPoints}\n");
        }

        if (_player.TryGetComponent(out SpellcastingComponent spells))
        {
            AppendSpell(sb, spells, stats);
        }

        if (_player.TryGetComponent(out StatusEffectsComponent effects))
        {
            AppendEffects(sb, effects);
        }

        if (_player.TryGetComponent(out QuestLogComponent quests))
        {
            AppendQuestTracker(sb, quests);
        }

        sb.Append($"Last hit: {_lastHit}");
        _info.Text = sb.ToString();
    }

    private void UpdateTarget()
    {
        if (_target is not Node node || !IsInstanceValid(node) ||
            !_target.TryGetComponent(out StatsComponent stats))
        {
            _targetPanel.Visible = false;
            return;
        }

        _targetPanel.Visible = true;
        _targetTitle.Text = $"{_target.DisplayName}  (#{_target.RuntimeId})";
        SetVital(_targetHpBar, _targetHpText, stats, StatType.Health);

        var sb = new StringBuilder();
        sb.Append($"PWR {stats.GetValue(StatType.PhysicalPower):0}   ARM {stats.GetValue(StatType.Armor):0}   ");
        sb.Append(stats.IsAlive ? "ALIVE" : "DEAD");

        if (_target.TryGetComponent(out StatusEffectsComponent effects))
        {
            AppendEffects(sb, effects);
        }

        _targetInfo.Text = sb.ToString();
    }

    private static void SetVital(ProgressBar bar, Label value, StatsComponent stats, StatType type)
    {
        bar.Value = stats.GetNormalized(type);
        value.Text = $"{stats.GetCurrent(type):0}/{stats.GetMax(type):0}";
    }

    private void OnDamageDealt(DamageDealtEvent e)
    {
        string tags = e.IsCrit ? " CRIT!" : e.IsBlocked ? " (blocked)" : string.Empty;
        _lastHit = $"{e.Amount:0} {e.Type} to {e.Target.DisplayName}{tags}";
    }

    // --- Text builders ------------------------------------------------------

    private static void AppendQuestTracker(StringBuilder sb, QuestLogComponent log)
    {
        foreach (QuestProgress progress in log.Quests)
        {
            if (progress.Status != QuestStatus.Active)
            {
                continue;
            }

            sb.Append($"Quest: {progress.Quest.Title}\n");
            var objectives = progress.Quest.ObjectiveList();
            for (int i = 0; i < objectives.Count; i++)
            {
                sb.Append($"  {objectives[i].ShortLabel()} {progress.Counts[i]}/{objectives[i].RequiredCount}\n");
            }

            return; // Track only the first active quest in the HUD.
        }
    }

    private static void AppendSpell(StringBuilder sb, SpellcastingComponent spells, StatsComponent stats)
    {
        SpellResource? spell = spells.Selected;
        if (spell == null)
        {
            return;
        }

        float cooldown = spells.CooldownOf(spell);
        string state = cooldown > 0f
            ? $"CD {cooldown:0.0}s"
            : stats.GetCurrent(StatType.Mana) >= spell.ManaCost ? "READY" : "no mana";
        sb.Append($"Spell: {spell.DisplayName} ({spell.ManaCost:0} MP) — {state}\n");
    }

    private static void AppendEffects(StringBuilder sb, StatusEffectsComponent effects)
    {
        if (effects.ActiveEffects.Count == 0)
        {
            return;
        }

        sb.Append("\nEffects:");
        foreach (StatusEffect effect in effects.ActiveEffects)
        {
            sb.Append($" {effect.Definition.DisplayName} ({effect.Remaining:0.0}s)");
        }

        sb.Append('\n');
    }

    private static T Ignore<T>(T control)
        where T : Control
    {
        control.MouseFilter = Control.MouseFilterEnum.Ignore;
        return control;
    }
}
