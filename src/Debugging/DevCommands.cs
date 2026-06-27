using System.Globalization;
using System.Text;
using Embervale.Core;
using Embervale.Core.Events;
using Embervale.Core.Services;
using Embervale.Corruption;
using Embervale.Enemies;
using Embervale.Factions;
using Embervale.Items;
using Embervale.Localization;
using Embervale.Magic;
using Embervale.Player;
using Embervale.Progression;
using Embervale.Save;
using Embervale.Settings;
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
        console.Register(new ConsoleCommand("corruption", "corruption <get|set N|add N|tier>", "Inspect or drive the player's corruption.", Corruption));
        console.Register(new ConsoleCommand("learn", "learn <spellId|perkId>", "Learn a spell or perk (respects corruption gating).", Learn));

        console.Register(new ConsoleCommand("time", "time <hour>", "Set the time of day (0–24).", Time));
        console.Register(new ConsoleCommand("weather", "weather <id>", "Force a weather state.", Weather));
        console.Register(new ConsoleCommand("event", "event <id>", "Force a world event.", Event));
        console.Register(new ConsoleCommand("region", "region <list|goto <id>>", "List regions or hard-load into one (Phase 25C).", Region));
        console.Register(new ConsoleCommand("travel", "travel <list|goto <id>>", "List attuned travel nodes or fast-travel to one (Phase 25G).", Travel));

        console.Register(new ConsoleCommand("seed", "seed <n>", "Seed the global RNG (for repro).", Seed));
        console.Register(new ConsoleCommand("repro", "repro [name]", "Run a repro scenario.", Repro));
        console.Register(new ConsoleCommand("invariants", "invariants", "Run the world integrity check.", (_, _) => WorldIntegrityChecker.Run()));
        console.Register(new ConsoleCommand("validate", "validate", "Validate authored content cross-references.", (_, _) => ContentValidator.Run()));
        console.Register(new ConsoleCommand("validate-all", "validate-all", "Full content battery (cross-refs + graph reachability).", (_, _) => ContentValidator.RunAll()));

        console.Register(new ConsoleCommand("autosave", "autosave [status]", "Force an autosave now, or show the ring status.", Autosave));
        console.Register(new ConsoleCommand("settings", "settings [set <field> <value>|reset]", "Show, change, or reset player settings (persists + applies).", SettingsCmd));
        console.Register(new ConsoleCommand("locale", "locale [code]", "Show loaded locales, or switch the active one.", LocaleCmd));

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

    private static string Corruption(DevConsole console, string[] args)
    {
        if (!TryPlayer(out PlayerCharacter player) || player.GetComponent<CorruptionComponent>() is not { } corruption)
        {
            return "no corruption component";
        }

        string verb = args.Length > 0 ? args[0].ToLowerInvariant() : "get";
        switch (verb)
        {
            case "get":
                break;
            case "set":
                corruption.Set(ParseInt(args, 1, 0));
                break;
            case "add":
                corruption.Add(ParseInt(args, 1, 10));
                break;
            case "tier":
                return CorruptionTiers.Label(corruption.Tier);
            default:
                return "usage: corruption <get|set N|add N|tier>";
        }

        string line = $"{corruption.Value}/{CorruptionTiers.Max} ({CorruptionTiers.Label(corruption.Tier)})";
        if (player.GetComponent<ReputationComponent>() is { Dread: > 0 } rep)
        {
            line += $" — dread -{rep.Dread}";
        }

        line += $" — ending: {corruption.EndingEligibility}";
        return line;
    }

    private static string Autosave(DevConsole console, string[] args)
    {
        if (ServiceLocator.Instance is not { } locator || !locator.TryGet(out AutosaveService autosave))
        {
            return "no autosave service (start a game first)";
        }

        if (args.Length > 0 && args[0].ToLowerInvariant() == "status")
        {
            string next = SaveManager.Instance is { } sm
                ? AutosaveService.NextAutosaveSlot(sm.ListSlots())
                : AutosaveService.RingSlots[0];
            return $"ring: {string.Join(", ", AutosaveService.RingSlots)} — next overwrite: {next}";
        }

        string? slot = autosave.ForceAutosave();
        return slot != null ? $"autosaved to '{slot}'" : "skipped (not in active play)";
    }

    private static string SettingsCmd(DevConsole console, string[] args)
    {
        if (ServiceLocator.Instance is not { } locator || !locator.TryGet(out SettingsService settings))
        {
            return "no settings service";
        }

        var s = settings.Current;

        if (args.Length > 0 && args[0].ToLowerInvariant() == "reset")
        {
            settings.ResetToDefaults();
            settings.Save();
            settings.Apply();
            return "settings reset to defaults (saved + applied)";
        }

        if (args.Length >= 3 && args[0].ToLowerInvariant() == "set")
        {
            string field = args[1].ToLowerInvariant();
            string raw = args[2];
            bool ok = field switch
            {
                "windowmode" => Set(v => s.WindowMode = v, ParseInt(args, 2, s.WindowMode)),
                "vsync" => Set(v => s.VSync = v, raw == "1" || raw.ToLowerInvariant() == "true"),
                "maxfps" => Set(v => s.MaxFps = v, ParseInt(args, 2, s.MaxFps)),
                "master" => Set(v => s.MasterVolume = v, ParseFloat(raw, s.MasterVolume)),
                "music" => Set(v => s.MusicVolume = v, ParseFloat(raw, s.MusicVolume)),
                "sfx" => Set(v => s.SfxVolume = v, ParseFloat(raw, s.SfxVolume)),
                "sensitivity" => Set(v => s.MouseSensitivity = v, ParseFloat(raw, s.MouseSensitivity)),
                _ => false,
            };

            if (!ok)
            {
                return "usage: settings set <windowmode|vsync|maxfps|master|music|sfx|sensitivity> <value>";
            }

            settings.Save();
            settings.Apply();
            return $"{field} = {raw} (saved + applied)";
        }

        return $"window:{s.WindowMode} vsync:{s.VSync} maxfps:{s.MaxFps} | master:{s.MasterVolume:0.00} " +
               $"music:{s.MusicVolume:0.00} sfx:{s.SfxVolume:0.00} | sens:{s.MouseSensitivity:0.00} diff:{s.Difficulty}";
    }

    private static bool Set<T>(System.Action<T> assign, T value)
    {
        assign(value);
        return true;
    }

    private static float ParseFloat(string raw, float fallback) =>
        float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : fallback;

    private static string LocaleCmd(DevConsole console, string[] args)
    {
        if (args.Length == 0)
        {
            string loaded = string.Join(", ", TranslationServer.GetLoadedLocales());
            return $"active: {TranslationServer.GetLocale()} | loaded: {loaded}";
        }

        // Re-open menus to see the change: strings are resolved at build time via Loc.T, so already-
        // built panels keep their text until rebuilt.
        return Loc.SetLocale(args[0])
            ? $"locale set to '{args[0]}' (re-open menus to see the change)"
            : $"locale '{args[0]}' is not loaded";
    }

    private static string Learn(DevConsole console, string[] args)
    {
        if (args.Length < 1)
        {
            return "usage: learn <spellId|perkId>";
        }

        if (!TryPlayer(out PlayerCharacter player))
        {
            return "no player";
        }

        string id = args[0];

        // A spell: gated by the caster's corruption tier (Phase 23H).
        if (SpellDatabase.Get(id) is { } spell)
        {
            if (player.GetComponent<SpellcastingComponent>() is not { } casting)
            {
                return "no spellcasting component";
            }

            if (!casting.MeetsCorruption(spell))
            {
                return $"cannot learn {id}: corruption below {CorruptionTiers.Label(spell.MinCorruptionTier)}";
            }

            casting.Learn(id);
            return $"learned spell {spell.DisplayName}";
        }

        // A perk: gated by corruption tier and skill points.
        if (PerkDatabase.Get(id) is { } perk)
        {
            if (player.GetComponent<PerksComponent>() is not { } perks)
            {
                return "no perks component";
            }

            if (!perks.MeetsCorruption(perk))
            {
                return $"cannot learn {id}: corruption below {CorruptionTiers.Label(perk.MinCorruptionTier)}";
            }

            return perks.Learn(perk)
                ? $"learned perk {perk.DisplayName} (rank {perks.RankOf(perk.Id)})"
                : $"cannot learn {id}: maxed or not enough skill points";
        }

        return $"unknown spell/perk id: {id}";
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

    private static string Region(DevConsole console, string[] args)
    {
        if (args.Length >= 1 && args[0] == "list")
        {
            var sb = new StringBuilder("regions:");
            foreach (RegionResource region in RegionDatabase.All)
            {
                sb.Append($"\n  {region.Id} — {region.DisplayName}");
            }

            return sb.ToString();
        }

        if (args.Length >= 2 && args[0] == "goto")
        {
            if (RegionDatabase.Get(args[1]) == null)
            {
                return $"unknown region '{args[1]}'";
            }

            EventBus.Instance?.Publish(new RegionTransitionRequestedEvent(args[1]));
            return $"transitioning to {args[1]}";
        }

        return "usage: region <list|goto <id>>";
    }

    private static string Travel(DevConsole console, string[] args)
    {
        if (ServiceLocator.Instance is not { } locator || !locator.TryGet(out FastTravelService travel))
        {
            return "fast-travel service unavailable";
        }

        if (args.Length >= 1 && args[0] == "list")
        {
            var sb = new StringBuilder("travel nodes:");
            bool any = false;
            foreach (TravelNode node in travel.Nodes)
            {
                any = true;
                sb.Append($"\n  {node.Id} — {node.Label} ({node.RegionId})");
            }

            return any ? sb.ToString() : "no travel nodes attuned yet";
        }

        if (args.Length >= 2 && args[0] == "goto")
        {
            if (!travel.HasNode(args[1]))
            {
                return $"unknown/undiscovered travel node '{args[1]}'";
            }

            EventBus.Instance?.Publish(new FastTravelRequestedEvent(args[1]));
            return $"fast travelling to {args[1]}";
        }

        return "usage: travel <list|goto <id>>";
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
