using Embervale.Animation;
using Embervale.Combat;
using Embervale.Core.Diagnostics;
using Embervale.Core.Events;
using Embervale.Dialogue;
using Embervale.Entities;
using Embervale.Save;
using Embervale.Stats;
using Embervale.World;
using Godot;
using Embervale.Core;

namespace Embervale.Movement;

/// <summary>Published when the player mounts or dismounts. <c>MessageKey</c> is a
/// <c>data/locale/strings.csv</c> key, resolved at display time like every other notice.</summary>
public readonly record struct MountChangedEvent(bool Mounted, string MessageKey) : IGameEvent;

/// <summary>Published when the horse refuses something — no mount owned, a blown gallop, or ground
/// it will not take (deep water, a steep drop).</summary>
public readonly record struct MountRefusedEvent(string ReasonKey) : IGameEvent;

/// <summary>
/// The player's mount (Phase 39A, upgraded by the Movement pass): whistle, ride, dismount, and a
/// horse that behaves like one.
///
/// <b>The mount is still a state of the rider, not a second body.</b> There is no horse
/// <c>CharacterBody3D</c> and no second locomotion motor — the player's own capsule keeps moving,
/// wearing a horse, and <see cref="LocomotionComponent"/> is driven through its ordinary
/// <c>Move</c>. What changed is what the rider's input means once it reaches the horse:
/// <list type="bullet">
/// <item><b>Gaits and a heading of its own</b> (<see cref="MountGaits"/>). Walk, trot, canter and
/// gallop are discrete; speed ramps between them; the horse carves toward where it is asked to go at
/// a turn rate that falls with speed while the rider looks around freely on top.</item>
/// <item><b>It judges ground</b> (<see cref="MountTerrain"/>): one ray at its own stopping distance
/// ahead, and it balks at deep declared water or a steep drop instead of carrying the rider in.</item>
/// <item><b>It jumps</b> from a trot or faster, higher than a man on foot.</item>
/// <item><b>It is somewhere</b> (<see cref="MountWhistle"/>). A whistle brings it galloping in from
/// out of sight; a dismount leaves it standing where you got off and steps you clear of it; a
/// whistle near it again brings <em>that</em> horse back. Every one of those steps that cannot be
/// done safely falls back to 39A's mount-on-the-spot, so a paid-for horse always arrives.</item>
/// </list>
///
/// ⚠️ <b>The capsule is still the player's</b>, so a mounted horse is exactly as wide and as tall a
/// climber as a man on foot — invariant 16, not a mount bug. The visible horse is a model parented
/// to the body and turned to its own heading; nothing collides with it.
///
/// ⚠️ <b>The parked horse and a horse mid-run-in are not saved, on purpose.</b> They are where the
/// horse happens to be, not a fact about the player: a load puts the rider on or off the horse and
/// the next whistle fetches it. The ride itself — mounted, the gallop pool, the heading — is.
///
/// <b>Ownership is 38D's flag and nothing else.</b> <c>ServiceKind.Stable</c> charged 400 gold and
/// set <see cref="OwnedFlagId"/>; this reads it. The two halves are held together by a
/// <c>--validate</c> rule rather than by a comment, because a flag id is a string in two files and
/// nothing else in the repo would ever notice them drifting apart.
/// </summary>
[GlobalClass]
public partial class MountComponent : EntityComponent, ISaveable
{
    /// <summary>The story flag <c>data/services/EmberCrownStable.tres</c> grants on purchase.
    /// ⚠️ <c>ContentValidator</c> asserts the two are the same string — see the class remarks.</summary>
    public const string OwnedFlagId = "flag.stable.mount_owned";

    internal const string MountModelPath = ModelAssets.Horse;

    /// <summary>Where the rider sits, measured against the imported model in the engine and then
    /// rendered from four angles with the market behind it (<c>tools/mount_shots.gd</c>).
    /// ⚠️ These are not derivable from the file: the pack's glTF accessors read ~4.8 m tall because
    /// its armature carries a 100x scale, and the seat is a bone height, not a fraction of a box.
    /// The imported horse stands 2.41 m to the ears and 2.84 m nose to tail at
    /// <c>nodes/root_scale = 0.5</c>.</summary>
    private const float SaddleHeight = 0.86f;

    /// <summary>Forward offset of the seat, so the rider sits over the withers rather than the rump.
    /// Negative is forward — Godot's forward is -Z.</summary>
    private const float SaddleForward = -0.52f;

    /// <summary>Mirrors <c>PlayerFactory.EyeHeight</c>. Duplicated rather than made internal because
    /// this component is not player-only by type — the value is the one thing it assumes about its
    /// owner, and a wrong one is visible the instant anybody renders it.</summary>
    private const float PlayerEyeHeight = 1.62f;

    /// <summary>The rider's capsule height (<c>PlayerFactory.CapsuleHeight</c>), for placing feet.</summary>
    private const float RiderHeight = 1.8f;

    // --- gaits (MountGaits) — multiples of the rider's own MoveSpeed ---------------------------

    [ExportGroup("Gaits")]
    [Export] public float ReinBackSpeed { get; set; } = GaitTuning.Default.ReinBackSpeed;
    [Export] public float WalkSpeed { get; set; } = GaitTuning.Default.WalkSpeed;
    [Export] public float TrotSpeed { get; set; } = GaitTuning.Default.TrotSpeed;
    [Export] public float CanterSpeed { get; set; } = GaitTuning.Default.CanterSpeed;
    [Export] public float GallopSpeed { get; set; } = GaitTuning.Default.GallopSpeed;

    /// <summary>Gathering pace, in rider speeds per second.</summary>
    [Export] public float Acceleration { get; set; } = GaitTuning.Default.Acceleration;

    /// <summary>Easing off, in rider speeds per second — harder than <see cref="Acceleration"/>.</summary>
    [Export] public float Deceleration { get; set; } = GaitTuning.Default.Deceleration;

    /// <summary>Stopping for refused ground, in rider speeds per second — the horse plants its feet.</summary>
    [Export] public float BalkDeceleration { get; set; } = GaitTuning.Default.BalkDeceleration;

    /// <summary>Turn rate standing still, degrees per second.</summary>
    [Export] public float PivotTurnDegrees { get; set; } = Mathf.RadToDeg(GaitTuning.Default.PivotTurnRate);

    /// <summary>Turn rate at a full gallop, degrees per second.</summary>
    [Export] public float GallopTurnDegrees { get; set; } = Mathf.RadToDeg(GaitTuning.Default.GallopTurnRate);

    /// <summary>A turn sharper than this collects a canter or gallop to a trot.</summary>
    [Export] public float CollectAngleDegrees { get; set; } = Mathf.RadToDeg(GaitTuning.Default.CollectAngle);

    /// <summary>Stick deflection below which the horse walks.</summary>
    [Export(PropertyHint.Range, "0,1,0.05")] public float WalkInput { get; set; } = GaitTuning.Default.WalkInput;

    /// <summary>Stick deflection below which the horse trots; at or above it, it canters.</summary>
    [Export(PropertyHint.Range, "0,1,0.05")] public float TrotInput { get; set; } = GaitTuning.Default.TrotInput;

    /// <summary>The rider's jump velocity while mounted, m/s. The on-foot value is restored on
    /// dismount; 5.4 clears roughly a fence rail where the rider's 4.5 clears a step.</summary>
    [Export] public float MountedJumpVelocity { get; set; } = 5.4f;

    // --- ground (MountTerrain) ----------------------------------------------------------------

    /// <summary>Deepest declared water the horse will walk into. Under the rider's own
    /// <c>WorldWater.WadeDepth</c> on purpose: the horse turns back before the rider would have to.</summary>
    [ExportGroup("Ground")]
    [Export] public float RefuseWaterDepth { get; set; } = 0.9f;

    /// <summary>A drop this small is a step at any grade.</summary>
    [Export] public float MaxStepDown { get; set; } = 1.2f;

    /// <summary>Steepest descent the horse will take, as drop over distance (1.2 ≈ 50°).</summary>
    [Export] public float MaxDescentGrade { get; set; } = 1.2f;

    /// <summary>Metres the ground probe reaches past the horse's stopping distance.</summary>
    [Export] public float ProbeMargin { get; set; } = 1.4f;

    /// <summary>The ground probe's longest reach, metres.</summary>
    [Export] public float ProbeMaxDistance { get; set; } = 12f;

    /// <summary>Seconds between two identical refusal notices, so a rider nosing along a bank is
    /// told once rather than every time the probe flickers.</summary>
    [Export] public float RefusalNoticeSeconds { get; set; } = 4f;

    // --- whistle and dismount (MountWhistle) --------------------------------------------------

    /// <summary>How far away a freshly whistled horse starts, metres.</summary>
    [ExportGroup("Whistle")]
    [Export] public float WhistleDistance { get; set; } = 18f;

    /// <summary>Start points tried round the rider before falling back to mounting on the spot.</summary>
    [Export] public int WhistleCandidates { get; set; } = 8;

    /// <summary>A parked horse within this many metres runs in from where it stands.</summary>
    [Export] public float WhistleRange { get; set; } = 90f;

    /// <summary>A parked horse within this many metres is simply mounted.</summary>
    [Export] public float MountReach { get; set; } = 3f;

    /// <summary>The run-in ends this close to the rider, and the rider is in the saddle.</summary>
    [Export] public float ArriveDistance { get; set; } = 0.6f;

    /// <summary>Seconds a run-in may take before the horse is simply there — the unreachable case.</summary>
    [Export] public float ArriveTimeout { get; set; } = 10f;

    /// <summary>Metres over which a running-in horse eases from gallop to walk.</summary>
    [Export] public float ArriveSlowRadius { get; set; } = 7f;

    /// <summary>How far to the side the rider steps down, metres.</summary>
    [Export] public float DismountStep { get; set; } = 1.2f;

    /// <summary>A parked horse further than this from the rider is gone (back to the stable).</summary>
    [Export] public float ParkedLeash { get; set; } = 160f;

    // --- presentation ------------------------------------------------------------------------

    /// <summary>Speed (rider speeds) at which the horse's Walk clip plays at 1x.</summary>
    [ExportGroup("Presentation")]
    [Export] public float WalkClipPace { get; set; } = 0.8f;

    /// <summary>Speed (rider speeds) at which the horse's Gallop clip plays at 1x.</summary>
    [Export] public float GallopClipPace { get; set; } = 2.4f;

    /// <summary>The horse's rough footprint for placing a whistled start point, metres.</summary>
    [Export] public float HorseRadius { get; set; } = 0.55f;

    /// <summary>The horse's height for the same test, metres.</summary>
    [Export] public float HorseHeight { get; set; } = 2.0f;

    private StatsComponent? _stats;
    private StoryFlagsComponent? _flags;
    private CharacterAnimationComponent? _animation;
    private LocomotionComponent? _locomotion;
    private Node3D? _bodyMesh;
    private Node3D? _cameraPivot;
    private Node3D? _visual;
    private AnimationPlayer? _visualAnimation;
    private string _idleClip = "", _walkClip = "", _gallopClip = "", _jumpClip = "", _grazeClip = "";
    private StatModifier? _speedModifier;
    private MountRules.GallopState _gallop = MountRules.Fresh;
    private readonly PhysicsRayQueryParameters3D _ray = new();

    private MountGait _gait = MountGait.Halt;
    private float _speed;     // signed, rider speeds, along the heading
    private float _measured;  // real horizontal speed last frame, rider speeds
    private float _heading;   // world yaw the horse faces
    private float _riderBaseYaw;
    private float? _riderJumpVelocity;
    private bool _jumping;
    private MountRefusal _refusal;
    private MountRefusal _lastNotice;
    private float _noticeTimer;

    private bool _approaching;
    private float _approachTimer;
    private bool _parked;
    private float _grazeTimer;

    /// <summary>Whether the player is currently on the horse.</summary>
    public bool IsMounted { get; private set; }

    /// <summary>Whether a whistled horse is running in.</summary>
    public bool IsSummoning => _approaching;

    /// <summary>Remaining gallop pool, for the HUD and for the save.</summary>
    public float Stamina => _gallop.Stamina;

    /// <summary>The gait the horse is in, for the HUD and the camera.</summary>
    public MountGait Gait => IsMounted ? _gait : MountGait.Halt;

    /// <summary>What the horse is refusing right now, if anything.</summary>
    public MountRefusal Refusal => IsMounted ? _refusal : MountRefusal.None;

    /// <summary>Whether the mount is delivering a charge — the pool granted the gallop <em>and</em>
    /// the body is actually moving at gallop pace (<see cref="MountedCombat.IsCharging"/>). 39B's
    /// charge bonus reads this, so a blown horse, a horse pinned on a wall and a gallop that has not
    /// gathered pace yet all swing for ordinary damage.</summary>
    public bool IsGalloping => IsMounted && MountedCombat.IsCharging(_gallop.Galloping, _measured, GallopSpeed);

    public string SaveId => SaveKey("mount");

    private GaitTuning Tuning => new(
        ReinBackSpeed, WalkSpeed, TrotSpeed, CanterSpeed, GallopSpeed,
        Acceleration, Deceleration, BalkDeceleration,
        Mathf.DegToRad(PivotTurnDegrees), Mathf.DegToRad(GallopTurnDegrees),
        Mathf.DegToRad(CollectAngleDegrees), WalkInput, TrotInput);

    protected override void OnInitialize()
    {
        IEntity owner = Entity!;
        _stats = owner.GetComponent<StatsComponent>();
        _flags = owner.GetComponent<StoryFlagsComponent>();
        _animation = owner.GetComponent<CharacterAnimationComponent>();
        _locomotion = owner.GetComponent<LocomotionComponent>();
        _bodyMesh = owner.Body.GetNodeOrNull<Node3D>("BodyMesh");

        // The mesh is built facing away from the camera. Seat(false) runs on every load, before any
        // mount, so the yaw it restores has to start as the mesh's own, not zero.
        _riderBaseYaw = _bodyMesh?.Rotation.Y ?? 0f;
        _cameraPivot = owner.Body.GetNodeOrNull<Node3D>("CameraPivot");
        if (owner.Body is CollisionObject3D body)
        {
            _ray.Exclude = new Godot.Collections.Array<Rid> { body.GetRid() };
        }

        RegisterSaveable();
    }

    protected override void OnTeardown()
    {
        SaveManager.Instance?.Unregister(this);
    }

    /// <summary>
    /// The one verb the key and the dev command both call. Mounted, it dismounts. On foot it
    /// whistles: a parked horse in reach is mounted, one in earshot runs in from where it stands, and
    /// otherwise a horse runs in from out of sight. <paramref name="instant"/> skips the run-in —
    /// the dev console's path, and the fallback for every run-in that cannot be done safely.
    /// </summary>
    public void Toggle(bool instant = false)
    {
        if (IsMounted)
        {
            Dismount("mount.dismissed");
            return;
        }

        if (_flags?.Has(OwnedFlagId) != true)
        {
            EventBus.Instance?.Publish(new MountRefusedEvent("mount.not_owned"));
            return;
        }

        if (_approaching && !instant)
        {
            return; // already coming; a second whistle is not a second horse
        }

        float riderYaw = Entity!.Body.GlobalRotation.Y;
        if (instant)
        {
            Mount("mount.summoned", _approaching ? VisualYaw() : riderYaw);
            return;
        }

        Vector3 rider = Entity.Body.GlobalPosition;
        if (VisualValid() && _parked)
        {
            Vector3 horse = _visual!.GlobalPosition;
            float distance = Flat(horse - rider).Length();
            if (distance <= MountReach)
            {
                // Swing up onto the horse where it stands, if the rider's capsule fits there — it
                // did when the rider got off. If not, the horse steps over instead.
                if (TryPlaceRider(horse, 1f, out Vector3 feet) && Mathf.Abs(feet.Y - horse.Y) < 1f)
                {
                    Entity.Body.GlobalPosition = feet;
                }

                Mount("mount.remounted", VisualYaw());
                return;
            }

            if (distance <= WhistleRange && ClearLine(horse, rider, 1.2f))
            {
                StartApproach(horse);
                return;
            }
        }

        if (TryFindSummonPoint(rider, riderYaw, out Vector3 start))
        {
            StartApproach(start);
            return;
        }

        // Nowhere safe for a horse to come from (a crowded interior, a ledge, open water all round):
        // 39A's behaviour, which is always safe because the capsule does not change.
        Mount("mount.summoned", riderYaw);
    }

    /// <summary>
    /// The mounted half of the controller, run once per physics frame before locomotion. On foot it
    /// hands every input back untouched — after advancing a run-in or a parked horse — so the
    /// controller has no mounted branch of its own. Mounted, the horse answers instead:
    /// <paramref name="move"/> becomes the horse's heading at its gait's speed (a fraction of the
    /// gallop, which the rider's MoveSpeed modifier is sized to), <paramref name="jump"/> is refused
    /// below a trot, and the returned sprint is always false because the gallop is already in the
    /// speed. <paramref name="wishDir"/> is the raw input direction — deliberately not the one a
    /// committed swing has scaled down, or every mounted blow would rein the horse in.
    /// </summary>
    public bool Ride(double delta, Vector3 wishDir, bool sprintHeld, ref Vector3 move, ref bool jump)
    {
        float dt = (float)delta;
        if (!IsMounted)
        {
            TickApproach(dt);
            TickParked(dt);
            return sprintHeld;
        }

        GaitTuning t = Tuning;
        float unit = Unit();
        Vector3 bodyVelocity = Entity!.Body is CharacterBody3D body ? body.Velocity : Vector3.Zero;
        Vector3 forward = HeadingVector(_heading);
        _measured = unit > 0f ? Flat(bodyVelocity).Length() / unit : 0f;

        // A horse pressed against a wall is not still doing 13 m/s: pull the ramp back to what the
        // body is really doing, so it gathers pace again from there rather than bursting off the wall.
        float along = unit > 0f ? Flat(bodyVelocity).Dot(forward) / unit : 0f;
        if (_speed > along + 0.5f)
        {
            _speed = Mathf.Max(along + 0.5f, 0f);
        }

        Vector3 flat = MotionSafety.Sanitize(Flat(wishDir));
        float magnitude = Mathf.Min(flat.Length(), 1f);
        bool asking = magnitude > MountGaits.InputDeadzone;
        Vector3 riderForward = -Entity.Body.GlobalBasis.Z;
        bool backing = asking && flat.Normalized().Dot(Flat(riderForward).Normalized()) < -0.7f;
        float desiredYaw = asking && !backing ? MountGaits.YawOf(flat.X, flat.Z) : _heading;
        float turn = MountGaits.AngleBetween(_heading, desiredYaw);

        // The pool is asked only when the rider is asking for a gallop the horse could give — not
        // while turning tight, backing or standing — so it does not drain for a gait never granted.
        MountGait candidate = MountGaits.Select(magnitude, turn, backing, gallopGranted: true, t);
        bool wasExhausted = _gallop.Exhausted;
        _gallop = MountRules.Step(
            _gallop, sprintHeld && candidate == MountGait.Gallop, dt, MountGaits.RegenScale(_gait));
        if (_gallop.Exhausted && !wasExhausted)
        {
            EventBus.Instance?.Publish(new MountRefusedEvent("mount.exhausted"));
        }

        _gait = _gallop.Galloping ? MountGait.Gallop : MountGaits.Select(magnitude, turn, backing, false, t);

        float target = MountGaits.SpeedOf(_gait, t);
        _refusal = target > 0f || _speed > 0.05f ? ProbeGround(forward, unit, t) : MountRefusal.None;
        NoticeRefusal(dt);
        bool balking = _refusal != MountRefusal.None && target > 0f;
        if (balking)
        {
            target = 0f;
        }

        _speed = MountGaits.StepSpeed(_speed, target, dt, t, balking);
        _heading = MountGaits.StepHeading(_heading, desiredYaw, MountGaits.TurnRate(_speed, t), dt);

        move = HeadingVector(_heading) * (t.GallopSpeed > 0f ? _speed / t.GallopSpeed : 0f);

        bool grounded = _locomotion?.IsGrounded ?? true;
        jump = jump && grounded && MountGaits.CanJump(_gait) && !balking;
        if (jump)
        {
            _jumping = true;
        }
        else if (grounded && bodyVelocity.Y <= 0f)
        {
            _jumping = false;
        }

        PlayGait();
        return false;
    }

    public override void _Process(double delta)
    {
        // The body turns with the mouse between physics frames; the horse must not. Re-deriving the
        // visual every rendered frame is what keeps it planted on its own heading while the rider
        // looks around — once per physics tick visibly swings it with the camera and snaps it back.
        if (IsMounted)
        {
            PoseMounted();
        }
    }

    private void Mount(string messageKey, float heading)
    {
        if (IsMounted)
        {
            return;
        }

        IsMounted = true;
        _approaching = false;
        _parked = false;
        _heading = MountGaits.Wrap(heading);
        _speed = 0f;
        _gait = MountGait.Halt;
        _refusal = MountRefusal.None;
        AttachVisual();
        Seat(true);

        // A fresh modifier per mount, kept so the exact instance can be pulled off again. Removing
        // by source (this component) would also be correct; keeping the instance means a stray
        // second Mount() cannot leave an unpaired one behind. ⚠️ A PercentMult of m multiplies by
        // (1 + m), so GallopSpeed - 1 makes the stat the gallop and every gait a fraction of it.
        if (_stats != null && _speedModifier == null)
        {
            _speedModifier = new StatModifier(GallopSpeed - 1f, ModifierType.PercentMult, this);
            _stats.GetStat(StatType.MoveSpeed).AddModifier(_speedModifier);
        }

        if (_locomotion != null && _riderJumpVelocity == null)
        {
            _riderJumpVelocity = _locomotion.JumpVelocity;
            _locomotion.JumpVelocity = MountedJumpVelocity;
        }

        EventBus.Instance?.Publish(new MountChangedEvent(true, messageKey));
        Log.Info("Mounted.");
    }

    private void Dismount(string messageKey)
    {
        if (!IsMounted)
        {
            return;
        }

        Vector3 spot = Entity!.Body.GlobalPosition;
        float heading = _heading;

        // The horse stays exactly where it was drawn — it pivots about the saddle, so that is not
        // the rider's origin — and on the ground, not wherever a mid-jump dismount left it.
        Vector3 horseAt = VisualValid() && _visual!.IsInsideTree() ? _visual.GlobalPosition : spot;
        if (GroundBelow(horseAt, 0.5f, 4f) is { } ground)
        {
            horseAt.Y = ground;
        }

        IsMounted = false;
        StripRider();
        Park(horseAt, heading);
        StepOff(spot, heading);
        EventBus.Instance?.Publish(new MountChangedEvent(false, messageKey));
        Log.Info("Dismounted.");
    }

    /// <summary>Everything <see cref="Mount"/> did to the rider, undone. ⚠️ The speed modifier is
    /// <b>removed</b>, never overwritten: it multiplies <c>StatType.MoveSpeed</c>, which
    /// <see cref="LocomotionComponent"/> feeds straight into a <c>CharacterBody3D</c>'s velocity, and
    /// a leaked one stacks silently on every reload. The jump velocity goes back the same way.</summary>
    private void StripRider()
    {
        if (_speedModifier != null)
        {
            _stats?.GetStat(StatType.MoveSpeed).RemoveModifier(_speedModifier);
            _speedModifier = null;
        }

        if (_riderJumpVelocity is { } jumpVelocity && _locomotion != null)
        {
            _locomotion.JumpVelocity = jumpVelocity;
        }

        _riderJumpVelocity = null;
        Seat(false);
        _speed = 0f;
        _measured = 0f;
        _gait = MountGait.Halt;
        _refusal = MountRefusal.None;
        _jumping = false;
    }

    /// <summary>The whole ride torn down, horse and all — the half <see cref="Load"/> needs.</summary>
    private void StripState()
    {
        StripRider();
        if (_visual != null)
        {
            if (GodotObject.IsInstanceValid(_visual))
            {
                _visual.QueueFree();
            }

            _visual = null;
            _visualAnimation = null;
        }

        _approaching = false;
        _parked = false;
        _gallop = MountRules.Fresh;
    }

    /// <summary>Raises (or lowers) the rider and the camera. The camera pivot moves with the body:
    /// without it the first-person eye stays at 1.62 m, which while mounted is <em>inside the
    /// horse's neck</em>. That is not a subtle framing complaint — it is the shipping camera mode.</summary>
    private void Seat(bool mounted)
    {
        if (_bodyMesh != null)
        {
            Vector3 seat = mounted ? new Vector3(0f, SaddleHeight, SaddleForward) : Vector3.Zero;
            _bodyMesh.Position = seat;

            // The rider faces the horse's heading, not the camera's, so the mesh's own yaw is
            // remembered here and turned by PoseMounted — and put back exactly on the way down.
            Vector3 rotation = _bodyMesh.Rotation;
            if (mounted)
            {
                _riderBaseYaw = rotation.Y;
            }
            else
            {
                _bodyMesh.Rotation = new Vector3(rotation.X, _riderBaseYaw, rotation.Z);
            }

            // ⚠️ HitReactionComponent owns this mesh's position while a hit lurch is easing out, and
            // it puts the mesh back at ITS idea of rest. Mounting inside that 0.18 s window would
            // otherwise leave the rider standing where the horse is not, until the next hit
            // re-sampled it. The component samples rest on its own too — this is the ordering case
            // that sampling cannot see, because the mesh is not at rest when it happens.
            if (Entity!.GetComponent<HitReactionComponent>() is { } recoil)
            {
                recoil.Rest = seat;
            }
        }

        if (_cameraPivot != null)
        {
            Vector3 seat = _cameraPivot.Position;
            _cameraPivot.Position = new Vector3(
                seat.X, mounted ? PlayerEyeHeight + SaddleHeight : PlayerEyeHeight, seat.Z);
        }

        if (_animation != null)
        {
            _animation.Riding = mounted;
        }
    }

    /// <summary>
    /// Turns the horse (and the rider on it) to the horse's own heading, relative to the body that
    /// carries them both. The horse pivots about the <em>saddle</em>, not its own origin, so the
    /// rider never moves: HitReactionComponent owns the rider mesh's position mid-lurch, and only its
    /// yaw is written here.
    /// </summary>
    private void PoseMounted()
    {
        float relative = MountGaits.Wrap(_heading - Entity!.Body.GlobalRotation.Y);
        if (VisualValid())
        {
            float back = -SaddleForward; // the horse's origin sits this far behind the seat
            _visual!.Position = new Vector3(
                back * Mathf.Sin(relative), 0f, SaddleForward + (back * Mathf.Cos(relative)));
            _visual.Rotation = new Vector3(0f, relative + Mathf.Pi, 0f); // glTF forward is +Z
        }

        if (_bodyMesh != null)
        {
            Vector3 rotation = _bodyMesh.Rotation;
            _bodyMesh.Rotation = new Vector3(rotation.X, _riderBaseYaw + relative, rotation.Z);
        }
    }

    // --- the horse's model ---------------------------------------------------------------------

    private bool VisualValid() => _visual != null && GodotObject.IsInstanceValid(_visual);

    /// <summary>The world yaw a free-standing horse faces (undoing the glTF turn).</summary>
    private float VisualYaw() =>
        VisualValid() && _visual!.IsInsideTree()
            ? MountGaits.Wrap(_visual.GlobalRotation.Y - Mathf.Pi)
            : Entity!.Body.GlobalRotation.Y;

    private bool EnsureVisual()
    {
        if (VisualValid())
        {
            return true;
        }

        if (GD.Load<PackedScene>(MountModelPath)?.Instantiate() is not Node3D horse)
        {
            return false;
        }

        horse.Name = "MountVisual";
        _visual = horse;
        _visualAnimation = null;

        // ⚠️ Deferred, per CLAUDE.md §7: a body mid-setup REFUSES the add, logs it and carries on,
        // leaving a live node that is not in the tree. Mounting normally happens long after setup,
        // but Load() runs on the restore path where it does not.
        Entity!.Body.CallDeferred(Node.MethodName.AddChild, horse);
        horse.Ready += ResolveGaitClips;
        return true;
    }

    /// <summary>The horse under the rider: parented, following the body, posed each frame.</summary>
    private void AttachVisual()
    {
        if (!EnsureVisual())
        {
            return;
        }

        _visual!.TopLevel = false;
        PoseMounted();
    }

    /// <summary>The horse left standing in the world at <paramref name="at"/>, facing
    /// <paramref name="yaw"/>. ⚠️ A top-level node's transform is a world transform, which is what
    /// lets it be set before the deferred add has landed.</summary>
    private void PlaceFree(Vector3 at, float yaw)
    {
        if (!VisualValid())
        {
            return;
        }

        _visual!.TopLevel = true;
        _visual.Transform = new Transform3D(new Basis(Vector3.Up, yaw + Mathf.Pi), at);
    }

    private void Park(Vector3 at, float yaw)
    {
        if (!VisualValid())
        {
            return;
        }

        _parked = true;
        _grazeTimer = 0f;
        PlaceFree(at, yaw);
        Play(_idleClip, 1f);
    }

    /// <summary>A parked horse idles and grazes, and goes home once the rider is far enough away
    /// that nobody would see it standing there.</summary>
    private void TickParked(float dt)
    {
        if (!_parked || _approaching)
        {
            return;
        }

        if (!VisualValid())
        {
            _parked = false;
            return;
        }

        if (Flat(_visual!.GlobalPosition - Entity!.Body.GlobalPosition).Length() > ParkedLeash)
        {
            _visual.QueueFree();
            _visual = null;
            _visualAnimation = null;
            _parked = false;
            return;
        }

        _grazeTimer += dt;
        if (_grazeTimer > 7f)
        {
            _grazeTimer = 0f;
            bool grazing = _visualAnimation?.CurrentAnimation == _grazeClip && _grazeClip.Length > 0;
            Play(grazing || _grazeClip.Length == 0 ? _idleClip : _grazeClip, 1f);
        }
    }

    // --- whistle -------------------------------------------------------------------------------

    private void StartApproach(Vector3 from)
    {
        if (!EnsureVisual())
        {
            Mount("mount.summoned", Entity!.Body.GlobalRotation.Y);
            return;
        }

        Vector3 to = Entity!.Body.GlobalPosition - from;
        _approaching = true;
        _parked = false;
        _approachTimer = 0f;
        PlaceFree(from, MountGaits.YawOf(to.X, to.Z));
        Log.Info($"Mount whistled from {Flat(to).Length():0} m.");
    }

    /// <summary>One frame of the run-in. The horse follows the ground with one downward ray; if the
    /// ground vanishes under it, the rider wanders out of earshot, or it simply takes too long, it
    /// is there — the fallback every step of the whistle lands on.</summary>
    private void TickApproach(float dt)
    {
        if (!_approaching)
        {
            return;
        }

        if (!VisualValid())
        {
            _approaching = false;
            Mount("mount.summoned", Entity!.Body.GlobalRotation.Y);
            return;
        }

        _approachTimer += dt;
        Vector3 rider = Entity!.Body.GlobalPosition;
        Vector3 at = _visual!.TopLevel ? _visual.Transform.Origin : rider;
        float unit = Unit();

        (float x, float z, float remaining) = MountWhistle.Approach(
            at.X, at.Z, rider.X, rider.Z,
            GallopSpeed * unit, WalkSpeed * unit, ArriveSlowRadius, dt);

        float? ground = GroundBelow(new Vector3(x, Mathf.Max(at.Y, rider.Y), z), 2.5f, 6f);
        bool arrived = remaining <= ArriveDistance;
        bool giveUp = ground == null || _approachTimer > ArriveTimeout ||
                      Flat(rider - at).Length() > WhistleRange * 1.5f;

        if (arrived || giveUp)
        {
            Vector3 run = rider - at;
            float yaw = Flat(run).LengthSquared() > 0.01f ? MountGaits.YawOf(run.X, run.Z) : VisualYaw();
            Mount("mount.summoned", yaw);
            return;
        }

        Vector3 step = new Vector3(x, ground!.Value, z) - at;
        float faceYaw = Flat(step).LengthSquared() > 0.000001f ? MountGaits.YawOf(step.X, step.Z) : VisualYaw();
        PlaceFree(new Vector3(x, ground.Value, z), faceYaw);

        float pace = dt > 0f && unit > 0f ? Flat(step).Length() / dt / unit : 0f;
        PlayForPace(pace);
    }

    /// <summary>A start point round the rider for a whistled horse: real ground the horse's own
    /// footprint fits on (<see cref="SafePlacementService"/>), not in water it would refuse, and with
    /// nothing solid between it and the rider — the run-in is a straight line and passes through
    /// nothing only because this checked.</summary>
    private bool TryFindSummonPoint(Vector3 rider, float yaw, out Vector3 start)
    {
        WorldHeightfield? field = WorldGround.Field;
        foreach ((float x, float z) in MountWhistle.Candidates(rider.X, rider.Z, yaw, WhistleDistance, WhistleCandidates))
        {
            float y = field != null ? field.Height(x, z) : rider.Y;
            if (!SafePlacementService.TryResolve(
                    Entity!.Body, new Vector3(x, y, z), out Vector3 resolved,
                    capsuleRadius: HorseRadius, capsuleHeight: HorseHeight))
            {
                continue;
            }

            Vector3 ground = resolved - (Vector3.Up * ((HorseHeight * 0.5f) + 0.06f));
            if (WorldWater.DepthAt(ground.X, ground.Z, field) > RefuseWaterDepth ||
                !ClearLine(ground, rider, 1.2f))
            {
                continue;
            }

            start = ground;
            return true;
        }

        start = rider;
        return false;
    }

    /// <summary>Steps the rider down beside the horse — left first, the traditional side, then
    /// right. ⚠️ <b>Staying in the saddle's footprint is always the safe answer</b>, because the
    /// capsule never changed shape: a side is only taken when the path to it is clear, the rider's
    /// capsule fits there, and it is not a drop. Otherwise the rider simply stands where the horse is.</summary>
    private void StepOff(Vector3 spot, float heading)
    {
        Vector3 left = new(-Mathf.Cos(heading), 0f, Mathf.Sin(heading));
        foreach (Vector3 side in new[] { left, -left })
        {
            Vector3 desired = spot + (side * DismountStep);
            if (!ClearLine(spot, desired, 0.9f) ||
                !TryPlaceRider(desired, 1f, out Vector3 feet) ||
                Mathf.Abs(feet.Y - spot.Y) > 0.8f ||
                Flat(feet - desired).Length() > 0.75f)
            {
                continue;
            }

            Entity!.Body.GlobalPosition = feet;
            if (Entity.Body is CharacterBody3D body)
            {
                body.Velocity = Vector3.Zero;
            }

            return;
        }
    }

    /// <summary>Where the rider's feet go to stand at <paramref name="desired"/>, per
    /// <see cref="SafePlacementService"/> (which answers with a capsule <em>centre</em>; the
    /// player's origin is at its feet).</summary>
    private bool TryPlaceRider(Vector3 desired, float maxCorrection, out Vector3 feet)
    {
        if (SafePlacementService.TryResolve(Entity!.Body, desired, out Vector3 centre, maxCorrection: maxCorrection))
        {
            feet = centre - (Vector3.Up * (RiderHeight * 0.5f));
            return true;
        }

        feet = desired;
        return false;
    }

    // --- ground --------------------------------------------------------------------------------

    /// <summary>Probes ahead of the horse by its own stopping distance and asks
    /// <see cref="MountTerrain"/> what it found. A wall in the way is not a refusal — that is
    /// <c>MoveAndSlide</c>'s business, and a ray started inside it would read as a bottomless drop.</summary>
    private MountRefusal ProbeGround(Vector3 forward, float unit, GaitTuning t)
    {
        Vector3 feet = Entity!.Body.GlobalPosition;
        float distance = MountTerrain.ProbeDistance(
            Mathf.Abs(_speed) * unit, t.BalkDeceleration * unit, ProbeMargin, ProbeMaxDistance);
        Vector3 ahead = feet + (forward * distance);

        if (!ClearLine(feet, ahead, 1f))
        {
            return MountRefusal.None;
        }

        float reach = MaxStepDown + (distance * MaxDescentGrade) + 0.5f;
        float? ground = GroundBelow(new Vector3(ahead.X, feet.Y, ahead.Z), 1.5f, reach);
        WorldHeightfield? field = WorldGround.Field;
        return MountTerrain.Judge(
            feet.Y, ground, WorldWater.SurfaceAt(ahead.X, ahead.Z, WorldWater.Bodies, field),
            WorldWater.DepthAt(feet.X, feet.Z, field),
            distance, RefuseWaterDepth, MaxStepDown, MaxDescentGrade);
    }

    private void NoticeRefusal(float dt)
    {
        _noticeTimer = Mathf.Max(0f, _noticeTimer - dt);
        if (_refusal == MountRefusal.None || (_refusal == _lastNotice && _noticeTimer > 0f))
        {
            return;
        }

        if (_refusal != _lastNotice || _noticeTimer <= 0f)
        {
            _lastNotice = _refusal;
            _noticeTimer = RefusalNoticeSeconds;
            EventBus.Instance?.Publish(new MountRefusedEvent(
                _refusal == MountRefusal.DeepWater ? "mount.refuses_water" : "mount.refuses_drop"));
        }
    }

    /// <summary>The first world surface below <paramref name="at"/>, searching from
    /// <paramref name="rise"/> above it to <paramref name="drop"/> below. Dynamic world counts as
    /// ground: a deck the horse can stand on is a deck whichever layer it was authored on.</summary>
    private float? GroundBelow(Vector3 at, float rise, float drop) =>
        Cast(at + (Vector3.Up * rise), at + (Vector3.Down * drop),
            CombatLayers.WorldStatic | CombatLayers.WorldDynamic) is { } hit
            ? hit.Y
            : null;

    /// <summary>Whether nothing solid lies between two points at <paramref name="height"/> above them.</summary>
    private bool ClearLine(Vector3 from, Vector3 to, float height) =>
        Cast(from + (Vector3.Up * height), to + (Vector3.Up * height),
            CombatLayers.WorldStatic | CombatLayers.WorldDynamic) == null;

    private Vector3? Cast(Vector3 from, Vector3 to, uint mask)
    {
        if (!Entity!.Body.IsInsideTree())
        {
            return null;
        }

        _ray.From = from;
        _ray.To = to;
        _ray.CollisionMask = mask;
        Godot.Collections.Dictionary hit = Entity.Body.GetWorld3D().DirectSpaceState.IntersectRay(_ray);
        return hit.Count > 0 && hit.TryGetValue("position", out Variant position) ? position.AsVector3() : null;
    }

    // --- animation -----------------------------------------------------------------------------

    private void ResolveGaitClips()
    {
        _visualAnimation = FindAnimationPlayer(_visual);
        if (_visualAnimation == null)
        {
            return;
        }

        string[] clips = _visualAnimation.GetAnimationList();
        _idleClip = AnimationClips.Resolve(clips, "idle");
        _walkClip = AnimationClips.Resolve(clips, "run");
        _gallopClip = AnimationClips.Resolve(clips, "gallop");
        _jumpClip = AnimationClips.Resolve(clips, "gallop_jump");
        _grazeClip = AnimationClips.Resolve(clips, "eating");
    }

    /// <summary>The clip for what the body is actually doing, not what the rider asked for — a horse
    /// held against a wall does not gallop on the spot — played at a rate matched to the real speed,
    /// so a trot is a quick walk and a canter a slow gallop rather than two more clips the pack
    /// does not have.</summary>
    private void PlayGait()
    {
        if (_jumping && _jumpClip.Length > 0)
        {
            Play(_jumpClip, 1f);
            return;
        }

        float signed = _speed < 0f ? -_measured : _measured;
        PlayForPace(signed);
    }

    private void PlayForPace(float pace)
    {
        float speed = Mathf.Abs(pace);
        if (speed < 0.08f)
        {
            Play(_idleClip, 1f);
        }
        else if (speed < (TrotSpeed + CanterSpeed) * 0.5f || _gallopClip.Length == 0)
        {
            float rate = Mathf.Clamp(speed / Mathf.Max(WalkClipPace, 0.01f), 0.5f, 1.8f);
            Play(_walkClip, pace < 0f ? -rate : rate); // a rein-back is the walk, played backwards
        }
        else
        {
            Play(_gallopClip, Mathf.Clamp(speed / Mathf.Max(GallopClipPace, 0.01f), 0.6f, 1.4f));
        }
    }

    private void Play(string clip, float rate)
    {
        if (_visualAnimation == null || clip.Length == 0)
        {
            return;
        }

        _visualAnimation.SpeedScale = rate;
        if (_visualAnimation.CurrentAnimation != clip)
        {
            _visualAnimation.Play(clip, customBlend: 0.2);
        }
    }

    private static AnimationPlayer? FindAnimationPlayer(Node? node)
    {
        if (node == null)
        {
            return null;
        }

        if (node is AnimationPlayer player)
        {
            return player;
        }

        foreach (Node child in node.GetChildren())
        {
            if (FindAnimationPlayer(child) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    // --- helpers -------------------------------------------------------------------------------

    /// <summary>Metres per second per "rider speed": the rider's MoveSpeed with the mount's own
    /// modifier taken back out, so a buff to the rider still carries the horse.</summary>
    private float Unit()
    {
        float gallop = Mathf.Max(GallopSpeed, 0.01f);
        float stat = _stats?.GetValue(StatType.MoveSpeed) ?? ((_locomotion?.BaseSpeed ?? 5f) * gallop);
        float value = _speedModifier != null ? stat / gallop : stat;
        return float.IsFinite(value) && value > 0f ? value : 0f;
    }

    private static Vector3 HeadingVector(float yaw) => new(-Mathf.Sin(yaw), 0f, -Mathf.Cos(yaw));

    private static Vector3 Flat(Vector3 v) => new(v.X, 0f, v.Z);

    // --- persistence ---------------------------------------------------------------------------

    public Godot.Collections.Dictionary Save() => new()
    {
        ["mounted"] = IsMounted,
        ["stamina"] = _gallop.Stamina,
        ["heading"] = _heading,
    };

    /// <summary>
    /// ⚠️ <b>Replaces, never merges.</b> A quickload keeps every live component, so a save taken on
    /// foot must actively put the player back on the ground — including stripping a speed modifier
    /// and a jump velocity the saved timeline never had, and sending home a parked or running-in
    /// horse that belongs to the abandoned one. Everything is torn down first and rebuilt from the
    /// file, which is the <c>EquipmentComponent.Load</c> / <c>PerksComponent.Load</c> pattern. A save
    /// from before the gait upgrade has no heading; the horse then faces where the rider does.
    /// </summary>
    public void Load(Godot.Collections.Dictionary data)
    {
        bool wasMounted = IsMounted;
        IsMounted = false;
        StripState();

        if (data.TryGetValue("mounted", out Variant mounted) && mounted.AsBool())
        {
            float heading = data.TryGetValue("heading", out Variant saved) && float.IsFinite(saved.AsSingle())
                ? saved.AsSingle()
                : Entity!.Body.Rotation.Y; // not Global: the restore path may run before the tree
            Mount(string.Empty, heading); // a load restores state; it does not narrate one
        }

        if (data.TryGetValue("stamina", out Variant stamina))
        {
            float value = Mathf.Clamp(stamina.AsSingle(), 0f, MountRules.StaminaMax);
            _gallop = new MountRules.GallopState(value, value <= 0f, false);
        }
        else
        {
            // Said out loud rather than left to StripState above: no saved pool is a rested horse,
            // whatever Mount() or a later edit to the teardown does to the live one in between.
            _gallop = MountRules.Fresh;
        }

        if (wasMounted != IsMounted)
        {
            Log.Info($"Mount restored: {(IsMounted ? "mounted" : "on foot")}.");
        }
    }
}
