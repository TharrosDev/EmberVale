using Embervale.Core.Diagnostics;
using Embervale.Entities;
using Embervale.Interaction;
using Embervale.Localization;
using Godot;

namespace Embervale.Quests;

/// <summary>
/// An interactable that offers a quest. Interacting (the player's <c>E</c> raycast)
/// starts the quest on the instigator's <see cref="QuestLogComponent"/> when it is
/// available; the <see cref="Prompt"/> reflects whether the quest is on offer, already
/// in progress, completed, or gated behind a prerequisite.
/// </summary>
[GlobalClass]
public partial class QuestGiverComponent : InteractableComponent
{
    /// <summary>Quest offered, resolved through the <see cref="QuestDatabase"/>.</summary>
    [Export] public string QuestId { get; set; } = string.Empty;

    private QuestResource? Quest => QuestDatabase.Get(QuestId);

    public override string Prompt
    {
        get
        {
            QuestResource? quest = Quest;
            if (quest == null)
            {
                return "Talk";
            }

            return $"Accept: {Loc.T(quest.Title)}";
        }
    }

    public override void Interact(IEntity instigator)
    {
        QuestResource? quest = Quest;
        if (quest == null)
        {
            return;
        }

        QuestLogComponent? log = instigator.GetComponent<QuestLogComponent>();
        if (log == null)
        {
            return;
        }

        if (log.IsCompleted(quest.Id))
        {
            Log.Info($"{quest.Title}: already completed.");
            return;
        }

        if (log.IsActive(quest.Id))
        {
            Log.Info($"{quest.Title}: already in your journal.");
            return;
        }

        if (!log.StartQuest(quest))
        {
            Log.Info($"{quest.Title}: not available yet.");
        }
    }
}
