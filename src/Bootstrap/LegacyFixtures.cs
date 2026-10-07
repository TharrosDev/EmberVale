using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Embervale.Core.Diagnostics;
using Embervale.Narrative;
using Embervale.Quests;
using Embervale.Save;
using Godot;

namespace Embervale.Bootstrap;

/// <summary>
/// Run C of <c>--story</c>: saves written by the previous build, rebuilt as fixtures and loaded through
/// the real save path. A legacy save carries only the old flags (boss defeats, the reveal, the gate, the
/// ending) and old quest states, and none of the campaign's mission flags; <c>CampaignCatchUp</c> must
/// land it on the right next mission with no reward granted a second time, and that mission (and the
/// ones after it) must be playable to the end.
/// </summary>
internal static class LegacyFixtures
{
    private sealed record Fixture(
        string Name,
        string[] Flags,
        (string Quest, QuestStatus Status, int[] Counts)[] Quests,
        int ThroughMission,
        string[] ExpectActive,
        string[] PlayNext,
        (string Item, int Count)[]? Held = null);

    private static readonly string[] WarbandDone = { "flag.frostfang.passage_open" };

    private static IEnumerable<Fixture> Fixtures()
    {
        var done = QuestStatus.Completed;
        var live = QuestStatus.Active;

        yield return new Fixture(
            "a save from before the first mission",
            Array.Empty<string>(),
            Array.Empty<(string, QuestStatus, int[])>(),
            ThroughMission: 0,
            ExpectActive: new[] { "quest.main.smoke_over_the_square" },
            PlayNext: new[] { "quest.main.smoke_over_the_square", "quest.main.the_pass_kept" });

        yield return new Fixture(
            "mid warband chain, the forge errand done",
            Array.Empty<string>(),
            new[]
            {
                ("quest.warband.bounty", done, new[] { 3 }),
                ("quest.warband.forge", done, new[] { 3 }),
                ("quest.warband.remedies", live, new[] { 2 }),
            },
            ThroughMission: 5,
            ExpectActive: new[] { "quest.warband.remedies" },
            PlayNext: new[] { "quest.warband.remedies", "quest.main.citadel_approach" },
            // the two herbs the old save had already picked up
            Held: new[] { ("item.material.healing_herb", 2) });

        yield return new Fixture(
            "the Iron King and the Storm Tyrant fallen",
            new[] { "flag.frostfang.passage_open", "flag.iron_king_defeated", "flag.storm_tyrant_defeated" },
            new[]
            {
                ("quest.warband.bounty", done, new[] { 3 }),
                ("quest.warband.forge", done, new[] { 3 }),
                ("quest.warband.remedies", done, new[] { 4 }),
                ("quest.warband.heart", done, new[] { 5 }),
                ("quest.main.gathering", live, new[] { 1, 0, 0 }),
            },
            ThroughMission: 12,
            ExpectActive: new[] { "quest.main.last_hearth", "quest.main.dry_wells" },
            PlayNext: new[] { "quest.main.last_hearth", "quest.main.dry_wells", "quest.main.prophets_flock" });

        yield return new Fixture(
            "the Pale Concord revealed, the Queen still standing",
            new[]
            {
                "flag.frostfang.passage_open", "flag.iron_king_defeated", "flag.storm_tyrant_defeated",
                "flag.beast_lord_defeated", "flag.crimson_prophet_defeated", "flag.pale_concord_revealed",
            },
            new[]
            {
                ("quest.warband.bounty", done, new[] { 3 }),
                ("quest.warband.forge", done, new[] { 3 }),
                ("quest.warband.remedies", done, new[] { 4 }),
                ("quest.warband.heart", done, new[] { 5 }),
                ("quest.main.gathering", done, new[] { 1, 1, 1 }),
                ("quest.main.hidden", live, new[] { 0 }),
            },
            ThroughMission: 18,
            // the old flow had started the Queen's quest; nothing in it is done, so it steps back until the court opens
            ExpectActive: new[] { "quest.main.pale_door" },
            PlayNext: new[] { "quest.main.pale_door", "quest.main.vesperhold", "quest.main.queens_count", "quest.main.hidden" });

        yield return new Fixture(
            "the Celestial gate open",
            new[]
            {
                "flag.frostfang.passage_open", "flag.iron_king_defeated", "flag.storm_tyrant_defeated",
                "flag.beast_lord_defeated", "flag.crimson_prophet_defeated", "flag.pale_concord_revealed",
                "flag.hollow_queen_defeated", "flag.celestial_gate_open",
            },
            new[]
            {
                ("quest.warband.bounty", done, new[] { 3 }),
                ("quest.warband.forge", done, new[] { 3 }),
                ("quest.warband.remedies", done, new[] { 4 }),
                ("quest.warband.heart", done, new[] { 5 }),
                ("quest.main.gathering", done, new[] { 1, 1, 1 }),
                ("quest.main.hidden", done, new[] { 1 }),
                ("quest.main.truth", done, new[] { 1 }),
                ("quest.main.celestial", live, new[] { 0, 0 }),
            },
            ThroughMission: 25,
            // the old flow had started the assault; nothing in it is done, so it steps back until the Stair is climbed
            ExpectActive: new[] { "quest.main.celestial_landing" },
            PlayNext: new[]
            {
                "quest.main.celestial_landing", "quest.main.godfall_choir", "quest.main.sundered_stair",
                "quest.main.celestial", "quest.main.throne",
            });

        yield return new Fixture(
            "the game complete",
            new[]
            {
                "flag.frostfang.passage_open", "flag.iron_king_defeated", "flag.storm_tyrant_defeated",
                "flag.beast_lord_defeated", "flag.crimson_prophet_defeated", "flag.pale_concord_revealed",
                "flag.hollow_queen_defeated", "flag.celestial_gate_open", "flag.ashen_knight_defeated",
                "flag.morthul_defeated", "flag.ending_dawnfire", "flag.game_complete",
            },
            new[]
            {
                ("quest.warband.bounty", done, new[] { 3 }),
                ("quest.warband.forge", done, new[] { 3 }),
                ("quest.warband.remedies", done, new[] { 4 }),
                ("quest.warband.heart", done, new[] { 5 }),
                ("quest.main.gathering", done, new[] { 1, 1, 1 }),
                ("quest.main.hidden", done, new[] { 1 }),
                ("quest.main.truth", done, new[] { 1 }),
                ("quest.main.celestial", done, new[] { 1, 1 }),
            },
            ThroughMission: 30,
            ExpectActive: Array.Empty<string>(),
            PlayNext: Array.Empty<string>());
    }

    /// <summary>Run C's choices: the clean road's picks, no embers, no optional objectives.</summary>
    private static RunPlan PlanC()
    {
        return new RunPlan
        {
            Name = "C",
            Absorb = false,
            DoOptional = false,
            OptionalOnly = { "quest.main.last_hearth#3" },
            Order = Array.Empty<string>(),
            Picks = new Dictionary<string, string[]>
            {
                ["dialogue.apothecary"] = new[] { "dlg.apothecary.cq_rem_check.c_herbs" },
                ["dialogue.dray"] = new[] { "dlg.dray.root.c_spare" },
                ["dialogue.frostfang_moot_stone"] = new[] { "dlg.frostfang_moot_stone.root.c_hjalvar" },
                ["dialogue.ashen_herd_choice"] = new[] { "dlg.ashen_herd_choice.root.c_calm" },
                ["dialogue.flock_deacon"] = new[] { "dlg.flock_deacon.root.c_expose" },
                ["dialogue.pale_count_stone"] = new[] { "pale.dlg.pale_count_stone.root.c_release" },
                ["dialogue.rival_gate"] = new[] { ".c_kneel" },
                ["dialogue.ash_throne"] = new[] { "dlg.throne.c_approach", "dlg.throne.c_refuse", "dlg.throne.c_dawn_yes" },
            },
            ExpectedForks = Array.Empty<string>(),
        };
    }

    /// <summary>The missions each fixture leaves done ("through N"), for <c>--story-mission</c> to
    /// choose a start from.</summary>
    public static List<int> Frontiers()
    {
        var frontiers = new List<int>();
        foreach (Fixture fixture in Fixtures())
        {
            frontiers.Add(fixture.ThroughMission);
        }

        return frontiers;
    }

    /// <summary>
    /// <c>--story-mission</c>: plays missions <paramref name="first"/> to <paramref name="last"/>
    /// (1-based, inclusive) without the campaign before them. A New Game is taken through the
    /// prologue, the fixture with the latest frontier before <paramref name="first"/> is loaded
    /// over it through the real save path, and every mission from that frontier to
    /// <paramref name="last"/> is played in campaign order.
    ///
    /// <para>⚠️ It enters from a legacy-shaped save, so it exercises the catch-up rather than the
    /// state run A would carry there (inventory, corruption and fork flags are the clean road's
    /// defaults). It is for iterating on one mission; the full gate is still the proof.</para>
    /// </summary>
    public static async Task PlayMissionsAsync(
        ApplicationRoot root, SessionLifecycleCoordinator lifecycle, string slotBase, int first, int last)
    {
        RunPlan plan = PlanC();
        var run = new StoryPlaythrough(root, lifecycle, slotBase, plan);
        if (!await run.StartNewGameAsync() || !await run.OpeningAsync())
        {
            return;
        }

        int frontier = StoryMissionRange.Frontier(first, Frontiers());
        Fixture? start = null;
        foreach (Fixture fixture in Fixtures())
        {
            if (fixture.ThroughMission == frontier && frontier > 0)
            {
                start = fixture;
            }
        }

        string slot = $"{slotBase}_mission";
        string label = $"M{first}..{last}";
        HeadlessStory.Note($"missions {first}..{last} from {(start == null ? "a new game" : $"'{start.Name}' (through mission {frontier})")}");
        if (start != null)
        {
            if (!await LoadFixtureAsync(run, start, slot, label))
            {
                return;
            }

            foreach ((string item, int count) in start.Held ?? Array.Empty<(string, int)>())
            {
                if (Items.ItemDatabase.Get(item) is { } held)
                {
                    run.Pack.AddItem(held, count);
                }
            }

            AssertLanding(run, start, label);
        }

        var order = new List<string>();
        foreach (CampaignCatchUp.Mission mission in CampaignCatchUp.Missions)
        {
            if (mission.Index > frontier && mission.Index <= last)
            {
                order.Add(mission.QuestId);
            }
        }

        var play = new StoryPlaythrough(root, lifecycle, slotBase, new RunPlan
        {
            Name = label, Absorb = false, DoOptional = false, OptionalOnly = plan.OptionalOnly,
            Order = order.ToArray(), Picks = plan.Picks, ExpectedForks = Array.Empty<string>(),
        });
        play.Adopt(run);
        foreach ((string quest, QuestStatus status, int[] _) in start?.Quests ?? Array.Empty<(string, QuestStatus, int[])>())
        {
            if (status == QuestStatus.Active)
            {
                play.LegacyHeld.Add(quest);
            }
        }

        await play.PlayQuestsAsync(order);
        SaveManager.Instance?.DeleteSlot(slot);
    }

    public static async Task RunAsync(ApplicationRoot root, SessionLifecycleCoordinator lifecycle, string slotBase)
    {
        RunPlan plan = PlanC();

        var run = new StoryPlaythrough(root, lifecycle, slotBase, plan);
        lifecycle.DestroySession();
        await HeadlessLifecycle.Frames(root, 8);
        if (!await run.StartNewGameAsync())
        {
            return;
        }

        // A New Game is the opening of the new chain, not a legacy save: nothing to catch up, the first mission starts after the prologue.
        await run.OpeningAsync();
        Check(run.Log.IsActive("quest.main.smoke_over_the_square"), "[C] a new game did not start the first mission after the prologue");
        Check(!run.Flags.Has("flag.main.the_pass_kept_done"), "[C] a new game began with a later mission's flag set");
        await HeadlessStory.CheckDriverObjectivesAsync(root, run);

        int number = 0;
        foreach (Fixture fixture in Fixtures())
        {
            number++;
            string slot = $"{slotBase}_legacy{number}";
            string label = $"C{number} {fixture.Name}";
            if (!await LoadFixtureAsync(run, fixture, slot, label))
            {
                continue;
            }

            foreach ((string item, int count) in fixture.Held ?? Array.Empty<(string, int)>())
            {
                if (Items.ItemDatabase.Get(item) is { } held)
                {
                    run.Pack.AddItem(held, count);
                }
            }

            AssertLanding(run, fixture, label);
            if (fixture.PlayNext.Length > 0)
            {
                var play = new StoryPlaythrough(root, lifecycle, slotBase, new RunPlan
                {
                    Name = label, Absorb = false, DoOptional = false, OptionalOnly = plan.OptionalOnly,
                    Order = fixture.PlayNext, Picks = plan.Picks, ExpectedForks = Array.Empty<string>(),
                });
                play.Adopt(run);
                foreach ((string quest, QuestStatus status, int[] _) in fixture.Quests)
                {
                    if (status == QuestStatus.Active)
                    {
                        play.LegacyHeld.Add(quest);
                    }
                }

                await play.PlayQuestsAsync(fixture.PlayNext);
                run = play;
            }

            SaveManager.Instance?.DeleteSlot(slot);
        }
    }

    private static void Check(bool ok, string message)
    {
        if (!ok)
        {
            HeadlessStory.Fail(message);
        }
    }

    /// <summary>Saves the live session, rewrites its quest log and flags as the old build would have left
    /// them (no <c>seen</c> key, only legacy flags), and loads that through the real path.</summary>
    private static async Task<bool> LoadFixtureAsync(StoryPlaythrough run, Fixture fixture, string slot, string label)
    {
        // A harness save is not a player save: a conversation or duel the driver left open must not refuse it.
        SaveManager.Instance?.ClearSaveBlocks();
        if (SaveManager.Instance?.SaveGame(slot) != true)
        {
            Fail(label, "could not write the base save");
            return false;
        }

        string path = SaveManager.Instance.SlotPath(slot);
        Variant parsed = Json.ParseString(FileAccess.GetFileAsString(path));
        if (parsed.VariantType != Variant.Type.Dictionary)
        {
            Fail(label, "the base save is not a JSON object");
            return false;
        }

        Godot.Collections.Dictionary document = parsed.AsGodotDictionary();
        Godot.Collections.Dictionary objects = document["objects"].AsGodotDictionary();

        var quests = new Godot.Collections.Array();
        foreach ((string questId, QuestStatus status, int[] counts) in fixture.Quests)
        {
            var countArray = new Godot.Collections.Array();
            foreach (int count in counts)
            {
                countArray.Add(count);
            }

            quests.Add(new Godot.Collections.Dictionary
            {
                ["id"] = questId, ["status"] = (int)status, ["counts"] = countArray, ["left"] = 0.0,
            });
        }

        // No "seen" and no "tracked" key: the shape the previous build wrote.
        objects[run.Log.SaveId] = new Godot.Collections.Dictionary { ["quests"] = quests };

        var flags = new Godot.Collections.Array();
        foreach (string flag in fixture.Flags)
        {
            flags.Add(flag);
        }

        objects[run.Flags.SaveId] = new Godot.Collections.Dictionary { ["flags"] = flags };

        // The objects no longer match the checksum the save was written with, and a legacy save
        // never carried one: an absent checksum is accepted, a stale one is refused as corrupt.
        document.Remove(SaveEnvelope.ChecksumKey);
        if (document.TryGetValue(SaveEnvelope.HeaderKey, out Variant header) &&
            header.VariantType == Variant.Type.Dictionary)
        {
            header.AsGodotDictionary().Remove(SaveEnvelope.ChecksumKey);
        }

        using (FileAccess? file = FileAccess.Open(path, FileAccess.ModeFlags.Write))
        {
            if (file == null)
            {
                Fail(label, $"could not rewrite the save ({FileAccess.GetOpenError()})");
                return false;
            }

            file.StoreString(Json.Stringify(document, "\t"));
        }

        int xp = run.Progression?.CurrentXp ?? 0;
        int level = run.Progression?.Level ?? 0;
        int gold = run.Pack.CountOf("item.currency.gold");
        run.RememberRewards(xp, level, gold);
        return await run.LoadAsync(slot, label);
    }

    private static void Fail(string label, string message) => HeadlessStory.Fail($"[{label}] {message}");

    private static void AssertLanding(StoryPlaythrough run, Fixture fixture, string label)
    {
        // the mission flags the legacy progress implies, and no later one
        foreach (CampaignCatchUp.Mission mission in CampaignCatchUp.Missions)
        {
            bool expected = mission.Index <= fixture.ThroughMission;
            if (run.Flags.Has(mission.DoneFlag) != expected)
            {
                Fail(label, $"{mission.DoneFlag} is {run.Flags.Has(mission.DoneFlag)} (expected {expected}) after the catch-up");
            }

            if (expected && QuestDatabase.Get(mission.QuestId) != null && !run.Log.IsCompleted(mission.QuestId))
            {
                Fail(label, $"{mission.QuestId} should read as completed in the journal after the catch-up");
            }
        }

        var active = new List<string>();
        foreach (QuestProgress progress in run.Log.Quests)
        {
            if (progress.Status == QuestStatus.Active && progress.Quest.IsMainQuest && !progress.Quest.IsLedger)
            {
                active.Add(progress.Quest.Id);
            }
        }

        foreach (string quest in fixture.ExpectActive)
        {
            Check(active.Contains(quest), $"[{label}] '{quest}' should be the live mission (active main quests: [{string.Join(", ", active)}])");
        }

        foreach (string quest in active)
        {
            Check(Array.IndexOf(fixture.ExpectActive, quest) >= 0,
                $"[{label}] '{quest}' is active but is not the mission the save's progress implies (active: [{string.Join(", ", active)}])");
        }

        run.AssertRewardsUnchanged(label);
        QuestProgress? tracked = run.Log.Tracked;
        Check(fixture.ExpectActive.Length == 0 || (tracked != null && tracked.Quest.IsMainQuest),
            $"[{label}] the tracker follows {tracked?.Quest.Id ?? "nothing"} after the load");
        Log.Info($"story: {label} -> active [{string.Join(", ", active)}], tracked {tracked?.Quest.Id ?? "-"}");
    }
}
