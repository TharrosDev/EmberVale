using System.Text;
using Embervale.Combat;
using Embervale.Core;
using Embervale.Core.Events;
using Embervale.Core.Services;
using Embervale.Corruption;
using Embervale.Entities;
using Embervale.Factions;
using Embervale.Magic;
using Embervale.Progression;
using Embervale.Quests;
using Embervale.Stats;
using Embervale.World;
using Godot;

namespace Embervale.UI;

/// <summary>
/// Developer diagnostics overlay, hidden by default and toggled with <c>F3</c>. Since the
/// Phase 18 game-UI overhaul, the on-screen game HUD is <see cref="GameHud"/>; this panel is
/// the deeper debug read-out (FPS, raw stats, target internals, the active world event) kept
/// for development. Built through <see cref="UiTheme"/> like the rest of the UI.
/// </summary>
public partial class DebugHud : CanvasLayer
{
    private IEntity? _target;
    private IEntity? _player;
    private WorldClock? _clock;
    private WeatherDirector? _weather;
    private WorldEventDirector? _worldEvents;
    private string _lastHit = "—";

    private PanelContainer _vitalsPanel = null!;
    private PanelContainer _controlsPanel = null!;
    private bool _shown;

    private Label _diag = null!;
    private Label _info = null!;
    private ProgressBar _hpBar = null!;
    private ProgressBar _staBar = null!;
    private ProgressBar _mpBar = null!;
    private Label _hpText = null!;
    private Label _staText = null!;
    private Label _mpText = null!;

    private VBoxContainer _targetSection = null!;
    private Label _targetTitle = null!;
    private ProgressBar _targetHpBar = null!;
    private Label _targetHpText = null!;
    private Label _targetInfo = null!;

    public override void _Ready()
    {
        BuildVitalsPanel();
        BuildControlsHint();
        SetShown(false); // hidden until F3

        EventBus.Instance?.Subscribe<DamageDealtEvent>(OnDamageDealt);
    }

    /// <summary>Shows/hides the whole debug overlay (bound to F3 by the bootstrap).</summary>
    public void Toggle() => SetShown(!_shown);

    /// <summary>Shows or hides the overlay outright (the <c>hud on|off</c> dev command).</summary>
    public void SetShown(bool shown)
    {
        _shown = shown;
        SetProcess(shown); // hidden (the default): no tick at all
        _vitalsPanel.Visible = shown;
        _controlsPanel.Visible = shown;
    }

    public override void _ExitTree()
    {
        EventBus.Instance?.Unsubscribe<DamageDealtEvent>(OnDamageDealt);
    }

    public void SetTarget(IEntity? target) => _target = target;

    public void SetPlayer(IEntity? player) => _player = player;

    public void SetClock(WorldClock? clock) => _clock = clock;

    public void SetWeather(WeatherDirector? weather) => _weather = weather;

    public void SetWorldEvents(WorldEventDirector? worldEvents) => _worldEvents = worldEvents;

    // --- Construction -------------------------------------------------------

    /// <summary>Top of the vitals card: the HUD's clock/weather band ends about 72 px down, so this starts below it.</summary>
    private const float ClockClearance = 96f;

    private void BuildVitalsPanel()
    {
        // Cards rather than Panels (37.5H): this is the F3 developer overlay, and two framed
        // screens each carrying a grain ShaderMaterial is chrome the dev overlay does not need.
        _vitalsPanel = Ignore(UiTheme.Card());
        // Below the GameHud's top-left clock/weather widget so the F3 overlay doesn't cover it.
        _vitalsPanel.Position = new Vector2(UiTheme.SpaceLg, ClockClearance);
        _vitalsPanel.CustomMinimumSize = new Vector2(320, 0);
        AddChild(_vitalsPanel);

        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", UiTheme.SpaceXs);
        _vitalsPanel.AddChild(col);

        col.AddChild(UiTheme.Header("DEBUG  (F3)"));
        _diag = UiTheme.Body("", UiTheme.Dim);
        col.AddChild(_diag);

        col.AddChild(new HSeparator());

        (_hpBar, _hpText) = AddVital(col, "HP", UiTheme.Health);
        (_staBar, _staText) = AddVital(col, "STA", UiTheme.Stamina);
        (_mpBar, _mpText) = AddVital(col, "MP", UiTheme.Mana);

        _info = UiTheme.Body("");
        col.AddChild(_info);

        // Target/dummy stats live in the *same* panel (a collapsible section below the
        // player's) so the two readouts stack and can never overlap on screen.
        _targetSection = new VBoxContainer { Visible = false };
        _targetSection.AddThemeConstantOverride("separation", UiTheme.SpaceXs);
        col.AddChild(_targetSection);

        _targetSection.AddChild(new HSeparator());
        _targetTitle = UiTheme.Header("Target");
        _targetSection.AddChild(_targetTitle);
        (_targetHpBar, _targetHpText) = AddVital(_targetSection, "HP", UiTheme.Health);
        _targetInfo = UiTheme.Body("", UiTheme.Dim);
        _targetSection.AddChild(_targetInfo);
    }

    private void BuildControlsHint()
    {
        // Bottom-right: bottom-left belongs to the GameHud vitals + hotbar bar (30.5B).
        _controlsPanel = Ignore(UiTheme.Card());
        _controlsPanel.AnchorLeft = 1f;
        _controlsPanel.AnchorRight = 1f;
        _controlsPanel.AnchorTop = 1f;
        _controlsPanel.AnchorBottom = 1f;
        _controlsPanel.OffsetLeft = -UiTheme.SpaceLg;
        _controlsPanel.OffsetRight = -UiTheme.SpaceLg;
        _controlsPanel.OffsetTop = -UiTheme.SpaceLg;
        _controlsPanel.OffsetBottom = -UiTheme.SpaceLg;
        _controlsPanel.GrowHorizontal = Control.GrowDirection.Begin;
        _controlsPanel.GrowVertical = Control.GrowDirection.Begin;
        AddChild(_controlsPanel);

        UiTheme.Compact(_controlsPanel);

        Label hint = UiTheme.Body(
            "WASD move · Mouse look · LMB attack · RMB block · Q cast · hold F spell wheel\n" +
            "E interact · I inventory · T spellbook · B bestiary · C party order · V view\n" +
            "[H] heal dummy · [R] respawn dummy · [X] +level · [P] +corruption · [K] +goblin rep\n" +
            "[F1] console · [F3] this · [F4] profiler · [F5/F9] save/load · [Esc] pause",
            UiTheme.Dim);
        _controlsPanel.AddChild(hint);
    }

    private static (ProgressBar Bar, Label Value) AddVital(VBoxContainer col, string caption, Color fill)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", UiTheme.SpaceSm);

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
        if (!_shown)
        {
            return;
        }

        _diag.Text = DiagnosticsText();
        UpdatePlayer();
        UpdateTarget();
    }

    /// <summary>
    /// Everything the overlay shows, as text, whether or not it is on screen: the diagnostics
    /// block, the player's vitals and read-out, and the target section when there is a target.
    /// This is what the <c>hud</c> dev command prints.
    /// </summary>
    public string Snapshot()
    {
        var sb = new StringBuilder(DiagnosticsText());
        if (_player is Node node && IsInstanceValid(node) && _player.TryGetComponent(out StatsComponent stats))
        {
            sb.Append($"\nHP {Vital(stats, StatType.Health)}   STA {Vital(stats, StatType.Stamina)}   MP {Vital(stats, StatType.Mana)}\n");
            sb.Append(PlayerText(stats));
        }

        if (_target is Node targetNode && IsInstanceValid(targetNode) && _target.TryGetComponent(out StatsComponent targetStats))
        {
            sb.Append($"\nTarget: {_target.DisplayName} (#{_target.RuntimeId})  HP {Vital(targetStats, StatType.Health)}  ");
            sb.Append(TargetText(targetStats));
        }

        return sb.ToString().TrimEnd();
    }

    private static string Vital(StatsComponent stats, StatType type) =>
        $"{stats.GetCurrent(type):0}/{stats.GetMax(type):0}";

    private string DiagnosticsText()
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

        if (_worldEvents is { } director && IsInstanceValid(director) && director.Active is { } worldEvent)
        {
            sb.Append($"\n★ {worldEvent.Name} — {worldEvent.ObjectiveLabel()}");
            if (worldEvent.IsTimed)
            {
                sb.Append($"  [{worldEvent.TimeLeft:0}s]");
            }
        }

        // Where: without it a still frame of the overlay cannot be placed in the world.
        if (_player is Node3D body && IsInstanceValid(body))
        {
            Vector3 p = body.GlobalPosition;
            string region = ServiceLocator.Instance is { } locator && locator.TryGet(out RegionStreamer streamer)
                ? $"{streamer.ActiveRegionId}  cells {streamer.ActiveCellCount()}/{streamer.ResidentCellCount()}  "
                : string.Empty;
            sb.Append($"\n{region}x {p.X:0.0} y {p.Y:0.0} z {p.Z:0.0}  {(SafeZones.Contains(p) ? "SAFE" : "WILD")}");
        }

        return sb.ToString();
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
        _info.Text = PlayerText(stats);
    }

    private string PlayerText(StatsComponent stats)
    {
        if (_player == null)
        {
            return string.Empty;
        }

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

        if (_player.TryGetComponent(out CorruptionComponent corruption))
        {
            sb.Append($"Corruption {corruption.Value}/{CorruptionTiers.Max}   ({CorruptionTiers.Label(corruption.Tier)})");
            if (_player.TryGetComponent(out ReputationComponent reputation) && reputation.Dread > 0)
            {
                sb.Append($"   dread -{reputation.Dread}");
            }
            sb.Append('\n');
        }

        sb.Append($"Last hit: {_lastHit}");
        return sb.ToString();
    }

    private void UpdateTarget()
    {
        if (_target is not Node node || !IsInstanceValid(node) ||
            !_target.TryGetComponent(out StatsComponent stats))
        {
            _targetSection.Visible = false;
            return;
        }

        _targetSection.Visible = true;
        _targetTitle.Text = $"{_target.DisplayName}  (#{_target.RuntimeId})";
        SetVital(_targetHpBar, _targetHpText, stats, StatType.Health);
        _targetInfo.Text = TargetText(stats);
    }

    private string TargetText(StatsComponent stats)
    {
        var sb = new StringBuilder();
        sb.Append($"PWR {stats.GetValue(StatType.PhysicalPower):0}   ARM {stats.GetValue(StatType.Armor):0}   ");
        sb.Append(stats.IsAlive ? "ALIVE" : "DEAD");

        if (_target != null && _target.TryGetComponent(out StatusEffectsComponent effects))
        {
            AppendEffects(sb, effects);
        }

        return sb.ToString();
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
                // The one surface that deliberately draws INERT objectives too (41D): every other
                // one hides them, so without this there is nowhere at all to see that a branch
                // exists and which side of it the save is on.
                string state = progress.IsObjectiveActive(i) ? string.Empty : " [inert]";
                sb.Append($"  {objectives[i].ShortLabel()} {progress.Counts[i]}/{objectives[i].RequiredCount}{state}\n");
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
        sb.Append($"Spell: {SpellText.Name(spell)} ({spell.ManaCost:0} MP) — {state}\n");
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
            sb.Append($" {SpellText.Name(effect.Definition)} ({effect.Remaining:0.0}s)");
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
