using Embervale.Combat.Actions;
using Embervale.Combat;
using Embervale.Core.Services;
using Embervale.Entities;
using Embervale.Player;
using Godot;

namespace Embervale.Enemies;

/// <summary>
/// Gives a large body three melee arcs instead of one (Phase 35A): jaws in front, a wing sweep to
/// either flank, the tail behind. Which one is armed is chosen every frame from the target's bearing
/// (<see cref="DragonMelee"/>) by naming which of its three authored actions to run — one
/// weapon component, three volumes.
///
/// A component rather than three weapons because <see cref="EnemyAIComponent"/> drives exactly one
/// <see cref="CharacterActionComponent"/> per actor (<c>Entity.GetComponent&lt;T&gt;</c> returns one), so
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

    private CharacterActionComponent? _weapon;
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
        _weapon = Entity!.GetComponent<CharacterActionComponent>();
        _ai = Entity.GetComponent<EnemyAIComponent>();
        _bite = Entity.Body.GetNodeOrNull<Hitbox>(BiteNode);
        _wing = Entity.Body.GetNodeOrNull<Hitbox>(WingNode);
        _tail = Entity.Body.GetNodeOrNull<Hitbox>(TailNode);

        // The three volumes are registered by name ONCE. The authored actions name which one they
        // open, so nothing has to reassign a hitbox per frame any more.
        Register("BiteArc", _bite);
        Register("WingArc", _wing);
        Register("TailArc", _tail);
        Armed = DragonAttack.Bite;
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

        Armed = DragonMelee.Choose(_ai.BearingTo(player.GlobalPosition));
    }

    /// <summary>The action id for the blow currently armed by bearing. The AI asks for this rather
    /// than for "an attack", which is how a directional creature keeps its choice while the action
    /// system keeps the timing.</summary>
    public string ArmedActionId => Armed switch
    {
        DragonAttack.Wing => "dragon.wing",
        DragonAttack.Tail => "dragon.tail",
        _ => "dragon.bite",
    };

    private void Register(string name, Hitbox? arc)
    {
        if (_weapon != null && arc != null)
        {
            _weapon.NamedHitboxes[name] = arc;
        }
    }

    private static Hitbox BuildArc(string name, Vector3 offset, Vector3 size)
    {
        var hitbox = new Hitbox { Name = name, Position = offset };
        hitbox.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
        return hitbox;
    }
}
