using System.Collections.Generic;
using Embervale.Companions;
using Embervale.Core.Diagnostics;
using Embervale.Core.Events;
using Embervale.Core.Services;
using Embervale.Corruption;
using Embervale.Economy;
using Embervale.Entities;
using Embervale.Magic;
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
    private readonly IEntity _player;
    private readonly QuestLogComponent? _questLog;
    private readonly StoryFlagsComponent? _flags;
    private readonly CorruptionComponent? _corruption;
    private readonly SpellcastingComponent? _spellcasting;

    public DialogueResource Dialogue { get; }

    public DialogueNode? CurrentNode { get; private set; }

    public bool IsEnded => CurrentNode == null;

    public DialogueSession(DialogueResource dialogue, IEntity player)
    {
        Dialogue = dialogue;
        _player = player;
        _questLog = player.GetComponent<QuestLogComponent>();
        _flags = player.GetComponent<StoryFlagsComponent>();
        _corruption = player.GetComponent<CorruptionComponent>();
        _spellcasting = player.GetComponent<SpellcastingComponent>();
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
            case DialogueCondition.CorruptionAtLeast:
                return _corruption != null && _corruption.Value >= ParseAmount(arg);
            case DialogueCondition.CorruptionBelow:
                return _corruption != null && _corruption.Value < ParseAmount(arg);
            case DialogueCondition.CompanionRecruited:
                return Roster()?.IsRecruited(arg) ?? false;
            case DialogueCondition.CompanionNotRecruited:
                return !(Roster()?.IsRecruited(arg) ?? false);
            case DialogueCondition.CompanionLoyaltyAtLeast:
                return CompanionArg.TryParse(arg, out string loyaltyId, out int threshold) &&
                    Roster() is { } loyaltyRoster && loyaltyRoster.LoyaltyOf(loyaltyId) >= threshold;
            default:
                return true;
        }
    }

    /// <summary>The party roster, resolved live from the <see cref="ServiceLocator"/> — a
    /// conversation may outlive any particular world build, and the roster is a world service rather
    /// than something the speaking actor owns.</summary>
    private static CompanionRoster? Roster() =>
        ServiceLocator.Instance != null && ServiceLocator.Instance.TryGet(out CompanionRoster roster)
            ? roster
            : null;

    /// <summary>Parses a numeric dialogue argument (corruption threshold/amount); 0 if malformed
    /// (the content validator flags non-numeric corruption args at author time).</summary>
    private static int ParseAmount(string arg) => int.TryParse(arg, out int value) ? value : 0;

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
            case DialogueEffect.AddCorruption:
                _corruption?.Add(ParseAmount(arg));
                break;
            case DialogueEffect.RecruitCompanion:
                if (Roster() is { } recruiting && !recruiting.Recruit(arg))
                {
                    Log.Warn($"Dialogue effect RecruitCompanion: '{arg}' was not recruited (unknown id, already in the party, or the party is full).");
                }

                break;
            case DialogueEffect.DismissCompanion:
                Roster()?.Dismiss(arg);
                break;
            case DialogueEffect.AddCompanionLoyalty:
                if (CompanionArg.TryParse(arg, out string loyaltyTarget, out int delta))
                {
                    Roster()?.AddLoyalty(loyaltyTarget, delta);
                }
                else
                {
                    Log.Warn($"Dialogue effect AddCompanionLoyalty: malformed argument '{arg}' (expected <companionId>:<delta>).");
                }

                break;
            case DialogueEffect.LearnSpell:
                if (SpellDatabase.Get(arg) is not { } taught)
                {
                    Log.Warn($"Dialogue effect LearnSpell: unknown spell '{arg}'.");
                }
                else if (_spellcasting != null && !_spellcasting.IsKnown(taught))
                {
                    // Learn re-checks the 23H corruption gate itself and no-ops when it fails, so a
                    // teacher offering a corrupted spell to the untainted is refused here as it is at
                    // a tome — silently, which is what "the words writhe out of reach" looks like.
                    _spellcasting.Learn(arg);
                    EventBus.Instance?.Publish(new SpellsChangedEvent(_player));
                }

                break;
            case DialogueEffect.OpenShop:
                // The conversation ends on the same choice (an OpenShop choice authors no Goto), so the
                // vendor window registers with UiState before the dialogue panel deregisters — the owner
                // count never reaches zero and neither the pause nor the mouse mode flickers between them.
                if (ShopDatabase.Get(arg) is { } shop)
                {
                    EventBus.Instance?.Publish(new ShopOpenedEvent(_player, shop));
                }
                else
                {
                    Log.Warn($"Dialogue effect OpenShop: unknown shop '{arg}'.");
                }

                break;
        }
    }
}
