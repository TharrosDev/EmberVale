using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Embervale.Core.Events;
using Embervale.Core.Services;
using Embervale.Corruption;
using Embervale.Dialogue;
using Embervale.Enemies;
using Embervale.Entities;
using Embervale.Interaction;
using Embervale.Items;
using Embervale.Quests;
using Embervale.World;
using Godot;

namespace Embervale.Bootstrap;

/// <summary>
/// A reusable harness driver for story probes (<c>--story</c> and the content-spec probes that follow
/// it). It does the things a player would, through the paths the live systems use, so a probe proves
/// the wiring and not a private shortcut:
/// <list type="bullet">
/// <item>Dialogue: <see cref="OpenDialogue"/> builds the same <see cref="DialogueSession"/> the panel
/// builds (start variants, OnEnter and both effect pairs included), <see cref="Choose(int)"/> and
/// <see cref="ChooseText"/> pick a visible choice, <see cref="EndDialogue"/> publishes
/// <c>DialogueEndedEvent</c> exactly as the panel does on close. The speaker is not modelled: a session
/// needs only the player.</item>
/// <item>Objectives: <see cref="Interact"/> and <see cref="Kill"/> publish the very events the
/// interaction system and the damage pipeline publish; <see cref="Talk"/> is a conversation opened and
/// ended; <see cref="Reach"/> and <see cref="Defend"/> stand the player at the place and let the quest
/// log's own positional poll count it (Defend runs the world clock faster so a hold of seconds costs
/// frames, not minutes). Milestones are met by setting their flag through <see cref="StoryFlagsComponent"/>.</item>
/// <item><see cref="SetCorruption"/> moves corruption through a real <c>AddCorruption</c> dialogue effect.</item>
/// <item><see cref="Report"/> describes a quest's status and objectives for a failure message.</item>
/// </list>
/// Quests and dialogues may be built in memory (<see cref="Quest"/>, <see cref="Objective"/>); nothing
/// here registers them in a database.
/// </summary>
public sealed class StoryDriver
{
    /// <summary>World-clock speed-up while a Defend hold runs. The poll counts game seconds.</summary>
    private const float DefendTimeScale = 30f;
    private const int PositionalFrameBudget = 6000;
    /// <summary>A poll counts an arrival within a second or two; a place that is not counted in this many frames never will be.</summary>
    private const int ArrivalFrameBudget = 900;

    private readonly ApplicationRoot _root;
    private readonly IEntity _player;
    private DialogueSession? _session;

    public StoryDriver(ApplicationRoot root, IEntity player)
    {
        _root = root;
        _player = player;
    }

    public IEntity Player => _player;

    public DialogueSession? Session => _session;

    // --- dialogue -----------------------------------------------------------------

    /// <summary>Opens a conversation by id, ending any one still open first (the panel ignores an
    /// overlapping start, which would silently show the first graph under the second id). Null when the
    /// id is unknown.</summary>
    public DialogueSession? OpenDialogue(string dialogueId) =>
        DialogueDatabase.Get(dialogueId) is { } dialogue ? OpenDialogue(dialogue) : null;

    public DialogueSession OpenDialogue(DialogueResource dialogue)
    {
        EndDialogue();
        _session = new DialogueSession(dialogue, _player);
        _openDialogue = dialogue;
        return _session;
    }

    private DialogueResource? _openDialogue;

    /// <summary>The Text keys of the choices on offer right now, in order.</summary>
    public List<string> ChoiceKeys()
    {
        var keys = new List<string>();
        if (_session != null)
        {
            foreach (DialogueChoice choice in _session.VisibleChoices())
            {
                keys.Add(choice.Text);
            }
        }

        return keys;
    }

    /// <summary>Picks the n-th visible choice. False when there is no such choice. The conversation
    /// is closed (and its end event published) when the choice ends it.</summary>
    public bool Choose(int index)
    {
        if (_session == null)
        {
            return false;
        }

        List<DialogueChoice> visible = _session.VisibleChoices();
        if (index < 0 || index >= visible.Count)
        {
            return false;
        }

        if (_session.Choose(visible[index]))
        {
            EndDialogue();
        }

        return true;
    }

    /// <summary>Picks the visible choice whose text key is <paramref name="textKey"/>.</summary>
    public bool ChooseText(string textKey) => Choose(ChoiceKeys().IndexOf(textKey));

    /// <summary>The node the open conversation is on, or empty.</summary>
    public string NodeId => _session?.CurrentNode?.Id ?? string.Empty;

    /// <summary>Closes the open conversation the way the panel does: drop it and publish the end event
    /// (which is what a Talk objective counts).</summary>
    public void EndDialogue()
    {
        DialogueResource? dialogue = _openDialogue;
        _session = null;
        _openDialogue = null;
        if (dialogue != null)
        {
            EventBus.Instance?.Publish(new DialogueEndedEvent(_player, dialogue));
        }
    }

    /// <summary>Has the conversation (open it, close it): advances a Talk objective.</summary>
    public bool Talk(string dialogueId)
    {
        if (OpenDialogue(dialogueId) == null)
        {
            return false;
        }

        EndDialogue();
        return true;
    }

    /// <summary>The choices on offer right now (the objects, so a caller can read their effects).</summary>
    public List<DialogueChoice> Visible() => _session?.VisibleChoices() ?? new List<DialogueChoice>();

    /// <summary>
    /// Runs the open conversation to its end the way a player who knows what they want would: an
    /// explicit pick from <see cref="DialogueAim.Picks"/> first (in order, full text key or a
    /// <c>.suffix</c>), then a reply whose effect raises a wanted flag or starts a wanted quest, then a
    /// reply that leads (through any number of nodes) to one, then a way out. A reply with a side effect
    /// nobody asked for (corruption, a fork or ending flag, recruiting, joining, taking items, opening a
    /// shop) is never taken unless it is the only way on. Closing publishes the end event, as the panel does.
    /// </summary>
    public ConversationResult Converse(DialogueAim aim, int maxSteps = 60)
    {
        var trace = new StringBuilder();
        var picks = new Queue<string>(aim.Picks);
        var visited = new Dictionary<string, int>();
        StoryFlagsComponent? flags = _player.GetComponent<StoryFlagsComponent>();
        bool Has(string f) => flags != null && flags.Has(f);
        for (int step = 0; step < maxSteps && _session is { CurrentNode: { } node } session; step++)
        {
            visited[node.Id] = visited.GetValueOrDefault(node.Id) + 1;
            List<DialogueChoice> choices = session.VisibleChoices();
            if (choices.Count == 0)
            {
                trace.Append($"{node.Id}:(dead end) ");
                break;
            }

            DialogueChoice choice = SelectChoice(session.Dialogue, node, choices, aim, picks, visited, Has);
            trace.Append($"{node.Id}:{choice.Text} ");
            if (session.Choose(choice))
            {
                EndDialogue();
                return new ConversationResult(trace.ToString().TrimEnd(), picks.ToArray(), true);
            }
        }

        bool forced = _session != null;
        if (forced)
        {
            EndDialogue();
        }

        return new ConversationResult(trace.ToString().TrimEnd(), picks.ToArray(), !forced);
    }

    private static DialogueChoice SelectChoice(
        DialogueResource dialogue, DialogueNode node, List<DialogueChoice> choices, DialogueAim aim,
        Queue<string> picks, Dictionary<string, int> visited, System.Func<string, bool> has)
    {
        if (picks.Count > 0)
        {
            string wanted = picks.Peek();
            foreach (DialogueChoice choice in choices)
            {
                if (choice.Text == wanted || (wanted.StartsWith('.') && choice.Text.EndsWith(wanted)))
                {
                    picks.Dequeue();
                    return choice;
                }
            }
        }

        // 1. a reply that does the wanted thing itself (a hand-in that takes an item is part of doing it)
        foreach (DialogueChoice choice in choices)
        {
            if (Advances(choice, aim, has) && !Dangerous(choice, aim, allowTake: true))
            {
                return choice;
            }
        }

        // 2. a reply that leads to one, nearest first, never back into a node already walked twice
        DialogueChoice? leading = null;
        int bestDepth = int.MaxValue;
        foreach (DialogueChoice choice in choices)
        {
            if (Dangerous(choice, aim) || choice.Goto.Length == 0 || visited.GetValueOrDefault(choice.Goto) > 1)
            {
                continue;
            }

            int depth = DepthToAim(dialogue, choice.Goto, aim, has, new HashSet<string> { node.Id });
            if (depth >= 0 && depth < bestDepth)
            {
                bestDepth = depth;
                leading = choice;
            }
        }

        if (leading != null)
        {
            return leading;
        }

        // 3. a way out that costs nothing, else any unvisited step on, else whatever is left
        foreach (DialogueChoice choice in choices)
        {
            if (!Dangerous(choice, aim) && IsExit(dialogue, choice))
            {
                return choice;
            }
        }

        foreach (DialogueChoice choice in choices)
        {
            if (!Dangerous(choice, aim) && !visited.ContainsKey(choice.Goto))
            {
                return choice;
            }
        }

        foreach (DialogueChoice choice in choices)
        {
            if (!Dangerous(choice, aim))
            {
                return choice;
            }
        }

        foreach (DialogueChoice choice in choices)
        {
            if (IsExit(dialogue, choice))
            {
                return choice;
            }
        }

        return choices[0];
    }

    private static bool IsExit(DialogueResource dialogue, DialogueChoice choice) =>
        choice.Goto.Length == 0 || dialogue.FindNode(choice.Goto) == null;

    /// <summary>Whether a reply is one the aim wants: a listed pick, or an effect that raises a wanted flag.</summary>
    internal static bool IsWanted(DialogueChoice choice, DialogueAim aim, System.Func<string, bool> has)
    {
        foreach (string pick in aim.Picks)
        {
            if (choice.Text == pick || (pick.StartsWith('.') && choice.Text.EndsWith(pick)))
            {
                return true;
            }
        }

        return Advances(choice, aim, has);
    }

    private static bool Advances(DialogueChoice choice, DialogueAim aim, System.Func<string, bool> has) =>
        AdvancesEffect(choice.Effect, choice.EffectArg, aim, has) ||
        AdvancesEffect(choice.Effect2, choice.Effect2Arg, aim, has);

    private static bool AdvancesEffect(DialogueEffect effect, string arg, DialogueAim aim, System.Func<string, bool> has) =>
        (effect == DialogueEffect.SetFlag && aim.Flags.Contains(arg) && !has(arg)) ||
        (effect == DialogueEffect.StartQuest && aim.Quests.Contains(arg));

    /// <summary>Fewest steps to a node whose choices (or entry) do something wanted, or -1.</summary>
    private static int DepthToAim(
        DialogueResource dialogue, string nodeId, DialogueAim aim, System.Func<string, bool> has, HashSet<string> seen)
    {
        if (dialogue.FindNode(nodeId) is not { } node || !seen.Add(nodeId))
        {
            return -1;
        }

        if (AdvancesEffect(node.OnEnterEffect, node.OnEnterEffectArg, aim, has))
        {
            return 0;
        }

        int best = -1;
        foreach (DialogueChoice choice in node.ChoiceList())
        {
            if (Advances(choice, aim, has))
            {
                return 0;
            }

            if (choice.Goto.Length == 0)
            {
                continue;
            }

            int below = DepthToAim(dialogue, choice.Goto, aim, has, seen);
            if (below >= 0 && (best < 0 || below + 1 < best))
            {
                best = below + 1;
            }
        }

        return best;
    }

    /// <summary>A reply with a side effect the caller did not ask for. The price attached to a reply that
    /// raises a flag the caller wants (the Choir's verse costs corruption) is part of the ask.</summary>
    private static bool Dangerous(DialogueChoice choice, DialogueAim aim, bool allowTake = false)
    {
        bool priced = SetsWanted(choice.Effect, choice.EffectArg, aim) || SetsWanted(choice.Effect2, choice.Effect2Arg, aim);
        return DangerousEffect(choice.Effect, choice.EffectArg, aim, allowTake, priced) ||
               DangerousEffect(choice.Effect2, choice.Effect2Arg, aim, allowTake, priced);
    }

    private static bool SetsWanted(DialogueEffect effect, string arg, DialogueAim aim) =>
        effect == DialogueEffect.SetFlag && aim.Flags.Contains(arg);

    private static bool DangerousEffect(DialogueEffect effect, string arg, DialogueAim aim, bool allowTake, bool priced)
    {
        switch (effect)
        {
            case DialogueEffect.AddCorruption:
                return !priced && (!int.TryParse(arg, out int amount) || amount > 0);
            case DialogueEffect.SetFlag:
                return !aim.Flags.Contains(arg) &&
                       (arg.StartsWith("flag.fork.") || arg.StartsWith("flag.rival.gate_") || arg.StartsWith("flag.ending_"));
            case DialogueEffect.StartQuest:
                return !aim.Quests.Contains(arg);
            case DialogueEffect.RecruitCompanion:
            case DialogueEffect.JoinGuild:
            case DialogueEffect.GuildRank:
            case DialogueEffect.OpenShop:
            case DialogueEffect.OpenService:
            case DialogueEffect.LearnSpell:
                return true;
            case DialogueEffect.TakeItem:
                return !allowTake;
            default:
                return false;
        }
    }

    /// <summary>Moves corruption to <paramref name="value"/> through a real AddCorruption dialogue
    /// effect (a one-node conversation, entered the way any other is).</summary>
    public bool SetCorruption(int value)
    {
        if (_player.GetComponent<CorruptionComponent>() is not { } corruption)
        {
            return false;
        }

        var node = new DialogueNode
        {
            Id = "fixture",
            Text = "fixture",
            OnEnterEffect = DialogueEffect.AddCorruption,
            OnEnterEffectArg = (value - corruption.Value).ToString(System.Globalization.CultureInfo.InvariantCulture),
        };
        var dialogue = new DialogueResource
        {
            Id = "dialogue.driver.fixture",
            StartNodeId = "fixture",
            Nodes = new Godot.Collections.Array { node },
        };
        OpenDialogue(dialogue);
        EndDialogue();
        return corruption.Value == value;
    }

    // --- objectives -----------------------------------------------------------------

    /// <summary>The player uses an interactable carrying this <c>InteractId</c>.</summary>
    public void Interact(string interactId)
    {
        var target = new DriverInteractable { InteractId = interactId };
        EventBus.Instance?.Publish(new InteractionPerformedEvent(_player, target));
        target.Free();
    }

    /// <summary>The player kills <paramref name="count"/> actors of this template.</summary>
    public void Kill(string templateId, int count = 1)
    {
        for (int i = 0; i < count; i++)
        {
            // In the tree, as a real corpse is: listeners read its position.
            var victim = new Entity { TemplateId = templateId };
            (_player.Body.GetParent() ?? _player.Body).AddChild(victim);
            victim.GlobalPosition = _player.Body.GlobalPosition;
            EventBus.Instance?.Publish(new EntityDiedEvent(victim, _player));
            victim.Free();
        }
    }

    /// <summary>The player picks up <paramref name="count"/> of an item: into the pack and then the
    /// very event the world pickup publishes (which is what a Collect objective counts).</summary>
    public bool Pickup(string itemId, int count)
    {
        if (ItemDatabase.Get(itemId) is not { } item || _player.GetComponent<InventoryComponent>() is not { } pack)
        {
            return false;
        }

        int added = pack.AddInstance(ItemInstance.Plain(item), count);
        if (added > 0)
        {
            EventBus.Instance?.Publish(new ItemPickedUpEvent(_player, item, added));
        }

        return added == count;
    }

    /// <summary>
    /// A real boss entity falls to the player's blow: built by the template registry, in the tree beside
    /// the player (its controller resolves its fight there), then the death event the damage pipeline
    /// would publish. <c>BossEncounterDirector</c> and the quest log answer it exactly as in play. The
    /// fight itself cannot be played headless; this is its end. Null when the template is no boss.
    /// </summary>
    public BossEntity? KillBoss(string templateId, string fightId = "")
    {
        BossEntity? boss = SpawnBoss(templateId, fightId);
        if (boss == null)
        {
            return null;
        }

        EventBus.Instance?.Publish(new EntityDiedEvent(boss, _player));
        boss.Free();
        return boss;
    }

    /// <summary>A yielding duel boss withdraws (the rival's duels): the same fight-end the director
    /// handles for a death, without a kill. Null when the template is no boss.</summary>
    public BossEntity? WithdrawBoss(string templateId, string fightId)
    {
        BossEntity? boss = SpawnBoss(templateId, fightId);
        if (boss == null)
        {
            return null;
        }

        EventBus.Instance?.Publish(new BossWithdrewEvent(boss));
        boss.Free();
        return boss;
    }

    private BossEntity? SpawnBoss(string templateId, string fightId)
    {
        EnemyEntity enemy = EnemyTemplateRegistry.Create(templateId, Vector3.Zero);
        if (enemy is not BossEntity boss)
        {
            enemy.Free();
            return null;
        }

        if (fightId.Length > 0 && boss.GetNodeOrNull<BossController>("BossController") is { } controller)
        {
            controller.BossId = fightId;
        }

        (_player.Body.GetParent() ?? _player.Body).AddChild(boss);
        boss.GlobalPosition = _player.Body.GlobalPosition;
        return boss;
    }

    /// <summary>
    /// Arrives at a place and waits for the quest log's own poll to count it. The place is brought to the
    /// player rather than the player to the place: the realms lie hundreds of metres apart in separate
    /// regions, no terrain is streamed at their coordinates, and a body teleported there falls out of the
    /// world. The place must exist (a baked or scene-placed map pin); only where it stands is borrowed.
    /// False when the place has no position or <paramref name="done"/> never became true.
    /// </summary>
    public async Task<bool> Reach(string locationId, System.Func<bool> done)
    {
        if (!Bring(locationId, out Vector3? original))
        {
            return false;
        }

        bool ok = await WaitFor(done, ArrivalFrameBudget);
        Return(locationId, original);
        return ok;
    }

    /// <summary>Holds a place: the player stands at it with the clock sped up until <paramref name="done"/>.
    /// The physics steps per frame are capped while it runs: the hold counts one poll per frame, so more
    /// simulation per frame buys nothing.</summary>
    public async Task<bool> Defend(string locationId, System.Func<bool> done)
    {
        if (!Bring(locationId, out Vector3? original))
        {
            return false;
        }

        float previous = (float)Engine.TimeScale;
        int steps = Engine.MaxPhysicsStepsPerFrame;
        Engine.TimeScale = DefendTimeScale;
        Engine.MaxPhysicsStepsPerFrame = 1;
        bool ok;
        try
        {
            ok = await WaitFor(done, PositionalFrameBudget, HoldPollsPerFrame);
        }
        finally
        {
            Engine.TimeScale = previous;
            Engine.MaxPhysicsStepsPerFrame = steps;
        }

        Return(locationId, original);
        return ok;
    }

    /// <summary>Puts a companion's destination where the companion stands, for an Escort the companion
    /// then completes by standing at it.</summary>
    public async Task<bool> EscortTo(string locationId, Vector3 companionAt, System.Func<bool> done)
    {
        if (!Bring(locationId, out Vector3? original, companionAt))
        {
            return false;
        }

        bool ok = await WaitFor(done, ArrivalFrameBudget);
        Return(locationId, original);
        return ok;
    }

    /// <summary>The map location nearest the player whose position the map can answer, or empty.</summary>
    public string NearestLocation()
    {
        if (ServiceLocator.Instance is not { } locator || !locator.TryGet(out MapService map))
        {
            return string.Empty;
        }

        string best = string.Empty;
        float bestDistance = float.MaxValue;
        Vector3 here = _player.Body.GlobalPosition;
        foreach (MapLocationResource location in MapLocationDatabase.All)
        {
            if (map.PositionOf(location.Id) is not { } at)
            {
                continue;
            }

            float distance = here.DistanceSquaredTo(at);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = location.Id;
            }
        }

        return best;
    }

    /// <summary>Where a place is when the map has no position for it (the bake is stale for a location
    /// authored since): the scene-text answer. Null leaves the place unresolvable.</summary>
    public System.Func<string, Vector3?>? PositionFallback { get; set; }

    /// <summary>Locations that had no baked position and were placed from scene text instead. A master bake empties it.</summary>
    public static HashSet<string> Unbaked { get; } = new();

    /// <summary>The map position for a place; when the bake has none, the fallback position, registered with
    /// the map the way a streaming cell registers its pins (so the quest log poll sees it).</summary>
    public Vector3? Resolve(string locationId)
    {
        if (ServiceLocator.Instance is not { } locator || !locator.TryGet(out MapService map))
        {
            return null;
        }

        if (map.PositionOf(locationId) is { } known)
        {
            return known;
        }

        if (PositionFallback?.Invoke(locationId) is not { } fallback)
        {
            return null;
        }

        map.RegisterLocation(locationId, fallback);
        Unbaked.Add(locationId);
        return fallback;
    }

    private static readonly Vector3 Parked = new(1_000_000f, 0f, 1_000_000f);

    private bool Bring(string locationId, out Vector3? original, Vector3? at = null)
    {
        original = Resolve(locationId);
        if (original == null || ServiceLocator.Instance is not { } locator || !locator.TryGet(out MapService map))
        {
            return false;
        }

        _brought = (locationId, at);
        map.RegisterLocation(locationId, at ?? _player.Body.GlobalPosition);
        return true;
    }

    private (string Id, Vector3? At)? _brought;

    /// <summary>The streamer re-registers a resident cell's pins at their baked positions while the world
    /// is still filling in (right after a load); the place is put back where the player stands each frame.</summary>
    private void KeepBrought()
    {
        if (_brought is { } brought && ServiceLocator.Instance is { } locator && locator.TryGet(out MapService map))
        {
            map.RegisterLocation(brought.Id, brought.At ?? _player.Body.GlobalPosition);
        }
    }

    private void Return(string locationId, Vector3? original)
    {
        _brought = null;
        if (ServiceLocator.Instance is { } locator && locator.TryGet(out MapService map))
        {
            map.RegisterLocation(locationId, original ?? Parked);
        }
    }

    /// <summary>
    /// The quest log counts a hold one poll (a quarter second) per frame. Extra polls are run through its
    /// own <c>_Process</c> (the same code, the same quarter-second step) so a minute of holding costs a
    /// handful of frames instead of two hundred; the frames are what the headless run spends its time on.
    /// </summary>
    private void HoldPollsPerFrame()
    {
        if (_player.GetComponent<QuestLogComponent>() is { } log)
        {
            for (int i = 0; i < 7; i++)
            {
                log._Process(0.25);
            }
        }
    }

    private async Task<bool> WaitFor(System.Func<bool> done, int budget = PositionalFrameBudget, System.Action? pump = null)
    {
        for (int frame = 0; frame < budget; frame++)
        {
            if (done())
            {
                return true;
            }

            pump?.Invoke();
            await HeadlessLifecycle.Frames(_root, 1);
            KeepBrought();
        }

        return done();
    }

    // --- in-memory content ------------------------------------------------------------

    /// <summary>An objective built in memory.</summary>
    public static ObjectiveResource Objective(
        ObjectiveType type, string target, int count = 1, bool optional = false, string completionFlag = "") =>
        new()
        {
            Type = type,
            TargetId = target,
            RequiredCount = count,
            IsOptional = optional,
            CompletionFlagId = completionFlag,
        };

    /// <summary>A quest built in memory (sequential objectives, not in any database).</summary>
    public static QuestResource Quest(string id, bool sequential, params ObjectiveResource[] objectives)
    {
        var list = new Godot.Collections.Array();
        foreach (ObjectiveResource objective in objectives)
        {
            list.Add(objective);
        }

        return new QuestResource { Id = id, Title = id, SequentialObjectives = sequential, Objectives = list };
    }

    // --- reporting --------------------------------------------------------------------

    /// <summary>One line: the quest's status and each objective as <c>index:type target count/needed</c>
    /// with a mark for done (<c>+</c>), live (<c>*</c>) or waiting (<c>.</c>).</summary>
    public string Report(string questId)
    {
        QuestLogComponent? log = _player.GetComponent<QuestLogComponent>();
        QuestProgress? progress = null;
        if (log != null)
        {
            foreach (QuestProgress candidate in log.Quests)
            {
                if (candidate.Quest.Id == questId)
                {
                    progress = candidate;
                    break;
                }
            }
        }

        if (progress == null)
        {
            return $"{questId}: not in the log";
        }

        var text = new StringBuilder($"{questId}: {progress.Status}");
        List<ObjectiveResource> objectives = progress.Quest.ObjectiveList();
        for (int i = 0; i < objectives.Count; i++)
        {
            char mark = progress.IsObjectiveComplete(i) ? '+' : progress.IsObjectiveActive(i) ? '*' : '.';
            text.Append($" [{i}{mark} {objectives[i].Type} {objectives[i].TargetId} {progress.Counts[i]}/{objectives[i].RequiredCount}]");
        }

        return text.ToString();
    }
}

/// <summary>A minimal interactable carrying only an id: the thing the quest log reads off an
/// <see cref="InteractionPerformedEvent"/>.</summary>
internal sealed partial class DriverInteractable : InteractableComponent
{
    public override string Prompt => string.Empty;

    public override bool Interact(IEntity instigator) => true;
}

/// <summary>What a conversation is meant to achieve, for <see cref="StoryDriver.Converse"/>.</summary>
public sealed class DialogueAim
{
    /// <summary>Explicit choices, in the order they are met: a full text key or a <c>.suffix</c>.</summary>
    public List<string> Picks { get; } = new();

    /// <summary>Flags the talk should raise (a reply that sets one is preferred).</summary>
    public HashSet<string> Flags { get; } = new();

    /// <summary>Quests the talk may start.</summary>
    public HashSet<string> Quests { get; } = new();
}

/// <summary>How a conversation went: the node:choice path, picks never met, and whether it ended by a choice.</summary>
public sealed record ConversationResult(string Trace, string[] UnusedPicks, bool Ended);
