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

    /// <summary>Tallest step this body climbs, in metres (Phase 39C). Defaults to
    /// <see cref="StepUp.MaxHeight"/>, which is the navmesh's own <c>agent_max_climb</c> — so every
    /// actor that walks can reach everywhere the navmesh paths one. Set to 0 to opt a body out.</summary>
    [Export]
    public float StepHeight { get; set; } = StepUp.MaxHeight;

    /// <summary>Flight mode (Phase 35B): gravity is skipped and the body servos its vertical velocity
    /// toward <see cref="TargetAltitude"/> instead. Horizontal movement is untouched, so a controller
    /// (the enemy AI, in practice) keeps steering a flier exactly as it steers a walker. Off — every
    /// actor before the dragon, and the dragon whenever it is on the ground — is the original motor.</summary>
    public bool Flying { get; set; }

    /// <summary>World-space Y the flier climbs or descends toward while <see cref="Flying"/>.</summary>
    public float TargetAltitude { get; set; }

    /// <summary>Vertical speed used to reach <see cref="TargetAltitude"/>, in m/s.</summary>
    public float ClimbSpeed { get; set; } = 6f;

    private CharacterBody3D _body = null!;
    private StatsComponent? _stats;
    private float _gravity = 9.8f;

    /// <summary>Whether this body has already reported a poisoned velocity. One line per body, not one
    /// per frame — the failure repeats every physics tick and would otherwise bury the log it exists
    /// to make readable.</summary>
    private bool _reportedBadVelocity;

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

        // ⚠️ THE POISON GUARD (37F). A CharacterBody3D keeps its velocity between frames, so ONE
        // non-finite value is not one bad frame — it is every frame for the rest of the run, and the
        // crash surfaces far from wherever the value came from. Two reports came in from this: a dead
        // enemy hitting Mathf.MoveToward (which throws inside Math.Sign on NaN) and a companion
        // hitting MoveAndSlide (which warns that a Vector3 cannot be normalized). Same bad value,
        // different first victim.
        //
        // ⚠️ IT LOGS, ONCE PER BODY, AND THAT IS THE HALF THAT MATTERS. A silent clamp fixes the crash
        // and destroys the evidence — the source is still unproven, so this line is what makes it
        // findable the next time it happens instead of a permanently hidden bug that stopped shouting.
        if (!MotionSafety.IsFinite(velocity))
        {
            if (!_reportedBadVelocity)
            {
                _reportedBadVelocity = true;
                Log.Error(
                    $"Locomotion: '{Entity?.DisplayName ?? _body.Name}' had a non-finite velocity " +
                    $"({velocity}); stopping it. Something upstream wrote NaN or infinity — the wish " +
                    "direction, a MoveSpeed modifier, or a dash direction are the candidates.");
            }

            velocity = Vector3.Zero;
        }

        // The other door into the same failure. Zero is the honest reading of "no usable direction",
        // and it is what Stand already passes deliberately.
        wishDir = MotionSafety.Sanitize(wishDir);

        if (Flying)
        {
            // Servo toward the target altitude and clamp on arrival, so a hovering body holds still
            // instead of oscillating through it. Descending into the floor is how landing ends:
            // MoveAndSlide stops the body and IsGrounded reports it — no ground probe needed.
            float gap = TargetAltitude - _body.GlobalPosition.Y;
            velocity.Y = Mathf.Abs(gap) < 0.05f ? 0f : Mathf.Sign(gap) * ClimbSpeed;
        }
        else if (!_body.IsOnFloor())
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

        // ⚠️ THIS IS THE DOOR THE REPORTED CRASH CAME THROUGH, AND IT IS NOT OBVIOUS. `Stand` passes
        // Vector3.Zero, so the enemy's NaN could not have been the direction — but `Zero * NaN` is
        // NaN, so a poisoned MoveSpeed stat produces a NaN target from a zero input, and MoveToward
        // throws on it THIS frame, before the velocity guard above ever sees the value.
        // Sanitised here rather than in CurrentSpeed so a bad stat cannot reach the motor by any route.
        Vector3 target = MotionSafety.Sanitize(horizontal * speed);

        velocity.X = Mathf.MoveToward(velocity.X, target.X, Acceleration * dt);
        velocity.Z = Mathf.MoveToward(velocity.Z, target.Z, Acceleration * dt);

        TryStepUp(new Vector3(velocity.X, 0f, velocity.Z) * dt);

        _body.Velocity = velocity;
        _body.MoveAndSlide();
    }

    /// <summary>
    /// Climbs a step the body is walking into (Phase 39C). Godot has no step offset, so the climb is
    /// <b>simulated with the engine rather than computed</b>: move up, move forward, drop back down,
    /// and keep the result only if the body actually ended up higher AND further along. Anything else
    /// is rolled back to the transform it started on, so a failed attempt costs nothing.
    ///
    /// ⚠️ <b>THE ARITHMETIC VERSION OF THIS WAS WRONG AND ITS UNIT TESTS AGREED WITH IT.</b> Probing
    /// down from a raised position and lifting by <c>height - drop</c> under-reports every step,
    /// because a capsule's rounded bottom catches the step's <em>corner</em> instead of its top face:
    /// the 0.3 m plaza dais measured 0.156 m. The body then lifted a fraction of what it needed and
    /// <c>MoveAndSlide</c>'s floor snap pulled it straight back down — a lift every frame, forever,
    /// and no movement. Letting the engine resolve each move is both shorter and correct.
    ///
    /// It runs <em>before</em> <c>MoveAndSlide</c> so the same frame's motion carries the body over
    /// the step rather than into its face and then up it, which reads as a stutter.
    ///
    /// ⚠️ <b>This is on the shared motor, so it moves every walking actor in the game</b> — the player,
    /// enemies and companions all route through here. That is deliberate and is the actual fix: the
    /// navmesh has always baked NPC paths over 0.5 m ground with <c>agent_max_climb</c>, while a body
    /// could climb 0.1 m. Opt one out with <see cref="StepHeight"/> = 0 rather than by branching here.
    /// </summary>
    private void TryStepUp(Vector3 motion)
    {
        if (StepHeight <= 0f || Flying || !_body.IsOnFloor() || motion.LengthSquared() < 0.000001f)
        {
            return;
        }

        Transform3D start = _body.GlobalTransform;
        if (!_body.TestMove(start, motion))
        {
            return; // nothing in the way: the overwhelmingly common case, and it costs one probe
        }

        // The forward leg is at least a body's width, not one frame's motion. A single frame at
        // walking speed is ~8 cm, which is not far enough to get a capsule's belly over the step —
        // it stops against the corner and the attempt reads as a wall.
        Vector3 forward = motion.Normalized() * Mathf.Max(motion.Length(), ProbeReach);

        _body.MoveAndCollide(Vector3.Up * StepHeight);
        _body.MoveAndCollide(forward);
        _body.MoveAndCollide(Vector3.Down * StepHeight);

        Vector3 moved = _body.GlobalPosition - start.Origin;
        if (!StepUp.Accept(moved.Y, new Vector2(moved.X, moved.Z).Dot(new Vector2(forward.X, forward.Z).Normalized()), StepHeight))
        {
            _body.GlobalTransform = start; // rolled back whole: a half-climbed body is worse than none
        }
    }

    /// <summary>How far forward a step attempt reaches, in metres — wide enough to carry a capsule's
    /// belly past the step's edge rather than leaving it resting on the corner.</summary>
    private const float ProbeReach = 0.5f;

    private float CurrentSpeed()
    {
        return _stats != null ? _stats.GetValue(StatType.MoveSpeed) : BaseSpeed;
    }
}
