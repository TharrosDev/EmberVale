using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Embervale.Core.Events;
using Embervale.Core.Services;
using Embervale.Corruption;
using Embervale.Dialogue;
using Embervale.Entities;
using Embervale.Interaction;
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
    private const int PositionalFrameBudget = 1200;

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

    /// <summary>Stands the player at the location and waits for the quest log's own poll to see it.
    /// False when the position is unknown or <paramref name="done"/> never became true.</summary>
    public async Task<bool> Reach(string locationId, System.Func<bool> done)
    {
        if (!StandAt(locationId, out Vector3 home))
        {
            return false;
        }

        bool ok = await WaitFor(done);
        _player.Body.GlobalPosition = home;
        return ok;
    }

    /// <summary>Holds a place: stands at it with the clock sped up until <paramref name="done"/>.</summary>
    public async Task<bool> Defend(string locationId, System.Func<bool> done)
    {
        if (!StandAt(locationId, out Vector3 home))
        {
            return false;
        }

        float previous = (float)Engine.TimeScale;
        Engine.TimeScale = DefendTimeScale;
        bool ok;
        try
        {
            ok = await WaitFor(done);
        }
        finally
        {
            Engine.TimeScale = previous;
        }

        _player.Body.GlobalPosition = home;
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

    private bool StandAt(string locationId, out Vector3 home)
    {
        home = _player.Body.GlobalPosition;
        if (ServiceLocator.Instance is not { } locator || !locator.TryGet(out MapService map) ||
            map.PositionOf(locationId) is not { } at)
        {
            return false;
        }

        _player.Body.GlobalPosition = at;
        return true;
    }

    private async Task<bool> WaitFor(System.Func<bool> done)
    {
        for (int frame = 0; frame < PositionalFrameBudget; frame++)
        {
            if (done())
            {
                return true;
            }

            await HeadlessLifecycle.Frames(_root, 2);
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
