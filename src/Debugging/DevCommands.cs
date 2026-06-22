using System.Globalization;
using System.Text;
using Embervale.Core;
using Embervale.Core.Services;
using Embervale.Enemies;
using Embervale.Factions;
using Embervale.Items;
using Embervale.Player;
using Embervale.Progression;
using Embervale.Save;
using Embervale.Stats;
using Embervale.World;
using Godot;

namespace Embervale.Debugging;

/// <summary>
/// Registers the built-in <see cref="DevConsole"/> commands. Each one reaches the gameplay
/// systems through the <see cref="ServiceLocator"/> (the player and the world directors) and
/// the databases, so adding a command is a one-liner here — no engine plumbing.
/// </summary>
public static class DevCommands
{
    public static void RegisterAll(DevConsole console)
    {
        console.Register(new ConsoleCommand("help", "help", "List commands.", Help));
        console.Register(new ConsoleCommand("clear", "clear", "Clear the console.", (c, _) => { c.ClearLog(); return string.Empty; }));

        console.Register(new ConsoleCommand("spawn", "spawn [n]", "Spawn n goblins near the player.", Spawn));
        console.Register(new ConsoleCommand("give", "give <itemId> [qty]", "Give the player an item.", Give));
        console.Register(new ConsoleCommand("xp", "xp <n>", "Grant the player XP.", Xp));
        console.Register(new ConsoleCommand("heal", "heal", "Refill the player's resources.", Heal));
        console.Register(new ConsoleCommand("rep", "rep <factionId> <delta>", "Shift faction standing.", Rep));

        console.Register(new ConsoleCommand("time", "time <hour>", "Set the time of day (0–24).", Time));
        console.Register(new ConsoleCommand("weather", "weather <id>", "Force a weather state.", Weather));
        console.Register(new ConsoleCommand("event", "event <id>", "Force a world event.", Event));

        console.Register(new ConsoleCommand("seed", "seed <n>", "Seed the global RNG (for repro).", Seed));
        console.Register(new ConsoleCommand("repro", "repro [name]", "Run a repro scenario.", Repro));
        console.Register(new ConsoleCommand("invariants", "invariants", "Run the world integrity check.", (_, _) => WorldIntegrityChecker.Run()));
        console.Register(new ConsoleCommand("validate", "validate", "Validate authored content cross-references.", (_, _) => ContentValidator.Run()));
        console.Register(new ConsoleCommand("validate-all", "validate-all", "Full content battery (cross-refs + graph reachability).", (_, _) => ContentValidator.RunAll()));

        console.Register(new ConsoleCommand("pspawn", "pspawn [templateId]", "Spawn a persistent actor at the player.", PSpawn));
        console.Register(new ConsoleCommand("pdespawn", "pdespawn <persistentId>", "Free a persistent actor (recreated on load).", PDespawn));
        console.Register(new ConsoleCommand("plist", "plist", "List tracked persistent actors.", PList));
        console.Register(new ConsoleCommand("stats", "stats", "Frame/object counts.", StatsCmd));
    }

    private static string Help(DevConsole console, string[] args)
    {
        var sb = new StringBuilder("Commands:\n");
        foreach (ConsoleCommand cmd in console.Commands.Values)
        {
            sb.Append($"  {cmd.Usage}  — {cmd.Summary}\n");
        }

        return sb.ToString().TrimEnd();
    }

    private static string Spawn(DevConsole console, string[] args)
    {
        if (!TryPlayer(out PlayerCharacter player))
        {
            return "no player";
        }

        int count = ParseInt(args, 0, 1);
        for (int i = 0; i < count; i++)
        {
            Vector3 offset = new((GD.Randf() * 2f - 1f) * 4f, 0.5f, (GD.Randf() * 2f - 1f) * 4f);
            EnemyEntity enemy = EnemyFactory.Create(player.GlobalPosition + offset);
            player.GetParent()?.AddChild(enemy);
        }

        return $"spawned {count} goblin(s)";
    }

    private static string Give(DevConsole console, string[] args)
    {
        if (args.Length < 1)
        {
            return "usage: give <itemId> [qty]";
        }

        if (!TryPlayer(out PlayerCharacter player) || player.GetComponent<InventoryComponent>() is not { } inventory)
        {
            return "no player inventory";
        }

        if (ItemDatabase.Get(args[0]) is not { } item)
        {
            return $"unknown item '{args[0]}'";
        }

        int qty = ParseInt(args, 1, 1);
        int added = inventory.AddItem(item, qty);
        return $"gave {added}x {item.DisplayName}";
    }

    private static string Xp(DevConsole console, string[] args)
    {
        if (!TryPlayer(out PlayerCharacter player) || player.GetComponent<ProgressionComponent>() is not { } prog)
        {
            return "no progression";
        }

        int amount = ParseInt(args, 0, 50);
        prog.AddXp(amount);
        return $"granted {amount} XP (level {prog.Level})";
    }

    private static string Heal(DevConsole console, string[] args)
    {
        if (!TryPlayer(out PlayerCharacter player) || player.GetComponent<StatsComponent>() is not { } stats)
        {
            return "no player stats";
        }

        stats.RefillResources();
        return "resources refilled";
    }

    private static string Rep(DevConsole console, string[] args)
    {
        if (args.Length < 2)
        {
            return "usage: rep <factionId> <delta>";
        }

        if (!TryPlayer(out PlayerCharacter player) || player.GetComponent<ReputationComponent>() is not { } rep)
        {
            return "no reputation";
        }

        int delta = ParseInt(args, 1, 0);
        rep.Add(args[0], delta);
        return $"{args[0]}: {rep.Get(args[0])} ({ReputationTiers.Label(rep.TierOf(args[0]))})";
    }

    private static string Time(DevConsole console, string[] args)
    {
        if (!TryService(out WorldClock clock))
        {
            return "no clock";
        }

        float hour = ParseFloat(args, 0, clock.TimeOfDay);
        clock.SetTimeOfDay(hour);
        return $"time set to {clock.Clock()}";
    }

    private static string Weather(DevConsole console, string[] args)
    {
        if (args.Length < 1)
        {
            return "usage: weather <id>";
        }

        if (!TryService(out WeatherDirector weather))
        {
            return "no weather director";
        }

        return weather.Force(args[0]) ? $"weather → {args[0]}" : $"unknown weather '{args[0]}'";
    }

    private static string Event(DevConsole console, string[] args)
    {
        if (args.Length < 1)
        {
            return "usage: event <id>";
        }

        if (!TryService(out WorldEventDirector director))
        {
            return "no world-event director";
        }

        return director.ForceStart(args[0]) ? $"started {args[0]}" : $"could not start '{args[0]}' (already active / unknown)";
    }

    private static string Seed(DevConsole console, string[] args)
    {
        ulong seed = (ulong)ParseInt(args, 0, 0);
        GD.Seed(seed);
        return $"global RNG seeded with {seed}";
    }

    private static string Repro(DevConsole console, string[] args)
    {
        if (args.Length < 1)
        {
            return "scenarios: " + string.Join(", ", ReproHarness.Names);
        }

        return ReproHarness.Run(args[0], console.Execute);
    }

    private static string StatsCmd(DevConsole console, string[] args)
    {
        double nodes = Performance.GetMonitor(Performance.Monitor.ObjectNodeCount);
        double orphans = Performance.GetMonitor(Performance.Monitor.ObjectOrphanNodeCount);
        return $"fps {Engine.GetFramesPerSecond()}  nodes {nodes:0}  orphans {orphans:0}  invariant-violations {Invariant.Violations}";
    }

    private static string PSpawn(DevConsole console, string[] args)
    {
        if (!TryService(out PersistentSpawnDirector director))
        {
            return "no spawn director";
        }

        if (!TryPlayer(out PlayerCharacter player))
        {
            return "no player";
        }

        string template = args.Length > 0 ? args[0] : GameIds.Templates.Cache;
        Embervale.Entities.IEntity? actor = director.Spawn(template, string.Empty, player.GlobalPosition + new Vector3(2f, 0f, 0f));
        return actor == null ? $"could not spawn '{template}'" : $"spawned {actor.PersistentId} ({template})";
    }

    private static string PDespawn(DevConsole console, string[] args)
    {
        if (args.Length < 1)
        {
            return "usage: pdespawn <persistentId>";
        }

        if (!TryService(out PersistentSpawnDirector director))
        {
            return "no spawn director";
        }

        return director.Despawn(args[0]) ? $"despawned {args[0]}" : $"no tracked actor '{args[0]}'";
    }

    private static string PList(DevConsole console, string[] args)
    {
        if (!TryService(out PersistentSpawnDirector director))
        {
            return "no spawn director";
        }

        return director.TrackedIds.Count == 0 ? "no persistent actors" : string.Join(", ", director.TrackedIds);
    }

    // --- Helpers ------------------------------------------------------------

    private static bool TryPlayer(out PlayerCharacter player)
    {
        player = null!;
        return ServiceLocator.Instance != null && ServiceLocator.Instance.TryGet(out player) && Node.IsInstanceValid(player);
    }

    private static bool TryService<T>(out T service)
        where T : class
    {
        service = null!;
        return ServiceLocator.Instance != null && ServiceLocator.Instance.TryGet(out service);
    }

    private static int ParseInt(string[] args, int index, int fallback)
    {
        return index < args.Length && int.TryParse(args[index], NumberStyles.Integer, CultureInfo.InvariantCulture, out int v)
            ? v
            : fallback;
    }

    private static float ParseFloat(string[] args, int index, float fallback)
    {
        return index < args.Length && float.TryParse(args[index], NumberStyles.Float, CultureInfo.InvariantCulture, out float v)
            ? v
            : fallback;
    }
}
