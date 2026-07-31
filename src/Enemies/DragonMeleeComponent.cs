using Embervale.Combat;
using Embervale.Core.Services;
using Embervale.Entities;
using Embervale.Player;
using Godot;

namespace Embervale.Enemies;

/// <summary>
/// Gives a large body three melee arcs instead of one (Phase 35A): jaws in front, a wing sweep to
/// either flank, the tail behind. Which one is armed is chosen every frame from the target's bearing
/// (<see cref="DragonMelee"/>) by swapping the <see cref="MeleeWeaponComponent"/>'s hitbox — one
/// weapon component, three volumes.
///
/// A component rather than three weapons because <see cref="EnemyAIComponent"/> drives exactly one
/// <see cref="MeleeWeaponComponent"/> per actor (<c>Entity.GetComponent&lt;T&gt;</c> returns one), so
/// three weapons would mean teaching the AI to pick between them. Swapping the volume underneath the
/// existing swing needs nothing from the AI at all.
///
/// This only bites with a slow <see cref="AIProfileResource.TurnSpeedDegrees"/>: the AI faces its
/// target before every swing, so a body that snaps round instantly is always looking at you and would
/// only ever bite. The turn rate is what makes flanking it a tactic and these arcs reachable.
/// </summary>
[GlobalClass]
public partial class DragonMeleeComponent : EntityComponent
{
    public const string BiteNode = "BiteHitbox";
    public const string WingNode = "WingHitbox";
    public const string TailNode = "TailHitbox";

    private MeleeWeaponComponent? _weapon;
    private EnemyAIComponent? _ai;
    private Hitbox? _bite;
    private Hitbox? _wing;
    private Hitbox? _tail;

    /// <summary>The arc currently armed. The choice itself is <see cref="DragonMelee"/>, which is
    /// pure and unit-tested; this is the live result of it.</summary>
    public DragonAttack Armed { get; private set; } = DragonAttack.Bite;

    /// <summary>
    /// Builds the three arcs as children of <paramref name="enemy"/> and returns the component that
    /// swaps between them. Called from the factory, <em>before</em> the actor enters the tree —
    /// Godot refuses an <c>AddChild</c> from inside a node's own <c>_Ready</c>, so the geometry
    /// cannot be built in <see cref="OnInitialize"/>.
    /// </summary>
    public static DragonMeleeComponent BuildArcs(EnemyEntity enemy, float height, float radius)
    {
        // Reach multipliers on the body's own radius: the jaws lead, the tail sweeps furthest back,
        // and the wing is the widest but shallowest — it covers both flanks at once, which is what a
        // sweep is.
        enemy.AddChild(BuildArc(BiteNode, new Vector3(0f, height * 0.7f, -radius * 1.1f),
            new Vector3(radius * 1.2f, height * 0.5f, radius * 1.4f)));
        enemy.AddChild(BuildArc(WingNode, new Vector3(0f, height * 0.5f, 0f),
            new Vector3(radius * 3f, height * 0.4f, radius * 1.6f)));
        enemy.AddChild(BuildArc(TailNode, new Vector3(0f, height * 0.3f, radius * 1.3f),
            new Vector3(radius * 1.6f, height * 0.4f, radius * 2f)));

        return new DragonMeleeComponent { Name = "DirectionalMelee" };
    }

    protected override void OnInitialize()
    {
        _weapon = Entity!.GetComponent<MeleeWeaponComponent>();
        _ai = Entity.GetComponent<EnemyAIComponent>();
        _bite = Entity.Body.GetNodeOrNull<Hitbox>(BiteNode);
        _wing = Entity.Body.GetNodeOrNull<Hitbox>(WingNode);
        _tail = Entity.Body.GetNodeOrNull<Hitbox>(TailNode);
        Arm(DragonAttack.Bite);
    }

    public override void _PhysicsProcess(double delta)
    {
        // Never mid-swing: an attack lands with the arc it was wound up with, so dodging behind a
        // dragon that has already committed to a bite does not get you hit by its tail instead.
        if (_weapon == null || _ai == null || _weapon.IsCommitted)
        {
            return;
        }

        if (ServiceLocator.Instance == null || !ServiceLocator.Instance.TryGet(out PlayerCharacter player))
        {
            return;
        }

        Arm(DragonMelee.Choose(_ai.BearingTo(player.GlobalPosition)));
    }

    private void Arm(DragonAttack attack)
    {
        Armed = attack;
        Hitbox? arc = attack switch
        {
            DragonAttack.Wing => _wing,
            DragonAttack.Tail => _tail,
            _ => _bite,
        };

        // A missing arc leaves the factory's default hitbox in place rather than disarming the
        // dragon — a greybox that still fights beats a decorative one.
        if (_weapon != null && arc != null)
        {
            _weapon.Hitbox = arc;
        }
    }

    private static Hitbox BuildArc(string name, Vector3 offset, Vector3 size)
    {
        var hitbox = new Hitbox { Name = name, Position = offset };
        hitbox.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
        return hitbox;
    }
}
