using System;
using System.Globalization;
using Embervale.Animation;
using Embervale.Core;
using Embervale.Core.Diagnostics;
using Embervale.Core.Events;
using Embervale.Core.Services;
using Embervale.Magic;
using Embervale.Movement;
using Embervale.Player;
using Embervale.Settings;
using Godot;

namespace Embervale.Debugging;

/// <summary>
/// The camera screenshot harness: <c>godot --path . -- --camshots</c>. The player is stood on a level
/// strip of ground near where the save stands and photographed in both views (<c>tp</c>, <c>fp</c>)
/// at idle (three frames across three seconds, for sway), walking, jogging, sprinting (the shot and
/// the three frames drawn before it, <c>_f1.._f3</c>, for surge), strafing each way, backpedalling,
/// charging a spell, channelling one, and looking straight down and up. Third person also runs on a
/// diagonal (forward and right together). First person also looks straight down at the widest field
/// of view the settings allow and while sprinting. Two more shots look at the
/// third-person body from the side through a camera of the harness's own, at idle and at a sprint,
/// with the collision capsule drawn and an arrow along the way the capsule faces.
/// Run WITHOUT <c>--headless</c>.
///
/// <para>The player is moved by holding the real input actions, so the router, the motor, the
/// animation tree and the camera rig all run as they do in play.</para>
///
/// <para>Each shot logs, for the frame the PNG shows: the camera's position relative to the head
/// bone, the heading of the visible body and of the chest against the capsule's, the camera's heading
/// against it, how far the hips are from the capsule's centre, the camera's position relative to the
/// chest bone, the field of view, and where the casting hand is on screen. A body that faces the
/// wrong way, a clip that carries the hips off the capsule, a camera that leaves the head or sits in
/// the chest and a casting hand that is out of frame are all provable from the log; a body drawn back
/// to front, an eye in the chest and a hand out of frame fail the run.</para>
/// </summary>
public sealed partial class CamShots : TimedShots
{
    private const string ChargeSpellId = "spell.flame_lance";
    private const string ChannelSpellId = "spell.storm_conduit";
    private const float LaneLength = 30f;
    private const float LaneHalfWidth = 2.5f;
    private const float ShotHour = 14f;
    private const double SettleSeconds = 0.9;
    private const float SideDistance = 6f;

    /// <summary>The widest field of view the settings slider allows (degrees).</summary>
    private const float WidestFov = 110f;

    /// <summary>How far the visible body may be turned from the capsule's facing before the shot is
    /// failed (degrees). Generous: this is for a body drawn back to front, not for a clip's lean.</summary>
    private const float MaxMeshTurn = 30f;

    /// <summary>Looking straight down at a stand, the eye must be at least this far ahead of the
    /// chest bone (metres): the chest's surface is about 0.14 m ahead of that bone, and the near
    /// plane is 0.08 m.</summary>
    private const float MinEyeAheadOfChest = 0.2f;

    /// <summary>Seconds the cast button is given to start a cast before the cast is begun directly.</summary>
    private const double InputGrace = 0.4;

    private enum Gait
    {
        Idle, Walk, Jog, Sprint, StrafeLeft, StrafeRight, Backpedal, Charge, Channel, LookDown, LookUp,
        Diagonal, LookDownWide, SprintLookDown,
    }

    /// <summary>The player on the frame about to be drawn, which is what a capture on the next frame shows.</summary>
    private readonly record struct Sample(
        bool Valid, bool FirstPerson, Vector3 Body, Vector3 BodyForward, Vector3 MeshForward,
        Vector3 Camera, Vector3 CameraForward, Vector3 CapsuleCentre, Vector3? Head, Vector3? Hips,
        Vector3? ChestForward, Vector3 Velocity, float Pitch, bool Sprinting, bool Charging, bool Channeling,
        double FrameSeconds, float Fov, Vector3? Chest, Vector3? Hand, bool HandInFrame, Vector2 HandOnScreen,
        Vector2 Screen, float ArmRaise);

    private ShotLane _lane;
    private bool _prepared;
    private string? _failure;
    private double _stagedAt;
    private double _started = -1;
    private bool _sideView;
    private bool _castBegunDirectly;
    private Gait _gait;
    private StringName[] _held = Array.Empty<StringName>();
    private Sample _sample;
    private Sample _previous;

    private Camera3D? _sideCamera;
    private Node3D? _markers;
    private Skeleton3D? _skeleton;
    private int _head = -1;
    private int _hips = -1;
    private int _shoulderLeft = -1;
    private int _shoulderRight = -1;
    private int _chest = -1;
    private float _baseFov = -1f;

    protected override string Flag => "--camshots";

    protected override string OutputDir => "user://cam_shots";

    // --- the shot list -----------------------------------------------------------------------------

    protected override void BuildTimedShots()
    {
        AddView(firstPerson: false);
        AddView(firstPerson: true);

        TimedShot("side_idle", () => Begin(false, Gait.Idle, side: true), After(0.8), () => Inspect(false, Gait.Idle));
        TimedShot("side_sprint", () => Begin(false, Gait.Sprint, side: true), After(1.2), () => Inspect(false, Gait.Sprint));
    }

    private void AddView(bool firstPerson)
    {
        string view = firstPerson ? "fp" : "tp";
        void Pose(string name, Gait gait, double seconds, int burst = 0) => TimedShot(
            $"{name}_{view}", () => Begin(firstPerson, gait, side: false), After(seconds),
            () => Inspect(firstPerson, gait), burst: burst);

        // One stand, three frames: idle sway is slow, and a single frame of it shows nothing.
        TimedShot($"idle_{view}_1", () => Begin(firstPerson, Gait.Idle, side: false), After(0.3), () => Inspect(firstPerson, Gait.Idle));
        TimedShot($"idle_{view}_2", () => { }, After(1.65), () => Inspect(firstPerson, Gait.Idle), minFrames: 1);
        TimedShot($"idle_{view}_3", () => { }, After(3.0), () => Inspect(firstPerson, Gait.Idle), minFrames: 1);

        Pose("walk", Gait.Walk, 1.2);
        Pose("jog", Gait.Jog, 1.0);
        Pose("sprint", Gait.Sprint, 1.2, burst: 3);
        Pose("strafe_left", Gait.StrafeLeft, 1.0);
        Pose("strafe_right", Gait.StrafeRight, 1.0);
        Pose("backpedal", Gait.Backpedal, 1.0);

        TimedShot($"charge_{view}", () => Begin(firstPerson, Gait.Charge, side: false),
            () => After(0.55)() && Casting() is { IsCharging: true },
            () => Inspect(firstPerson, Gait.Charge), timeout: 8.0);
        TimedShot($"channel_{view}", () => Begin(firstPerson, Gait.Channel, side: false),
            () => After(0.9)() && Casting() is { IsChanneling: true, PendingSpell: null },
            () => Inspect(firstPerson, Gait.Channel), timeout: 8.0);

        Pose("look_down", Gait.LookDown, 0.7);
        Pose("look_up", Gait.LookUp, 0.7);

        if (firstPerson)
        {
            // The two looks down that put the most of the body nearest the camera: the widest view
            // the slider allows, and a sprint, where the torso leans out under the eye.
            Pose("look_down_maxfov", Gait.LookDownWide, 0.7);
            Pose("sprint_look_down", Gait.SprintLookDown, 1.2);
        }
        else
        {
            // Forward and right together: the blend between the run and the strafe.
            Pose("diagonal", Gait.Diagonal, 1.0);
        }
    }

    /// <summary>True once the gait has been running for <paramref name="seconds"/>.</summary>
    private Func<bool> After(double seconds) => () => _failure != null || (_started >= 0 && Clock >= _started + seconds);

    private static SpellcastingComponent? Casting() => ShotStage.Player()?.GetComponent<SpellcastingComponent>();

    protected override string? Fatal(string name)
    {
        if (ShotStage.Player() is not { } player)
        {
            return "player is not registered";
        }

        bool ownCamera = player.GetComponent<PlayerCameraRig>()?.Camera is { Current: true };
        bool sideCamera = _sideCamera != null && IsInstanceValid(_sideCamera) && _sideCamera.Current;
        return ownCamera || sideCamera ? null : "neither the gameplay camera nor the side camera is current";
    }

    // --- staging -----------------------------------------------------------------------------------

    private void Begin(bool firstPerson, Gait gait, bool side)
    {
        _started = -1;
        _failure = null;
        _held = Array.Empty<StringName>();
        _castBegunDirectly = false;
        _gait = gait;
        ShotStage.ReleaseInputs();
        Then(() => ShotStage.Player() != null, () => Stage(firstPerson, gait, side));

        // The gait starts between ticks, so the router sees the cast button's edge (TimedShots.NextFrame).
        Then(
            () => _failure != null || (Clock >= _stagedAt + SettleSeconds && (ShotStage.Player()?.IsOnFloor() ?? true)),
            () => NextFrame(() => Start(gait)));
    }

    private void Stage(bool firstPerson, Gait gait, bool side)
    {
        if (ShotStage.Player() is not { } player ||
            player.GetComponent<PlayerCameraRig>() is not { Camera: { } camera } ||
            player.GetComponent<LocomotionComponent>() is not { } locomotion)
        {
            _failure = "the player, its camera rig or its motor is missing";
            return;
        }

        if (!_prepared)
        {
            Prepare(player);
        }

        ShotStage.ClearSpellNodes(GetTree());
        ShotStage.ResetCaster(player);
        ShotStage.SetHour(ShotHour);
        ShotStage.ClearWeather();
        ShotStage.SetView(player, firstPerson);
        SetFov(gait == Gait.LookDownWide ? WidestFov : _baseFov);
        locomotion.Walking = gait == Gait.Walk;

        // Every gait travels up the lane, so the body is turned to suit: a left strafe faces the
        // lane's right, a backpedal faces back down it.
        Vector3 facing = gait switch
        {
            Gait.StrafeLeft => _lane.Right,
            Gait.StrafeRight => -_lane.Right,
            Gait.Backpedal => -_lane.Forward,
            Gait.Diagonal => (_lane.Forward - _lane.Right).Normalized(),
            _ => _lane.Forward,
        };
        ShotStage.Place(player, ShotStage.OnGround(player, _lane.At(2f)), facing);
        ShotStage.SetPitch(player, 0f);

        if (gait is Gait.Charge or Gait.Channel)
        {
            string id = gait == Gait.Charge ? ChargeSpellId : ChannelSpellId;
            SpellcastingComponent? casting = player.GetComponent<SpellcastingComponent>();
            if (SpellDatabase.Get(id) is not { } spell || casting == null)
            {
                Problem($"{id} or the player's spellbook is missing; the {gait} shot has nothing to cast.");
            }
            else
            {
                casting.Teach(spell);
                if (!casting.Select(spell.Id))
                {
                    Problem($"{id} could not be selected for the {gait} shot.");
                }
            }
        }

        _sideView = side;
        if (_markers != null && IsInstanceValid(_markers))
        {
            _markers.Visible = side;
        }

        if (side && _sideCamera != null && IsInstanceValid(_sideCamera))
        {
            PlaceSideCamera(player);
            _sideCamera.MakeCurrent();
        }
        else
        {
            camera.MakeCurrent();
        }

        _stagedAt = Clock;
    }

    /// <summary>Sets the field of view setting on the live settings (never saved) and announces it,
    /// which is how the rig picks it up. A negative value means the setting was never read.</summary>
    private static void SetFov(float degrees)
    {
        if (degrees <= 0f || LiveSettings() is not { } settings ||
            Mathf.IsEqualApprox(settings.Current.FieldOfView, degrees))
        {
            return;
        }

        settings.Current.FieldOfView = degrees;
        EventBus.Instance?.Publish(new SettingsAppliedEvent(settings.Current));
    }

    private static SettingsService? LiveSettings() =>
        ServiceLocator.Instance is { } locator && locator.TryGet(out SettingsService settings) ? settings : null;

    private void Prepare(PlayerCharacter player)
    {
        _prepared = true;
        _baseFov = LiveSettings()?.Current.FieldOfView ?? -1f;
        ShotStage.PreparePlayer(player);
        _lane = ShotStage.FindLane(player, LaneLength, LaneHalfWidth);
        string ground = $"ground at {_lane.Start:0.0} heading {_lane.Forward:0.00}, level within {_lane.Spread:0.00} m";
        if (_lane.Clear)
        {
            Log.Info($"{Flag}: {ground}; {ShotStage.ControlState()}.");
        }
        else
        {
            Log.Warn($"{Flag}: no level, open strip within reach of the save; using the best found ({ground}). " +
                     "A gait may run into scenery or up a slope.");
        }

        if (GetTree().CurrentScene is { } scene)
        {
            _sideCamera = new Camera3D { Name = "CamShotSideCamera", Fov = 40f, Near = 0.1f };
            scene.AddChild(_sideCamera);
        }

        _markers = BuildMarkers(player);
        player.AddChild(_markers);
    }

    /// <summary>The collision capsule as a translucent shell, and an arrow at chest height along the
    /// way the capsule faces. Shown only for the side shots: where the drawn body is against them is
    /// what those shots are for.</summary>
    private static Node3D BuildMarkers(PlayerCharacter player)
    {
        float radius = 0.4f;
        float height = 1.8f;
        if (player.GetNodeOrNull<CollisionShape3D>("Collision")?.Shape is CapsuleShape3D capsule)
        {
            radius = capsule.Radius;
            height = capsule.Height;
        }

        var root = new Node3D { Name = "CamShotMarkers", Visible = false };
        root.AddChild(new MeshInstance3D
        {
            Name = "Capsule",
            Mesh = new CapsuleMesh { Radius = radius, Height = height },
            Position = new Vector3(0f, height * 0.5f, 0f),
            MaterialOverride = Flat(new Color(0.2f, 0.8f, 1f, 0.22f)),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });

        StandardMaterial3D yellow = Flat(new Color(1f, 0.85f, 0.1f));
        root.AddChild(new MeshInstance3D
        {
            Name = "FacingShaft",
            Mesh = new BoxMesh { Size = new Vector3(0.05f, 0.05f, 1.4f) },
            Position = new Vector3(0f, height * 0.75f, -(radius + 0.7f)),
            MaterialOverride = yellow,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
        root.AddChild(new MeshInstance3D
        {
            Name = "FacingTip",
            Mesh = new BoxMesh { Size = new Vector3(0.16f, 0.16f, 0.16f) },
            Position = new Vector3(0f, height * 0.75f, -(radius + 1.4f)),
            MaterialOverride = yellow,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
        return root;
    }

    private static StandardMaterial3D Flat(Color color) => new()
    {
        AlbedoColor = color,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        Transparency = color.A < 1f ? BaseMaterial3D.TransparencyEnum.Alpha : BaseMaterial3D.TransparencyEnum.Disabled,
        CullMode = BaseMaterial3D.CullModeEnum.Disabled,
    };

    /// <summary>Square on to the lane from its right, level with the chest, following the body.</summary>
    private void PlaceSideCamera(PlayerCharacter player)
    {
        if (_sideCamera == null || !IsInstanceValid(_sideCamera))
        {
            return;
        }

        Vector3 focus = player.GlobalPosition + (Vector3.Up * 0.95f);
        _sideCamera.GlobalPosition = focus + (_lane.Right * SideDistance) + (Vector3.Up * 0.15f);
        _sideCamera.LookAt(focus, Vector3.Up);
    }

    /// <summary>Runs between ticks: holds the actions the gait is made of.</summary>
    private void Start(Gait gait)
    {
        if (ShotStage.Player() is not { } player)
        {
            return;
        }

        switch (gait)
        {
            case Gait.Walk:
            case Gait.Jog:
                _held = new[] { InputActions.MoveForward };
                break;
            case Gait.Sprint:
                _held = new[] { InputActions.MoveForward, InputActions.Sprint };
                break;
            case Gait.Diagonal:
                _held = new[] { InputActions.MoveForward, InputActions.MoveRight };
                break;
            case Gait.SprintLookDown:
                _held = new[] { InputActions.MoveForward, InputActions.Sprint };
                ShotStage.SetPitch(player, -Mathf.Pi * 0.5f);
                break;
            case Gait.StrafeLeft:
                _held = new[] { InputActions.MoveLeft };
                break;
            case Gait.StrafeRight:
                _held = new[] { InputActions.MoveRight };
                break;
            case Gait.Backpedal:
                _held = new[] { InputActions.MoveBack };
                break;
            case Gait.Charge:
            case Gait.Channel:
                Godot.Input.ActionPress(InputActions.Cast);
                break;
            case Gait.LookDown:
            case Gait.LookDownWide:
                ShotStage.SetPitch(player, -Mathf.Pi * 0.5f);
                break;
            case Gait.LookUp:
                ShotStage.SetPitch(player, Mathf.Pi * 0.5f);
                break;
        }

        foreach (StringName action in _held)
        {
            Godot.Input.ActionPress(action);
        }

        _started = Clock;
    }

    /// <summary>Keeps the gait's movement actions down. The engine lets go of every pressed action
    /// when the window loses focus, and a run that someone clicks away from would otherwise stand
    /// still for the rest of its shots. The cast button is left alone: pressing it again is a new cast.</summary>
    protected override void Frame(double delta)
    {
        if (_started < 0)
        {
            return;
        }

        foreach (StringName action in _held)
        {
            if (!Godot.Input.IsActionPressed(action))
            {
                Godot.Input.ActionPress(action);
            }
        }
    }

    /// <summary>The cast button did not start the charge or the channel (its edge never reached the
    /// router): the cast is begun on the spellbook itself. The button is still down, so the router
    /// goes on updating it as it does a cast it began.</summary>
    protected override void PhysicsTick(double delta)
    {
        if (_castBegunDirectly || _started < 0 || _gait is not (Gait.Charge or Gait.Channel) ||
            Clock < _started + InputGrace || Casting() is not { SelectionLocked: false } casting)
        {
            return;
        }

        _castBegunDirectly = true;
        Log.Warn($"{Flag}: '{CurrentShot}': the cast button did not start a cast ({ShotStage.ControlState()}); beginning it directly.");
        if (!Godot.Input.IsActionPressed(InputActions.Cast))
        {
            NextFrame(() => Godot.Input.ActionPress(InputActions.Cast));
        }

        casting.BeginCast();
    }

    // --- what each frame showed --------------------------------------------------------------------

    protected override void PreDraw()
    {
        _previous = _sample;
        _sample = default;
        if (ShotStage.Player() is not { } player ||
            player.GetComponent<PlayerCameraRig>() is not { Camera: { } camera, CameraPivot: { } pivot } rig)
        {
            return;
        }

        if (_sideView)
        {
            PlaceSideCamera(player);
        }

        ResolveBones(player);
        float height = player.GetNodeOrNull<CollisionShape3D>("Collision")?.Shape is CapsuleShape3D capsule ? capsule.Height : 1.8f;
        Vector3? left = Bone(_shoulderLeft);
        Vector3? right = Bone(_shoulderRight);
        Vector3? chest = left is { } l && right is { } r && (r - l).LengthSquared() > 1e-4f
            ? Vector3.Up.Cross(r - l)
            : null;
        SpellcastingComponent? casting = player.GetComponent<SpellcastingComponent>();

        // The casting hand as it is drawn: the animation component answers with the first-person
        // arm's hand while that is up, and with the bone otherwise.
        Vector3? hand = null;
        bool handInFrame = false;
        Vector2 handOnScreen = Vector2.Zero;
        if (player.GetComponent<CharacterAnimationComponent>() is { } animation &&
            animation.TryGetCastingHand(out Vector3 handAt))
        {
            hand = handAt;
            if (camera.Current && camera.IsInsideTree())
            {
                handInFrame = camera.IsPositionInFrustum(handAt);
                handOnScreen = camera.UnprojectPosition(handAt);
            }
        }

        _sample = new Sample(
            Valid: true,
            FirstPerson: rig.IsFirstPerson,
            Body: player.GlobalPosition,
            BodyForward: -player.GlobalBasis.Z,
            MeshForward: player.GetNodeOrNull<Node3D>("BodyMesh")?.GlobalBasis.Z ?? -player.GlobalBasis.Z,
            Camera: camera.GlobalPosition,
            CameraForward: -camera.GlobalBasis.Z,
            CapsuleCentre: player.GlobalPosition + (Vector3.Up * (height * 0.5f)),
            Head: Bone(_head),
            Hips: Bone(_hips),
            ChestForward: chest,
            Velocity: player.Velocity,
            Pitch: pivot.Rotation.X,
            Sprinting: player.GetComponent<LocomotionComponent>() is { IsSprinting: true },
            Charging: casting is { IsCharging: true },
            Channeling: casting is { IsChanneling: true },
            FrameSeconds: GetProcessDeltaTime(),
            Fov: camera.Fov,
            Chest: Bone(_chest),
            Hand: hand,
            HandInFrame: handInFrame,
            HandOnScreen: handOnScreen,
            Screen: camera.GetViewport()?.GetVisibleRect().Size ?? Vector2.Zero,
            ArmRaise: player.GetComponent<FirstPersonArmComponent>()?.Raise ?? 0f);
    }

    private void ResolveBones(PlayerCharacter player)
    {
        Skeleton3D? skeleton = player.GetComponent<CharacterAnimationComponent>()?.Skeleton;
        if (ReferenceEquals(skeleton, _skeleton))
        {
            return;
        }

        _skeleton = skeleton;
        _head = _hips = _shoulderLeft = _shoulderRight = _chest = -1;
        if (skeleton != null)
        {
            _chest = skeleton.FindBone("UpperChest");
            if (_chest < 0)
            {
                _chest = skeleton.FindBone("Chest");
            }

            _head = EquipmentSockets.Resolve(skeleton, EquipmentSocket.Head);
            _hips = EquipmentSockets.Resolve(skeleton, EquipmentSocket.Hips);
            _shoulderLeft = EquipmentSockets.Resolve(skeleton, EquipmentSocket.ShoulderL);
            _shoulderRight = EquipmentSockets.Resolve(skeleton, EquipmentSocket.ShoulderR);
        }
    }

    private Vector3? Bone(int index) =>
        _skeleton != null && IsInstanceValid(_skeleton) && _skeleton.IsInsideTree() && index >= 0 && index < _skeleton.GetBoneCount()
            ? (_skeleton.GlobalTransform * _skeleton.GetBoneGlobalPose(index)).Origin
            : null;

    private void Inspect(bool firstPerson, Gait gait)
    {
        Sample s = _sample;
        Log.Info($"{Flag}: shot {CurrentShot} view={(firstPerson ? "fp" : "tp")} gait={gait} {Describe(s)}");

        if (_failure != null)
        {
            Problem($"'{CurrentShot}': {_failure}.");
            return;
        }

        if (!s.Valid)
        {
            Problem($"'{CurrentShot}': the player could not be sampled on the captured frame.");
            return;
        }

        if (s.FirstPerson != firstPerson)
        {
            Problem($"'{CurrentShot}': the view is {(s.FirstPerson ? "first" : "third")} person, not the one asked for.");
        }

        float speed = new Vector2(s.Velocity.X, s.Velocity.Z).Length();
        string? wrong = gait switch
        {
            Gait.Walk or Gait.Jog or Gait.StrafeLeft or Gait.StrafeRight or Gait.Backpedal or Gait.Diagonal when speed < 0.5f =>
                $"the body is not moving (speed {speed:0.00} m/s; {ShotStage.ControlState()})",
            Gait.Sprint or Gait.SprintLookDown when !s.Sprinting =>
                $"the body is not sprinting (speed {speed:0.00} m/s; {ShotStage.ControlState()})",
            Gait.Charge when !s.Charging => $"no spell is being charged ({ShotStage.ControlState()})",
            Gait.Channel when !s.Channeling => $"no spell is being channelled ({ShotStage.ControlState()})",
            Gait.LookDown or Gait.LookDownWide or Gait.SprintLookDown when s.Pitch > -0.5f =>
                $"the view is pitched {Mathf.RadToDeg(s.Pitch):0} degrees, not down",
            Gait.LookDownWide when s.Fov < WidestFov - 1f =>
                $"the field of view is {s.Fov:0} degrees, not the widest ({WidestFov:0})",
            Gait.LookUp when s.Pitch < 0.5f => $"the view is pitched {Mathf.RadToDeg(s.Pitch):0} degrees, not up",
            _ => null,
        };
        if (wrong != null)
        {
            Problem($"'{CurrentShot}': {wrong}.");
        }

        // The visible body against the capsule. Half a turn out is a body drawn back to front: it
        // faces the third-person camera, and in first person its back is where its chest should be
        // and its arms are behind the eye.
        float meshTurn = Mathf.RadToDeg(Flatten(s.BodyForward).SignedAngleTo(Flatten(s.MeshForward), Vector3.Up));
        if (Mathf.Abs(meshTurn) > MaxMeshTurn)
        {
            Problem($"'{CurrentShot}': the visible body is turned {meshTurn:0} degrees from the way the capsule faces " +
                    "(drawn back to front).");
        }

        // Standing and looking straight down, the eye has to be out in front of the chest, or the
        // frame is the player's own torso.
        if (firstPerson && gait is Gait.LookDown or Gait.LookDownWide && EyeAheadOfChest(s) is { } ahead &&
            ahead < MinEyeAheadOfChest)
        {
            Problem($"'{CurrentShot}': looking down, the eye is {ahead:0.00} m ahead of the chest bone " +
                    $"(at least {MinEyeAheadOfChest:0.00} m is needed to clear the chest).");
        }

        // A spell held in first person is held where the player can see it.
        if (firstPerson && gait is Gait.Charge or Gait.Channel && !s.HandInFrame)
        {
            Problem($"'{CurrentShot}': the casting hand is not in the first-person frame ({HandText(s)}).");
        }
    }

    /// <summary>How far the camera is ahead of the chest bone along the capsule's facing, in metres.</summary>
    private static float? EyeAheadOfChest(Sample s) =>
        s.Chest is { } chest ? (s.Camera - chest).Dot(Flatten(s.BodyForward)) : null;

    private static string HandText(Sample s)
    {
        if (s.Hand is not { } hand)
        {
            return "no hand bone";
        }

        string where = s.Screen.X > 1f && s.Screen.Y > 1f
            ? $"at {N(s.HandOnScreen.X / s.Screen.X, "0.00")},{N(s.HandOnScreen.Y / s.Screen.Y, "0.00")} of the frame"
            : "frame size unknown";
        return $"{(s.HandInFrame ? "in frame" : "OUT of frame")} {where}, {N(hand.DistanceTo(s.Camera), "0.00")} m from the camera, " +
               $"arm_raise={N(s.ArmRaise, "0.00")}";
    }

    /// <summary>The three frames before a sprint shot: how far the camera and the body each moved since
    /// the frame before. A surge is the camera's step departing from the body's.</summary>
    protected override void BurstFrame(string name)
    {
        Sample s = _sample;
        if (!s.Valid || !_previous.Valid)
        {
            Log.Info($"{Flag}: frame {name} (not sampled)");
            return;
        }

        float cameraStep = s.Camera.DistanceTo(_previous.Camera);
        float bodyStep = s.Body.DistanceTo(_previous.Body);
        Log.Info($"{Flag}: frame {name} dt={N(s.FrameSeconds * 1000.0, "0.0")}ms camera_step={N(cameraStep, "0.000")} " +
                 $"body_step={N(bodyStep, "0.000")} camera_minus_body_step={N(cameraStep - bodyStep, "+0.000;-0.000")} {Describe(s)}");
    }

    private static string Describe(Sample s)
    {
        if (!s.Valid)
        {
            return "(not sampled)";
        }

        Vector3 forward = Flatten(s.BodyForward);
        Vector3 right = forward.Cross(Vector3.Up);
        string cameraFromHead = "no head bone";
        if (s.Head is { } head)
        {
            Vector3 offset = s.Camera - head;
            cameraFromHead = $"({Signed(offset.Dot(right))},{Signed(offset.Y)},{Signed(offset.Dot(forward))}) " +
                             $"dist={N(offset.Length(), "0.00")}";
        }

        string hips = "no hips bone";
        if (s.Hips is { } hipsAt)
        {
            Vector3 offset = hipsAt - s.CapsuleCentre;
            hips = $"({Signed(offset.Dot(right))},{Signed(offset.Dot(forward))}) " +
                   $"horiz={N(new Vector2(offset.X, offset.Z).Length(), "0.00")} dy={Signed(offset.Y)}";
        }

        string cameraFromChest = "no chest bone";
        if (s.Chest is { } chestAt)
        {
            Vector3 offset = s.Camera - chestAt;
            cameraFromChest = $"({Signed(offset.Dot(right))},{Signed(offset.Y)},{Signed(offset.Dot(forward))})";
        }

        float speed = new Vector2(s.Velocity.X, s.Velocity.Z).Length();
        return $"camera_from_head(right,up,forward)={cameraFromHead} " +
               $"camera_from_chest(right,up,forward)={cameraFromChest} " +
               $"fov={N(s.Fov, "0.0")} casting_hand=[{HandText(s)}] " +
               $"body_heading={N(Mathf.RadToDeg(Mathf.Atan2(-forward.X, -forward.Z)), "0.0")} " +
               $"mesh_vs_body={Turn(forward, s.MeshForward)} " +
               $"chest_vs_body={(s.ChestForward is { } chest ? Turn(forward, chest) : "no shoulder bones")} " +
               $"camera_vs_body={Turn(forward, s.CameraForward)} " +
               $"hips_from_capsule(right,forward)={hips} " +
               $"speed={N(speed, "0.00")} pitch={N(Mathf.RadToDeg(s.Pitch), "0.0")} sprinting={s.Sprinting}";
    }

    private static Vector3 Flatten(Vector3 v)
    {
        var flat = new Vector3(v.X, 0f, v.Z);
        return flat.LengthSquared() < 1e-6f ? Vector3.Forward : flat.Normalized();
    }

    /// <summary>Degrees <paramref name="other"/> is turned from <paramref name="forward"/> about the
    /// vertical, signed (positive is to the left, anticlockwise from above).</summary>
    private static string Turn(Vector3 forward, Vector3 other) =>
        N(Mathf.RadToDeg(forward.SignedAngleTo(Flatten(other), Vector3.Up)), "+0.0;-0.0");

    private static string Signed(float value) => N(value, "+0.00;-0.00");

    private static string N(double value, string format) => value.ToString(format, CultureInfo.InvariantCulture);

    public override void _ExitTree()
    {
        base._ExitTree();
        ShotStage.ReleaseInputs();
    }
}
