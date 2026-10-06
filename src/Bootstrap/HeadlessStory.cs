using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Embervale.Companions;
using Embervale.Core.Diagnostics;
using Embervale.Core.Services;
using Embervale.Dialogue;
using Embervale.Enemies;
using Embervale.Localization;
using Embervale.Narrative;
using Embervale.Quests;
using Embervale.Races;
using Embervale.Save;
using Embervale.UI;
using Godot;

namespace Embervale.Bootstrap;

/// <summary>
/// <c>--story</c>: the whole campaign, New Game to credits, played through the real systems three ways
/// in one process. <b>Run A</b> is the clean road (every ember refused, the first answer of every fork:
/// Dray spared, Hjalvar, the herd calmed, the Flock exposed, the Queen released, the knee at the Gate)
/// and must end in the Dawnfire ending. <b>Run B</b> takes every ember through the real absorb
/// conversations and the second answer of every fork (including the Flock's kin, offered only from
/// corruption 40, and the Queen's truce from 60) and must end as the Lord of Embers. <b>Run C</b> loads
/// legacy-save fixtures written through the real save path and checks the catch-up lands each on its
/// next mission, with no reward granted twice, and that the mission can be played to its end.
///
/// "Played" means: each live objective is completed by the event the game raises for it
/// (<see cref="StoryPlaythrough"/>), a quest must auto-start from the flag its predecessor raised
/// (never from the test), the tracker follows the main thread, and a Flamebearer's defeat is a death
/// published on a real boss entity so the director's own flag, relic and ember conversation run. Side
/// variants branch from real saves: the Flock turned, and Kael recruited (the optional Escort advances
/// and never becomes required). Exit 0 PASS / 1 FAIL with the failing step named.
/// It proves the wiring and the reachability of the story, not that a fight can be won (needs a player).
/// </summary>
public static class HeadlessStory
{
    public const string FlagArgument = "--story";
    private const string Slot = "story_probe";

    private static readonly string[] Bosses =
    {
        "enemy.iron_king", "enemy.storm_tyrant", "enemy.beast_lord", "enemy.crimson_prophet",
        "enemy.hollow_queen", "enemy.ashen_knight", "enemy.morthul",
    };

    private static readonly List<string> Failures = new();
    private static readonly List<string> Report = new();

    public static bool Requested() => HeadlessValidation.HasFlag(FlagArgument);

    /// <summary>A developer filter, <c>-- --story --story-only=C</c> (letters A, B, C; blank runs all):
    /// plays only those runs while debugging one of them. A filtered run never replaces the full gate,
    /// and says so in its report; the cross-run coverage is skipped.</summary>
    private static string Only()
    {
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--story-only=", StringComparison.Ordinal))
            {
                return arg["--story-only=".Length..].ToUpperInvariant();
            }
        }

        return string.Empty;
    }

    private static bool Wants(string run) => Only().Length == 0 || Only().Contains(run, StringComparison.Ordinal);

    /// <summary>Records a failed step. Public to the harness; every message names its run and step.</summary>
    internal static void Fail(string message)
    {
        if (!Failures.Contains(message))
        {
            Failures.Add(message);
            Log.Error($"story FAIL: {message}");
        }
    }

    private static void Check(bool ok, string failure)
    {
        if (!ok)
        {
            Fail(failure);
        }
    }

    internal static void Note(string line)
    {
        Report.Add(line);
        Log.Info($"story: {line}");
    }

    public static async void Run(ApplicationRoot root, SessionLifecycleCoordinator lifecycle)
    {
        Log.Info("=== story probe ===");

        // The probe saves and reads back on consecutive lines and builds sessions of its own: no
        // autosave may land between a session being built and the playthrough taking it over, and
        // every write has to be on disk when SaveGame returns.
        AutosaveService.Suppressed = true;
        SaveWriteQueue.ForceInline = true;
        Failures.Clear();
        Report.Clear();
        ulong started = Time.GetTicksMsec();
        try
        {
            await RunAllAsync(root, lifecycle);
        }
        catch (Exception ex)
        {
            Fail($"the gate threw: {ex}");
        }

        if (StoryDriver.Unbaked.Count > 0)
        {
            Note($"{StoryDriver.Unbaked.Count} location(s) had no baked position and were placed from scene text (bake is stale): " +
                 string.Join(", ", StoryDriver.Unbaked));
        }

        Note($"total {(Time.GetTicksMsec() - started) / 1000.0:0.0} s");
        Finish(root);
    }

    private static async Task RunAllAsync(ApplicationRoot root, SessionLifecycleCoordinator lifecycle)
    {
        CheckStatics();
        Note($"scenes readable: {ScenePlacement.Placed != null} ({ScenePlacement.AllBraziers.Count} braziers, {ScenePlacement.Placed?.Count ?? 0} placed dialogues)");

        var completed = new HashSet<string>();
        var forks = new HashSet<string>();

        bool full = Only().Length == 0;
        if (!full)
        {
            Note($"PARTIAL run (--story-only={Only()}): the cross-run coverage is skipped; this is not the gate");
        }

        // --- Run A: the clean road -----------------------------------------------------
        ulong t = Time.GetTicksMsec();
        if (Wants("A"))
        {
            StoryPlaythrough a = new(root, lifecycle, Slot, PlanA());
            if (await a.StartNewGameAsync() && await a.OpeningAsync())
            {
                await PlayRunAAsync(a, lifecycle);
            }

            completed.UnionWith(a.Completed);
            forks.UnionWith(a.ForksTaken);
            Note($"run A: {a.Completed.Count} quests completed in {(Time.GetTicksMsec() - t) / 1000.0:0.0} s");
            Check(a.SawVision, "[A] no Flamebearer vision played after any refused ember conversation");
        }

        // --- Run B: every ember, the second answers -------------------------------------
        t = Time.GetTicksMsec();
        if (Wants("B"))
        {
            StoryPlaythrough b = new(root, lifecycle, Slot, PlanB());
            lifecycle.DestroySession();
            await HeadlessLifecycle.Frames(root, 8);
            if (await b.StartNewGameAsync() && await b.OpeningAsync())
            {
                await PlayRunBAsync(b, lifecycle);
            }

            completed.UnionWith(b.Completed);
            forks.UnionWith(b.ForksTaken);
            Note($"run B: {b.Completed.Count} quests completed in {(Time.GetTicksMsec() - t) / 1000.0:0.0} s");
        }

        // --- branches from A's saves: the Flock turned, Kael recruited -----------------
        t = Time.GetTicksMsec();
        if (full)
        {
            await BranchFlockTurnedAsync(root, lifecycle, forks);
            await BranchKaelAsync(root, lifecycle);
            Note($"branches: {(Time.GetTicksMsec() - t) / 1000.0:0.0} s");
        }

        // --- Run C: legacy saves --------------------------------------------------------
        t = Time.GetTicksMsec();
        if (Wants("C"))
        {
            await LegacyFixtures.RunAsync(root, lifecycle, Slot);
            Note($"run C: legacy fixtures in {(Time.GetTicksMsec() - t) / 1000.0:0.0} s");
        }

        // --- across the runs ----------------------------------------------------------------
        if (full)
        {
            Note($"coverage: {completed.Count}/{CampaignCatchUp.Missions.Count} main quests completed through play; fork answers exercised: {string.Join(", ", forks)}");
            foreach (CampaignCatchUp.Mission mission in CampaignCatchUp.Missions)
            {
                Check(completed.Contains(mission.QuestId), $"main quest '{mission.QuestId}' (mission {mission.Index}) was never completed through play in runs A and B");
            }

            foreach ((string slug, string[] flags) in EndingSequence.ForkEpilogues)
            {
                foreach (string flag in flags)
                {
                    Check(forks.Contains(flag), $"fork answer '{flag}' ({slug}) was never exercised by any run");
                }
            }
        }

        foreach (string slot in new[] { Slot, Slot + "_checkpoint", Slot + "_flock", Slot + "_kael" })
        {
            SaveManager.Instance?.DeleteSlot(slot);
        }
    }

    // --- run plans --------------------------------------------------------------------

    private static readonly string[] OpeningActI =
    {
        "quest.main.smoke_over_the_square", "quest.main.the_pass_kept", "quest.warband.bounty",
        "quest.main.crossway_whispers", "quest.warband.forge", "quest.warband.remedies",
        "quest.main.citadel_approach", "quest.warband.heart", "quest.main.iron_king",
    };

    private static readonly string[] ArcFrostfang = { "quest.main.closed_hold", "quest.main.succession", "quest.main.storm_tyrant" };
    private static readonly string[] ArcAshen = { "quest.main.last_hearth", "quest.main.herd_and_hearth", "quest.main.beast_lord" };
    private static readonly string[] ArcSunspire = { "quest.main.dry_wells", "quest.main.prophets_flock", "quest.main.crimson_prophet" };

    private static readonly string[] PaleAndAfter =
    {
        "quest.main.pale_door", "quest.main.vesperhold", "quest.main.queens_count", "quest.main.hidden",
        "quest.main.sundering_pages", "quest.main.deep_stacks", "quest.main.truth",
        "quest.main.celestial_landing", "quest.main.godfall_choir", "quest.main.sundered_stair",
        "quest.main.celestial", "quest.main.throne",
    };

    private static string[] Order(params string[][] parts)
    {
        var list = new List<string>();
        foreach (string[] part in parts)
        {
            list.AddRange(part);
        }

        return list.ToArray();
    }

    private static Dictionary<string, string[]> CommonPicks() => new()
    {
        // The hand-in takes the four herbs the remedies errand asked for.
        ["dialogue.apothecary"] = new[] { "dlg.apothecary.cq_rem_check.c_herbs" },
    };

    private static RunPlan PlanA()
    {
        Dictionary<string, string[]> picks = CommonPicks();
        picks["dialogue.dray"] = new[] { "dlg.dray.root.c_spare" };
        picks["dialogue.frostfang_moot_stone"] = new[] { "dlg.frostfang_moot_stone.root.c_hjalvar" };
        picks["dialogue.ashen_herd_choice"] = new[] { "dlg.ashen_herd_choice.root.c_calm" };
        picks["dialogue.flock_deacon"] = new[] { "dlg.flock_deacon.root.c_expose" };
        picks["dialogue.pale_count_stone"] = new[] { "pale.dlg.pale_count_stone.root.c_release" };
        picks["dialogue.rival_gate"] = new[] { ".c_kneel" };
        picks["dialogue.ash_throne"] = new[] { "dlg.throne.c_approach", "dlg.throne.c_refuse", "dlg.throne.c_dawn_yes" };
        return new RunPlan
        {
            Name = "A",
            Absorb = false,
            DoOptional = false,
            // three herbs for the calm answer: the Last Hearth errand's own optional gathering
            OptionalOnly = { "quest.main.last_hearth#3" },
            Order = Order(OpeningActI, ArcAshen, ArcFrostfang, ArcSunspire, PaleAndAfter),
            Picks = picks,
            ExpectedForks = new[]
            {
                "flag.fork.dray_spared", "flag.fork.succession_hjalvar", "flag.fork.herd_calmed",
                "flag.fork.flock_exposed", "flag.fork.queen_released", "flag.rival.gate_kneel",
            },
        };
    }

    private static RunPlan PlanB()
    {
        Dictionary<string, string[]> picks = CommonPicks();
        picks["dialogue.dray"] = new[] { "dlg.dray.root.c_press" };
        picks["dialogue.frostfang_moot_stone"] = new[] { "dlg.frostfang_moot_stone.root.c_halvar" };
        picks["dialogue.ashen_herd_choice"] = new[] { "dlg.ashen_herd_choice.root.c_slay" };
        picks["dialogue.flock_deacon"] = new[] { "dlg.flock_deacon.root.c_kin" };
        picks["dialogue.pale_count_stone"] = new[] { "pale.dlg.pale_count_stone.root.c_keep" };
        picks["dialogue.rival_gate"] = new[] { ".c_draw" };
        picks["dialogue.ash_throne"] = new[] { "dlg.throne.c_approach", "dlg.throne.c_sit", "dlg.throne.c_embers_yes" };
        return new RunPlan
        {
            Name = "B",
            Absorb = true,
            DoOptional = true,
            // corruption is over 40 by the Choir: its echo answers instead of the nave being cleared
            Avoid = { "quest.main.godfall_choir#1" },
            // the Frostfang arc first: two embers (corruption 50) before the Flock, whose kin answer needs 40
            Order = Order(OpeningActI, ArcFrostfang, ArcSunspire, ArcAshen, PaleAndAfter),
            Picks = picks,
            ExpectedForks = new[]
            {
                "flag.fork.dray_pressed", "flag.fork.succession_halvar", "flag.fork.herd_slain",
                "flag.fork.flock_kin", "flag.fork.queen_kept", "flag.rival.gate_draw",
            },
        };
    }

    // --- run A / B ----------------------------------------------------------------------

    private static async Task PlayRunAAsync(StoryPlaythrough run, SessionLifecycleCoordinator lifecycle)
    {
        bool ok = await run.PlayQuestsAsync(run.PlanOrder(), async questId =>
        {
            await AfterQuestAsync(run, questId, "A", dawnfire: true);
        });
        if (!ok)
        {
            return;
        }

        await FinishEndingAsync(run, "A", dawnfire: true, lifecycle);
    }

    private static async Task PlayRunBAsync(StoryPlaythrough run, SessionLifecycleCoordinator lifecycle)
    {
        bool ok = await run.PlayQuestsAsync(run.PlanOrder(), async questId =>
        {
            await AfterQuestAsync(run, questId, "B", dawnfire: false);
        });
        if (!ok)
        {
            return;
        }

        await FinishEndingAsync(run, "B", dawnfire: false, lifecycle);
    }

    /// <summary>Act-level assertions and the save checkpoints, after each completed quest.</summary>
    private static async Task AfterQuestAsync(StoryPlaythrough run, string questId, string name, bool dawnfire)
    {
        switch (questId)
        {
            case "quest.main.smoke_over_the_square" when dawnfire:
                SaveManager.Instance?.SaveGame(Slot + "_kael");
                break;

            case "quest.main.iron_king":
                ExpectActiveMain(run, name, "after the Iron King",
                    "quest.main.closed_hold", "quest.main.last_hearth", "quest.main.dry_wells");
                Check(run.Log.IsActive("quest.main.gathering"), $"[{name}] the Act II ledger did not start on the Iron King's defeat");
                Check(!run.Flags.Has("flag.pale_concord_revealed"), $"[{name}] the hidden realm revealed before Act II was done");
                break;

            case "quest.main.dry_wells" when dawnfire:
                SaveManager.Instance?.SaveGame(Slot + "_flock");
                break;

            case "quest.main.crimson_prophet" when dawnfire:
            case "quest.main.beast_lord" when !dawnfire:
                // the last arc quest of each run: all three Flamebearers have fallen
                await AfterArcsAsync(run, name);
                if (dawnfire)
                {
                    await run.CheckpointAsync("after the Act II arcs");
                }

                break;

            case "quest.main.hidden":
                Check(run.Flags.Has("flag.hollow_queen_defeated"), $"[{name}] the Hollow Queen's defeat flag is missing after the hidden quest");
                if (dawnfire)
                {
                    await run.CheckpointAsync("after the Pale Concord");
                }

                break;

            case "quest.main.truth":
                Check(run.Flags.Has("flag.celestial_gate_open"), $"[{name}] the Truth quest completed without opening the Celestial gate");
                ExpectActiveMain(run, name, "after the Truth", "quest.main.celestial_landing");
                break;

            case "quest.main.godfall_choir":
                // the corruption-gated route: under 40 the echoes are broken, from 40 the Choir answers
                Check(run.Flags.Has("flag.beat.choir_silenced") == dawnfire && run.Flags.Has("flag.beat.choir_answered") == !dawnfire,
                    $"[{name}] the Choir was passed the wrong way at corruption {run.Corruption.Value}: " +
                    $"silenced={run.Flags.Has("flag.beat.choir_silenced")} answered={run.Flags.Has("flag.beat.choir_answered")}");
                if (dawnfire)
                {
                    Check(run.Driver.OpenDialogue("dialogue.choir_echo") != null && run.Driver.NodeId == "unknown",
                        $"[A] the Choir's echo opened on '{run.Driver.NodeId}' for a clean hand (it should not know it)");
                    run.Driver.EndDialogue();
                    await run.CheckpointAsync("in Act IV");
                }

                break;

            case "quest.main.sundered_stair":
                ExpectActiveMain(run, name, "after the Sundered Stair", "quest.main.celestial");
                break;

            case "quest.main.celestial":
                Check(run.Flags.Has("flag.morthul_defeated") && run.Flags.Has("flag.ashen_knight_defeated"),
                    $"[{name}] the Celestial assault completed without both bosses' defeat flags");
                ExpectActiveMain(run, name, "after Morthul", "quest.main.throne");
                if (dawnfire)
                {
                    await ThroneOffersAsync(run);
                }
                else
                {
                    Check(run.Corruption.Value >= 60, $"[{name}] every ember taken yet corruption is only {run.Corruption.Value}");
                }

                break;
        }

        await Task.CompletedTask;
    }

    /// <summary>The Pale Concord reveal after the third Flamebearer, and the Act II ledger done.</summary>
    private static async Task AfterArcsAsync(StoryPlaythrough run, string name)
    {
        await run.IdleAsync("the Pale Concord reveal", 6);
        Check(run.Flags.Has("flag.pale_concord_revealed"), $"[{name}] three Act II Flamebearers fell but the hidden realm was not revealed");
        Check(run.Flags.Has("flag.beat.arcs_complete"), $"[{name}] the three arcs are done but flag.beat.arcs_complete was not derived");
        Check(run.Log.IsCompleted("quest.main.gathering"), $"[{name}] the Act II ledger is not complete after all three Flamebearers fell");
        Check(run.SawPaleReveal, $"[{name}] the Pale Concord reveal never played its card");
    }

    /// <summary>The main quests active right now are exactly <paramref name="expected"/> (the ledger is not a main quest here).</summary>
    private static void ExpectActiveMain(StoryPlaythrough run, string name, string when, params string[] expected)
    {
        var active = new List<string>();
        foreach (QuestProgress progress in run.Log.Quests)
        {
            if (progress.Status == QuestStatus.Active && progress.Quest.IsMainQuest && !progress.Quest.IsLedger)
            {
                active.Add(progress.Quest.Id);
            }
        }

        foreach (string quest in expected)
        {
            Check(active.Contains(quest), $"[{name}] {when}: '{quest}' should have started by itself (active main quests: [{string.Join(", ", active)}])");
        }

        foreach (string quest in active)
        {
            Check(Array.IndexOf(expected, quest) >= 0,
                $"[{name}] {when}: '{quest}' is active but not expected (active main quests: [{string.Join(", ", active)}])");
        }
    }

    private static async Task ThroneOffersAsync(StoryPlaythrough run)
    {
        StoryDriver driver = run.Driver;
        int home = run.Corruption.Value;
        foreach ((int corruption, bool refuse, bool sit) in new[] { (10, true, false), (50, true, true), (70, false, true) })
        {
            Check(driver.SetCorruption(corruption), $"[A] the driver could not set corruption to {corruption}");
            Check(driver.OpenDialogue("dialogue.ash_throne") != null, "[A] dialogue.ash_throne is missing");
            Check(driver.ChooseText("dlg.throne.c_approach"), $"[A] at corruption {corruption} the throne offered no approach");
            List<string> offered = driver.ChoiceKeys();
            Check(offered.Contains("dlg.throne.c_refuse") == refuse && offered.Contains("dlg.throne.c_sit") == sit,
                $"[A] at corruption {corruption} the throne offered [{string.Join(", ", offered)}]");
            driver.EndDialogue();
        }

        Check(driver.SetCorruption(home), "[A] the driver could not restore corruption after the throne sweep");
        await Task.CompletedTask;
    }

    /// <summary>The ending: flags, the card script with its fork epilogues, the cards' text, the sky.</summary>
    private static async Task FinishEndingAsync(
        StoryPlaythrough run, string name, bool dawnfire, SessionLifecycleCoordinator lifecycle)
    {
        string chosen = dawnfire ? EndingSequence.DawnfireFlag : EndingSequence.EmbersFlag;
        string other = dawnfire ? EndingSequence.EmbersFlag : EndingSequence.DawnfireFlag;
        Check(run.Flags.Has(chosen), $"[{name}] the throne did not set {chosen}");
        Check(!run.Flags.Has(other), $"[{name}] the throne also set {other}");
        Check(run.Log.IsCompleted("quest.main.throne"), $"[{name}] the throne quest is not complete after the ending choice");
        if (dawnfire)
        {
            Check(run.Corruption.Value < 40, $"[A] the clean road ended at corruption {run.Corruption.Value}");
        }
        else
        {
            Check(run.Corruption.Value >= 60, $"[B] the ember road ended at corruption {run.Corruption.Value} (expected 60 and over)");
        }

        run.RecordForks();
        foreach (string fork in dawnfire ? PlanA().ExpectedForks : PlanB().ExpectedForks)
        {
            Check(run.Flags.Has(fork), $"[{name}] fork flag {fork} is not held at the end");
        }

        int absorbed = EndingSequence.CountAbsorbed(run.Flags.Has);
        Check(dawnfire ? absorbed == 0 : absorbed == 6, $"[{name}] {absorbed} embers absorbed (expected {(dawnfire ? 0 : 6)})");

        string[] script = EndingSequence.Script(dawnfire, absorbed, run.Flags.Has);
        string[] forkCards = EndingSequence.ForkCards(run.Flags.Has);
        Check(forkCards.Length == 6, $"[{name}] the ending's fork epilogue has {forkCards.Length} card(s), expected 6: [{string.Join(", ", forkCards)}]");
        foreach (string card in forkCards)
        {
            Check(Array.IndexOf(script, card) >= 0, $"[{name}] the ending script lacks the fork card '{card}'");
        }

        foreach (string card in script)
        {
            Check(Loc.Has(card), $"[{name}] ending card '{card}' has no text");
        }

        // The ending sets its sky the moment its flag is raised; the cards that follow run for a minute of game time.
        string sky = dawnfire ? "weather.dawnfire" : "weather.embers";
        for (int i = 0; i < 60 && StoryPlaythrough.Sky() != sky; i++)
        {
            await run.FramesAsync(10);
        }

        Check(StoryPlaythrough.Sky() == sky, $"[{name}] the ending did not bring {sky} (sky '{StoryPlaythrough.Sky()}')");
        await run.IdleAsync($"the {(dawnfire ? "Dawnfire" : "Lord of Embers")} ending", 8);
        Check(run.Flags.Has(EndingSequence.CompleteFlag), $"[{name}] the ending played but {EndingSequence.CompleteFlag} was not set");

        if (!dawnfire)
        {
            // a save holding weather the story forbids: the ember sky is re-derived from the flags on load
            StoryPlaythrough.ForceSky("weather.rain");
            if (await run.ReloadAsync(Slot + "_checkpoint", "the post-ending reload"))
            {
                for (int i = 0; i < 60 && StoryPlaythrough.Sky() != sky; i++)
                {
                    await run.FramesAsync(10);
                }

                Check(StoryPlaythrough.Sky() == sky,
                    $"[B] after load the ember sky was not re-derived from the ending flag (sky '{StoryPlaythrough.Sky()}')");
            }
        }

        _ = lifecycle;
    }

    // --- branches -----------------------------------------------------------------------

    private static async Task BranchFlockTurnedAsync(
        ApplicationRoot root, SessionLifecycleCoordinator lifecycle, HashSet<string> forks)
    {
        RunPlan plan = PlanA();
        plan.Picks["dialogue.flock_deacon"] = new[] { "dlg.flock_deacon.root.c_turn" };
        var run = new StoryPlaythrough(root, lifecycle, Slot, new RunPlan
        {
            Name = "A/flock-turned", Absorb = false, DoOptional = false, Order = new[] { "quest.main.prophets_flock" },
            Picks = plan.Picks, ExpectedForks = new[] { "flag.fork.flock_turned" },
        });
        if (!SaveManager.Instance!.SaveExists(Slot + "_flock") || !await run.LoadAsync(Slot + "_flock", "the Flock branch"))
        {
            Fail("[A/flock-turned] the branch save from run A is missing");
            return;
        }

        if (await run.PlayQuestsAsync(new[] { "quest.main.prophets_flock" }))
        {
            Check(run.Flags.Has("flag.fork.flock_turned") && !run.Flags.Has("flag.fork.flock_exposed") && !run.Flags.Has("flag.fork.flock_kin"),
                "[A/flock-turned] the Flock's turned answer did not leave exactly flag.fork.flock_turned");
            Check(run.Log.IsActive("quest.main.crimson_prophet"), "[A/flock-turned] the Prophet's quest did not start after the Flock was turned");
            run.RecordForks();
            forks.UnionWith(run.ForksTaken);
        }
    }

    private static async Task BranchKaelAsync(ApplicationRoot root, SessionLifecycleCoordinator lifecycle)
    {
        foreach (bool escort in new[] { true, false })
        {
            string name = escort ? "A/kael-escort" : "A/kael-skipped";
            var plan = PlanA();
            var run = new StoryPlaythrough(root, lifecycle, Slot, new RunPlan
            {
                Name = name, Absorb = false, DoOptional = false, Order = new[] { "quest.main.the_pass_kept" },
                Picks = plan.Picks, ExpectedForks = Array.Empty<string>(),
                Companion = "companion.kael", EscortCompanion = escort,
            });
            if (!SaveManager.Instance!.SaveExists(Slot + "_kael") || !await run.LoadAsync(Slot + "_kael", name))
            {
                Fail($"[{name}] the branch save from run A is missing");
                return;
            }

            await run.KaelSegmentAsync();
        }
    }

    // --- the driver itself ---------------------------------------------------------------

    /// <summary>
    /// Proves the driver on an in-memory quest, objective type by objective type through the real event
    /// paths: Milestone (flag), Interact, Kill (and that a kill of an objective not yet live does not
    /// count), Talk, Reach, Defend. The quest is left one step short on purpose and then reset: nothing
    /// here may complete a quest (completion itself is covered by the unit tests and by the runs).
    /// </summary>
    internal static async Task CheckDriverObjectivesAsync(ApplicationRoot root, StoryPlaythrough run)
    {
        StoryDriver driver = run.Driver;
        QuestLogComponent log = run.Log;
        StoryFlagsComponent flags = run.Flags;
        const string questId = "quest.test.driver";
        const string gate = "flag.test.driver_gate";
        string place = driver.NearestLocation();
        Check(place.Length > 0, "no map location resolves to a position for the driver's Reach/Defend");

        var list = new List<ObjectiveResource>
        {
            StoryDriver.Objective(ObjectiveType.Milestone, gate),
            StoryDriver.Objective(ObjectiveType.Interact, "interact.test.driver_lever"),
            StoryDriver.Objective(ObjectiveType.Kill, "enemy.goblin", 2),
            StoryDriver.Objective(ObjectiveType.Talk, "dialogue.ash_throne"),
            StoryDriver.Objective(ObjectiveType.Collect, "item.material.iron_ore", 2),
        };
        if (place.Length > 0)
        {
            list.Add(StoryDriver.Objective(ObjectiveType.Reach, place));
            list.Add(StoryDriver.Objective(ObjectiveType.Defend, place, 2));
        }

        // A last step nothing ever meets: the quest must not complete here.
        list.Add(StoryDriver.Objective(ObjectiveType.Milestone, "flag.test.driver_never"));

        QuestResource quest = StoryDriver.Quest(questId, true, list.ToArray());
        Check(log.StartQuest(quest), "the in-memory driver quest did not start");
        string state() => driver.Report(questId);

        driver.Kill("enemy.goblin"); // objective 2 is not live yet: must not count
        driver.Interact("interact.test.driver_lever");
        Check(!log.IsCompleted(questId) && state().Contains("[0* Milestone"),
            $"events for objectives that are not live advanced the quest: {state()}");

        flags.Set(gate);
        Check(state().Contains("[0+ Milestone") && state().Contains("[1* Interact"), $"the milestone flag did not open the next step: {state()}");

        driver.Interact("interact.other"); // wrong id
        Check(state().Contains("[1* Interact"), $"an interactable with another id advanced the objective: {state()}");
        driver.Interact("interact.test.driver_lever");
        Check(state().Contains("[1+ Interact") && state().Contains("[2* Kill"), $"Interact did not complete: {state()}");

        driver.Kill("enemy.goblin", 2);
        Check(state().Contains("[2+ Kill") && state().Contains("[3* Talk"), $"Kill did not complete: {state()}");

        Check(driver.Talk("dialogue.ash_throne"), "the driver could not hold the throne conversation");
        Check(state().Contains("[3+ Talk") && state().Contains("[4* Collect"), $"Talk did not complete: {state()}");

        Check(driver.Pickup("item.material.iron_ore", 2), "the driver could not pick up iron ore");
        Check(state().Contains("[4+ Collect"), $"Collect did not complete through the pickup event: {state()}");

        if (place.Length > 0)
        {
            Check(await driver.Reach(place, () => state().Contains("[5+ Reach")), $"Reach did not complete: {state()}");
            Check(await driver.Defend(place, () => state().Contains("[6+ Defend")), $"Defend did not complete: {state()}");
            Check(state().Contains("[7* Milestone") && log.IsActive(questId),
                $"the quest should wait on its last step: {state()}");
        }

        log.Reset(questId);
        flags.Clear(gate);
        await HeadlessLifecycle.Frames(root, 2);
    }

    // --- statics ------------------------------------------------------------------------

    private static void CheckStatics()
    {
        foreach (string id in Bosses)
        {
            EnemyEntity enemy = EnemyTemplateRegistry.Create(id, Vector3.Zero);
            Check(enemy is BossEntity, $"'{id}' does not build a BossEntity");
            enemy.Free();
        }

        foreach (string name in VisionSequence.Visions.Values)
        {
            for (int i = 1; i <= 3; i++)
            {
                Check(Loc.Has($"vision.{name}.{i}"), $"vision card 'vision.{name}.{i}' has no text");
            }
        }

        CheckRivalDuels();

        foreach (bool dawnfire in new[] { true, false })
        {
            foreach (int absorbed in new[] { 0, 3, 6 })
            {
                foreach (string card in EndingSequence.Script(dawnfire, absorbed))
                {
                    Check(Loc.Has(card), $"ending card '{card}' has no text");
                }
            }
        }

        foreach ((string slug, string[] flags) in EndingSequence.ForkEpilogues)
        {
            for (int i = 0; i < flags.Length; i++)
            {
                string key = $"ending.epilogue.{slug}.{(char)('a' + i)}";
                Check(Loc.Has(key), $"fork epilogue card '{key}' has no text");
            }
        }

        foreach (CampaignCatchUp.Mission mission in CampaignCatchUp.Missions)
        {
            Check(QuestDatabase.Get(mission.QuestId) != null, $"mission {mission.Index} '{mission.QuestId}' is not a quest in the database");
        }

        // Every Flamebearer has a brazier that summons it (scene text; skipped where scenes are not readable).
        if (ScenePlacement.Placed != null)
        {
            foreach (string id in Bosses)
            {
                var found = false;
                foreach (SceneBrazier _ in ScenePlacement.Braziers(id))
                {
                    found = true;
                }

                Check(found, $"no scene has a brazier that summons '{id}'");
            }
        }
    }

    /// <summary>Phase 47.5: both duel fights yield and record their own flag and conversation; the
    /// Act IV fight is still to the death on the registry's flag.</summary>
    private static void CheckRivalDuels()
    {
        for (int n = 1; n <= 2; n++)
        {
            BossResource? duel = BossDatabase.Get($"boss.ashen_knight_duel{n}");
            Check(duel != null, $"boss.ashen_knight_duel{n} is missing");
            if (duel == null)
            {
                continue;
            }

            Check(duel.WithdrawHealthFraction > 0f, $"duel {n} does not withdraw; the rival would die in Act II");
            Check(duel.DefeatFlagId == $"flag.rival.duel{n}_won", $"duel {n} records '{duel.DefeatFlagId}'");
            Check(DialogueDatabase.Get(duel.DefeatDialogueId) != null, $"duel {n} has no parting conversation");
        }

        BossResource? final = BossDatabase.Get("boss.ashen_knight");
        Check(final is { WithdrawHealthFraction: 0f, DefeatFlagId: "flag.ashen_knight_defeated" },
            "the Act IV Ashen Knight must fight to the death and set flag.ashen_knight_defeated");
    }

    private static void Finish(ApplicationRoot root)
    {
        SaveManager.Instance?.DeleteSlot(Slot);
        foreach (string line in Report)
        {
            Log.Info($"story report: {line}");
        }

        foreach (string failure in Failures)
        {
            Log.Error($"story: {failure}");
        }

        Log.Info(Failures.Count == 0 ? "story: PASS" : $"story: FAIL ({Failures.Count} failure(s))");
        root.GetTree().Quit(Failures.Count == 0 ? 0 : 1);
    }
}
