using System.Text;
using Embervale.Combat;
using Embervale.Core;
using Embervale.Core.Events;
using Embervale.Entities;
using Embervale.Progression;
using Embervale.Quests;
using Embervale.Stats;
using Embervale.World;
using Godot;

namespace Embervale.UI;

/// <summary>
/// Minimal on-screen diagnostics overlay. It exists so the otherwise invisible
/// core systems (game state, stats, combat) are observable while running — a
/// stand-in until real gameplay UI arrives. Built entirely in code so the
/// bootstrap scene stays a single node.
/// </summary>
public partial class DebugHud : CanvasLayer
{
    private Label _label = null!;
    private IEntity? _target;
    private IEntity? _player;
    private WorldClock? _clock;
    private string _lastHit = "—";

    public override void _Ready()
    {
        var panel = new PanelContainer
        {
            Position = new Vector2(16, 16),
        };
        AddChild(panel);

        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 10);
        margin.AddThemeConstantOverride("margin_right", 10);
        margin.AddThemeConstantOverride("margin_top", 8);
        margin.AddThemeConstantOverride("margin_bottom", 8);
        panel.AddChild(margin);

        _label = new Label();
        _label.AddThemeFontSizeOverride("font_size", 15);
        margin.AddChild(_label);

        EventBus.Instance?.Subscribe<DamageDealtEvent>(OnDamageDealt);
    }

    public override void _ExitTree()
    {
        EventBus.Instance?.Unsubscribe<DamageDealtEvent>(OnDamageDealt);
    }

    public void SetTarget(IEntity? target)
    {
        _target = target;
    }

    public void SetPlayer(IEntity? player)
    {
        _player = player;
    }

    public void SetClock(WorldClock? clock)
    {
        _clock = clock;
    }

    public override void _Process(double delta)
    {
        var sb = new StringBuilder();
        sb.Append("EMBERVALE — Combat Sandbox\n");
        sb.Append($"FPS: {Engine.GetFramesPerSecond()}\n");
        sb.Append($"State: {GameManager.Instance?.State.ToString() ?? "?"}\n");

        if (_clock is { } clock && IsInstanceValid(clock))
        {
            sb.Append($"Time: {clock.Clock()}  ({DayPhases.Label(clock.Phase)})\n");
        }

        if (_player is Node playerNode && IsInstanceValid(playerNode) &&
            _player.TryGetComponent(out StatsComponent playerStats))
        {
            sb.Append('\n');
            AppendResource(sb, "Player HP ", playerStats, StatType.Health);
            AppendResource(sb, "Player STA", playerStats, StatType.Stamina);

            if (_player.TryGetComponent(out ProgressionComponent prog))
            {
                string xp = prog.IsMaxLevel ? "MAX" : $"{prog.CurrentXp}/{prog.XpToNext}";
                sb.Append($"Level {prog.Level}  XP {xp}  SP {prog.SkillPoints}\n");
            }

            if (_player.TryGetComponent(out QuestLogComponent quests))
            {
                AppendQuestTracker(sb, quests);
            }
        }

        if (_target is Node targetNode && IsInstanceValid(targetNode) &&
            _target.TryGetComponent(out StatsComponent stats))
        {
            sb.Append('\n');
            sb.Append($"Target: {_target.DisplayName} (#{_target.RuntimeId})\n");
            AppendResource(sb, "HP ", stats, StatType.Health);
            sb.Append($"PWR {stats.GetValue(StatType.PhysicalPower):0} | ");
            sb.Append($"ARM {stats.GetValue(StatType.Armor):0}\n");
            sb.Append(stats.IsAlive ? "Status: ALIVE" : "Status: DEAD");
            sb.Append('\n');
        }

        sb.Append($"\nLast hit: {_lastHit}\n");
        sb.Append("\nWASD move | Mouse look | LMB attack | RMB block\nE interact | I character | J journal | [H] heal | [R] respawn | [X] +XP\n[F5/F9] save/load | [Esc] pause");
        _label.Text = sb.ToString();
    }

    private void OnDamageDealt(DamageDealtEvent e)
    {
        string tags = e.IsCrit ? " CRIT!" : e.IsBlocked ? " (blocked)" : string.Empty;
        _lastHit = $"{e.Amount:0} {e.Type} to {e.Target.DisplayName}{tags}";
    }

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

    private static void AppendResource(StringBuilder sb, string label, StatsComponent stats, StatType type)
    {
        float current = stats.GetCurrent(type);
        float max = stats.GetMax(type);
        sb.Append($"{label}: {current:0}/{max:0}  [{Bar(stats.GetNormalized(type))}]\n");
    }

    private static string Bar(float ratio, int width = 16)
    {
        int filled = Mathf.RoundToInt(ratio * width);
        return new string('|', filled).PadRight(width, '.');
    }
}
