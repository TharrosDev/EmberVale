using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Embervale.Core;
using Embervale.Core.Events;
using Embervale.Core.Services;
using Embervale.Combat;
using Embervale.Companions;
using Embervale.Corruption;
using Embervale.Economy;
using Embervale.Enemies;
using Embervale.Factions;
using Embervale.Items;
using Embervale.Localization;
using Embervale.Magic;
using Embervale.Movement;
using Embervale.Player;
using Embervale.Progression;
using Embervale.Quests;
using Embervale.Races;
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
        console.Register(new ConsoleCommand("mount", "mount [own]", "Toggle the mount; 'own' grants the stable flag first (Phase 39A).", Mount));
        console.Register(new ConsoleCommand("rep", "rep <factionId> <delta>", "Shift faction standing.", Rep));
        console.Register(new ConsoleCommand("guild", "guild <list|<guildId> <offer|join|rank N|leave|refuse|finale|clear>>", "Inspect or drive guild membership through the real story-flag path (Phase 42A).", Guild));
        console.Register(new ConsoleCommand("corruption", "corruption <get|set N|add N|tier>", "Inspect or drive the player's corruption.", Corruption));
        console.Register(new ConsoleCommand("learn", "learn <spellId|perkId>", "Learn a spell or perk (respects corruption gating).", Learn));
        console.Register(new ConsoleCommand("race", "race [id]", "Show races, or live-apply one to the player (Phase 26C).", RaceCmd));
        console.Register(new ConsoleCommand("mastery", "mastery", "Show the player's per-school spell mastery (Phase 29.5C).", Mastery));
        console.Register(new ConsoleCommand("weave", "weave [set <0..1>|restore]", "Inspect or tune the region's magic potency — the fading Weave (Phase 29.5E).", WeaveCmd));

        console.Register(new ConsoleCommand("time", "time <hour>", "Set the time of day (0–24).", Time));
        console.Register(new ConsoleCommand("weather", "weather <id>", "Force a weather state.", Weather));
        console.Register(new ConsoleCommand("event", "event <id>", "Force a world event.", Event));
        console.Register(new ConsoleCommand("region", "region <list|goto <id>>", "List regions or hard-load into one (Phase 25C).", Region));
        console.Register(new ConsoleCommand("travel", "travel <list|goto <id>>", "List attuned travel nodes or fast-travel to one (Phase 25G).", Travel));
        console.Register(new ConsoleCommand("economy", "economy [arbitrage]", "Print the realm's best buy-low/sell-high routes (Phase 38N1).", Economy));
        console.Register(new ConsoleCommand("shock", "shock [list|force <cellId> <tag> <shortage|glut|fair> [days]|clear <cellId>]", "Inspect or drive supply shocks (Phase 38T).", Shock));
        console.Register(new ConsoleCommand("tutorial", "tutorial <status|skip|restart>", "Inspect or drive the onboarding hints (Phase 33B).", Tutorial));
        console.Register(new ConsoleCommand("opening", "opening", "Replay the new-game prologue (Phase 33A).", Opening));
        console.Register(new ConsoleCommand("companion", "companion <list|recruit <id>|dismiss <id>|stance <id> <follow|hold|engage>|order|loyalty <id> [delta]>", "Inspect and drive the companion party (Phase 32A).", Companion));
        console.Register(new ConsoleCommand("shop", "shop [id|restock <id>|invest <id>]", "List shops, open one's trade window, force a restock, or buy a stake (Phase 38A/B/I).", Shop));
        console.Register(new ConsoleCommand("service", "service [id]", "List services, or use one on the player (Phase 38D).", Service));
        console.Register(new ConsoleCommand("quest", "quest <start|advance|complete|reset> <questId> [objectiveIndex] [amount]", "Drive a quest through its real log, reward, event, and world-change paths (Phase 41F).", Quest));
        console.Register(new ConsoleCommand("savecheck", "savecheck", "Audit registered saveables for volatile (would-orphan) keys (Phase 25.5A).", SaveCheck));

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
        // Optional template id (e.g. `spawn 2 enemy.ashen_acolyte`); defaults to the goblin archetype.
        string templateId = args.Length >= 2 ? args[1] : EnemyTemplateRegistry.FallbackTemplateId;
        for (int i = 0; i < count; i++)
        {
            Vector3 offset = new((GD.Randf() * 2f - 1f) * 4f, 0.5f, (GD.Randf() * 2f - 1f) * 4f);
            EnemyEntity enemy = EnemyTemplateRegistry.Create(templateId, player.GlobalPosition + offset);
            player.GetParent()?.AddChild(enemy);
        }

        return $"spawned {count} x {templateId}";
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

    /// <summary>
    /// Opens a shop's trade window (Phase 38A). This is how the maintainer reaches the screen before
    /// Phase 38E places a merchant in the world: an entity gets one interactable and all three town
    /// vendors already hold a <c>DialogueComponent</c>, so whether trade replaces their conversation
    /// is 38E's call. It publishes the same <see cref="ShopOpenedEvent"/> a
    /// <see cref="VendorComponent"/> does, so nothing about the path being tested is a dev-only shim.
    /// </summary>
    private static string Shop(DevConsole console, string[] args)
    {
        if (args.Length < 1)
        {
            var sb = new StringBuilder($"{ShopDatabase.All.Count} shop(s):\n");
            foreach (ShopResource s in ShopDatabase.All)
            {
                string restock = s.RestockDays > 0 ? $"every {s.RestockDays}d" : "never";

                // 38J: hours and the visit cycle. Without these the console cannot tell a merchant who
                // is shut from one who is out of town, and both look identical from the square.
                string hours = s.OpenHour == s.CloseHour
                    ? "always open"
                    : $"{s.OpenHour:00}:00–{s.CloseHour:00}:00";
                string visits = s.VisitEveryDays > 0
                    ? $", 1 day in {s.VisitEveryDays} (offset {s.VisitDayOffset})"
                    : string.Empty;
                int rungs = s.InvestmentTierList().Count;
                string stake = rungs == 0
                    ? string.Empty
                    : $", stake {(TryService(out ShopStockService held) ? held.InvestmentOf(s) : 0)}/{rungs}";
                sb.Append(
                    $"  {s.Id}  — {s.StockList().Count} row(s), buy x{s.BuyMarkup}, sell x{s.SellFraction}, " +
                    $"restocks {restock}{(s.LeveledTable != null ? ", leveled pool" : string.Empty)}{stake}, " +
                    $"{hours}{visits}\n");
            }

            return sb.ToString().TrimEnd();
        }

        // `shop restock <id>` skips the wait: an in-game day is DayLengthSeconds of real time, so
        // watching a restock happen naturally means three minutes of standing still per day.
        bool forceRestock = args[0].Equals("restock", System.StringComparison.OrdinalIgnoreCase);

        // `shop invest <id>` buys a rung without the gold (38I). A stake is deliberately expensive —
        // reaching the tiered shelves by farming would be an evening's work per rung, and what needs
        // exercising is the gate and the raised purse, not the earning.
        bool invest = args[0].Equals("invest", System.StringComparison.OrdinalIgnoreCase);
        string shopId = forceRestock || invest ? (args.Length > 1 ? args[1] : string.Empty) : args[0];

        if (ShopDatabase.Get(shopId) is not { } shop)
        {
            return $"unknown shop '{shopId}'";
        }

        if (invest)
        {
            if (!TryService(out ShopStockService stakes))
            {
                return "no shop stock service";
            }

            int rungs = shop.InvestmentTierList().Count;
            if (!stakes.Invest(shop))
            {
                return rungs == 0
                    ? $"{shop.Id} sells no stake"
                    : $"{shop.Id} stake already full ({rungs}/{rungs})";
            }

            return $"{shop.Id} stake now {stakes.InvestmentOf(shop)}/{rungs}, purse {stakes.PurseFor(shop)}";
        }

        if (forceRestock)
        {
            if (!TryService(out ShopStockService stock))
            {
                return "no shop stock service";
            }

            stock.ForceRestock(shop);
            return $"restocked {shop.Id} ({stock.OfferFor(shop).Count} row(s) on the shelf)";
        }

        if (!TryPlayer(out PlayerCharacter player))
        {
            return "no player";
        }

        EventBus.Instance?.Publish(new ShopOpenedEvent(player, shop));

        // 38J: the console deliberately opens a shop through its closing time and on a day its merchant
        // is away — inspecting a shelf should not require waiting three real minutes per in-game day.
        // It says so, because a window that opened when the world said "closed" is otherwise the sort of
        // thing that gets reported as the hours being broken.
        bool closed = TryService(out WorldClock clock) &&
            !ShopHours.IsOpenAt(clock.Hour, shop.OpenHour, shop.CloseHour);
        bool away = TryService(out WorldClock dayClock) &&
            !ShopHours.IsInTown(dayClock.Day, shop.VisitEveryDays, shop.VisitDayOffset);

        string note = (closed, away) switch
        {
            (true, true) => " (closed and out of town — dev override)",
            (true, false) => " (closed at this hour — dev override)",
            (false, true) => " (merchant is out of town — dev override)",
            _ => string.Empty,
        };

        return $"opened {shop.Id}{note}";
    }

    /// <summary>
    /// Lists services, or uses one on the player (Phase 38D). It resolves the placed
    /// <see cref="ServiceComponent"/> rather than re-implementing the verbs, so what the console
    /// exercises is the same code path the world does — a dev command that charged and granted by itself
    /// would test nothing but itself.
    /// </summary>
    private static string Service(DevConsole console, string[] args)
    {
        if (args.Length < 1)
        {
            var sb = new StringBuilder($"{ServiceDatabase.All.Count} service(s):\n");
            foreach (ServiceResource s in ServiceDatabase.All)
            {
                string once = s.UnlockFlagId.Length > 0 ? $", once ({s.UnlockFlagId})" : ", per use";
                sb.Append($"  {s.Id}  — {s.Kind}, {s.PriceGold}g{once}\n");
            }

            return sb.ToString().TrimEnd();
        }

        if (!TryPlayer(out PlayerCharacter player))
        {
            return "no player";
        }

        if (ServiceDatabase.Get(args[0]) is not { } service)
        {
            return $"unknown service '{args[0]}'";
        }

        foreach (ServiceComponent component in FindServices(player))
        {
            if (component.ServiceId != service.Id)
            {
                continue;
            }

            string before = component.Prompt;
            component.Interact(player);
            return $"used {service.Id} (prompt was: {(before.Length > 0 ? before : "<silent>")})";
        }

        return $"'{service.Id}' is authored but no ServiceComponent in the loaded world offers it";
    }

    /// <summary>
    /// Drives authored quests through <see cref="QuestLogComponent"/>, not by editing its save
    /// data. That keeps console exercise on the player-facing path: objective events refresh the
    /// journal, completion grants normal rewards, and a completion flag reaches its ordinary world
    /// consumers. Reset deliberately removes only the log entry; a quest completion's story flag is
    /// a separate persistent world fact and this command must not guess that it owns it.
    /// </summary>
    private static string Quest(DevConsole console, string[] args)
    {
        if (args.Length < 2)
        {
            return "usage: quest <start|advance|complete|reset> <questId> [objectiveIndex] [amount]";
        }

        if (!TryPlayer(out PlayerCharacter player) || player.GetComponent<QuestLogComponent>() is not { } log)
        {
            return "no player quest log";
        }

        string action = args[0].ToLowerInvariant();
        string questId = args[1];
        QuestResource? quest = QuestDatabase.Get(questId);
        if (quest == null)
        {
            return $"unknown quest '{questId}'";
        }

        return action switch
        {
            "start" => log.StartQuest(quest)
                ? $"started {quest.Id}"
                : $"cannot start {quest.Id} (already active/completed, or prerequisite unfinished)",
            "advance" => AdvanceQuest(log, quest, args),
            "complete" => CompleteQuest(log, quest),
            "reset" => log.Reset(quest.Id)
                ? $"reset {quest.Id} (persistent story flags and world changes are unchanged)"
                : $"{quest.Id} is not in the log",
            _ => "usage: quest <start|advance|complete|reset> <questId> [objectiveIndex] [amount]",
        };
    }

    private static string AdvanceQuest(QuestLogComponent log, QuestResource quest, string[] args)
    {
        if (args.Length < 3 || !int.TryParse(args[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int index))
        {
            return "usage: quest advance <questId> <objectiveIndex> [amount]";
        }

        int amount = ParseInt(args, 3, 1);
        QuestDebugAdvanceResult result = log.DebugAdvance(quest.Id, index, amount);
        return result == QuestDebugAdvanceResult.Advanced
            ? $"advanced {quest.Id} objective {index}"
            : QuestDebugRules.Describe(result, quest.Id, index);
    }

    private static string CompleteQuest(QuestLogComponent log, QuestResource quest)
    {
        QuestDebugCompleteResult result = log.DebugComplete(quest.Id);
        return result switch
        {
            QuestDebugCompleteResult.Completed => $"completed {quest.Id}",
            QuestDebugCompleteResult.AlreadyCompleted => $"{quest.Id} is already completed",
            QuestDebugCompleteResult.NoLiveObjectives =>
                $"cannot complete {quest.Id}: no live objectives (choose its branch through play first)",
            QuestDebugCompleteResult.NotActive => $"{quest.Id} is not active",
            _ => $"cannot complete {quest.Id}",
        };
    }

    /// <summary>Every placed service in the tree. Walked rather than registered: services are ordinary
    /// interactables on region-cell entities, so they come and go with cell streaming and a registry
    /// would hand out freed nodes.</summary>
    private static System.Collections.Generic.List<ServiceComponent> FindServices(Node from)
    {
        var found = new System.Collections.Generic.List<ServiceComponent>();
        Collect(from.GetTree()?.Root, found);
        return found;

        static void Collect(Node? node, System.Collections.Generic.List<ServiceComponent> into)
        {
            if (node == null)
            {
                return;
            }

            if (node is ServiceComponent service)
            {
                into.Add(service);
            }

            foreach (Node child in node.GetChildren())
            {
                Collect(child, into);
            }
        }
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

    /// <summary>Mounts or dismounts, and with <c>own</c> grants 38D's purchase flag first — the one
    /// thing that otherwise costs 400 gold to reach from a fresh save.</summary>
    private static string Mount(DevConsole console, string[] args)
    {
        if (!TryPlayer(out PlayerCharacter player) || player.GetComponent<MountComponent>() is not { } mount)
        {
            return "no mount component";
        }

        if (args.Length > 0 && args[0].Equals("own", System.StringComparison.OrdinalIgnoreCase))
        {
            player.GetComponent<Dialogue.StoryFlagsComponent>()?.Set(MountComponent.OwnedFlagId);
        }

        mount.Toggle();
        return mount.IsMounted
            ? $"mounted (gallop pool {mount.Stamina:0})"
            : "on foot";
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

    /// <summary>
    /// The guild report and mutator (42A) — the one way to drive membership before 42C authors
    /// dialogue for it.
    ///
    /// ⚠️ <b>Every mutation goes through <c>StoryFlagsComponent.Set/Clear</c>, the same choke point a
    /// dialogue effect uses.</b> Writing a private field here would give the console a second path
    /// into membership, and the first thing that would diverge is the very save/UI behaviour this
    /// sub-phase exists to pin. `rank` sets the whole cumulative run 1..N, because a rank flag on
    /// its own is exactly the hand-authored gap `GuildRules` reports as a contradiction.
    /// </summary>
    private static string Guild(DevConsole console, string[] args)
    {
        if (!TryPlayer(out PlayerCharacter player) ||
            player.GetComponent<Dialogue.StoryFlagsComponent>() is not { } flags)
        {
            return "no story flags";
        }

        System.Predicate<string> has = flags.Has;

        if (args.Length == 0 || args[0].Equals("list", System.StringComparison.OrdinalIgnoreCase))
        {
            var report = new System.Text.StringBuilder();
            foreach (FactionResource g in FactionDatabase.All)
            {
                if (g.IsGuild)
                {
                    report.AppendLine(Describe(g, GuildRules.Resolve(has, g)));
                }
            }

            return report.Length > 0 ? report.ToString().TrimEnd() : "no guilds authored";
        }

        string id = args[0];
        if (FactionDatabase.Get(id) is not { } guild || !guild.IsGuild)
        {
            return $"'{id}' is not a guild (try: guild list)";
        }

        string verb = args.Length > 1 ? args[1].ToLowerInvariant() : "state";
        switch (verb)
        {
            case "state":
                break;
            case "offer":
                flags.Set(GuildRules.OfferedFlag(id));
                break;
            case "refuse":
                // A refusal answers an offer, so it clears the join — the two cannot both be true
                // (GuildContradiction.RefusedAndJoined), and the console must not author the state
                // it reports as broken.
                flags.Clear(GuildRules.JoinedFlag(id));
                flags.Set(GuildRules.RefusedFlag(id));
                break;
            case "join":
            {
                GuildStanding before = GuildRules.Resolve(has, guild);
                if (!GuildRules.CanJoin(before, guild.RejoinAllowed))
                {
                    return $"{guild.DisplayName} will not take you back (RejoinAllowed = false)";
                }

                flags.Clear(GuildRules.RefusedFlag(id));
                flags.Clear(GuildRules.LeftFlag(id));
                flags.Set(GuildRules.OfferedFlag(id));
                flags.Set(GuildRules.JoinedFlag(id));
                break;
            }

            case "rank":
            {
                int rank = ParseInt(args, 2, 1);
                if (rank < 0 || rank > guild.RankNameKeys.Count)
                {
                    return $"rank must be 0..{guild.RankNameKeys.Count}";
                }

                for (int i = 1; i <= GuildRules.MaxRanks; i++)
                {
                    if (i <= rank)
                    {
                        flags.Set(GuildRules.RankFlag(id, i));
                    }
                    else
                    {
                        flags.Clear(GuildRules.RankFlag(id, i));
                    }
                }

                break;
            }

            case "leave":
                flags.Set(GuildRules.LeftFlag(id));
                break;
            case "finale":
                flags.Set(GuildRules.FinaleFlag(id));
                break;
            case "clear":
                flags.Clear(GuildRules.OfferedFlag(id));
                flags.Clear(GuildRules.RefusedFlag(id));
                flags.Clear(GuildRules.JoinedFlag(id));
                flags.Clear(GuildRules.LeftFlag(id));
                flags.Clear(GuildRules.FinaleFlag(id));
                for (int i = 1; i <= GuildRules.MaxRanks; i++)
                {
                    flags.Clear(GuildRules.RankFlag(id, i));
                }

                break;
            default:
                return "usage: guild <list|<guildId> <offer|join|rank N|leave|refuse|finale|clear>>";
        }

        return Describe(guild, GuildRules.Resolve(has, guild));
    }

    private static string Describe(FactionResource guild, GuildStanding standing)
    {
        string rank = standing.Rank > 0 ? $"rank {standing.Rank}/{guild.RankNameKeys.Count}" : "unranked";
        string problem = standing.Contradiction == GuildContradiction.None
            ? string.Empty
            : $"  ⚠ {standing.Contradiction}";
        return $"{guild.Id,-28} {standing.State,-9} {rank}{problem}";
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

    private static string WeaveCmd(DevConsole console, string[] args)
    {
        if (args.Length >= 1)
        {
            string verb = args[0].ToLowerInvariant();
            if (verb == "restore")
            {
                Weave.Reset();
            }
            else if (verb == "set" && args.Length >= 2 &&
                float.TryParse(args[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float p))
            {
                Weave.Set(p);
            }
            else
            {
                return "usage: weave [set <0..1>|restore]";
            }
        }

        return $"Weave potency {Weave.Potency:0.00} | ordinary cast ×{Weave.PowerMultiplier(false):0.00} pow, " +
            $"×{Weave.CostMultiplier(false):0.00} cost | corrupted ×{Weave.PowerMultiplier(true):0.00} pow, " +
            $"×{Weave.CostMultiplier(true):0.00} cost";
    }

    private static string Mastery(DevConsole console, string[] args)
    {
        if (!TryPlayer(out PlayerCharacter player))
        {
            return "no player";
        }

        if (player.GetComponent<SchoolMasteryComponent>() is not { } mastery)
        {
            return "no school-mastery component";
        }

        DamageType[] schools =
        {
            DamageType.Fire, DamageType.Frost, DamageType.Lightning,
            DamageType.Arcane, DamageType.Nature, DamageType.Necrotic,
        };

        var lines = new List<string>();
        foreach (DamageType school in schools)
        {
            lines.Add($"{school}: rank {mastery.RankOf(school)}/{SchoolMasteryMath.MaxRank} " +
                $"({mastery.PointsIn(school)} casts, ×{mastery.PowerMultiplier(school):0.00})");
        }

        return string.Join("\n", lines);
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

    private static string RaceCmd(DevConsole console, string[] args)
    {
        if (args.Length < 1)
        {
            var ids = new List<string>();
            foreach (RaceResource race in RaceDatabase.All)
            {
                ids.Add(race.Id);
            }

            return ids.Count > 0 ? $"races: {string.Join(", ", ids)}" : "no races loaded";
        }

        if (!TryPlayer(out PlayerCharacter player) || player.GetComponent<RaceComponent>() is not { } raceComponent)
        {
            return "no race component";
        }

        return raceComponent.SwapRaceForDebug(args[0]);
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

    /// <summary>
    /// The realm's price landscape (Phase 38N1). Delegates to <see cref="Economy.EconomyReport"/>, the
    /// same function <c>--economy</c> prints headlessly — so the table the maintainer reads at F1 and
    /// the one a tool captures from the command line are the same table.
    ///
    /// 38V's brief reserves an <c>economy</c> command for the full price landscape; this is that
    /// command arriving with its first subcommand rather than a second one to merge later.
    /// </summary>
    private static string Economy(DevConsole console, string[] args)
    {
        if (args.Length >= 1 && args[0] != "arbitrage")
        {
            return "usage: economy [arbitrage]";
        }

        return Embervale.Economy.EconomyReport.Arbitrage();
    }

    /// <summary>
    /// Supply shocks (38T). The roll is a pure function of the day, so the only way to see a specific
    /// one from a session is to force it — waiting for the dice is a week of pressing <c>time</c>.
    /// </summary>
    private static string Shock(DevConsole console, string[] args)
    {
        if (ServiceLocator.Instance is not { } locator ||
            !locator.TryGet(out Embervale.Economy.SupplyShockService shocks))
        {
            return "supply shock service unavailable";
        }

        int day = locator.TryGet(out WorldClock clock) ? clock.Day : 0;

        if (args.Length == 0 || args[0] == "list")
        {
            var sb = new StringBuilder($"supply shocks (day {day}):");
            bool any = false;
            foreach (Embervale.Economy.SupplyShock shock in shocks.ActiveOn(day))
            {
                any = true;
                sb.Append($"\n  {shock.CellId} — {shock.Kind} of '{shock.Tag}', {shock.DaysLeft(day)} day(s) left" +
                    $" ({shocks.DeliveredTo(shock)}/{Embervale.Economy.SupplyShockRules.ReliefUnits} hauled)");
            }

            return any ? sb.ToString() : sb.Append("\n  (none — the roads are quiet)").ToString();
        }

        if (args[0] == "clear" && args.Length >= 2)
        {
            return shocks.Clear(args[1]) ? $"cleared the shock at '{args[1]}'" : "nothing was running there";
        }

        if (args[0] == "force" && args.Length >= 4)
        {
            if (!System.Enum.TryParse(args[3], ignoreCase: true, out Embervale.Economy.ShockKind kind))
            {
                return "kind must be shortage, glut or fair";
            }

            int days = args.Length >= 5 && int.TryParse(args[4], out int parsed)
                ? parsed
                : Embervale.Economy.SupplyShockRules.MinDays;

            shocks.Force(args[1], args[2], kind, day, days);

            return $"{kind} of '{args[2]}' at '{args[1]}' for {days} day(s)";
        }

        return "usage: shock [list|force <cellId> <tag> <shortage|glut|fair> [days]|clear <cellId>]";
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

    /// <summary>Inspects or drives the onboarding, so its hints can be checked without replaying a
    /// new game to reach them.</summary>
    private static string Tutorial(DevConsole console, string[] args)
    {
        if (ServiceLocator.Instance is not { } locator ||
            !locator.TryGet(out Onboarding.TutorialDirector tutorial))
        {
            return "tutorial director unavailable";
        }

        switch (args.Length >= 1 ? args[0] : "status")
        {
            case "skip":
                tutorial.Skip();
                return "tutorial skipped";
            case "restart":
                tutorial.Restart();
                return "tutorial restarted";
            default:
                return tutorial.IsFinished
                    ? "tutorial: finished"
                    : $"tutorial: teaching {tutorial.Step}";
        }
    }

    /// <summary>Replays the prologue, so its pacing and copy can be checked without starting a new
    /// game each time.</summary>
    private static string Opening(DevConsole console, string[] args)
    {
        if (ServiceLocator.Instance is not { } locator || !locator.TryGet(out UI.OpeningSequence opening))
        {
            return "opening sequence unavailable";
        }

        opening.Play(TryPlayer(out PlayerCharacter player)
            ? player.GetComponent<RaceComponent>()?.Profile ?? CharacterProfile.Human
            : CharacterProfile.Human);
        return "replaying the prologue";
    }

    /// <summary>Drives the Phase 32A party from the console: who is recruitable, who is in the band,
    /// what each is doing, and the recruit/dismiss/stance verbs the dialogue hooks will call.</summary>
    private static string Companion(DevConsole console, string[] args)
    {
        if (ServiceLocator.Instance is not { } locator || !locator.TryGet(out CompanionRoster roster))
        {
            return "companion roster unavailable";
        }

        if (args.Length == 0 || args[0] == "list")
        {
            var sb = new StringBuilder($"party {roster.Count}/{roster.MaxPartySize}:");
            foreach (string id in CompanionRegistry.Ids)
            {
                if (!roster.IsRecruited(id))
                {
                    sb.Append($"\n  {id} — not recruited (loyalty {roster.LoyaltyOf(id)}, {roster.TierOf(id)})");
                    continue;
                }

                string state = roster.TryGet(id, out CompanionEntity companion)
                    ? companion.GetComponent<CompanionAIComponent>()?.State.ToString() ?? "?"
                    : "no actor";
                sb.Append($"\n  {id} — {roster.StanceOf(id)} / {state} / loyalty {roster.LoyaltyOf(id)} ({roster.TierOf(id)})");
            }

            return sb.ToString();
        }

        if (args.Length >= 2 && args[0] == "recruit")
        {
            if (!CompanionRegistry.IsRegistered(args[1]))
            {
                return $"unknown companion '{args[1]}'";
            }

            return roster.Recruit(args[1]) ? $"recruited {args[1]}" : $"could not recruit {args[1]}";
        }

        if (args.Length >= 2 && args[0] == "dismiss")
        {
            return roster.Dismiss(args[1]) ? $"dismissed {args[1]}" : $"{args[1]} is not in the party";
        }

        if (args.Length >= 3 && args[0] == "stance")
        {
            CompanionStance stance = args[2].ToLowerInvariant() switch
            {
                "hold" => CompanionStance.Hold,
                "engage" => CompanionStance.Engage,
                _ => CompanionStance.Follow,
            };

            return roster.SetStance(args[1], stance)
                ? $"{args[1]} is now {stance}"
                : $"{args[1]} is not in the party";
        }

        if (args.Length >= 2 && args[0] == "loyalty")
        {
            if (!CompanionRegistry.IsRegistered(args[1]))
            {
                return $"unknown companion '{args[1]}'";
            }

            if (args.Length >= 3 && int.TryParse(args[2], out int delta))
            {
                roster.AddLoyalty(args[1], delta);
            }

            return $"{args[1]} loyalty {roster.LoyaltyOf(args[1])} ({roster.TierOf(args[1])})";
        }

        if (args[0] == "order")
        {
            return roster.Count == 0 ? "party is empty" : $"party order is now {roster.CycleOrder()}";
        }

        return "usage: companion <list|recruit <id>|dismiss <id>|stance <id> <follow|hold|engage>|order|loyalty <id> [delta]>";
    }

    private static string SaveCheck(DevConsole console, string[] args)
    {
        if (SaveManager.Instance is not { } manager)
        {
            return "save manager unavailable";
        }

        var volatileKeys = new List<string>();
        int total = 0;
        foreach (string id in manager.RegisteredSaveIds)
        {
            total++;
            if (SaveKeyPolicy.IsVolatile(id))
            {
                volatileKeys.Add(id);
            }
        }

        if (volatileKeys.Count == 0)
        {
            return $"savecheck OK: {total} registered saveable(s), 0 volatile/would-orphan keys.";
        }

        var sb = new StringBuilder($"savecheck FOUND {volatileKeys.Count} volatile key(s) of {total} (these orphan on reload):");
        foreach (string id in volatileKeys)
        {
            sb.Append($"\n  {id}");
        }

        return sb.ToString();
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
