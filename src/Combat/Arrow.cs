using System;
using Embervale.Core.Events;
using Embervale.Entities;
using Godot;

namespace Embervale.Combat;

/// <summary>
/// A flying arrow: the ranged analogue of a melee <see cref="Hitbox"/>, and the sibling of
/// <c>SpellProjectile</c>.
///
/// <para>⚠️ <b>It sub-steps its flight and asks the physics space, not a monitored overlap list.</b> An
/// arrow at 42 m/s covers 0.7 m in a physics frame — comfortably more than its own collision radius —
/// so a projectile that simply moves and then tests overlaps passes clean through a body between
/// frames. That is tunnelling, and it is invisible in every log: the shot just misses, occasionally,
/// and the player calls the hit detection unreliable. Each frame is therefore walked in steps no longer
/// than the arrow's own radius, and every step queries the space directly.</para>
///
/// <para>⚠️ <b>The old version never actually used its sub-steps.</b> It moved an <c>Area3D</c> a few
/// times in one frame and read <c>GetOverlappingAreas()</c>, but an Area's overlap list only updates
/// on the physics step, so only the frame's last position was ever tested. A thin hurtbox slipped
/// through at speed and nothing in the suite could see it. A direct shape query per step is immediate.</para>
///
/// <para>Flight is ballistic (gravity) and stops on solid world geometry: it sticks in the wall for a
/// moment and then goes, so a missed shot reads as a miss instead of an arrow that flew through the
/// building. It never collides with actors' bodies, only their hurtboxes.</para>
///
/// <para>Pooled like the spell bolt: the visual is built once, each shot reconfigures through
/// <see cref="Launch"/>, and resolution calls <see cref="Released"/> so rapid fire does not churn the
/// scene tree.</para>
/// </summary>
public partial class Arrow : Area3D
{
    /// <summary>Collision radius, and the longest distance the arrow may travel between two
    /// collision tests.</summary>
    private const float Radius = 0.12f;

    /// <summary>Ceiling on sub-steps per frame, so an absurd speed or a hitched frame cannot spin
    /// here. At the cap the arrow is moving faster than the sweep can honestly resolve.</summary>
    private const int MaxSubSteps = 24;

    /// <summary>Seconds an arrow stays in the wall it hit before it is reclaimed.</summary>
    private const float StickSeconds = 1.2f;

    /// <summary>How far the arrow buries its head in the surface, in metres.</summary>
    private const float Embed = 0.1f;

    /// <summary>Most hurtboxes one step can be overlapping.</summary>
    private const int MaxOverlaps = 8;

    private DamagePacket _packet;
    private IEntity? _shooter;
    private int _shooterTeam;
    private Node? _shooterBody;
    private Vector3 _velocity;
    private float _gravity;
    private float _rangeLeft;
    private float _flown;
    private float _stuckLeft;
    private bool _resolved = true;   // inert until Launch arms it

    private MeshInstance3D _visual = null!;
    private Node3D? _model;
    private readonly WorldRay _world = new();
    private readonly PhysicsShapeQueryParameters3D _sweep = new()
    {
        Shape = new SphereShape3D { Radius = Radius },
        CollisionMask = CombatLayers.Hurtbox,
        CollideWithAreas = true,
        CollideWithBodies = false,
    };

    /// <summary>Reclaim callback (the pool's <c>Return</c>). When null, the arrow frees itself.</summary>
    public Action<Arrow>? Released { get; set; }

    public override void _Ready()
    {
        // The arrow is a moving point to the physics space, not a body in it: it queries and is never
        // queried. Monitorable off keeps it out of every broadphase pair.
        CollisionLayer = 0;
        CollisionMask = 0;
        Monitoring = false;
        Monitorable = false;

        _visual = new MeshInstance3D
        {
            Mesh = new CylinderMesh
            {
                TopRadius = 0.012f,
                BottomRadius = 0.012f,
                Height = 0.7f,
                RadialSegments = 5,
            },
            // The mesh's long axis is Y; the arrow flies along -Z, so it is laid down once here
            // rather than rotated on every launch.
            RotationDegrees = new Vector3(90f, 0f, 0f),
        };
        AddChild(_visual);
        SetPhysicsProcess(false);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _world.Dispose();
            _sweep.Dispose();
        }

        base.Dispose(disposing);
    }

    /// <summary>Arms the arrow and sends it flying. Reconfigures a pooled instance in place.
    /// <paramref name="gravity"/> of 0 flies it straight, as every arrow did before drop existed.</summary>
    public void Launch(
        DamagePacket packet, IEntity? shooter, int shooterTeam, Vector3 direction,
        float speed, float range, string modelPath, float gravity = 0f)
    {
        _packet = packet;
        _shooter = shooter;
        _shooterTeam = shooterTeam;
        _shooterBody = shooter?.Body;
        _velocity = direction.Normalized() * speed;
        _gravity = gravity;
        _rangeLeft = range;
        _flown = 0f;
        _stuckLeft = 0f;
        _resolved = false;

        _world.Ignore(_shooterBody is CollisionObject3D collider ? collider.GetRid() : default);

        SwapModel(modelPath);
        LookAlongVelocity();
        SetPhysicsProcess(true);
    }

    private void SwapModel(string modelPath)
    {
        if (modelPath.Length == 0)
        {
            _visual.Visible = true;
            return;
        }

        if (_model == null && GD.Load<PackedScene>(modelPath)?.Instantiate() is Node3D instance)
        {
            _model = instance;
            AddChild(instance);
        }

        _visual.Visible = _model == null;
    }

    private void LookAlongVelocity()
    {
        if (_velocity.LengthSquared() <= 0.0001f)
        {
            return;
        }

        Vector3 direction = _velocity.Normalized();
        Vector3 up = Mathf.Abs(direction.Dot(Vector3.Up)) > 0.99f ? Vector3.Forward : Vector3.Up;
        LookAt(GlobalPosition + direction, up);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_resolved)
        {
            return;
        }

        if (_stuckLeft > 0f)
        {
            _stuckLeft -= (float)delta;
            if (_stuckLeft <= 0f)
            {
                Resolve();
            }

            return;
        }

        float dt = (float)delta;
        float travel = _velocity.Length() * dt;
        if (travel <= 0f)
        {
            return;
        }

        // Walk the frame in steps no longer than the arrow's own radius. This is the anti-tunnelling
        // rule and the whole reason this loop exists rather than a single translation.
        int steps = Mathf.Min(MaxSubSteps, Mathf.Max(1, Mathf.CeilToInt(travel / Radius)));
        float h = dt / steps;
        PhysicsDirectSpaceState3D space = GetWorld3D().DirectSpaceState;

        for (int i = 0; i < steps && !_resolved; i++)
        {
            _velocity.Y -= _gravity * h;
            Vector3 from = GlobalPosition;
            Vector3 to = from + (_velocity * h);
            float length = from.DistanceTo(to);

            // A wall ends the step where it stands; nothing behind it can be reached this step.
            (Vector3 Point, Vector3 Normal)? wall = _world.FirstSolid(space, from, to);
            Vector3 reach = wall?.Point ?? to;

            if (TryStrike(space, reach))
            {
                return;
            }

            if (wall is { } solid)
            {
                Stick(solid.Point, solid.Normal);
                return;
            }

            GlobalPosition = to;
            _flown += length;
            _rangeLeft -= length;
            if (_rangeLeft <= 0f)
            {
                Resolve();
                return;
            }
        }

        LookAlongVelocity();
    }

    /// <summary>True when the arrow struck a body at <paramref name="at"/> and resolved.</summary>
    private bool TryStrike(PhysicsDirectSpaceState3D space, Vector3 at)
    {
        _sweep.Transform = new Transform3D(Basis.Identity, at);

        Span<HitZoneRouting.Candidate> found = stackalloc HitZoneRouting.Candidate[MaxOverlaps];
        Span<Hurtbox> boxes = new Hurtbox[MaxOverlaps];
        int count = 0;

        foreach (Godot.Collections.Dictionary hit in space.IntersectShape(_sweep, MaxOverlaps))
        {
            if (hit["collider"].AsGodotObject() is not Hurtbox hurtbox || hurtbox.Combat == null)
            {
                continue;
            }

            // The shooter and its allies are not targets. Read live, exactly as Hitbox does, so a
            // faction change mid-flight is respected rather than baked in at launch.
            if (ReferenceEquals(hurtbox.Combat.Entity?.Body, _shooterBody) ||
                hurtbox.Combat.Team == _shooterTeam)
            {
                continue;
            }

            boxes[count] = hurtbox;
            found[count] = new HitZoneRouting.Candidate(
                (int)hurtbox.Combat.GetInstanceId(),
                hurtbox.DamageMultiplier,
                hurtbox.GlobalPosition.DistanceSquaredTo(at));
            count++;
        }

        int index = HitZoneRouting.Pick(found[..count]);
        if (index < 0)
        {
            return false;
        }

        Strike(boxes[index], at);
        Resolve();
        return true;
    }

    private void Strike(Hurtbox hurtbox, Vector3 at)
    {
        DamagePacket packet = _packet;
        bool headshot = hurtbox.DamageMultiplier > 1f || hurtbox.ZoneId == "head";

        // A single whole-body hurtbox has no head zone of its own, so a tall one is read by height:
        // the top slice of a man-sized capsule is his head. Multi-zone bodies author theirs, and the
        // hurtbox applies that multiplier itself.
        if (!headshot && hurtbox.DamageMultiplier == 1f && hurtbox.ZoneId.Length == 0 &&
            BodyBounds(hurtbox) is { } bounds && RangedMath.IsHeadHeight(at.Y, bounds.Bottom, bounds.Top))
        {
            headshot = true;
            packet = packet with
            {
                Amount = packet.Amount * RangedMath.HeadshotDamage,
                PoiseDamage = packet.PoiseDamage * RangedMath.HeadshotPoise,
            };
        }

        if (headshot)
        {
            packet = packet with { IsCrit = true };
        }

        hurtbox.Receive(packet);

        if (hurtbox.OwnerEntity is { } target)
        {
            EventBus.Instance?.Publish(new ArrowHitEvent(_shooter, target, _packet.Charge, headshot, _flown));
        }
    }

    /// <summary>The vertical extent of a hurtbox's first shape, or null when it is not a shape whose
    /// height can be read. Enough for the capsule, sphere or box every actor here uses.</summary>
    private static (float Bottom, float Top)? BodyBounds(Hurtbox hurtbox)
    {
        foreach (Node child in hurtbox.GetChildren())
        {
            if (child is not CollisionShape3D { Shape: { } shape } holder)
            {
                continue;
            }

            float half = shape switch
            {
                CapsuleShape3D capsule => capsule.Height * 0.5f,
                SphereShape3D sphere => sphere.Radius,
                BoxShape3D box => box.Size.Y * 0.5f,
                _ => -1f,
            };

            if (half < 0f)
            {
                return null;
            }

            float centre = holder.GlobalPosition.Y;
            return (centre - half, centre + half);
        }

        return null;
    }

    /// <summary>Embeds the arrow in what it hit and leaves it there a moment.</summary>
    private void Stick(Vector3 point, Vector3 normal)
    {
        _ = normal;
        Vector3 direction = _velocity.Normalized();
        GlobalPosition = point + (direction * Embed);
        _velocity = Vector3.Zero;
        _stuckLeft = StickSeconds;
    }

    private void Resolve()
    {
        _resolved = true;
        SetPhysicsProcess(false);

        if (Released is { } release)
        {
            release(this);
        }
        else
        {
            QueueFree();
        }
    }
}
