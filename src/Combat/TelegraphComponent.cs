using Embervale.Combat.Actions;
using Embervale.Core.Events;
using Embervale.Core.Services;
using Embervale.Entities;
using Embervale.Magic;
using Embervale.Player;
using Godot;

namespace Embervale.Combat;

/// <summary>
/// Shows a wind-up warning under its owner (Phase 36C). Subscribes to
/// <see cref="AttackPerformedEvent"/> for its own entity, arms a <see cref="TelegraphRing"/> for
/// exactly the wind-up the event reports, and clears it on <see cref="AttackInterruptedEvent"/> —
/// so a punished swing visibly dies rather than quietly not happening.
///
/// Deliberately <b>actor-agnostic</b>: nothing here knows what a boss is. `EnemyArchetypeFactory`
/// currently attaches it to boss archetypes only, but any factory can, which is what makes this the
/// reusable half of the phase rather than another boss-shaped special case. A `BossController`, when
/// present, pushes its current phase's colour in through <see cref="RingColor"/>; with no controller
/// the exported default stands.
///
/// Purely cosmetic. The wind-up window, the interrupt and the damage all live in
/// <see cref="CharacterActionComponent"/> and <see cref="CombatComponent"/>.
/// </summary>
[GlobalClass]
public partial class TelegraphComponent : EntityComponent
{
    /// <summary>Ring colour when nothing overrides it (a boss phase usually does).</summary>
    [Export] public Color RingColor { get; set; } = new(1.0f, 0.25f, 0.05f);

    /// <summary>Ring radius in metres at full extension. Sized to the creature by its factory.</summary>
    [Export] public float RingRadius { get; set; } = 2.2f;

    private TelegraphRing? _ring;

    protected override void OnInitialize()
    {
        _ring = new TelegraphRing { Name = "TelegraphRing" };

        // ⚠️ Deferred, and it must be: the body is still setting up its children during this
        // component's _Ready, so a direct AddChild is refused ("parent node is busy setting up
        // children"). Godot logs that and carries on rather than throwing, so the ring silently stayed
        // out of the tree — which meant its _Ready never ran, every Arm/Clear dereferenced a null mesh,
        // and the unparented node leaked as an orphan for the lifetime of the run. One missed
        // CallDeferred produced all three symptoms; WeaponTrailComponent, LairSpawnComponent and
        // TrophyStandComponent all defer for exactly this reason.
        Entity!.Body.CallDeferred(Node.MethodName.AddChild, _ring);

        EventBus.Instance?.Subscribe<AttackPerformedEvent>(OnAttack);
        EventBus.Instance?.Subscribe<AttackInterruptedEvent>(OnInterrupted);
        EventBus.Instance?.Subscribe<CastWindupStartedEvent>(OnCastWindup);
    }

    protected override void OnTeardown()
    {
        EventBus.Instance?.Unsubscribe<CastWindupStartedEvent>(OnCastWindup);
        EventBus.Instance?.Unsubscribe<AttackPerformedEvent>(OnAttack);
        EventBus.Instance?.Unsubscribe<AttackInterruptedEvent>(OnInterrupted);
    }

    private void OnAttack(AttackPerformedEvent e)
    {
        if (!ReferenceEquals(e.Attacker, Entity))
        {
            return;
        }

        // The class comes from the action that just began (Current is set before this event is
        // published); with no action to read it is an ordinary ring. The duration is still the real
        // wind-up, and the parry cue lights off the player's own parry window.
        TelegraphClass cls = TelegraphClass.Standard;
        float sweep = TelegraphClasses.DefaultSweepDegrees;
        if (Entity!.GetComponent<CharacterActionComponent>()?.Current is { } action)
        {
            TelegraphSource source = TelegraphClasses.FromAction(action);
            cls = TelegraphClasses.Classify(source);
            sweep = TelegraphClasses.SweepDegrees(source);
        }

        float parryWindow = ServiceLocator.Instance is { } locator && locator.TryGet(out PlayerCharacter player) &&
                            player.GetComponent<CombatComponent>() is { } combat
            ? combat.ParryWindow
            : 0.2f;
        _ring?.Arm(e.WindupSeconds, RingRadius, RingColor, cls, parryWindow, sweep);
    }

    /// <summary>A wind-up that turns out to be a spell: the warning already armed for it takes the
    /// spell's school as its body colour (a fire breath's fan is ember, an ash breath's violet-grey,
    /// an arcane shout's blue). The caster names its spell just after the action starts, which is why
    /// this is a second event and not part of <see cref="OnAttack"/>. Look only: shape, size and
    /// timing were set by the attack event.</summary>
    private void OnCastWindup(CastWindupStartedEvent e)
    {
        if (ReferenceEquals(e.Caster, Entity) &&
            Entity!.GetComponent<SpellcastingComponent>()?.PendingSpell is { } spell)
        {
            _ring?.Tint(SpellSchools.Color(spell.School));
        }
    }

    private void OnInterrupted(AttackInterruptedEvent e)
    {
        if (ReferenceEquals(e.Attacker, Entity))
        {
            _ring?.Clear();
        }
    }
}
