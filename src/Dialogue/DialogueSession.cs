using System.Collections.Generic;
using Embervale.Core.Diagnostics;
using Embervale.Entities;
using Embervale.Quests;

namespace Embervale.Dialogue;

/// <summary>
/// The runtime walk of a single <see cref="DialogueResource"/> for one actor. It
/// tracks the current node, filters each node's choices by their
/// <see cref="DialogueCondition"/> against the player's quest log and story flags, and
/// applies a choice's <see cref="DialogueEffect"/> when picked before advancing. It is
/// a plain object (not a node) so the dialogue UI stays a thin view: it renders
/// <see cref="CurrentNode"/> + <see cref="VisibleChoices"/> and forwards clicks to
/// <see cref="Choose"/>.
/// </summary>
public sealed class DialogueSession
{
    private readonly QuestLogComponent? _questLog;
    private readonly StoryFlagsComponent? _flags;

    public DialogueResource Dialogue { get; }

    public DialogueNode? CurrentNode { get; private set; }

    public bool IsEnded => CurrentNode == null;

    public DialogueSession(DialogueResource dialogue, IEntity player)
    {
        Dialogue = dialogue;
        _questLog = player.GetComponent<QuestLogComponent>();
        _flags = player.GetComponent<StoryFlagsComponent>();
        CurrentNode = dialogue.StartNode();
    }

    /// <summary>Speaker name for the current node (node override, else the conversation's).</summary>
    public string CurrentSpeaker()
    {
        if (CurrentNode != null && !string.IsNullOrEmpty(CurrentNode.Speaker))
        {
            return CurrentNode.Speaker;
        }

        return Dialogue.SpeakerName;
    }

    /// <summary>The current node's choices whose conditions currently pass.</summary>
    public List<DialogueChoice> VisibleChoices()
    {
        var visible = new List<DialogueChoice>();
        if (CurrentNode == null)
        {
            return visible;
        }

        foreach (DialogueChoice choice in CurrentNode.ChoiceList())
        {
            if (Evaluate(choice.Condition, choice.ConditionArg))
            {
                visible.Add(choice);
            }
        }

        return visible;
    }

    /// <summary>Applies the choice's effect and advances to its target node.
    /// Returns true if the conversation is now ended.</summary>
    public bool Choose(DialogueChoice choice)
    {
        if (choice == null)
        {
            return IsEnded;
        }

        ApplyEffect(choice.Effect, choice.EffectArg);
        CurrentNode = Dialogue.FindNode(choice.Goto); // empty/unknown id => null => ended
        return IsEnded;
    }

    private bool Evaluate(DialogueCondition condition, string arg)
    {
        switch (condition)
        {
            case DialogueCondition.Always:
                return true;
            case DialogueCondition.QuestAvailable:
                return QuestDatabase.Get(arg) is { } q && (_questLog?.CanStart(q) ?? false);
            case DialogueCondition.QuestActive:
                return _questLog?.IsActive(arg) ?? false;
            case DialogueCondition.QuestCompleted:
                return _questLog?.IsCompleted(arg) ?? false;
            case DialogueCondition.QuestNotStarted:
                return !(_questLog?.HasQuest(arg) ?? false);
            case DialogueCondition.HasFlag:
                return _flags?.Has(arg) ?? false;
            case DialogueCondition.MissingFlag:
                return !(_flags?.Has(arg) ?? false);
            default:
                return true;
        }
    }

    private void ApplyEffect(DialogueEffect effect, string arg)
    {
        switch (effect)
        {
            case DialogueEffect.None:
                break;
            case DialogueEffect.StartQuest:
                if (QuestDatabase.Get(arg) is { } quest)
                {
                    _questLog?.StartQuest(quest);
                }
                else
                {
                    Log.Warn($"Dialogue effect StartQuest: unknown quest '{arg}'.");
                }

                break;
            case DialogueEffect.SetFlag:
                _flags?.Set(arg);
                break;
            case DialogueEffect.ClearFlag:
                _flags?.Clear(arg);
                break;
        }
    }
}
