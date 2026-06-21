using System.Text;
using Embervale.Core.Diagnostics;
using Embervale.Core.Services;
using Embervale.Items;
using Embervale.Player;
using Embervale.Stats;
using Godot;

namespace Embervale.Debugging;

/// <summary>
/// Periodically validates the world's runtime invariants and reports any breakage — a
/// standing sanity net that catches "impossible" states (a player with no stats, a resource
/// above its max, a NaN position, leaked orphan nodes) close to where they happen. Runs on a
/// timer and is also invokable on demand from the dev console (<c>invariants</c>).
/// </summary>
[GlobalClass]
public partial class WorldIntegrityChecker : Node
{
    /// <summary>Seconds between automatic checks.</summary>
    [Export] public float Interval { get; set; } = 5f;

    private double _timer;

    public override void _Process(double delta)
    {
        _timer += delta;
        if (_timer < Interval)
        {
            return;
        }

        _timer = 0d;

        // The periodic pass only speaks up when something is wrong.
        int before = Invariant.Violations;
        Run();
        if (Invariant.Violations > before)
        {
            Log.Warn($"WorldIntegrityChecker: {Invariant.Violations - before} new invariant violation(s).");
        }
    }

    /// <summary>Runs every check once and returns a human-readable summary.</summary>
    public static string Run()
    {
        var sb = new StringBuilder();
        int before = Invariant.Violations;

        CheckPlayer(sb);
        CheckOrphans(sb);

        int found = Invariant.Violations - before;
        return found == 0 ? "Integrity OK." + sb : $"Integrity: {found} issue(s).\n{sb}";
    }

    private static void CheckPlayer(StringBuilder sb)
    {
        if (ServiceLocator.Instance == null || !ServiceLocator.Instance.TryGet(out PlayerCharacter player))
        {
            sb.Append("• player not registered\n");
            Invariant.Check(false, "player is not registered in the ServiceLocator");
            return;
        }

        if (!Invariant.Check(player.GetComponent<StatsComponent>() is not null, "player has no StatsComponent"))
        {
            sb.Append("• player missing StatsComponent\n");
        }

        if (!Invariant.Check(player.GetComponent<InventoryComponent>() is not null, "player has no InventoryComponent"))
        {
            sb.Append("• player missing InventoryComponent\n");
        }

        Vector3 pos = player.GlobalPosition;
        bool finite = !(float.IsNaN(pos.X) || float.IsNaN(pos.Y) || float.IsNaN(pos.Z) ||
                        float.IsInfinity(pos.X) || float.IsInfinity(pos.Y) || float.IsInfinity(pos.Z));
        if (!Invariant.Check(finite, $"player position is not finite ({pos})"))
        {
            sb.Append("• player position not finite\n");
        }

        if (player.GetComponent<StatsComponent>() is { } stats)
        {
            CheckResource(sb, stats, StatType.Health);
            CheckResource(sb, stats, StatType.Stamina);
            CheckResource(sb, stats, StatType.Mana);
        }
    }

    private static void CheckResource(StringBuilder sb, StatsComponent stats, StatType type)
    {
        float current = stats.GetCurrent(type);
        float max = stats.GetMax(type);
        bool ok = current >= -0.01f && current <= max + 0.01f && max >= 0f;
        if (!Invariant.Check(ok, $"{type} current {current:0.##} out of range [0, {max:0.##}]"))
        {
            sb.Append($"• {type} out of range ({current:0}/{max:0})\n");
        }
    }

    private static void CheckOrphans(StringBuilder sb)
    {
        var orphans = (int)Performance.GetMonitor(Performance.Monitor.ObjectOrphanNodeCount);
        if (!Invariant.Check(orphans == 0, $"{orphans} orphan node(s) leaked"))
        {
            sb.Append($"• {orphans} orphan node(s)\n");
        }
    }
}
