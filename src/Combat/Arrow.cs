using System;
using Embervale.Entities;
using Godot;

namespace Embervale.Combat;

/// <summary>
/// A flying arrow: the ranged analogue of a melee <see cref="Hitbox"/>, and the sibling of
/// <c>SpellProjectile</c>.
///
/// <para>⚠️ <b>It sub-steps its flight, and that is the only interesting thing about it.</b> An arrow
/// at 38 m/s covers 0.63 m in a physics frame — comfortably more than its own collision radius — so a
/// projectile that simply moves and then tests overlaps passes clean through a body between frames.
/// That is tunnelling, and it is invisible in every log: the shot just misses, occasionally, and the
/// player calls the hit detection unreliable. Each frame is therefore walked in steps no longer than
/// the arrow's own radius, exactly as <c>SpellProjectile</c> does, with the same cap on how many
/// steps one frame may take.</para>
///
/// <para>Pooled like the spell bolt: the visual and collision children are built once, each shot
/// reconfigures through <see cref="Launch"/>, and resolution calls <see cref="Released"/> so rapid
/// fire does not churn the scene tree.</para>
/// </summary>
public partial class Arrow : Area3D
{
    /// <summary>Collision radius, and the longest distance the arrow may travel between two
    /// collision tests.</summary>
    private const float Radius = 0.12f;

    /// <summary>Ceiling on sub-steps per frame, so an absurd speed or a hitched frame cannot spin
    /// here. At the cap the arrow is moving faster than the sweep can honestly resolve.</summary>
    private const int MaxSubSteps = 24;

    private DamagePacket _packet;
    private IEntity? _shooter;
    private int _shooterTeam;
    private Node? _shooterBody;
    private Vector3 _direction;
    private float _speed;
    private float _rangeLeft;
    private bool _resolved = true;   // inert until Launch arms it

    private MeshInstance3D _visual = null!;
    private CollisionShape3D _shape = null!;
    private Node3D? _model;

    /// <summary>Reclaim callback (the pool's <c>Return</c>). When null, the arrow frees itself.</summary>
    public Action<Arrow>? Released { get; set; }

    public override void _Ready()
    {
        CollisionLayer = CombatLayers.Hitbox;
        CollisionMask = CombatLayers.Hurtbox;
        Monitoring = false;

        _shape = new CollisionShape3D { Shape = new SphereShape3D { Radius = Radius } };
        AddChild(_shape);

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

    /// <summary>Arms the arrow and sends it flying. Reconfigures a pooled instance in place.</summary>
    public void Launch(
        DamagePacket packet, IEntity? shooter, int shooterTeam, Vector3 direction,
        float speed, float range, string modelPath)
    {
        _packet = packet;
        _shooter = shooter;
        _shooterTeam = shooterTeam;
        _shooterBody = shooter?.Body;
        _direction = direction.Normalized();
        _speed = speed;
        _rangeLeft = range;
        _resolved = false;

        SwapModel(modelPath);
        LookAtDirection();
        Monitoring = true;
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

    private void LookAtDirection()
    {
        if (_direction.LengthSquared() <= 0.0001f)
        {
            return;
        }

        Vector3 up = Mathf.Abs(_direction.Dot(Vector3.Up)) > 0.99f ? Vector3.Forward : Vector3.Up;
        LookAt(GlobalPosition + _direction, up);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_resolved)
        {
            return;
        }

        float travel = _speed * (float)delta;
        if (travel <= 0f)
        {
            return;
        }

        // Walk the frame in steps no longer than the arrow's own radius. This is the anti-tunnelling
        // rule and the whole reason this loop exists rather than a single translation.
        int steps = Mathf.Min(MaxSubSteps, Mathf.Max(1, Mathf.CeilToInt(travel / Radius)));
        float step = travel / steps;

        for (int i = 0; i < steps && !_resolved; i++)
        {
            GlobalPosition += _direction * step;
            _rangeLeft -= step;

            if (HitSomething())
            {
                return;
            }

            if (_rangeLeft <= 0f)
            {
                Resolve();
                return;
            }
        }
    }

    /// <summary>True when this sub-step resolved the arrow.</summary>
    private bool HitSomething()
    {
        foreach (Area3D area in GetOverlappingAreas())
        {
            if (area is not Hurtbox hurtbox || hurtbox.Combat == null)
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

            hurtbox.Receive(_packet);
            Resolve();
            return true;
        }

        return false;
    }

    private void Resolve()
    {
        _resolved = true;
        Monitoring = false;
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
