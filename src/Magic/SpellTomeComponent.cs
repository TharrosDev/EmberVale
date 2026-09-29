using Embervale.Core;
using Embervale.Core.Diagnostics;
using Embervale.Core.Events;
using Embervale.Core.Services;
using Embervale.Entities;
using Embervale.Interaction;
using Embervale.Localization;
using Embervale.Player;
using Godot;

namespace Embervale.Magic;

/// <summary>
/// A recovered-spellcraft tome: the in-world vehicle for the fading Weave's rule that spells are
/// <em>found</em>, not vendored. Interacting teaches its <see cref="SpellId"/> through
/// <see cref="SpellLearning.TryLearn"/>, the one route every taught spell shares, so it raises
/// <see cref="SpellLearnedEvent"/> once and honours the corruption gate.
///
/// <para>A corrupted tome (its spell gated above Untainted) is an explicit choice, never a silent pickup:
/// a reader who has the tier is warned on the first interaction and takes the words on the second, within
/// <see cref="ConfirmWindowSeconds"/>. A reader who lacks the tier is told which tier and where they stand.
/// A tome that names a retired spell id teaches its replacement.</para>
/// </summary>
[GlobalClass]
public partial class SpellTomeComponent : InteractableComponent
{
    /// <summary>How long the corrupted-words warning stays armed for the second interaction.</summary>
    public const double ConfirmWindowSeconds = 8.0;

    /// <summary>The spell this tome restores (a <c>spell.*</c> id resolved via <see cref="SpellDatabase"/>;
    /// a retired id resolves to its replacement).</summary>
    [Export] public string SpellId { get; set; } = string.Empty;

    /// <summary>Optional story flag the reader must hold before the tome opens (Phase 35F) — a dragon's
    /// hoard can then sit in its lair from the start and still only yield once the dragon is dead
    /// (see <see cref="Enemies.LairSpawnComponent.DefeatFlagId"/>). Empty = always readable.</summary>
    [Export] public string RequiredFlagId { get; set; } = string.Empty;

    // The corrupted-words confirmation: who was warned and until when (ms since engine start).
    private IEntity? _warned;
    private ulong _warnedUntilMsec;

    private SpellResource? Spell => SpellDatabase.Get(SpellId);

    public override string Prompt
    {
        get
        {
            SpellResource? spell = Spell;
            if (spell == null)
            {
                return Loc.T("magic.learn.tome_prompt_blank");
            }

            string name = SpellText.Name(spell);
            if (ServiceLocator.Instance != null && ServiceLocator.Instance.TryGet(out PlayerCharacter player)
                && IsInstanceValid(player) && player.GetComponent<SpellcastingComponent>() is { } casting)
            {
                if (casting.IsKnown(spell))
                {
                    return Loc.TF("magic.learn.tome_prompt_known", name);
                }

                if (!casting.MeetsCorruption(spell))
                {
                    return Loc.TF("magic.learn.tome_prompt_locked", name);
                }
            }

            return Loc.TF(
                SpellLearnRules.IsCorrupted((int)spell.MinCorruptionTier)
                    ? "magic.learn.tome_prompt_corrupt"
                    : "magic.learn.tome_prompt",
                name);
        }
    }

    public override bool Interact(IEntity instigator)
    {
        SpellResource? spell = Spell;
        if (spell == null || instigator.GetComponent<SpellcastingComponent>() is not { } casting)
        {
            return false;
        }

        if (RequiredFlagId.Length > 0 && instigator.GetComponent<Dialogue.StoryFlagsComponent>()?.Has(RequiredFlagId) != true)
        {
            EventBus.Instance?.Publish(new SpellLearnRefusedEvent(
                instigator, spell.Id, SpellLearnRefusal.Sealed, 0, 0));
            return false;
        }

        // The corrupted-words choice: a reader who could take the spell is asked once, and only a second
        // interaction inside the window commits. Locked and known readers skip it and are told why.
        if (casting.CanLearn(spell) && SpellLearnRules.IsCorrupted((int)spell.MinCorruptionTier)
            && !IsConfirmed(instigator))
        {
            _warned = instigator;
            _warnedUntilMsec = Time.GetTicksMsec() + (ulong)(ConfirmWindowSeconds * 1000.0);
            EventBus.Instance?.Publish(new SpellLearnRefusedEvent(
                instigator, spell.Id, SpellLearnRefusal.ConfirmCorrupted,
                (int)SpellLearning.TierOf(instigator), (int)spell.MinCorruptionTier));
            return false;
        }

        // TryLearn re-checks known and the corruption gate, so a double press in one frame teaches once.
        LearnOutcome outcome = SpellLearning.TryLearn(instigator, spell.Id, LearnRoutes.Tome);
        if (outcome != LearnOutcome.Learned)
        {
            return false;
        }

        _warned = null;
        Log.Info($"You recover lost spellcraft: {spell.DisplayName}.");
        return true;
    }

    private bool IsConfirmed(IEntity instigator) =>
        ReferenceEquals(_warned, instigator) && Time.GetTicksMsec() <= _warnedUntilMsec;
}
