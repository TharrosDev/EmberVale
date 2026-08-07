using Embervale.Core.Events;
using Embervale.Entities;
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
/// <see cref="MeleeWeaponComponent"/> and <see cref="CombatComponent"/>.
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
    }

    protected override void OnTeardown()
    {
        EventBus.Instance?.Unsubscribe<AttackPerformedEvent>(OnAttack);
        EventBus.Instance?.Unsubscribe<AttackInterruptedEvent>(OnInterrupted);
    }

    private void OnAttack(AttackPerformedEvent e)
    {
        if (ReferenceEquals(e.Attacker, Entity))
        {
            _ring?.Arm(e.WindupSeconds, RingRadius, RingColor);
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
