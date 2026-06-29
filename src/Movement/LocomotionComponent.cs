using Embervale.Core.Diagnostics;
using Embervale.Entities;
using Embervale.Stats;
using Godot;

namespace Embervale.Movement;

/// <summary>
/// Reusable ground-locomotion motor for any <see cref="CharacterEntity"/>. It is
/// input-agnostic: a controller (player input, or later enemy AI) feeds it a
/// desired world-space direction each physics frame and it handles gravity,
/// acceleration, jumping and <c>MoveAndSlide</c>.
///
/// Movement speed is sourced from the owner's <see cref="StatsComponent"/>
/// (<see cref="StatType.MoveSpeed"/>) when present, so buffs/gear that modify the
/// stat automatically affect movement — falling back to <see cref="BaseSpeed"/>.
/// </summary>
[GlobalClass]
public partial class LocomotionComponent : EntityComponent
{
    [Export]
    public float BaseSpeed { get; set; } = 5f;

    [Export]
    public float Acceleration { get; set; } = 60f;

    [Export]
    public float JumpVelocity { get; set; } = 4.5f;

    [Export]
    public float SprintMultiplier { get; set; } = 1.6f;

    private CharacterBody3D _body = null!;
    private StatsComponent? _stats;
    private float _gravity = 9.8f;

    private bool _dashing;
    private double _dashTimer;
    private Vector3 _dashDir;
    private float _dashSpeed;

    public bool IsGrounded => _body != null && _body.IsOnFloor();

    public bool IsDashing => _dashing;

    /// <summary>Begins a fixed-velocity burst (a dodge roll, Phase 29E): <see cref="Move"/> drives the body
    /// along <paramref name="dir"/> at <paramref name="speed"/> for <paramref name="duration"/> seconds,
    /// ignoring movement input (gravity still applies).</summary>
    public void StartDash(Vector3 dir, float speed, float duration)
    {
        Vector3 flat = new(dir.X, 0f, dir.Z);
        if (flat.LengthSquared() < 0.0001f)
        {
            return;
        }

        _dashDir = flat.Normalized();
        _dashSpeed = speed;
        _dashTimer = duration;
        _dashing = true;
    }

    protected override void OnInitialize()
    {
        if (Entity?.Body is not CharacterBody3D body)
        {
            Log.Error($"{nameof(LocomotionComponent)} requires a CharacterEntity owner.");
            return;
        }

        _body = body;
        _stats = Entity!.GetComponent<StatsComponent>();
        _gravity = ProjectSettings.GetSetting("physics/3d/default_gravity", 9.8f).AsSingle();
    }

    /// <summary>
    /// Advances physics one step. <paramref name="wishDir"/> is a world-space
    /// direction on the horizontal plane (its Y is ignored); magnitude &gt; 1 is
    /// clamped so diagonal input is not faster.
    /// </summary>
    public void Move(double delta, Vector3 wishDir, bool sprint, bool jump)
    {
        if (_body == null)
        {
            return;
        }

        float dt = (float)delta;
        Vector3 velocity = _body.Velocity;

        if (!_body.IsOnFloor())
        {
            velocity.Y -= _gravity * dt;
        }
        else if (jump && !_dashing)
        {
            velocity.Y = JumpVelocity;
        }

        // A dodge roll overrides input: fixed-velocity burst for its duration (gravity still applies).
        if (_dashing)
        {
            _dashTimer -= delta;
            velocity.X = _dashDir.X * _dashSpeed;
            velocity.Z = _dashDir.Z * _dashSpeed;
            _body.Velocity = velocity;
            _body.MoveAndSlide();
            if (_dashTimer <= 0d)
            {
                _dashing = false;
            }

            return;
        }

        Vector3 horizontal = new(wishDir.X, 0f, wishDir.Z);
        if (horizontal.LengthSquared() > 1f)
        {
            horizontal = horizontal.Normalized();
        }

        float speed = CurrentSpeed() * (sprint ? SprintMultiplier : 1f);
        Vector3 target = horizontal * speed;

        velocity.X = Mathf.MoveToward(velocity.X, target.X, Acceleration * dt);
        velocity.Z = Mathf.MoveToward(velocity.Z, target.Z, Acceleration * dt);

        _body.Velocity = velocity;
        _body.MoveAndSlide();
    }

    private float CurrentSpeed()
    {
        return _stats != null ? _stats.GetValue(StatType.MoveSpeed) : BaseSpeed;
    }
}
