using System.Collections.Generic;
using Embervale.Combat;
using Embervale.Enemies;
using Embervale.Player;
using Embervale.Stats;
using Godot;

namespace Embervale.Debugging;

/// <summary>
/// Setting up and reading a fight: <c>god</c>, <c>hurt</c>, <c>enemies</c>, <c>killall</c>.
///
/// <para>Damage goes through <see cref="CombatComponent.ReceiveDamage"/>, the pipeline every blow
/// uses, so mitigation, the damage and death events, kill credit, loot and quest counters all run.
/// Nothing here frees an actor or writes a health value.</para>
/// </summary>
public static partial class DevCommands
{
    /// <summary>The source every <c>god</c> modifier carries, so it can be found and removed.</summary>
    private const string GodSource = "dev.god";

    private static void RegisterCombat(DevConsole console)
    {
        console.Register(new ConsoleCommand("god", "god [on|off]", "Toggle or set god mode: a stat modifier that multiplies the player's maximum health by 1000 and refills it. Blows still land, stagger and raise their events.", God));
        console.Register(new ConsoleCommand("hurt", "hurt <amount> [physical|fire|frost|lightning|arcane|nature|necrotic|true]", "Deal damage to the player through the damage pipeline (armour and resistances apply; 'true' ignores them).", Hurt));
        console.Register(new ConsoleCommand("enemies", "enemies [radius]", "List live enemies nearest first: runtime id, template, distance, health, AI state.", Enemies));
        console.Register(new ConsoleCommand("killall", "killall [radius]", "Kill every live enemy (within radius metres) with a lethal blow credited to the player, so loot, XP and quest counters fire.", KillAll));
    }

    private static string God(DevConsole console, string[] args)
    {
        if (!TryPlayer(out PlayerCharacter player) || player.GetComponent<StatsComponent>() is not { } stats)
        {
            return console.Fail("no player stats");
        }

        bool on = IsGod(stats);
        string wanted = args.Length > 0 ? args[0].ToLowerInvariant() : (on ? "off" : "on");
        if (wanted is not ("on" or "off"))
        {
            return console.Fail("usage: god [on|off]");
        }

        Stat health = stats.GetStat(StatType.Health);
        health.RemoveModifiersFromSource(GodSource);
        if (wanted == "on")
        {
            health.AddModifier(new StatModifier(999f, ModifierType.PercentAdd, GodSource));
            stats.RefillResources();
        }

        return console.Reply(
            $"god {wanted} (max health {stats.GetMax(StatType.Health):0})",
            new Godot.Collections.Dictionary { ["god"] = wanted == "on", ["hp_max"] = R(stats.GetMax(StatType.Health)) });
    }

    private static bool IsGod(StatsComponent stats)
    {
        foreach (StatModifier modifier in stats.GetStat(StatType.Health).Modifiers)
        {
            if (Equals(modifier.Source, GodSource))
            {
                return true;
            }
        }

        return false;
    }

    private static string Hurt(DevConsole console, string[] args)
    {
        const string usage = "usage: hurt <amount> [physical|fire|frost|lightning|arcane|nature|necrotic|true]";
        if (args.Length < 1 || !TryFloat(args, 0, 0f, out float amount) || amount <= 0f)
        {
            return console.Fail(usage);
        }

        var type = DamageType.Physical;
        if (args.Length > 1 && !System.Enum.TryParse(args[1], ignoreCase: true, out type))
        {
            return console.Fail(usage);
        }

        if (!TryPlayer(out PlayerCharacter player) || player.GetComponent<CombatComponent>() is not { } combat ||
            player.GetComponent<StatsComponent>() is not { } stats)
        {
            return console.Fail("no player combat component");
        }

        float before = stats.GetCurrent(StatType.Health);
        combat.ReceiveDamage(new DamagePacket(amount, type, null, false, 0f, Unblockable: true));
        float after = stats.GetCurrent(StatType.Health);
        var data = new Godot.Collections.Dictionary
        {
            ["requested"] = R(amount), ["taken"] = R(before - after), ["hp"] = R(after),
            ["hp_max"] = R(stats.GetMax(StatType.Health)), ["alive"] = stats.IsAlive,
        };
        string reply = $"hurt {amount:0.#} {type}: took {before - after:0.#} ({after:0}/{stats.GetMax(StatType.Health):0})";
        return before - after <= 0f
            ? console.Fail(reply + " — nothing landed (dead, or inside dodge i-frames)")
            : console.Reply(reply, data);
    }

    private static string Enemies(DevConsole console, string[] args)
    {
        if (!TryFloat(args, 0, float.MaxValue, out float radius) || radius <= 0f)
        {
            return console.Fail("usage: enemies [radius]");
        }

        if (!TryPlayer(out PlayerCharacter player))
        {
            return console.Fail("no player");
        }

        List<EnemyEntity> enemies = LiveEnemies(player, radius);
        Godot.Collections.Array data = EnemyRows(player, enemies);
        var lines = new List<string> { $"{enemies.Count} live enem{(enemies.Count == 1 ? "y" : "ies")}" };
        for (int i = 0; i < enemies.Count && i < 20; i++)
        {
            var row = data[i].AsGodotDictionary();
            lines.Add($"  #{row["id"]} {row["template"]}  {row["dist"]} m  hp {row["hp"]}/{row["hp_max"]}  {row["state"]}");
        }

        if (enemies.Count > 20)
        {
            lines.Add($"  ... {enemies.Count - 20} more (--json for all)");
        }

        return console.Reply(string.Join("\n", lines), data);
    }

    private static string KillAll(DevConsole console, string[] args)
    {
        if (!TryFloat(args, 0, float.MaxValue, out float radius) || radius <= 0f)
        {
            return console.Fail("usage: killall [radius]");
        }

        if (!TryPlayer(out PlayerCharacter player))
        {
            return console.Fail("no player");
        }

        List<EnemyEntity> enemies = LiveEnemies(player, radius);
        int killed = 0;
        foreach (EnemyEntity enemy in enemies)
        {
            if (enemy.GetComponent<StatsComponent>() is not { } stats)
            {
                continue;
            }

            // A blow the size of a thousand health bars, credited to the player. An enemy inside
            // dodge i-frames whiffs it, so what is left goes through the stats component's own
            // damage call, which raises the same damaged and died events.
            float lethal = (stats.GetMax(StatType.Health) * 1000f) + 1000f;
            enemy.GetComponent<CombatComponent>()?.ReceiveDamage(
                new DamagePacket(lethal, DamageType.True, player, false, 0f, Unblockable: true));
            if (stats.IsAlive)
            {
                stats.ApplyDamage(stats.GetCurrent(StatType.Health), player);
            }

            if (!stats.IsAlive)
            {
                killed++;
            }
        }

        var data = new Godot.Collections.Dictionary { ["found"] = enemies.Count, ["killed"] = killed };
        string reply = $"killed {killed}/{enemies.Count}";
        return killed < enemies.Count ? console.Fail(reply) : console.Reply(reply, data);
    }

    // --- Shared -------------------------------------------------------------

    /// <summary>Live enemies within <paramref name="radius"/> of the player, nearest first. Walked,
    /// not registered: enemies come and go with cell streaming and encounters.</summary>
    private static List<EnemyEntity> LiveEnemies(PlayerCharacter player, float radius)
    {
        var found = new List<EnemyEntity>();
        Vector3 origin = player.GlobalPosition;
        Collect(player.GetTree()?.Root);
        found.Sort((a, b) =>
        {
            int byDistance = a.GlobalPosition.DistanceSquaredTo(origin).CompareTo(b.GlobalPosition.DistanceSquaredTo(origin));
            return byDistance != 0 ? byDistance : a.RuntimeId.CompareTo(b.RuntimeId);
        });
        return found;

        void Collect(Node? node)
        {
            if (node == null)
            {
                return;
            }

            if (node is EnemyEntity enemy)
            {
                if (!enemy.IsQueuedForDeletion() && enemy.IsInsideTree() &&
                    enemy.GetComponent<StatsComponent>() is { IsAlive: true } &&
                    enemy.GlobalPosition.DistanceTo(origin) <= radius)
                {
                    found.Add(enemy);
                }

                return; // nothing under an enemy is another enemy
            }

            foreach (Node child in node.GetChildren())
            {
                Collect(child);
            }
        }
    }

    private static Godot.Collections.Array EnemyRows(PlayerCharacter player, List<EnemyEntity> enemies)
    {
        var rows = new Godot.Collections.Array();
        foreach (EnemyEntity enemy in enemies)
        {
            StatsComponent? stats = enemy.GetComponent<StatsComponent>();
            Vector3 p = enemy.GlobalPosition;
            rows.Add(new Godot.Collections.Dictionary
            {
                ["id"] = enemy.RuntimeId,
                ["template"] = enemy.TemplateId,
                ["name"] = enemy.DisplayName,
                ["dist"] = R(p.DistanceTo(player.GlobalPosition)),
                ["hp"] = R(stats?.GetCurrent(StatType.Health) ?? 0f),
                ["hp_max"] = R(stats?.GetMax(StatType.Health) ?? 0f),
                ["state"] = enemy.GetComponent<EnemyAIComponent>()?.State.ToString() ?? "none",
                ["x"] = R(p.X), ["y"] = R(p.Y), ["z"] = R(p.Z),
            });
        }

        return rows;
    }
}
