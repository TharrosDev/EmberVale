using Embervale.Core.Events;
using Embervale.Corruption;
using Embervale.Entities;

namespace Embervale.Magic;

/// <summary>The routes a spell is learned by. The route string rides <see cref="SpellLearnedEvent"/>.</summary>
public static class LearnRoutes
{
    /// <summary>Recovered from a tome (<see cref="SpellTomeComponent"/>).</summary>
    public const string Tome = "tome";

    /// <summary>Taught by a conversation (<c>DialogueEffect.LearnSpell</c>).</summary>
    public const string Dialogue = "dialogue";

    /// <summary>Taught by a trainer service. No spell trainer is authored yet; the route exists so one
    /// funnels through <see cref="SpellLearning.TryLearn"/> like every other.</summary>
    public const string Trainer = "trainer";

    /// <summary>Bought with spell points in the spellbook.</summary>
    public const string Spellbook = "spellbook";
}

/// <summary>What a learn attempt came to.</summary>
public enum LearnOutcome
{
    Learned,
    AlreadyKnown,
    UnknownSpell,
    CorruptionLocked,
    NoCaster,
}

/// <summary>
/// The pure half of learning: given the facts, what happens. Primitives only so it is unit-testable
/// (a <c>SpellResource</c> cannot be constructed off the engine).
/// </summary>
public static class SpellLearnRules
{
    /// <summary>The outcome of trying to learn a spell.</summary>
    public static LearnOutcome Evaluate(bool hasCaster, bool spellExists, bool known, int tierNow, int tierRequired)
    {
        if (!hasCaster)
        {
            return LearnOutcome.NoCaster;
        }

        if (!spellExists)
        {
            return LearnOutcome.UnknownSpell;
        }

        if (known)
        {
            return LearnOutcome.AlreadyKnown;
        }

        return tierNow >= tierRequired ? LearnOutcome.Learned : LearnOutcome.CorruptionLocked;
    }

    /// <summary>A spell gated above Untainted is a corrupted craft: taking it is a choice, not a pickup.</summary>
    public static bool IsCorrupted(int tierRequired) => tierRequired > (int)CorruptionTier.Untainted;

    /// <summary>The route as the event carries it; anything unrecognised is reported as a dialogue
    /// teaching rather than an empty string.</summary>
    public static string NormalizeRoute(string? route) => route switch
    {
        LearnRoutes.Tome or LearnRoutes.Dialogue or LearnRoutes.Trainer or LearnRoutes.Spellbook => route,
        _ => LearnRoutes.Dialogue,
    };

    /// <summary>Whether a refusal is worth telling the player. A conversation re-offering a spell they
    /// already have stays silent; everything else that fails explains itself.</summary>
    public static bool ShouldAnnounceRefusal(LearnOutcome outcome, string route) => outcome switch
    {
        LearnOutcome.CorruptionLocked => true,
        LearnOutcome.AlreadyKnown => route == LearnRoutes.Tome,
        _ => false,
    };
}

/// <summary>
/// The one route every taught spell goes through: tome, dialogue, trainer. It resolves a retired id to its
/// replacement, applies the corruption gate, teaches through <see cref="SpellcastingComponent.Learn"/>, and
/// raises <see cref="SpellLearnedEvent"/> exactly once. A load never comes through here (it restores the
/// spell list directly), which is what keeps a reload from narrating knowledge the player already had.
/// </summary>
public static class SpellLearning
{
    /// <summary>Tries to teach <paramref name="spellId"/> to <paramref name="learner"/>.</summary>
    public static LearnOutcome TryLearn(IEntity learner, string spellId, string route)
    {
        route = SpellLearnRules.NormalizeRoute(route);
        SpellcastingComponent? casting = learner.GetComponent<SpellcastingComponent>();
        SpellResource? spell = SpellDatabase.Get(spellId); // resolves a retired id to its replacement
        int tierNow = (int)TierOf(learner);
        int tierRequired = spell == null ? 0 : (int)spell.MinCorruptionTier;

        LearnOutcome outcome = SpellLearnRules.Evaluate(
            casting != null, spell != null, casting != null && spell != null && casting.IsKnown(spell),
            tierNow, tierRequired);

        if (outcome == LearnOutcome.Learned && casting != null && spell != null)
        {
            // Always the resolved id: the caster stores what it is given, and an alias stored beside its
            // replacement is a duplicate spell.
            casting.Learn(spell.Id);
            if (!casting.IsKnown(spell))
            {
                return LearnOutcome.NoCaster;
            }

            EventBus.Instance?.Publish(new SpellsChangedEvent(learner));
            EventBus.Instance?.Publish(new SpellLearnedEvent(learner, spell.Id, route));
            return LearnOutcome.Learned;
        }

        if (spell != null && SpellLearnRules.ShouldAnnounceRefusal(outcome, route))
        {
            SpellLearnRefusal reason = outcome == LearnOutcome.CorruptionLocked
                ? SpellLearnRefusal.CorruptionTooLow
                : SpellLearnRefusal.AlreadyKnown;
            EventBus.Instance?.Publish(new SpellLearnRefusedEvent(learner, spell.Id, reason, tierNow, tierRequired));
        }

        return outcome;
    }

    /// <summary>The learner's corruption tier (Untainted with no corruption component).</summary>
    public static CorruptionTier TierOf(IEntity learner) =>
        learner.GetComponent<CorruptionComponent>()?.Tier ?? CorruptionTier.Untainted;
}
