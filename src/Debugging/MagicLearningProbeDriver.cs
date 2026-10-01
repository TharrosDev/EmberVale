using Embervale.Combat;
using Embervale.Core;
using Embervale.Core.Events;
using Embervale.Entities;
using Embervale.Magic;
using Embervale.UI;
using Embervale.World;
using Godot;

namespace Embervale.Debugging;

/// <summary>
/// The C# half of <c>tools/magic_learning_probe.gd</c>. A GDScript probe cannot call a static class, pass
/// an <see cref="IEntity"/>, or subscribe to the generic event bus, so this lets it drive the real learning
/// route, the real tome and the real mastery component, and counts the events they publish. It contains no
/// rules of its own.
/// </summary>
public partial class MagicLearningProbeDriver : RefCounted
{
    public int Learned { get; private set; }

    public string LastLearnedId { get; private set; } = string.Empty;

    public string LastRoute { get; private set; } = string.Empty;

    public int Refused { get; private set; }

    public int LastRefusal { get; private set; } = -1;

    public int RankedUp { get; private set; }

    public int LastRank { get; private set; }

    /// <summary>Subscribes to the events this probe counts. Call once, after the tree exists.</summary>
    public void Listen()
    {
        EventBus bus = EventBus.Instance;
        bus.Subscribe<SpellLearnedEvent>(e =>
        {
            Learned++;
            LastLearnedId = e.SpellId;
            LastRoute = e.Route;
        });
        bus.Subscribe<SpellLearnRefusedEvent>(e =>
        {
            Refused++;
            LastRefusal = (int)e.Reason;
        });
        bus.Subscribe<SchoolRankedUpEvent>(e =>
        {
            RankedUp++;
            LastRank = e.Rank;
        });
    }

    /// <summary>Registers the input actions the panels poll and loads the string catalogue (the bootstrap
    /// does both in a real run).</summary>
    public void EnsureInput()
    {
        GameInput.EnsureActions();
        Localization.Loc.Initialize();
    }

    /// <summary>Enters the Playing state so the HUD reads as a live session.</summary>
    public void Play() => GameManager.Instance.ChangeState(GameState.Playing);

    /// <summary>Points the HUD at a caster (the player parameter is an interface GDScript cannot pass).</summary>
    public void HudFor(GameHud hud, Node body) => hud.SetPlayer(body as IEntity);

    /// <summary>How many spells the caster holds, and whether it holds one (the list is an interface).</summary>
    public int SpellCount(SpellcastingComponent casting) => casting.Spells.Count;

    public bool Knows(SpellcastingComponent casting, string spellId) =>
        SpellDatabase.Get(spellId) is { } spell && casting.IsKnown(spell);

    /// <summary>Applies a status to a body (the source parameter is an interface).</summary>
    public void ApplyStatus(StatusEffectsComponent effects, StatusEffectResource definition, Node body) =>
        effects.Apply(definition, body as IEntity);

    /// <summary>Teaches a spell through the one learning route. Returns the <see cref="LearnOutcome"/> ordinal.</summary>
    public int Learn(Node body, string spellId, string route) =>
        body is IEntity entity ? (int)SpellLearning.TryLearn(entity, spellId, route) : -1;

    /// <summary>Interacts with a tome as <paramref name="body"/> would.</summary>
    public bool ReadTome(SpellTomeComponent tome, Node body) => body is IEntity entity && tome.Interact(entity);

    /// <summary>Publishes a cast the way the caster does, so mastery banks it.</summary>
    public void Cast(Node body, string spellId)
    {
        if (body is IEntity entity)
        {
            EventBus.Instance.Publish(new SpellCastEvent(entity, spellId));
        }
    }

    /// <summary>The id a spell name resolves to (retired ids resolve to their replacement).</summary>
    public string Resolve(string spellId) => SpellDatabase.Get(spellId)?.Id ?? string.Empty;

    /// <summary>Sets the live Weave and reports what it does to an ordinary and a corrupted cast.</summary>
    public Godot.Collections.Dictionary WeaveAt(float potency)
    {
        Weave.Set(potency);
        return new Godot.Collections.Dictionary
        {
            ["potency"] = Weave.Potency,
            ["band"] = (int)Weave.Band,
            ["power"] = Weave.PowerMultiplier(false),
            ["cost"] = Weave.CostMultiplier(false),
            ["corrupt_power"] = Weave.PowerMultiplier(true),
            ["corrupt_cost"] = Weave.CostMultiplier(true),
        };
    }

    public void ResetWeave() => Weave.Reset();

    /// <summary>The special-rule line keys a spell earns from its contract fields.</summary>
    public string[] RuleKeys(string spellId)
    {
        if (SpellDatabase.Get(spellId) is not { } spell)
        {
            return System.Array.Empty<string>();
        }

        var keys = new System.Collections.Generic.List<string>();
        foreach (SpellRuleLine line in SpellBookRules.Rules(SpellBookRules.FactsOf(spell)))
        {
            keys.Add(line.Key);
        }

        return keys.ToArray();
    }

    /// <summary>Damage type ordinal for a school name (Fire = 1 ... Necrotic = 6).</summary>
    public int School(string name) => (int)System.Enum.Parse<DamageType>(name, true);
}
