using Embervale.Core.Diagnostics;
using Embervale.Core.Events;
using Embervale.Entities;
using Embervale.Interaction;
using Godot;

namespace Embervale.Magic;

/// <summary>
/// A recovered-spellcraft tome (Phase 29.5E): the in-world vehicle for the fading Weave's rule that
/// spells are <em>found</em>, not vendored. Interacting teaches its <see cref="SpellId"/> to the
/// instigator through the same corruption-gated <see cref="SpellcastingComponent.Learn"/> path the
/// 23H trainer uses — so a corrupted tome (a spell gated above Untainted) only yields to the Marked.
///
/// A trainer NPC is the same seam with a dialogue effect instead of an interactable; this is the
/// pickup-style version. ponytail: one tome teaches one spell; a multi-spell archive is just several.
/// </summary>
[GlobalClass]
public partial class SpellTomeComponent : InteractableComponent
{
    /// <summary>The spell this tome restores (a <c>spell.*</c> id resolved via <see cref="SpellDatabase"/>).</summary>
    [Export] public string SpellId { get; set; } = string.Empty;

    private SpellResource? Spell => SpellDatabase.Get(SpellId);

    public override string Prompt
    {
        get
        {
            SpellResource? spell = Spell;
            return spell == null ? "Study the tome" : $"Recover {spell.DisplayName}";
        }
    }

    public override void Interact(IEntity instigator)
    {
        SpellResource? spell = Spell;
        if (spell == null || instigator.GetComponent<SpellcastingComponent>() is not { } casting)
        {
            return;
        }

        if (casting.IsKnown(spell))
        {
            Log.Info($"The tome of {spell.DisplayName} holds nothing new — you already know it.");
            return;
        }

        // The 23H gate: a corrupted spell's words writhe out of reach until the reader is Marked enough.
        if (!casting.MeetsCorruption(spell))
        {
            Log.Info($"The tome of {spell.DisplayName} resists you — its power lies behind corruption you have not yet taken.");
            return;
        }

        casting.Learn(spell.Id);
        EventBus.Instance?.Publish(new SpellsChangedEvent(instigator));
        Log.Info($"You recover lost spellcraft: {spell.DisplayName}.");
    }
}
