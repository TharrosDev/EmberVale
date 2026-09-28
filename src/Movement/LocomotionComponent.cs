using Embervale.Core.Diagnostics;
using Embervale.Entities;
using Embervale.Stats;
using Embervale.World;
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
///
/// <para>On top of that baseline (2026-09 traversal pass, Skyrim as the reference): three gaits
/// (<see cref="Walking"/>, run, sprint), sprint paid for in stamina where
/// <see cref="SprintCostsStamina"/>, coyote time and a jump buffer (<see cref="JumpAssist"/>), an uphill
/// speed penalty and step-<em>down</em> snapping (<see cref="LocomotionRules"/>,
/// <see cref="StepDownHeight"/>), a landing stumble after a long drop and fall damage where
/// <see cref="TakesFallDamage"/> (<see cref="FallRules"/>), and a position guard that returns a body
/// found below the world (<see cref="MotionSafety.IsInWorld"/>). The rules are pure and tested; this
/// class only measures and applies.</para>
///
/// ⚠️ <b>EVERY WALKING ACTOR RUNS THROUGH THIS MOTOR</b> — the player, enemies, companions, and the
/// player again while mounted. The costs a player should feel (stamina, fall damage) are therefore
/// opt-in flags <c>PlayerFactory</c> sets, not defaults: an enemy that chases at a sprint would
/// otherwise arrive with no stamina to block with, which is an AI rebalance nobody asked for.
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

    /// <summary>
    /// How hard the body slows when the player lets go, in m/s².
    ///
    /// Lower than <see cref="Acceleration"/> on purpose: a character that decelerates as fast as it
    /// accelerates stops dead the instant the stick is released, which is the single clearest tell
    /// that a body has no mass. Not so low that stopping feels like ice.
    /// </summary>
    [Export] public float Friction { get; set; } = 34f;

    /// <summary>
    /// Fraction of ground acceleration available while airborne.
    ///
    /// ⚠️ A value of 1 — which is what a single acceleration term meant — lets a jump be re-aimed
    /// mid-flight, so a leap is a decision that can be taken back and there is no commitment in it.
    /// Enough to correct, not enough to change your mind.
    /// </summary>
    [Export(PropertyHint.Range, "0,1,0.05")] public float AirControl { get; set; } = 0.35f;

    [Export]
    public float SprintMultiplier { get; set; } = 1.6f;

    /// <summary>Speed multiplier while <see cref="Walking"/> — Skyrim's caps-lock walk. Under half a
    /// run, because a walk that is merely a slower run reads as a sluggish run rather than a gait.</summary>
    [Export(PropertyHint.Range, "0.1,1,0.05")] public float WalkMultiplier { get; set; } = 0.45f;

    /// <summary>Walk mode (a toggle for the player). Sprint overrides it — see
    /// <see cref="LocomotionRules.GaitScale"/>.</summary>
    public bool Walking { get; set; }

    /// <summary>Whether sprinting spends the owner's <see cref="StatType.Stamina"/>. Off by default and
    /// on for the player — see the class remarks for why AI bodies are exempt. Never while mounted:
    /// the horse's own gallop pool answers then.</summary>
    [Export] public bool SprintCostsStamina { get; set; }

    /// <summary>Stamina per second spent while actually sprinting on the ground. 100 stamina is about
    /// eight seconds of it — Skyrim's rhythm. Every tick is a spend, so <c>StaminaPacing</c>'s regen
    /// pause holds for as long as the sprint does.</summary>
    [Export] public float SprintStaminaPerSecond { get; set; } = 12f;

    /// <summary>Stamina a winded body must recover (and let go of sprint) before sprinting again —
    /// the hysteresis in <see cref="SprintStamina"/>.</summary>
    [Export] public float SprintResumeStamina { get; set; } = 20f;

    /// <summary>Seconds after leaving the ground in which a jump still fires (coyote time).</summary>
    [Export(PropertyHint.Range, "0,0.3,0.01")] public float CoyoteTime { get; set; } = 0.12f;

    /// <summary>Seconds a jump pressed early — before landing, or during a roll — stays queued.</summary>
    [Export(PropertyHint.Range, "0,0.3,0.01")] public float JumpBuffer { get; set; } = 0.12f;

    /// <summary>Speed lost walking straight up the steepest walkable ground (the body's
    /// <c>floor_max_angle</c>), as a fraction; linear in the slope angle below that.</summary>
    [Export(PropertyHint.Range, "0,0.9,0.05")] public float UphillPenalty { get; set; } = 0.3f;

    /// <summary>How far a grounded body is pulled down to stay on the ground, in metres — written to
    /// the body's <c>floor_snap_length</c> (never lowering one authored higher).
    ///
    /// ⚠️ The engine default is 0.1 m, which is under a single stair riser: a body walking DOWN steps
    /// or running down a hill left the floor on every edge and fell the difference, so descending read
    /// as a string of small hops — and each hop was a frame of air control rather than ground control.
    /// Step-up fixed the climb and nothing fixed the way back. A jump is unaffected: the engine never
    /// snaps a body that is moving upward.</summary>
    [Export(PropertyHint.Range, "0,0.6,0.05")] public float StepDownHeight { get; set; } = 0.35f;

    /// <summary>Drops shorter than this, in metres, land without a stumble. A jump off a wall is free;
    /// a first-floor window is not.</summary>
    [Export] public float HardLandingHeight { get; set; } = 2.5f;

    /// <summary>How much each metre past <see cref="HardLandingHeight"/> lengthens the stumble, in seconds.</summary>
    [Export] public float LandingRecoveryPerMetre { get; set; } = 0.05f;

    /// <summary>The longest landing stumble, in seconds.</summary>
    [Export] public float LandingRecoveryMax { get; set; } = 0.8f;

    /// <summary>Speed multiplier on the landing frame of a stumble, easing back to 1.</summary>
    [Export(PropertyHint.Range, "0,1,0.05")] public float LandingSlowdown { get; set; } = 0.35f;

    /// <summary>Whether a long fall costs health. Off by default, on for the player.</summary>
    [Export] public bool TakesFallDamage { get; set; }

    /// <summary>The tallest free fall, in metres — about a two-storey roof.</summary>
    [Export] public float SafeFallHeight { get; set; } = 7f;

    /// <summary>The fall that costs all of max health, in metres. Linear between the two, so a
    /// twelve-metre drop costs about a fifth of the bar.</summary>
    [Export] public float LethalFallHeight { get; set; } = 30f;

    /// <summary>World Y below which a body is lost rather than low — far under any authored ground.
    /// See <see cref="MotionSafety.IsInWorld"/>.</summary>
    [Export] public float WorldFloorY { get; set; } = -500f;

    /// <summary>Tallest step this body climbs, in metres (Phase 39C). Defaults to
    /// <see cref="StepUp.MaxHeight"/>, which is a ceiling over every cell's <c>agent_max_climb</c> — so
    /// every actor that walks can reach everywhere the navmesh paths one, and a little further (the bake
    /// floors climb to a voxel; see <see cref="StepUp"/>). Set to 0 to opt a body out.</summary>
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
    private MountComponent? _mount;
    private float _gravity = 9.8f;

    private JumpAssist.State _jump = JumpAssist.Fresh;
    private bool _winded;

    // Fall measurement. _lastEnd is where the previous Move left the body, so any gap at the start of
    // the next one was made by something else — a teleport — and restarts the measurement.
    private bool _hasHistory;
    private Vector3 _lastEnd;
    private Vector3? _lastFloor;
    private bool _wasGrounded;
    private float _fallPeakY;
    private float _recoveryLeft;
    private float _recoveryDuration;

    /// <summary>Whether this body has already reported being somewhere impossible — once per body,
    /// for the same reason as <see cref="_reportedBadVelocity"/>.</summary>
    private bool _reportedBadPosition;

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

    /// <summary>Whether the body was granted a sprint on its last <see cref="Move"/> — not the same
    /// as whether one was asked for, since a winded body is refused. The camera's sprint framing reads
    /// this so it never shows a sprint the legs are not doing.</summary>
    public bool IsSprinting { get; private set; }

    /// <summary>
    /// Ends a dash early.
    ///
    /// ⚠️ <b>A CANCELLED ROLL HAD TO BE CANCELLED IN TWO PLACES AND ONLY EVER WAS IN ONE.</b> A
    /// stagger landing mid-roll cleared <c>DodgeComponent</c>'s own flag and its i-frames, but the
    /// burst it had started here ran to its full duration: the owner kept sliding at roll speed
    /// through a stagger they were supposed to be locked in, invulnerable for none of it. Two
    /// components disagreeing about whether the same roll is happening is the whole defect.
    /// </summary>
    public void CancelDash() => _dashing = false;

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
        _mount = Entity.GetComponent<MountComponent>();
        _gravity = ProjectSettings.GetSetting("physics/3d/default_gravity", 9.8f).AsSingle();
        _body.FloorSnapLength = Mathf.Max(_body.FloorSnapLength, StepDownHeight);
    }

    /// <summary>
    /// Advances physics one step. <paramref name="wishDir"/> is a world-space
    /// direction on the horizontal plane (its Y is ignored); magnitude &gt; 1 is
    /// clamped so diagonal input is not faster, and under 1 is kept, so a half-pushed stick walks.
    /// <paramref name="sprint"/> is a request (a winded body with <see cref="SprintCostsStamina"/> is
    /// refused — read <see cref="IsSprinting"/> for the answer); <paramref name="jump"/> is a press on
    /// this frame, which <see cref="JumpAssist"/> buffers and forgives off an edge.
    /// </summary>
    public void Move(double delta, Vector3 wishDir, bool sprint, bool jump)
    {
        if (_body == null || !GuardPosition())
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

        bool grounded = _body.IsOnFloor();
        (_jump, bool jumpNow) = JumpAssist.Step(
            _jump, grounded && !Flying, jump, canJump: !_dashing && !Flying, dt, CoyoteTime, JumpBuffer);

        if (Flying)
        {
            // Servo toward the target altitude and clamp on arrival, so a hovering body holds still
            // instead of oscillating through it. Descending into the floor is how landing ends:
            // MoveAndSlide stops the body and IsGrounded reports it — no ground probe needed.
            float gap = TargetAltitude - _body.GlobalPosition.Y;
            velocity.Y = Mathf.Abs(gap) < 0.05f ? 0f : Mathf.Sign(gap) * ClimbSpeed;
        }
        else if (jumpNow)
        {
            // Replaces whatever gravity had built up, so a coyote jump taken a few frames into a fall
            // is the same jump as one taken from the lip.
            velocity.Y = JumpVelocity;
        }
        else if (!grounded)
        {
            velocity.Y -= _gravity * dt;
        }

        // A dodge roll overrides input: fixed-velocity burst for its duration (gravity still applies).
        if (_dashing)
        {
            _dashTimer -= delta;
            velocity.X = _dashDir.X * _dashSpeed;
            velocity.Z = _dashDir.Z * _dashSpeed;
            IsSprinting = false;
            Slide(velocity, dt);
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

        bool moving = horizontal.LengthSquared() > 0.0001f;
        IsSprinting = GrantSprint(sprint, moving && grounded, dt);

        float speed = CurrentSpeed()
            * LocomotionRules.GaitScale(IsSprinting, Walking, SprintMultiplier, WalkMultiplier)
            * FallRules.RecoveryScale(_recoveryLeft, _recoveryDuration, LandingSlowdown)
            * (grounded && moving ? UphillScale(horizontal) : 1f);
        _recoveryLeft = Mathf.Max(0f, _recoveryLeft - dt);

        // ⚠️ THIS IS THE DOOR THE REPORTED CRASH CAME THROUGH, AND IT IS NOT OBVIOUS. `Stand` passes
        // Vector3.Zero, so the enemy's NaN could not have been the direction — but `Zero * NaN` is
        // NaN, so a poisoned MoveSpeed stat produces a NaN target from a zero input, and MoveToward
        // throws on it THIS frame, before the velocity guard above ever sees the value.
        // Sanitised here rather than in CurrentSpeed so a bad stat cannot reach the motor by any route.
        Vector3 target = MotionSafety.Sanitize(horizontal * speed);

        // ⚠️ ACCELERATING, SLOWING AND BEING AIRBORNE ARE THREE DIFFERENT RATES, and they were one.
        // A single Acceleration meant a character stopped exactly as sharply as it started (so
        // letting go of the stick was a handbrake) and steered in mid-air exactly as well as on the
        // ground (so a jump could be turned into a different jump). Both read as weightless.
        bool wantsToMove = target.LengthSquared() > 0.0001f;
        float rate = grounded
            ? (wantsToMove ? Acceleration : Friction)
            : Acceleration * AirControl;

        velocity.X = Mathf.MoveToward(velocity.X, target.X, rate * dt);
        velocity.Z = Mathf.MoveToward(velocity.Z, target.Z, rate * dt);

        Slide(velocity, dt);
    }

    /// <summary>Steps, slides, and then reads what the slide did: every path through
    /// <see cref="Move"/> ends here, so a roll climbs kerbs and lands falls exactly as a walk does.</summary>
    private void Slide(Vector3 velocity, float dt)
    {
        // ⚠️ THE ROLL USED TO SKIP THIS AND STOP DEAD AT EVERY KERB. The dash branch returned before
        // step-up ran, so a dodge into a 20 cm step was a dodge into a wall — the one moment the
        // player most needs the ground to cooperate.
        TryStepUp(new Vector3(velocity.X, 0f, velocity.Z) * dt);

        _body.Velocity = velocity;
        _body.MoveAndSlide();
        AfterSlide();
    }

    /// <summary>
    /// Runs before anything moves: is the body somewhere it can be, and did something else move it
    /// since the last frame? Returns false when there is nowhere safe to put a lost body, in which
    /// case the frame is skipped rather than simulated from an impossible position.
    /// </summary>
    private bool GuardPosition()
    {
        Vector3 here = _body.GlobalPosition;
        if (!MotionSafety.IsInWorld(here, WorldFloorY))
        {
            // Logged once per body, like the velocity guard and for the same reason: the report is
            // the evidence, and the failure repeats every tick.
            if (!_reportedBadPosition)
            {
                _reportedBadPosition = true;
                Log.Error(
                    $"Locomotion: '{Entity?.DisplayName ?? _body.Name}' was at {here}, outside the world " +
                    $"(non-finite, or below y={WorldFloorY}); " +
                    (_lastFloor is { } at ? $"returning it to its last floor at {at}." : "it has never stood anywhere, so it is held.") +
                    " A teleport that wrote a bad position, or a terrain seam, are the candidates.");
            }

            _body.Velocity = Vector3.Zero;
            if (_lastFloor is not { } safe)
            {
                return false;
            }

            _body.GlobalPosition = safe;
            here = safe;
        }

        // ⚠️ ANY JUMP BETWEEN FRAMES WAS A TELEPORT, AND A TELEPORT IS NOT A FALL. Nothing moves the
        // body between the end of one Move and the start of the next except something that means to
        // place it — world recovery, fast travel, a load, Blink, the guard above. Without this reset a
        // player pulled out of a crevasse onto the lip would "land" from the peak of the fall that put
        // them in it, and take the damage twice.
        if (!_hasHistory || here.DistanceSquaredTo(_lastEnd) > TeleportDistance * TeleportDistance)
        {
            _hasHistory = true;
            _fallPeakY = here.Y;
            _lastFloor = here;
            _recoveryLeft = 0f;
        }

        return true;
    }

    /// <summary>Tracks the fall and the last good floor from what <c>MoveAndSlide</c> just did.</summary>
    private void AfterSlide()
    {
        Vector3 here = _body.GlobalPosition;
        bool grounded = _body.IsOnFloor();

        if (grounded && !_wasGrounded && !Flying)
        {
            Land(_fallPeakY - here.Y, here);
        }

        // A flier's altitude is its own choice, not a fall: the measurement starts where flight ends.
        _fallPeakY = grounded || Flying ? here.Y : Mathf.Max(_fallPeakY, here.Y);
        if (grounded && MotionSafety.IsInWorld(here, WorldFloorY))
        {
            _lastFloor = here;
        }

        _wasGrounded = grounded;
        _lastEnd = here;
    }

    private void Land(float drop, Vector3 at)
    {
        // Deep water breaks a fall. The body stands on the basin floor (there is no swimming), so the
        // depth is the surface above the feet.
        float depth = WorldWater.SurfaceAt(at.X, at.Z) is float surface ? surface - at.Y : 0f;
        float height = FallRules.Cushioned(drop, depth, WorldWater.WadeDepth);

        _recoveryDuration = FallRules.RecoverySeconds(
            height, HardLandingHeight, LandingRecoveryPerMetre, LandingRecoveryMax);
        _recoveryLeft = _recoveryDuration;

        if (TakesFallDamage && _stats != null)
        {
            // Straight to health, past armour, guard and i-frames, as StatusEffectsComponent's ticks
            // go: a fall has no attacker to block and a roll does not make the ground softer.
            float damage = FallRules.Damage(height, SafeFallHeight, LethalFallHeight, _stats.GetMax(StatType.Health));
            if (damage > 0f)
            {
                _stats.ApplyDamage(damage);
            }
        }
    }

    /// <summary>Whether this frame's sprint request is granted, spending stamina when it is.</summary>
    private bool GrantSprint(bool wanted, bool spending, float dt)
    {
        // Mounted, the horse's gallop pool has already answered (PlayerInputRouter asks it first).
        if (!SprintCostsStamina || _stats == null || _mount is { IsMounted: true })
        {
            return wanted;
        }

        // StatsComponent.IsWinded (zero until 35% refills; the HUD dims the bar for it) also refuses,
        // so the bar the player sees and the sprint they get are one state, not two.
        SprintStamina.Result result = SprintStamina.Step(
            _winded || _stats.IsWinded, wanted, _stats.GetCurrent(StatType.Stamina), SprintResumeStamina);
        _winded = result.Exhausted;

        // Only moving on the ground costs anything: holding the key while standing, or while in the
        // air, is a request, not a sprint.
        if (result.Sprinting && spending && SprintStaminaPerSecond > 0f)
        {
            _stats.ModifyCurrent(StatType.Stamina, -SprintStaminaPerSecond * dt);
        }

        return result.Sprinting;
    }

    private float UphillScale(Vector3 horizontal)
    {
        Vector3 normal = _body.GetFloorNormal();
        return LocomotionRules.UphillScale(
            normal.X, normal.Y, normal.Z, horizontal.X, horizontal.Z, _body.FloorMaxAngle, UphillPenalty);
    }

    /// <summary>Displacement between frames, in metres, past which the body was placed rather than
    /// moved. A motion warp — the only other thing that sweeps a body between Moves — is bounded well
    /// under this per frame.</summary>
    private const float TeleportDistance = 2f;

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

        // ⚠️ ONLY WHEN THE LAST MoveAndSlide ACTUALLY HIT A WALL, AND THIS IS A BUG FIX AS MUCH AS
        // A SAVING. TestMove against the *terrain soup* answers true for any micro-bump in a 2.5 m
        // triangle ahead of the capsule, so the three engine moves below fired on ordinary open
        // ground and StepUp.Accept took them: measured, a body walking near-flat terrain travelled
        // 7.83 m/s against a BaseSpeed of 5, because every frame teleported it ProbeReach forward on
        // top of its own MoveAndSlide. IsOnWall is the engine's own classification from last frame's
        // slide — free to read, false for walkable ground and true for the vertical face of a step,
        // which is the only thing this is for. The cost is that a step is climbed on the frame after
        // first contact rather than on it; at 60 Hz that is invisible, and MoveAndSlide has already
        // stopped the body against the face either way.
        if (!_body.IsOnWall())
        {
            return;
        }

        Transform3D start = _body.GlobalTransform;
        if (!_body.TestMove(start, motion))
        {
            return; // the wall is not in *this* direction: strafing along it, not into it
        }

        // The forward leg is at least a body's width, not one frame's motion. A single frame at
        // walking speed is ~8 cm, which is not far enough to get a capsule's belly over the step —
        // it stops against the corner and the attempt reads as a wall.
        Vector3 forward = motion.Normalized() * Mathf.Max(motion.Length(), ProbeReach);

        _body.MoveAndCollide(Vector3.Up * StepHeight);
        _body.MoveAndCollide(forward);
        KinematicCollision3D? landing = _body.MoveAndCollide(Vector3.Down * StepHeight);

        // What the down leg came to rest on: 0 when it rested on nothing, so a climb into thin air
        // is refused (see StepUp.Accept for why the landing has to be a floor).
        float landingNormalY = landing?.GetNormal().Y ?? 0f;

        Vector3 moved = _body.GlobalPosition - start.Origin;
        if (!StepUp.Accept(
                moved.Y,
                new Vector2(moved.X, moved.Z).Dot(new Vector2(forward.X, forward.Z).Normalized()),
                StepHeight,
                landingNormalY,
                Mathf.Cos(_body.FloorMaxAngle)))
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
