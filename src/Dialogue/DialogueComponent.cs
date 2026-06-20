using Embervale.Core.Diagnostics;
using Embervale.Core.Events;
using Embervale.Entities;
using Embervale.Interaction;
using Godot;

namespace Embervale.Dialogue;

/// <summary>
/// An interactable that starts a conversation. On the player's <c>E</c> raycast it
/// resolves its <see cref="DialogueResource"/> through the <see cref="DialogueDatabase"/>
/// and publishes a <see cref="DialogueStartedEvent"/>; the dialogue UI picks that up and
/// runs the conversation (quests are offered, flags set, etc. via the dialogue's own
/// choices). This replaces bare quest-givers: an NPC now talks, and offering a quest is
/// just a choice effect.
/// </summary>
[GlobalClass]
public partial class DialogueComponent : InteractableComponent
{
    /// <summary>Conversation offered, resolved through the <see cref="DialogueDatabase"/>.</summary>
    [Export] public string DialogueId { get; set; } = string.Empty;

    /// <summary>Optional prompt-name override; falls back to the conversation's speaker.</summary>
    [Export] public string SpeakerName { get; set; } = string.Empty;

    private DialogueResource? Dialogue => DialogueDatabase.Get(DialogueId);

    public override string Prompt
    {
        get
        {
            string who = !string.IsNullOrEmpty(SpeakerName)
                ? SpeakerName
                : Dialogue?.SpeakerName ?? Entity?.DisplayName ?? "someone";
            return $"Talk to {who}";
        }
    }

    public override void Interact(IEntity instigator)
    {
        DialogueResource? dialogue = Dialogue;
        if (dialogue == null)
        {
            Log.Warn($"DialogueComponent: unknown dialogue id '{DialogueId}'.");
            return;
        }

        if (Entity == null)
        {
            return;
        }

        EventBus.Instance?.Publish(new DialogueStartedEvent(instigator, Entity, dialogue));
    }
}
