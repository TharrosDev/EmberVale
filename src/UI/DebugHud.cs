using System.Text;
using Embervale.Core;
using Embervale.Entities;
using Embervale.Stats;
using Godot;

namespace Embervale.UI;

/// <summary>
/// Minimal on-screen diagnostics overlay. It exists so the otherwise invisible
/// core systems (game state, stats, resources) are observable while running —
/// a stand-in until real gameplay UI arrives. Built entirely in code so the
/// bootstrap scene stays a single node.
/// </summary>
public partial class DebugHud : CanvasLayer
{
    private Label _label = null!;
    private IEntity? _target;

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
    }

    public void SetTarget(IEntity? target)
    {
        _target = target;
    }

    public override void _Process(double delta)
    {
        var sb = new StringBuilder();
        sb.Append("EMBERVALE — Core Systems Sandbox\n");
        sb.Append($"FPS: {Engine.GetFramesPerSecond()}\n");
        sb.Append($"State: {GameManager.Instance?.State.ToString() ?? "?"}\n");

        if (_target is Node targetNode && IsInstanceValid(targetNode) &&
            _target.TryGetComponent(out StatsComponent stats))
        {
            sb.Append('\n');
            sb.Append($"Target: {_target.DisplayName} (#{_target.RuntimeId})\n");
            AppendResource(sb, "HP ", stats, StatType.Health);
            AppendResource(sb, "STA", stats, StatType.Stamina);
            AppendResource(sb, "MP ", stats, StatType.Mana);
            sb.Append($"STR {stats.GetValue(StatType.Strength):0} | ");
            sb.Append($"PWR {stats.GetValue(StatType.PhysicalPower):0} | ");
            sb.Append($"ARM {stats.GetValue(StatType.Armor):0}\n");
            sb.Append(stats.IsAlive ? "Status: ALIVE" : "Status: DEAD");
        }

        sb.Append("\n\nWASD move | Mouse look | LMB attack\n[H] heal  [R] respawn  [F5/F9] save/load  [Esc] pause");
        _label.Text = sb.ToString();
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
