using System.Collections.Generic;
using System.Threading.Tasks;
using Embervale.Core.Diagnostics;
using Embervale.Core.Services;
using Embervale.Dialogue;
using Embervale.Enemies;
using Embervale.Localization;
using Embervale.Quests;
using Embervale.Races;
using Embervale.Save;
using Embervale.UI;
using Embervale.World;
using Godot;

namespace Embervale.Bootstrap;

/// <summary>
/// <c>--story</c>: the main thread end to end in a real session (finish run). Starts a new game, then
/// raises each act's trigger flag the way its boss or conversation would, and asserts the next act's
/// quest started by itself, the hidden realm revealed, the chain survives a save/load, every
/// Flamebearer template builds a boss, and an ending flag plays the ending. Exit 0 PASS / 1 FAIL.
/// Phase 47.5: the rival duels are wired (yielding fights, their flags and conversations) and the
/// chain runs without them. Phase 44.5: each ending brings its sky, and the Lord of Embers sky is
/// re-derived from the flags on load even from a save holding weather the story forbids.
/// It proves the wiring between acts — not that a fight can be won, which needs a player.
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

    public static bool Requested() => HeadlessValidation.HasFlag(FlagArgument);

    public static async void Run(ApplicationRoot root, SessionLifecycleCoordinator lifecycle)
    {
        Log.Info("=== story probe ===");
        Failures.Clear();

        lifecycle.StartNewGame(Slot, CharacterProfile.Human);
        if (!await HeadlessLifecycle.WaitForPlaying(root) || lifecycle.Session?.Players.Player is not { } player)
        {
            Finish(root, "the new game never reached Playing with a player");
            return;
        }

        var flags = player.GetComponent<StoryFlagsComponent>()!;
        var log = player.GetComponent<QuestLogComponent>()!;

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

        await Raise(root, flags, "flag.iron_king_defeated");
        Check(log.IsActive("quest.main.gathering"), "Act II did not start on the Iron King's defeat");
        Check(!flags.Has("flag.pale_concord_revealed"), "the hidden realm revealed before Act II was done");

        await Raise(root, flags, "flag.storm_tyrant_defeated", "flag.beast_lord_defeated", "flag.crimson_prophet_defeated");
        Check(flags.Has("flag.pale_concord_revealed"), "three known-realm Flamebearers did not reveal the hidden realm");
        Check(log.IsActive("quest.main.hidden"), "the hidden-realm quest did not start on the reveal");

        await Raise(root, flags, "flag.hollow_queen_defeated");
        Check(log.IsActive("quest.main.truth"), "Act III did not start on the Hollow Queen's defeat");

        await Raise(root, flags, "flag.celestial_gate_open");
        Check(log.IsActive("quest.main.celestial"), "Act IV did not start when the Archivist opened the gate");

        Check(SaveManager.Instance?.SaveGame(Slot) == true, "the mid-story session failed to save");
        lifecycle.DestroySession();
        await HeadlessLifecycle.Frames(root, 8);
        lifecycle.StartLoadedGame(Slot);
        if (!await HeadlessLifecycle.WaitForPlaying(root) || lifecycle.Session?.Players.Player is not { } loaded)
        {
            Finish(root, "the saved story session never reloaded");
            return;
        }

        flags = loaded.GetComponent<StoryFlagsComponent>()!;
        log = loaded.GetComponent<QuestLogComponent>()!;
        Check(log.IsActive("quest.main.celestial") && flags.Has("flag.pale_concord_revealed"),
            "the story state did not survive save/load");
        Check(!flags.Has("flag.rival.duel1_won") && !flags.Has("flag.rival.duel2_won"),
            "a rival duel flag was set without a duel; the duels must stay optional");

        var driver = new StoryDriver(root, loaded);
        await CheckDriverObjectives(root, driver, flags, log);

        // The throne conversation, driven the way a player would: corruption set through a real dialogue
        // effect decides which of the two endings it offers, and the confirmed choice sets the flag.
        flags.Set("flag.morthul_defeated");
        foreach ((int corruption, bool refuse, bool sit) in new[] { (10, true, false), (50, true, true), (70, false, true) })
        {
            Check(driver.SetCorruption(corruption), $"the driver could not set corruption to {corruption}");
            Check(driver.OpenDialogue("dialogue.ash_throne") != null, "dialogue.ash_throne is missing");
            Check(driver.ChooseText("dlg.throne.c_approach"), "the throne gate offered no approach");
            List<string> offered = driver.ChoiceKeys();
            Check(offered.Contains("dlg.throne.c_refuse") == refuse && offered.Contains("dlg.throne.c_sit") == sit,
                $"at corruption {corruption} the throne offered [{string.Join(", ", offered)}]");
            driver.EndDialogue();
        }

        driver.SetCorruption(10);
        driver.OpenDialogue("dialogue.ash_throne");
        Check(driver.ChooseText("dlg.throne.c_approach") && driver.ChooseText("dlg.throne.c_refuse") &&
              driver.NodeId == "dawn", "the throne did not lead to the Dawnfire confirmation");
        Check(driver.ChooseText("dlg.throne.c_dawn_yes") && driver.Session == null,
            "confirming the Dawnfire ending did not close the conversation");
        Check(flags.Has(EndingSequence.DawnfireFlag), "the driven throne conversation did not set the Dawnfire flag");
        await HeadlessLifecycle.Frames(root, 4);
        EndingSequence? ending = lifecycle.Session!.GetNodeOrNull<EndingSequence>("Ending");
        Check(ending?.IsPlaying == true, "setting the Dawnfire flag did not play the ending");
        Check(Weather()?.Current?.Id == "weather.dawnfire", "the Dawnfire ending did not bring the Dawnfire sky");

        // The other ending, and its sky derived from flags on load. game_complete first so the
        // ending sequence ignores the second ending flag.
        flags.Set(EndingSequence.CompleteFlag);
        flags.Clear(EndingSequence.DawnfireFlag);
        await Raise(root, flags, EndingSequence.EmbersFlag);
        Check(Weather()?.Current?.Id == "weather.embers", "the Lord of Embers ending did not bring the ember sky");

        Weather()?.Force("weather.rain"); // a save holding weather the story no longer allows
        Check(SaveManager.Instance?.SaveGame(Slot) == true, "the post-ending session failed to save");
        lifecycle.DestroySession();
        await HeadlessLifecycle.Frames(root, 8);
        lifecycle.StartLoadedGame(Slot);
        if (!await HeadlessLifecycle.WaitForPlaying(root))
        {
            Finish(root, "the post-ending session never reloaded");
            return;
        }

        for (int i = 0; i < 60 && Weather()?.Current?.Id != "weather.embers"; i++)
        {
            await HeadlessLifecycle.Frames(root, 10);
        }

        Check(Weather()?.Current?.Id == "weather.embers",
            $"after load the ember sky was not re-derived from the ending flag (weather '{Weather()?.Current?.Id}')");

        Finish(root, null);
    }

    /// <summary>
    /// Proves the driver on an in-memory quest, objective type by objective type through the real event
    /// paths: Milestone (flag), Interact, Kill (and that a kill of an objective not yet live does not
    /// count), Talk, Reach, Defend. The quest is left one step short on purpose: completing it would
    /// autosave into the developer's own ring slots (completion itself is covered by the unit tests).
    /// </summary>
    private static async Task CheckDriverObjectives(
        ApplicationRoot root, StoryDriver driver, StoryFlagsComponent flags, QuestLogComponent log)
    {
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
        };
        if (place.Length > 0)
        {
            list.Add(StoryDriver.Objective(ObjectiveType.Reach, place));
            list.Add(StoryDriver.Objective(ObjectiveType.Defend, place, 2));
        }

        // A last step nothing ever meets: the quest must not complete here, because completing publishes
        // the event that autosaves into the developer's own ring slots.
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
        Check(state().Contains("[3+ Talk"), $"Talk did not complete: {state()}");

        if (place.Length > 0)
        {
            Check(await driver.Reach(place, () => state().Contains("[4+ Reach")), $"Reach did not complete: {state()}");

            Check(await driver.Defend(place, () => state().Contains("[5+ Defend")), $"Defend did not complete: {state()}");
            Check(state().Contains("[6* Milestone") && log.IsActive(questId),
                $"the quest should wait on its last step: {state()}");
        }

        log.Reset(questId);
        flags.Clear(gate);
        await HeadlessLifecycle.Frames(root, 2);
    }

    private static WeatherDirector? Weather() =>
        ServiceLocator.Instance is { } sl && sl.TryGet(out WeatherDirector weather) ? weather : null;

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

    private static async Task Raise(ApplicationRoot root, StoryFlagsComponent flags, params string[] ids)
    {
        foreach (string id in ids)
        {
            flags.Set(id);
        }

        await HeadlessLifecycle.Frames(root, 4);
    }

    private static void Check(bool ok, string failure)
    {
        if (!ok)
        {
            Failures.Add(failure);
        }
    }

    private static void Finish(ApplicationRoot root, string? fatal)
    {
        if (fatal != null)
        {
            Failures.Add(fatal);
        }

        SaveManager.Instance?.DeleteSlot(Slot);
        foreach (string failure in Failures)
        {
            Log.Error($"story: {failure}");
        }

        Log.Info(Failures.Count == 0 ? "story: PASS" : "story: FAIL");
        root.GetTree().Quit(Failures.Count == 0 ? 0 : 1);
    }
}
