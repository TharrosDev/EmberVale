using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Embervale.Companions;
using Embervale.Core.Events;
using Embervale.Core.Services;
using Embervale.Corruption;
using Embervale.Dialogue;
using Embervale.Enemies;
using Embervale.Entities;
using Embervale.Items;
using Embervale.Narrative;
using Embervale.Progression;
using Embervale.Quests;
using Embervale.Save;
using Embervale.UI;
using Embervale.World;
using Godot;

namespace Embervale.Bootstrap;

/// <summary>
/// One scripted choice of path through the campaign: which embers are taken, which optional
/// objectives are done, the main-quest order and the explicit dialogue picks (forks, hand-ins).
/// Everything else a quest needs is derived from the quest's own objectives.
/// </summary>
internal sealed class RunPlan
{
    public required string Name { get; init; }

    /// <summary>Take every ember through the real absorb conversations (else refuse every one).</summary>
    public bool Absorb { get; init; }

    /// <summary>Also do every optional objective the player can reach (else only <see cref="OptionalOnly"/>).</summary>
    public bool DoOptional { get; init; }

    /// <summary>Optional objectives done even when <see cref="DoOptional"/> is off, as "quest#index".</summary>
    public HashSet<string> OptionalOnly { get; init; } = new();

    /// <summary>Objectives the run will not play even though they are live, as "quest#index": the other
    /// route has to carry the quest (the Choir's answer in place of its echoes).</summary>
    public HashSet<string> Avoid { get; init; } = new();

    /// <summary>The main quests in the order the run completes them.</summary>
    public required string[] Order { get; init; }

    /// <summary>Explicit conversation picks by dialogue id (full text key or <c>.suffix</c>, in order).</summary>
    public required Dictionary<string, string[]> Picks { get; init; }

    /// <summary>The fork flags this run must end holding.</summary>
    public required string[] ExpectedForks { get; init; }

    /// <summary>A companion recruited (through the real roster) at the start of a segment, or empty.</summary>
    public string Companion { get; init; } = string.Empty;

    /// <summary>The recruited companion also does the optional Escort objectives.</summary>
    public bool EscortCompanion { get; init; }

    public string[] PicksFor(string dialogueId) =>
        Picks.TryGetValue(dialogueId, out string[]? picks) ? picks : Array.Empty<string>();
}

/// <summary>
/// Plays the campaign through the real systems, one quest at a time, the way a player would: each live
/// objective is completed by the event the game raises for it (a kill is an <c>EntityDiedEvent</c> with
/// the player as killer, an Interact an <c>InteractionPerformedEvent</c> carrying the interactable's
/// id, a Talk a conversation run to its end, Reach and Defend the player standing at the place while
/// the quest log's own poll counts it, a Collect an <c>ItemPickedUpEvent</c>), and nothing is raised by
/// hand except what a fight would raise: a Flamebearer's defeat, published as a death on a real boss
/// entity so <c>BossEncounterDirector</c> grants the flag, the relic and the ember conversation itself.
/// Milestones are never set directly: the writer of the flag is found in the dialogues and the story
/// rules and driven.
/// </summary>
internal sealed class StoryPlaythrough
{
    private const int ObjectiveGuard = 160;
    private const int StuckLimit = 3;

    private readonly ApplicationRoot _root;
    private readonly SessionLifecycleCoordinator _lifecycle;
    private readonly string _slot;
    private readonly RunPlan _plan;

    private GameSession _session = null!;
    private IEntity _player = null!;
    private StoryFlagsComponent _flags = null!;
    private QuestLogComponent _log = null!;
    private CorruptionComponent _corruption = null!;
    private InventoryComponent _pack = null!;
    private StoryDriver _driver = null!;

    public StoryPlaythrough(ApplicationRoot root, SessionLifecycleCoordinator lifecycle, string slot, RunPlan plan)
    {
        _root = root;
        _lifecycle = lifecycle;
        _slot = slot;
        _plan = plan;
    }

    public StoryFlagsComponent Flags => _flags;

    public string[] PlanOrder() => _plan.Order;

    public Task FramesAsync(int count) => HeadlessLifecycle.Frames(_root, count);

    public QuestLogComponent Log => _log;

    public StoryDriver Driver => _driver;

    public CorruptionComponent Corruption => _corruption;

    public GameSession Session => _session;

    public InventoryComponent Pack => _pack;

    public IEntity Player => _player;

    /// <summary>Main quests this playthrough saw complete through play (not through a load catch-up).</summary>
    public HashSet<string> Completed { get; } = new();

    /// <summary>Fork flags held at the end of the run, for the cross-run coverage.</summary>
    public HashSet<string> ForksTaken { get; } = new();

    public bool SawPaleReveal { get; private set; }

    public bool SawVision { get; private set; }

    private void Fail(string message) => HeadlessStory.Fail($"[{_plan.Name}] {message}");

    private void Check(bool ok, string message)
    {
        if (!ok)
        {
            Fail(message);
        }
    }

    // --- session --------------------------------------------------------------------

    /// <summary>Starts a New Game and waits for the world, then through the prologue.</summary>
    public async Task<bool> StartNewGameAsync()
    {
        _lifecycle.StartNewGame(_slot, Races.CharacterProfile.Human);
        if (!await HeadlessLifecycle.WaitForPlaying(_root) || !Bind())
        {
            Fail("the new game never reached Playing with a player");
            return false;
        }

        return true;
    }

    /// <summary>Rebinds to the live session after a load; false when the session never came back.</summary>
    public async Task<bool> RebindAsync(string what)
    {
        if (!await HeadlessLifecycle.WaitForPlaying(_root) || !Bind())
        {
            Fail($"{what}: the session never reached Playing with a player");
            return false;
        }

        await HeadlessLifecycle.Frames(_root, 6);
        return true;
    }

    private bool Bind()
    {
        if (_lifecycle.Session is not { } session || session.Players.Player is not { } player ||
            player.GetComponent<StoryFlagsComponent>() is not { } flags ||
            player.GetComponent<QuestLogComponent>() is not { } log ||
            player.GetComponent<CorruptionComponent>() is not { } corruption ||
            player.GetComponent<InventoryComponent>() is not { } pack)
        {
            return false;
        }

        _session = session;
        _player = player;
        _flags = flags;
        _log = log;
        _corruption = corruption;
        _pack = pack;
        _driver = new StoryDriver(_root, player) { PositionFallback = ScenePlacement.LocationPosition };

        // The harness completes dozens of quests; the autosave ring is the developer's own saves.
        if (session.GetNodeOrNull("Autosave") is { } autosave)
        {
            session.RemoveChild(autosave);
            autosave.QueueFree();
        }

        // A hold runs the clock 30 times faster, which makes the standing integrity scan (every five game
        // seconds) fire every frame and print a backtrace each time; the lifecycle gate owns that check.
        if (session.DevTools?.GetNodeOrNull<Debugging.WorldIntegrityChecker>("WorldIntegrityChecker") is { } checker)
        {
            checker.Interval = 600f;
        }
        else if (session.DevTools != null)
        {
            foreach (Node child in session.DevTools.GetChildren())
            {
                if (child is Debugging.WorldIntegrityChecker found)
                {
                    found.Interval = 600f;
                }
            }
        }

        return true;
    }

    /// <summary>The real save path and a real load, with the session torn down in between.</summary>
    public async Task<bool> ReloadAsync(string slot, string what)
    {
        // A harness save is not a player save: a conversation or duel the driver left open must not refuse it.
        SaveManager.Instance?.ClearSaveBlocks();
        if (SaveManager.Instance?.SaveGame(slot) != true)
        {
            Fail($"{what}: the session failed to save to '{slot}'");
            return false;
        }

        return await LoadAsync(slot, what);
    }

    public async Task<bool> LoadAsync(string slot, string what)
    {
        _lifecycle.DestroySession();
        await HeadlessLifecycle.Frames(_root, 8);
        _lifecycle.StartLoadedGame(slot);
        return await RebindAsync(what);
    }

    // --- cinematics -----------------------------------------------------------------

    private bool Busy(out string what)
    {
        foreach (Node child in _session.GetChildren())
        {
            if (child is NarrationSequence { IsPlaying: true } narration)
            {
                what = narration.GetType().Name;
                if (narration is VisionSequence)
                {
                    SawVision = true;
                }

                return true;
            }

            if (child is PaleRevealSequence { IsPending: true })
            {
                what = "PaleRevealSequence (pending)";
                return true;
            }
        }

        what = string.Empty;
        return false;
    }

    /// <summary>Lets every narration card, vision and pending reveal run out, the clock sped up.</summary>
    public async Task IdleAsync(string reason, int minFrames = 3)
    {
        await HeadlessLifecycle.Frames(_root, minFrames);
        float previous = (float)Engine.TimeScale;
        string what = string.Empty;
        int guard = 0;
        try
        {
            while (Busy(out what) && guard++ < 6000)
            {
                if (what.StartsWith(nameof(PaleRevealSequence), StringComparison.Ordinal) ||
                    what == nameof(PaleRevealSequence))
                {
                    SawPaleReveal = true;
                }

                Engine.TimeScale = 25f;
                await HeadlessLifecycle.Frames(_root, 2);
            }
        }
        finally
        {
            Engine.TimeScale = previous < 1f ? 1f : previous;
        }

        if (guard >= 6000)
        {
            Fail($"{reason}: '{what}' never finished");
        }

        foreach (Node child in _session.GetChildren())
        {
            if (child is PaleRevealSequence { IsPlaying: true })
            {
                SawPaleReveal = true;
            }
        }
    }

    // --- the campaign ---------------------------------------------------------------

    /// <summary>Waits out the prologue (the real narration) and checks the first mission starts by itself.</summary>
    public async Task<bool> OpeningAsync()
    {
        Check(_corruption.Value == 0, $"a new game starts with corruption {_corruption.Value}");
        Check(!_flags.Has("flag.main.opening_done"), "a new game starts with the opening already done");
        await IdleAsync("the prologue", 6);
        await HeadlessLifecycle.Frames(_root, 6);
        if (!_flags.Has("flag.main.opening_done"))
        {
            Fail("the prologue ended but flag.main.opening_done was not raised (the first mission cannot start)");
            return false;
        }

        return true;
    }

    /// <summary>Plays <paramref name="questIds"/> in order. Returns false when the run cannot go on.</summary>
    public async Task<bool> PlayQuestsAsync(IEnumerable<string> questIds, Func<string, Task>? after = null)
    {
        string previous = "the opening";
        foreach (string questId in questIds)
        {
            QuestResource? quest = QuestDatabase.Get(questId);
            if (quest == null)
            {
                Fail($"{questId} is not a quest in the database");
                return false;
            }

            if (_log.IsCompleted(questId))
            {
                Fail($"{questId} was already completed before its turn (something completed it early)");
                continue;
            }

            if (!_log.IsActive(questId))
            {
                Fail($"{questId} did not start by itself after {previous} (auto-start flag '{quest.AutoStartFlagId}'" +
                     $" held: {_flags.Has(quest.AutoStartFlagId)}; in the log: {_log.HasQuest(questId)})");
                return false;
            }

            AssertTracked($"before {questId}");
            AssertNoEarlyStarts($"before {questId}");
            ulong began = Time.GetTicksMsec();
            if (!await CompleteQuestAsync(questId))
            {
                return false;
            }

            HeadlessStory.Played(_plan.Name, questId, (Time.GetTicksMsec() - began) / 1000.0);

            Completed.Add(questId);
            await HeadlessLifecycle.Frames(_root, 3);
            AssertNoEarlyStarts($"after {questId}");
            AssertTracked($"after {questId}");
            previous = questId;
            if (after != null)
            {
                await after(questId);
            }
        }

        return true;
    }

    /// <summary>The tracker follows a main-story quest whenever one is active (never a side quest, never the ledger).</summary>
    private void AssertTracked(string context)
    {
        bool anyMain = false;
        foreach (QuestProgress progress in _log.Quests)
        {
            if (progress.Status == QuestStatus.Active && progress.Quest.IsMainQuest && !progress.Quest.IsLedger)
            {
                anyMain = true;
                break;
            }
        }

        QuestProgress? tracked = _log.Tracked;
        if (anyMain && (tracked == null || !tracked.Quest.IsMainQuest || tracked.Quest.IsLedger))
        {
            Fail($"{context}: a main-story quest is active but the tracker follows {tracked?.Quest.Id ?? "nothing"}");
        }
    }

    /// <summary>Main quests a legacy save already held when it was loaded (exempt from the early-start check).</summary>
    public HashSet<string> LegacyHeld { get; } = new();

    /// <summary>No main quest is active before its auto-start flag is held (a sequence break or an early start).</summary>
    private void AssertNoEarlyStarts(string context)
    {
        foreach (QuestResource quest in QuestDatabase.All)
        {
            if (!quest.IsMainQuest || quest.AutoStartFlagId.Length == 0 || _flags.Has(quest.AutoStartFlagId) ||
                LegacyHeld.Contains(quest.Id) || _log.IsCompleted(quest.Id))
            {
                continue;
            }

            if (_log.HasQuest(quest.Id))
            {
                Fail($"{context}: {quest.Id} is in the log although its auto-start flag '{quest.AutoStartFlagId}' is not held");
            }
        }
    }

    private QuestProgress? Progress(string questId)
    {
        foreach (QuestProgress progress in _log.Quests)
        {
            if (progress.Quest.Id == questId)
            {
                return progress;
            }
        }

        return null;
    }

    /// <summary>Completes a quest by playing each live objective, until it is Completed.</summary>
    public async Task<bool> CompleteQuestAsync(string questId)
    {
        string last = string.Empty;
        int stuck = 0;
        for (int guard = 0; guard < ObjectiveGuard; guard++)
        {
            if (_log.IsCompleted(questId))
            {
                QuestResource? done = QuestDatabase.Get(questId);
                Check(done == null || done.CompletionFlagId.Length == 0 || _flags.Has(done.CompletionFlagId),
                    $"{questId} completed but its completion flag '{done?.CompletionFlagId}' is not set");
                return true;
            }

            if (_log.IsFailed(questId))
            {
                Fail($"{questId} failed: {_driver.Report(questId)}");
                return false;
            }

            QuestProgress? progress = Progress(questId);
            if (progress == null)
            {
                Fail($"{questId} is not in the log");
                return false;
            }

            int index = NextObjective(progress);
            if (index < 0)
            {
                await HeadlessLifecycle.Frames(_root, 8);
                if (_log.IsCompleted(questId))
                {
                    continue;
                }

                Fail($"{questId} has no live objective left yet is not complete: {_driver.Report(questId)}");
                return false;
            }

            string before = _driver.Report(questId) + FlagStamp(progress);
            await PerformAsync(progress, index);
            await HeadlessLifecycle.Frames(_root, 2);
            string after = _driver.Report(questId) + FlagStamp(progress);
            if (after == before && !_log.IsCompleted(questId))
            {
                if (++stuck >= StuckLimit)
                {
                    Fail($"{questId} is stuck on objective {index} ({Describe(progress.Quest.ObjectiveList()[index])}): {after}");
                    return false;
                }
            }
            else
            {
                stuck = 0;
            }

            last = after;
        }

        Fail($"{questId} did not finish within {ObjectiveGuard} steps: {last}");
        return false;
    }

    /// <summary>A change in any gate flag the quest names counts as progress for the stuck detector.</summary>
    private string FlagStamp(QuestProgress progress)
    {
        var text = new StringBuilder();
        foreach (ObjectiveResource objective in progress.Quest.ObjectiveList())
        {
            foreach (string flag in new[] { objective.RequiredFlagId, objective.ForbiddenFlagId, objective.CompletionFlagId })
            {
                if (flag.Length > 0)
                {
                    text.Append(_flags.Has(flag) ? '1' : '0');
                }
            }
        }

        return text.ToString();
    }

    private static string Describe(ObjectiveResource objective) =>
        $"{objective.Type} {objective.TargetId} x{objective.RequiredCount}" +
        (objective.RequiredFlagId.Length > 0 ? $" needs {objective.RequiredFlagId}" : string.Empty);

    /// <summary>The next objective the player should work on: live, unmet, and required (or an optional
    /// the plan says to do). Null/-1 when none.</summary>
    private int NextObjective(QuestProgress progress)
    {
        List<ObjectiveResource> objectives = progress.Quest.ObjectiveList();
        for (int i = 0; i < objectives.Count; i++)
        {
            if (!progress.IsObjectiveActive(i) || progress.IsObjectiveComplete(i))
            {
                continue;
            }

            ObjectiveResource objective = objectives[i];
            if (objective.Type == ObjectiveType.Stealth)
            {
                continue;
            }

            if (_plan.Avoid.Contains($"{progress.Quest.Id}#{i}"))
            {
                continue;
            }

            if (objective.IsOptional && !WantsOptional(progress.Quest.Id, i, objective))
            {
                continue;
            }

            return i;
        }

        return -1;
    }

    private bool WantsOptional(string questId, int index, ObjectiveResource objective)
    {
        if (objective.Type == ObjectiveType.Escort)
        {
            // needs a recruited companion; only the companion segment drives it
            return _plan.EscortCompanion && objective.TargetId == _plan.Companion;
        }

        return _plan.DoOptional || _plan.OptionalOnly.Contains($"{questId}#{index}");
    }

    // --- objectives -----------------------------------------------------------------

    private async Task PerformAsync(QuestProgress progress, int index)
    {
        ObjectiveResource objective = progress.Quest.ObjectiveList()[index];
        string questId = progress.Quest.Id;
        int remaining = Math.Max(1, objective.RequiredCount - progress.Counts[index]);
        switch (objective.Type)
        {
            case ObjectiveType.Kill:
                await KillAsync(objective.TargetId, remaining);
                break;
            case ObjectiveType.Collect:
                Check(_driver.Pickup(objective.TargetId, remaining),
                    $"{questId}: the player could not pick up {remaining}x {objective.TargetId}");
                break;
            case ObjectiveType.Reach:
                if (!await _driver.Reach(objective.TargetId, () => progress.IsObjectiveComplete(index)))
                {
                    Fail($"{questId}: Reach '{objective.TargetId}' did not complete" +
                         (PositionKnown(objective.TargetId) ? " (the poll never counted the arrival)" : " (the location has no position: not baked and not placed in any scene)"));
                }

                break;
            case ObjectiveType.Defend:
                if (!await _driver.Defend(objective.TargetId, () => progress.IsObjectiveComplete(index) || !_log.IsActive(questId)))
                {
                    Fail($"{questId}: Defend '{objective.TargetId}' did not complete" +
                         (PositionKnown(objective.TargetId) ? string.Empty : " (the location has no position: not baked and not placed in any scene)"));
                }

                break;
            case ObjectiveType.Interact:
                _driver.Interact(objective.TargetId);
                break;
            case ObjectiveType.Talk:
                await TalkAsync(objective.TargetId, progress);
                break;
            case ObjectiveType.Milestone:
                await MilestoneAsync(progress, objective);
                break;
            case ObjectiveType.Escort:
                if (objective.IsOptional && _plan.EscortCompanion)
                {
                    await EscortAsync(progress, index, objective);
                }
                else
                {
                    Fail($"{questId}: a required Escort objective ({objective.TargetId}); companions must stay optional");
                }

                break;
        }
    }

    private bool PositionKnown(string locationId) => _driver.Resolve(locationId) != null;

    private async Task KillAsync(string template, int count)
    {
        if (EnemyArchetypeDatabase.Get(template) is { IsBoss: true })
        {
            await BossFightAsync(template);
            return;
        }

        _driver.Kill(template, count);
        await Task.CompletedTask;
    }

    // --- conversations --------------------------------------------------------------

    /// <summary>The flags a conversation may need to raise for <paramref name="progress"/>'s quest, with
    /// the story rules followed backwards (a rule's conditions are wanted when its product is).</summary>
    private HashSet<string> WantedFlags(QuestProgress progress)
    {
        var wanted = new HashSet<string>();
        foreach (ObjectiveResource objective in progress.Quest.ObjectiveList())
        {
            if (objective.Type == ObjectiveType.Milestone && !_flags.Has(objective.TargetId))
            {
                wanted.Add(objective.TargetId);
            }

            if (objective.RequiredFlagId.Length > 0 && !_flags.Has(objective.RequiredFlagId))
            {
                wanted.Add(objective.RequiredFlagId);
            }
        }

        ExpandThroughRules(wanted);
        return wanted;
    }

    private void ExpandThroughRules(HashSet<string> wanted)
    {
        if (_session.GetNodeOrNull<StoryRuleDirector>("StoryRules") is not { } director)
        {
            return;
        }

        for (int pass = 0; pass < 6; pass++)
        {
            bool grew = false;
            foreach (StoryRule rule in director.Engine.Rules)
            {
                foreach (string product in rule.Set)
                {
                    if (!wanted.Contains(product))
                    {
                        continue;
                    }

                    foreach (string need in rule.All)
                    {
                        grew |= wanted.Add(need);
                    }
                }
            }

            if (!grew)
            {
                break;
            }
        }
    }

    private async Task TalkAsync(string dialogueId, QuestProgress progress)
    {
        DialogueAim aim = new();
        aim.Picks.AddRange(_plan.PicksFor(dialogueId));
        foreach (string flag in WantedFlags(progress))
        {
            aim.Flags.Add(flag);
        }

        await ConverseAsync(dialogueId, aim, progress.Quest.Id);
    }

    /// <summary>Opens a conversation by id, checks what it should offer, and runs it to its end.</summary>
    public async Task<ConversationResult?> ConverseAsync(string dialogueId, DialogueAim aim, string context)
    {
        if (DialogueDatabase.Get(dialogueId) == null)
        {
            Fail($"{context}: dialogue '{dialogueId}' does not exist");
            return null;
        }

        if (ScenePlacement.Placed is { } placed && placed.Count > 0 && !placed.Contains(dialogueId))
        {
            Fail($"{context}: no scene, companion or boss places '{dialogueId}', so the player could never have this conversation");
        }

        Provision(DialogueDatabase.Get(dialogueId)!, aim);
        _driver.OpenDialogue(dialogueId);
        AssertOffers(dialogueId);
        ConversationResult result = _driver.Converse(aim);
        if (!result.Ended)
        {
            Fail($"{context}: conversation '{dialogueId}' did not reach an end: {result.Trace}");
        }

        if (result.UnusedPicks.Length > 0)
        {
            Fail($"{context}: conversation '{dialogueId}' never offered the planned pick(s) [{string.Join(", ", result.UnusedPicks)}]: {result.Trace}");
        }

        await HeadlessLifecycle.Frames(_root, 2);
        return result;
    }

    /// <summary>Things a shop sells and a hand-in may ask for; the player has them (bought or looted), the
    /// harness has no shopkeeper. Quest materials are never provisioned: the herbs of the calm answer must
    /// come from play.</summary>
    private static readonly HashSet<string> Purchasable = new() { "item.potion.health" };

    /// <summary>A reply the run wants that asks for a purchasable item: the player is given it first (and
    /// the run says so), the way a purchase at the market would.</summary>
    private void Provision(DialogueResource dialogue, DialogueAim aim)
    {
        foreach (DialogueNode node in dialogue.NodeList())
        {
            foreach (DialogueChoice choice in node.ChoiceList())
            {
                foreach ((DialogueCondition condition, string arg) in new[]
                         {
                             (choice.Condition, choice.ConditionArg), (choice.Condition2, choice.Condition2Arg),
                         })
                {
                    if (condition != DialogueCondition.HasItem ||
                        !DialogueRules.TryParseIdAmount(arg, 1, out string item, out int needed) ||
                        !Purchasable.Contains(item) || _pack.Contains(item, needed) || !StoryDriver.IsWanted(choice, aim, _flags.Has))
                    {
                        continue;
                    }

                    _driver.Pickup(item, needed - _pack.CountOf(item));
                    HeadlessStory.Note($"[{_plan.Name}] bought {needed}x {item} for '{dialogue.Id}'");
                }
            }
        }
    }

    /// <summary>What a conversation must (and must not) offer at this moment, read off its open choices.</summary>
    private void AssertOffers(string dialogueId)
    {
        List<string> keys = _driver.ChoiceKeys();
        switch (dialogueId)
        {
            case "dialogue.flock_deacon" when _driver.NodeId == "root":
                Check(keys.Contains("dlg.flock_deacon.root.c_kin") == _corruption.Value >= 40,
                    $"the Flock's kin answer is offered={keys.Contains("dlg.flock_deacon.root.c_kin")} at corruption {_corruption.Value} (it is for 40 and over)");
                Check(keys.Contains("dlg.flock_deacon.root.c_expose") || keys.Contains("dlg.flock_deacon.root.c_expose_archive"),
                    "the Flock's expose answer is not offered after the wells were read");
                break;
        }
    }

    /// <summary>Raises <paramref name="flag"/> the way the game does: a conversation that sets it, else the
    /// story rule that derives it (and the flags that rule waits on), recursively.</summary>
    private async Task<bool> SatisfyFlagAsync(string flag, QuestProgress? progress, int depth = 0)
    {
        if (_flags.Has(flag))
        {
            return true;
        }

        if (depth > 3)
        {
            return false;
        }

        foreach (DialogueResource dialogue in DialogueDatabase.All)
        {
            if (!SetsFlag(dialogue, flag) || dialogue.Id == "dialogue.ash_throne" && !_flags.Has("flag.morthul_defeated"))
            {
                continue;
            }

            var aim = new DialogueAim();
            aim.Picks.AddRange(_plan.PicksFor(dialogue.Id));
            aim.Flags.Add(flag);
            if (progress != null)
            {
                foreach (string wanted in WantedFlags(progress))
                {
                    aim.Flags.Add(wanted);
                }
            }

            if (ScenePlacement.Placed is { Count: > 0 } placed && !placed.Contains(dialogue.Id))
            {
                continue;
            }

            await ConverseAsync(dialogue.Id, aim, $"raising {flag}");
            if (_flags.Has(flag))
            {
                return true;
            }
        }

        if (_session.GetNodeOrNull<StoryRuleDirector>("StoryRules") is { } director)
        {
            foreach (StoryRule rule in director.Engine.Rules)
            {
                if (rule.IsTriggered || !Contains(rule.Set, flag))
                {
                    continue;
                }

                bool any = false;
                foreach (string need in rule.All)
                {
                    any |= !_flags.Has(need) && await SatisfyFlagAsync(need, progress, depth + 1);
                }

                await HeadlessLifecycle.Frames(_root, 3);
                if (_flags.Has(flag))
                {
                    return true;
                }

                _ = any;
            }
        }

        return _flags.Has(flag);
    }

    private static bool Contains(IReadOnlyList<string> list, string value)
    {
        foreach (string item in list)
        {
            if (item == value)
            {
                return true;
            }
        }

        return false;
    }

    private static bool SetsFlag(DialogueResource dialogue, string flag)
    {
        foreach (DialogueNode node in dialogue.NodeList())
        {
            if (node.OnEnterEffect == DialogueEffect.SetFlag && node.OnEnterEffectArg == flag)
            {
                return true;
            }

            foreach (DialogueChoice choice in node.ChoiceList())
            {
                if ((choice.Effect == DialogueEffect.SetFlag && choice.EffectArg == flag) ||
                    (choice.Effect2 == DialogueEffect.SetFlag && choice.Effect2Arg == flag))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private async Task MilestoneAsync(QuestProgress progress, ObjectiveResource objective)
    {
        await HeadlessLifecycle.Frames(_root, 4);
        if (progress.IsObjectiveComplete(progress.Quest.ObjectiveList().IndexOf(objective)))
        {
            return;
        }

        string flag = objective.TargetId;
        foreach (BossResource boss in BossDatabase.All)
        {
            if (boss.DefeatFlagId == flag && boss.WithdrawHealthFraction > 0f)
            {
                await DuelAsync(boss);
                return;
            }
        }

        if (!await SatisfyFlagAsync(flag, progress))
        {
            Fail($"{progress.Quest.Id}: milestone '{flag}' is never raised: no conversation sets it and no story rule derives it from flags the run can raise");
        }
    }

    // --- bosses ---------------------------------------------------------------------

    private static BossResource? FightOf(string template, string fightId = "")
    {
        if (fightId.Length > 0)
        {
            return BossDatabase.Get(fightId);
        }

        return EnemyArchetypeDatabase.Get(template) is { IsBoss: true } archetype ? BossDatabase.Get(archetype.BossId) : null;
    }

    /// <summary>The rival's duel: the Ashen Knight summoned under a duel fight yields.</summary>
    private async Task DuelAsync(BossResource duel)
    {
        string template = ScenePlacement.TemplateForFight(duel.Id) ?? "enemy.ashen_knight";
        Check(_flags.Has(ScenePlacement.RequiredFlagForFight(duel.Id) ?? string.Empty) ||
              ScenePlacement.RequiredFlagForFight(duel.Id) == null,
            $"the brazier of '{duel.Id}' is still cold ({ScenePlacement.RequiredFlagForFight(duel.Id)}) when the quest asks for the duel");
        var started = new List<string>();
        Action<DialogueStartedEvent> listener = e => started.Add(e.Dialogue.Id);
        EventBus.Instance?.Subscribe(listener);
        try
        {
            Check(_driver.WithdrawBoss(template, duel.Id) != null, $"'{template}' did not build a boss for '{duel.Id}'");
            Check(_flags.Has(duel.DefeatFlagId), $"the duel '{duel.Id}' ended without recording {duel.DefeatFlagId}");
            await DismissDefeatDialogueAsync(duel, started, "the duel's parting words");
        }
        finally
        {
            EventBus.Instance?.Unsubscribe(listener);
        }

        if (duel.DefeatDialogueId.Length > 0)
        {
            await ConverseAsync(duel.DefeatDialogueId, new DialogueAim(), "the duel's parting words");
        }
    }

    /// <summary>A Flamebearer falls: gate checked, real death published, director's flag/relic/conversation
    /// asserted, then the ember conversation driven per plan.</summary>
    private async Task BossFightAsync(string template)
    {
        BossResource? fight = FightOf(template);
        if (fight == null)
        {
            Fail($"'{template}' is a boss template with no boss fight");
            return;
        }

        await PreFightAsync(template);
        await AssertBrazierGateAsync(template);

        int corruptionBefore = _corruption.Value;
        bool hadFlag = _flags.Has(fight.DefeatFlagId);
        var started = new List<string>();
        Action<DialogueStartedEvent> listener = e => started.Add(e.Dialogue.Id);
        EventBus.Instance?.Subscribe(listener);
        try
        {
            if (_driver.KillBoss(template) == null)
            {
                Fail($"'{template}' did not build a boss entity");
                return;
            }

            Check(_flags.Has(fight.DefeatFlagId),
                $"the death of '{template}' did not set {fight.DefeatFlagId} (BossEncounterDirector)");
            if (!hadFlag && fight.RewardItemId.Length > 0)
            {
                Check(_pack.Contains(fight.RewardItemId), $"the victor's due '{fight.RewardItemId}' was not granted for '{template}'");
            }

            if (!hadFlag)
            {
                await DismissDefeatDialogueAsync(fight, started, $"the defeat of {template}");
            }
        }
        finally
        {
            EventBus.Instance?.Unsubscribe(listener);
        }

        // The ember conversation, the way a player takes or refuses it.
        if (fight.DefeatDialogueId.Length > 0 && fight.DefeatDialogueId != "dialogue.ash_throne")
        {
            string absorbedFlag = fight.DefeatFlagId.Replace("_defeated", "_absorbed");
            var aim = new DialogueAim();
            aim.Picks.Add(_plan.Absorb ? ".c_absorb" : ".c_decline");
            await ConverseAsync(fight.DefeatDialogueId, aim, $"the ember of {template}");
            if (_plan.Absorb)
            {
                Check(_flags.Has(absorbedFlag), $"absorbing the ember of '{template}' did not set {absorbedFlag}");
                Check(_corruption.Value == Math.Min(100, corruptionBefore + 25),
                    $"absorbing the ember of '{template}' moved corruption {corruptionBefore} -> {_corruption.Value} (expected +25)");
            }
            else
            {
                Check(!_flags.Has(absorbedFlag), $"refusing the ember of '{template}' still set {absorbedFlag}");
                Check(_corruption.Value == corruptionBefore,
                    $"refusing the ember of '{template}' moved corruption {corruptionBefore} -> {_corruption.Value}");
            }

            await IdleAsync($"the vision after {template}");
        }
        else if (fight.DefeatDialogueId == "dialogue.ash_throne")
        {
            Check(_flags.Has("flag.morthul_defeated"), "Morthul's death did not set flag.morthul_defeated");
        }

        // Slow-motion defeat beat from the director: let the clock settle before the next step.
        await SettleTimeAsync();
    }

    private async Task SettleTimeAsync()
    {
        ulong deadline = Time.GetTicksMsec() + 4000;
        while (Engine.TimeScale < 0.99f && Time.GetTicksMsec() < deadline)
        {
            await HeadlessLifecycle.Frames(_root, 2);
        }

        if (Engine.TimeScale < 0.99f)
        {
            Engine.TimeScale = 1f;
        }
    }

    /// <summary>The director queues the defeat conversation behind its slow-motion beat; it must arrive,
    /// and it must be the boss's own. The real panel opens it; closing it frees the world for the harness.</summary>
    private async Task DismissDefeatDialogueAsync(BossResource fight, List<string> started, string context)
    {
        if (fight.DefeatDialogueId.Length == 0)
        {
            return;
        }

        ulong deadline = Time.GetTicksMsec() + 9000;
        while (!started.Contains(fight.DefeatDialogueId) && Time.GetTicksMsec() < deadline)
        {
            await HeadlessLifecycle.Frames(_root, 2);
        }

        if (!started.Contains(fight.DefeatDialogueId))
        {
            Fail($"{context}: the director never opened '{fight.DefeatDialogueId}' (opened: [{string.Join(", ", started)}])");
            return;
        }

        if (_session.Ui.Dialogue.IsOpen)
        {
            _session.Ui.Dialogue.EndConversation();
        }

        await SettleTimeAsync();
    }

    /// <summary>Before the Ashen Knight's gate fight and the Queen's: the conversations the story puts first.</summary>
    private async Task PreFightAsync(string template)
    {
        if (template == "enemy.hollow_queen")
        {
            // The Queen's parley before the fight: a truce is offered only to the deeply corrupted.
            _driver.OpenDialogue("dialogue.hollow_queen_parley");
            bool truce = _driver.NodeId == "truce";
            Check(truce == _corruption.Value >= 60,
                $"the Queen's parley opened on '{_driver.NodeId}' at corruption {_corruption.Value} (a truce is offered at 60 and over)");
            var aim = new DialogueAim();
            if (truce)
            {
                aim.Picks.Add(".c_accept");
            }

            _driver.Converse(aim);
            Check(_flags.Has("flag.beat.queen_truce") == truce, $"flag.beat.queen_truce is {_flags.Has("flag.beat.queen_truce")} after the parley at corruption {_corruption.Value}");
            await HeadlessLifecycle.Frames(_root, 2);
        }
    }

    private const string KnightVigilFlag = "flag.beat.gate_vigil_done";

    /// <summary>The brazier in the scene must be lightable now: its quest/flag gate held, not closed, not
    /// already beaten. If the gate waits on a conversation, the player has it (as the locked prompt says).</summary>
    private async Task AssertBrazierGateAsync(string template)
    {
        // An export ships binary scenes, so no brazier can be read and the Knight's vigil (the optional
        // conversation his gate brazier waits on, with fork F6) would never be played there. The one gate
        // that waits on a conversation is known by name, so raise it the same way when the scenes are opaque.
        if (ScenePlacement.Placed == null && template == "enemy.ashen_knight" &&
            !_flags.Has(KnightVigilFlag))
        {
            await SatisfyFlagAsync(KnightVigilFlag, null);
        }

        foreach (SceneBrazier brazier in ScenePlacement.Braziers(template))
        {
            if (brazier.FightId.Length > 0)
            {
                continue;
            }

            if (brazier.RequiredFlag.Length > 0 && !_flags.Has(brazier.RequiredFlag))
            {
                await SatisfyFlagAsync(brazier.RequiredFlag, null);
            }

            bool questOk = brazier.RequiredQuest.Length == 0 || _log.IsCompleted(brazier.RequiredQuest);
            bool flagOk = brazier.RequiredFlag.Length == 0 || _flags.Has(brazier.RequiredFlag);
            bool open = !_flags.Has(brazier.Defeated) && (brazier.Closed.Length == 0 || !_flags.Has(brazier.Closed));
            Check(questOk && flagOk && open,
                $"the brazier of '{template}' in {brazier.Scene} is cold when the story asks for the fight: " +
                $"quest '{brazier.RequiredQuest}' completed={questOk}, flag '{brazier.RequiredFlag}' held={flagOk}, open={open}");
        }
    }

    // --- companion segment, legacy bookkeeping ----------------------------------------

    /// <summary>Takes over another playthrough's live session (a fixture that was just loaded).</summary>
    public void Adopt(StoryPlaythrough other)
    {
        _session = other._session;
        _player = other._player;
        _flags = other._flags;
        _log = other._log;
        _corruption = other._corruption;
        _pack = other._pack;
        _driver = other._driver;
        _rewardXp = other._rewardXp;
    }

    private int _rewardXp;
    private int _rewardLevel;
    private int _rewardGold;

    public void RememberRewards(int xp, int level, int gold)
    {
        _rewardXp = xp;
        _rewardLevel = level;
        _rewardGold = gold;
    }

    /// <summary>The catch-up completes quests silently: experience, level and gold are what they were before the load.</summary>
    public void AssertRewardsUnchanged(string label)
    {
        int xp = Progression?.CurrentXp ?? 0;
        int level = Progression?.Level ?? 0;
        int gold = _pack.CountOf("item.currency.gold");
        Check(xp == _rewardXp && level == _rewardLevel && gold == _rewardGold,
            $"[{label}] the load changed rewards: xp {_rewardXp}->{xp}, level {_rewardLevel}->{level}, gold {_rewardGold}->{gold} (a quest completed silently must not pay again)");
    }

    /// <summary>
    /// Kael joins through the real roster and the optional Escort of the quest at hand follows: with the
    /// escort done it advances and the quest still needs only its required steps; with it skipped the
    /// quest completes all the same, so a companion is never required.
    /// </summary>
    public async Task KaelSegmentAsync()
    {
        string questId = _plan.Order[0];
        if (Roster() is not { } roster || !roster.Recruit(_plan.Companion))
        {
            Fail($"the roster would not recruit {_plan.Companion}");
            return;
        }

        await HeadlessLifecycle.Frames(_root, 6);
        string partyFlag = StoryRuleData.PartyFlag(_plan.Companion);
        Check(_flags.Has(partyFlag), $"recruiting {_plan.Companion} did not raise {partyFlag}");
        QuestProgress? progress = Progress(questId);
        if (progress == null)
        {
            Fail($"{questId} is not in the branch save");
            return;
        }

        int escort = -1;
        List<ObjectiveResource> objectives = progress.Quest.ObjectiveList();
        for (int i = 0; i < objectives.Count; i++)
        {
            if (objectives[i].Type == ObjectiveType.Escort)
            {
                escort = i;
            }
        }

        if (escort < 0 || !objectives[escort].IsOptional)
        {
            Fail($"{questId} has no optional Escort objective for the Kael segment");
            return;
        }

        Check(progress.IsObjectiveInBranch(escort), $"{questId}: the Escort did not open with {_plan.Companion} in the party");
        if (!await CompleteQuestAsync(questId))
        {
            return;
        }

        Check(progress.IsObjectiveComplete(escort) == _plan.EscortCompanion,
            _plan.EscortCompanion
                ? $"{questId}: the optional Escort did not advance with {_plan.Companion} brought to {objectives[escort].LocationId}"
                : $"{questId}: the Escort completed although it was skipped");
        Check(_log.IsCompleted(questId), $"{questId} did not complete {(_plan.EscortCompanion ? "after the escort" : "without the optional Escort")}");
        Check(_log.IsActive("quest.warband.bounty"), $"{questId} completed but the next mission did not start");
    }

    /// <summary>The companion is at the objective's destination (they follow by their own AI in play).</summary>
    private async Task EscortAsync(QuestProgress progress, int index, ObjectiveResource objective)
    {
        if (Roster() is not { } roster || !roster.TryGet(objective.TargetId, out CompanionEntity companion))
        {
            Fail($"{progress.Quest.Id}: cannot escort '{objective.TargetId}' (not in the party)");
            return;
        }

        if (!await _driver.EscortTo(objective.LocationId, companion.Body.GlobalPosition, () => progress.IsObjectiveComplete(index)))
        {
            Fail($"{progress.Quest.Id}: the escort of '{objective.TargetId}' to '{objective.LocationId}' did not complete (position known: {PositionKnown(objective.LocationId)})");
        }
    }

    // --- hooks the runs call --------------------------------------------------------

    /// <summary>The player and the quest set as they stand, for a save/load comparison.</summary>
    public string StateStamp()
    {
        var active = new List<string>();
        var completed = new List<string>();
        foreach (QuestProgress progress in _log.Quests)
        {
            (progress.Status == QuestStatus.Active ? active : completed).Add(progress.Quest.Id);
        }

        active.Sort(StringComparer.Ordinal);
        completed.Sort(StringComparer.Ordinal);
        return $"active=[{string.Join(",", active)}] completed={completed.Count} tracked={_log.Tracked?.Quest.Id ?? "-"} " +
               $"corruption={_corruption.Value} flags={_flags.Flags.Count}";
    }

    /// <summary>Saves, destroys the session, loads: the active quest set, tracked quest, corruption and
    /// flags must come back exactly.</summary>
    public async Task CheckpointAsync(string label)
    {
        string before = StateStamp();
        string slot = _slot + "_checkpoint";
        if (!await ReloadAsync(slot, $"checkpoint '{label}'"))
        {
            return;
        }

        string after = StateStamp();
        Check(before == after, $"checkpoint '{label}': the state changed across save/load\n   before {before}\n   after  {after}");
        AssertTracked($"after loading checkpoint '{label}'");
        SaveManager.Instance?.DeleteSlot(slot);
    }

    public void RecordForks()
    {
        foreach ((string _, string[] flags) in EndingSequence.ForkEpilogues)
        {
            foreach (string flag in flags)
            {
                if (_flags.Has(flag))
                {
                    ForksTaken.Add(flag);
                }
            }
        }
    }

    /// <summary>The weather director's current sky id, or empty.</summary>
    public static string Sky() =>
        ServiceLocator.Instance is { } sl && sl.TryGet(out WeatherDirector weather) ? weather.Current?.Id ?? string.Empty : string.Empty;

    public static void ForceSky(string id) =>
        (ServiceLocator.Instance is { } sl && sl.TryGet(out WeatherDirector weather) ? weather : null)?.Force(id);

    public ProgressionComponent? Progression => _player.GetComponent<ProgressionComponent>();

    public ReputationHolder Reputation => new(_player);

    public readonly struct ReputationHolder
    {
        private readonly IEntity _player;

        public ReputationHolder(IEntity player) => _player = player;

        public int Get(string faction) =>
            _player.GetComponent<Factions.ReputationComponent>()?.Get(faction) ?? 0;
    }

    public CompanionRoster? Roster() =>
        ServiceLocator.Instance is { } sl && sl.TryGet(out CompanionRoster roster) ? roster : null;
}
