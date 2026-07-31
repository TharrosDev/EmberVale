using Embervale.Core.Diagnostics;
using Embervale.Core.Events;
using Embervale.Entities;
using Embervale.Interaction;
using Embervale.Localization;
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
            // No offer to talk to a corpse or to something mid-swing. The HUD hides the prompt panel on
            // an empty string, so this and the Interact guard stay one decision — a visible prompt that
            // does nothing when pressed reads as a broken key, not as a refusal.
            if (!CanTalk())
            {
                return string.Empty;
            }

            string who = !string.IsNullOrEmpty(SpeakerName)
                ? SpeakerName
                : Dialogue?.SpeakerName ?? Entity?.DisplayName ?? string.Empty;

            // Speaker names may be authored as Loc keys (companions are) or as plain text (the older
            // NPCs); Loc.T returns the key unchanged when it isn't in the catalogue, so both work.
            return Loc.TF("interact.talk_to", Loc.T(who));
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

        if (Entity == null || !CanTalk())
        {
            return;
        }

        EventBus.Instance?.Publish(new DialogueStartedEvent(instigator, Entity, dialogue));
    }

    /// <summary>
    /// Whether this actor is in any state to hold a conversation. Every dialogue owner up to Phase 35F
    /// was a peaceful, non-combat <see cref="Entities.Entity"/>, so the question never came up; 35F put
    /// a <see cref="DialogueComponent"/> on a boss for the first time and opened two holes at once.
    ///
    /// <b>Dead things do not talk.</b> A slain enemy lingers for its profile's <c>DespawnDelay</c>
    /// before it is freed, and the interact raycast keeps resolving it the whole time.
    ///
    /// <b>Nor do things currently trying to kill you.</b> A modal panel frees the mouse and suspends the
    /// player controller (<see cref="Core.UiState"/>) but does <em>not</em> pause the world — so talking
    /// mid-fight strands the player unable to move, block or dodge while the fight continues. That is a
    /// death with no counterplay, which DESIGN §1.3 forbids outright.
    ///
    /// The guard lives here rather than in any one conversation because every interaction routes through
    /// this method: one check covers the whole roster, now and for every creature added later.
    /// </summary>
    private bool CanTalk()
    {
        if (Entity!.GetComponent<Stats.StatsComponent>() is { IsAlive: false })
        {
            return false;
        }

        return Entity.GetComponent<Enemies.EnemyAIComponent>() is not { IsHostileToPlayer: true };
    }
}
