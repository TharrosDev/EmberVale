using System.Collections.Generic;
using System.Threading.Tasks;
using Embervale.Core.Diagnostics;
using Embervale.Dialogue;
using Embervale.Enemies;
using Embervale.Localization;
using Embervale.Quests;
using Embervale.Races;
using Embervale.Save;
using Embervale.UI;
using Godot;

namespace Embervale.Bootstrap;

/// <summary>
/// <c>--story</c>: the main thread end to end in a real session (finish run). Starts a new game, then
/// raises each act's trigger flag the way its boss or conversation would, and asserts the next act's
/// quest started by itself, the hidden realm revealed, the chain survives a save/load, every
/// Flamebearer template builds a boss, and an ending flag plays the ending. Exit 0 PASS / 1 FAIL.
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

        await Raise(root, flags, EndingSequence.DawnfireFlag);
        EndingSequence? ending = lifecycle.Session!.GetNodeOrNull<EndingSequence>("Ending");
        Check(ending?.IsPlaying == true, "setting the Dawnfire flag did not play the ending");

        Finish(root, null);
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
