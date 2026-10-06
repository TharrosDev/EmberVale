using System;
using System.Collections.Generic;
using Embervale.Appearance;
using Embervale.Combat;
using Embervale.Core;
using Embervale.Core.Events;
using Embervale.Dialogue;
using Embervale.Core.Services;
using Embervale.Entities;
using Embervale.Settings;
using Godot;

namespace Embervale.Player;

/// <summary>
/// Where the camera sits, and which of the two views the game is in.
///
/// <para>The game is <b>hybrid</b>: the same controls drive first person and an over-the-shoulder
/// third person, swapped at any time from the settings panel or the toggle-camera key. Body yaw
/// always equals camera yaw in both modes, and pitch lives on the pivot in both, so a swap keeps the
/// look direction by construction and combat, lock-on, dodge and melee reach are mode-agnostic — the
/// only things that differ are where the camera sits and that third person aims from the camera
/// rather than the head so the crosshair still means something.</para>
///
/// <para><b>First person is TRUE first person.</b> The body stays visible; you see its arms, its
/// weapon and its equipment because they are the same arms, weapon and equipment the world sees. The
/// eye sits where the head RESTS, on a neck that pitches with the look, and takes only a little of
/// the head's animated travel (<see cref="CameraRigMath.EyeLocal"/>); the head itself is cut out of
/// the body by its shader while the camera is inside it, and still casts its shadow.</para>
///
/// <para><b>This component is the only writer of the camera's position, rotation and field of view.</b>
/// Each frame it composes: the mode blend (a spring, so a toggle pressed mid-swap turns around
/// smoothly), the context profile (eased asymmetrically, leaning by speed), the player's own
/// settings, the wall spring, and the summed nudge of every <see cref="ICameraLayer"/> on the entity.
/// Layers ask for framing; they never touch the transform. The pure arithmetic is in
/// <see cref="CameraRigMath"/>, which is engine-free and unit-tested; what is left here is the node
/// writes and the physics sweeps.</para>
/// </summary>
[GlobalClass]
public partial class PlayerCameraRig : EntityComponent
{
    /// <summary>Seconds the mode blend takes to settle (about 95% of the way) between the two views.</summary>
    private const float ModeSettleSeconds = 0.4f;

    /// <summary>Radius of the sphere swept from the pivot to the camera. Bigger than the camera's
    /// near plane so a corner can never poke inside it.</summary>
    private const float CameraProbeRadius = 0.22f;

    /// <summary>Radius of the sphere swept from the pivot to the eye in first person. Smaller: it only
    /// has to keep the near plane out of a wall when the head leans into one.</summary>
    private const float EyeProbeRadius = 0.08f;

    /// <summary>Seconds of clear space before the wall spring lets the camera start easing back out
    /// after geometry stops crowding it. Pulling in is instant; see
    /// <see cref="CameraRigMath.SpringStep"/>.</summary>
    private const float PushHoldSeconds = 0.25f;

    /// <summary>Push-out rates, in fractions of full extension per second: the seat as a whole, the
    /// shoulder and rise squeezes, and the first-person eye.</summary>
    private const float SeatPushOutPerSecond = 1.6f;
    private const float SqueezePushOutPerSecond = 2.5f;
    private const float EyePushOutPerSecond = 4f;

    /// <summary>Seconds the camera takes to cross to the other shoulder.</summary>
    private const float ShoulderSwapSeconds = 0.15f;

    /// <summary>The degrees a dodge adds to the field of view at full FOV Kick, and how fast the punch
    /// lands and bleeds off (seconds).</summary>
    private const float DodgeFovPunch = 5f;
    private const float PunchRiseSeconds = 0.05f;
    private const float PunchFallSeconds = 0.3f;

    /// <summary>Seconds of smoothing on the head's animated travel before the eye takes its share
    /// of it. The head bone is animated, so following it raw hands the player every footfall and
    /// every swing as camera shake.</summary>
    private const float EyeSmoothSeconds = 0.1f;

    /// <summary>Seconds of smoothing on the eye's rest anchor.</summary>
    private const float RestSmoothSeconds = 0.06f;

    /// <summary>Seconds the eye takes to settle into (and out of) the seated pose on a mount.</summary>
    private const float MountedEyeSeconds = 0.25f;

    /// <summary>How often, while the head is cut out, the head socket is looked over again for a
    /// helm put on since (seconds).</summary>
    private const double HeadAttachmentRescanSeconds = 0.5d;

    /// <summary>0 = first person, 1 = third person, sprung toward <see cref="_modeTarget"/>.</summary>
    private float _modeBlend;

    private float _modeVelocity;

    /// <summary>The blend target (0/1). Third person is only targeted once the seat has room.</summary>
    private float _modeTarget;

    /// <summary>Whether the player has asked for third person. It can be true while the camera is
    /// still in first person, waiting for a seat with room in it.</summary>
    private bool _wantThird;

    /// <summary>The wall spring: the fraction of the seat, of the shoulder offset and of the rise the
    /// sweeps currently allow, each with the timer its delayed push-out runs on.</summary>
    private float _seatFraction = 1f;
    private float _lateralFraction = 1f;
    private float _riseFraction = 1f;
    private float _seatClear;
    private float _lateralClear;
    private float _riseClear;

    /// <summary>The first-person eye's own guard against the head leaning into a wall.</summary>
    private float _eyeGuard = 1f;
    private float _eyeClear;

    /// <summary>Whether the camera is over the shoulder the player did NOT choose, because that one is
    /// against a wall; how far across it currently is (0..1); and seconds since it last swapped.</summary>
    private bool _shoulderSwapped;
    private float _swapBlend;
    private float _sinceSwap = CameraRigMath.SwapHoldSeconds;

    /// <summary>The camera's current shape, eased toward whatever the context and speed ask for, and
    /// the time constant (seconds) it is easing with — chosen when the context changes, since going
    /// into a context and coming out of one deliberately take different times.</summary>
    private CameraProfile _profile = CameraProfile.Neutral;
    private float _profileSeconds = CameraProfile.Neutral.BlendSeconds;

    private CameraInputs _inputs = CameraInputs.Idle;
    private float _dodgePunch;

    /// <summary>Degrees added to the player's FOV setting this frame (profile, dodge punch, layers).</summary>
    private float _fovOffset;

    private float _pitchLimit = CameraProfile.Neutral.PitchLimit;
    private float _lookScale = 1f;

    private readonly List<ICameraLayer> _layers = new();
    private CameraNudge[] _samples = Array.Empty<CameraNudge>();
    private int _hostChildCount = -1;

    private Skeleton3D? _skeleton;
    private int _headBone = -1;
    private Vector3 _headRest;
    private Vector3 _restHead;
    private Vector3 _eyeLocal;
    private Vector3 _eyeDelta;
    private float _mountedEye;
    private bool _eyeSeeded;

    /// <summary>The third-person seat and the layers' nudge from the last <see cref="Tick"/>, kept so
    /// the per-frame eye update can rewrite the camera between ticks.</summary>
    private Vector3 _third;
    private CameraNudge _nudge = CameraNudge.Identity;

    /// <summary>True while <see cref="_Process"/> is advancing the eye every drawn frame, which is
    /// the normal case in play. <see cref="Tick"/> then leaves the eye's smoothing alone, so it is
    /// stepped exactly once per frame rather than once per frame and again per physics tick.</summary>
    private bool _frameDriven;

    /// <summary>The body's own surfaces (the ones drawn with the player-body shader), the pieces
    /// hung on the head socket with the shadow setting each had, and whether the head is currently
    /// cut out.</summary>
    private readonly List<MeshInstance3D> _bodySurfaces = new();
    private readonly Dictionary<GeometryInstance3D, GeometryInstance3D.ShadowCastingSetting> _headPieces = new();
    private bool _headHidden;
    private double _headRescan;
    private Vector3 _cameraRest = Vector3.Zero;
    private float _pitch;
    private PlayerPhysicsQueries? _queries;
    private SettingsService? _settings;

    /// <summary>Pitch node (rotated up/down). The camera is its child. Injected by
    /// <see cref="PlayerFactory"/> so the component does not assume a scene path.</summary>
    public Node3D? CameraPivot { get; set; }

    /// <summary>The player camera, injected by <see cref="PlayerFactory"/> so the rig can move it
    /// between the eye and the over-the-shoulder orbit.</summary>
    public Camera3D? Camera { get; set; }

    /// <summary>What the camera is currently for, resolved each frame from what the router fed the
    /// rig. Read-only: nothing sets a context, so two systems cannot disagree about it.</summary>
    public CameraContext Context { get; private set; } = CameraContext.Exploration;

    /// <summary>Whether gameplay is currently first-person (the shipping default). True while a swap
    /// to third person is being held for room: this is where the camera is going, not where the
    /// player asked for.</summary>
    public bool IsFirstPerson { get; private set; } = true;

    /// <summary>The camera's live rest position — the single source of truth shared with
    /// <see cref="CameraShake"/>, which offsets around it per frame. It follows the mode blend and
    /// the wall spring but not the layers, so whatever is shaking the camera shakes around where the
    /// camera actually is, not where the mode says it should be (the "camera glitches into the head
    /// on a crit while third-person" bug).</summary>
    public Vector3 CameraRestPosition => _cameraRest;

    /// <summary>How much of its normal rate the look should turn at, so a narrowed view (aiming) turns
    /// through the same part of the screen per movement rather than the same angle. Cached each
    /// frame, so reading it never touches the camera node.</summary>
    public float LookScale => _lookScale;

    protected override void OnInitialize()
    {
        _queries = Entity!.GetComponent<PlayerPhysicsQueries>();
        _settings = ServiceLocator.Instance is { } locator && locator.TryGet(out SettingsService settings)
            ? settings
            : null;

        EventBus.Instance?.Subscribe<SettingsAppliedEvent>(OnSettingsApplied);
        EventBus.Instance?.Subscribe<DialogueStartedEvent>(OnDialogueStarted);
        EventBus.Instance?.Subscribe<DialogueEndedEvent>(OnDialogueEnded);

        // A dialogue pauses the tree, and a paused node does not process. Always keeps _Process alive
        // for the one case in the gate below; while the tree runs it does nothing.
        ProcessMode = ProcessModeEnum.Always;
        ApplyFieldOfView(_settings?.Current);
        SetFirstPerson(!(_settings?.Current.ThirdPersonCamera ?? false), immediate: true);
    }

    protected override void OnTeardown()
    {
        EventBus.Instance?.Unsubscribe<SettingsAppliedEvent>(OnSettingsApplied);
        EventBus.Instance?.Unsubscribe<DialogueStartedEvent>(OnDialogueStarted);
        EventBus.Instance?.Unsubscribe<DialogueEndedEvent>(OnDialogueEnded);
        RestoreHeadPieces();
        _bodySurfaces.Clear();
        _skeleton = null;
    }

    /// <summary>The look's pitch in radians, positive up. Read by the camera probe.</summary>
    public float Pitch => _pitch;

    /// <summary>Whether the player's own head is currently cut out of the body (and the pieces on
    /// its head socket drawn as shadows only). Read by the camera probe.</summary>
    public bool HeadHidden => _headHidden;

    /// <summary>World-space centre and radius of the head cut-out as last computed. Read by the
    /// camera probe.</summary>
    public Vector3 HeadSphereCentre { get; private set; }

    public float HeadSphereRadius => CameraRigMath.HeadSphereRadius;

    /// <summary>True from a conversation opening to it closing. Set by the dialogue events rather
    /// than read off the panel, so the rig does not know the UI exists. Public so the camera probe
    /// can stand in for the events, which a script cannot publish.</summary>
    public bool DialogueOpen { get; set; }

    private void OnDialogueStarted(DialogueStartedEvent e)
    {
        if (ReferenceEquals(e.Player, Entity))
        {
            DialogueOpen = true;
        }
    }

    private void OnDialogueEnded(DialogueEndedEvent e)
    {
        if (ReferenceEquals(e.Player, Entity))
        {
            DialogueOpen = false;
        }
    }

    /// <summary>
    /// Keeps the camera alive during a dialogue. The router, the rig's usual caller, does not run
    /// while the dialogue pauses the tree, so the special-view layer's push-in would never step.
    /// Fed <see cref="CameraInputs.Idle"/> because the router is not reading the player: the last
    /// frame's sprint or fight would otherwise keep framing and bobbing the camera through a
    /// conversation, and the profile eases back to exploration instead.
    ///
    /// <para>In ordinary play it does the other per-frame job: the first-person eye. The router ticks
    /// the rig at the physics rate, but the body is animated and the look is turned every drawn
    /// frame, so an eye placed only on physics ticks stepped against both. The eye's share of the
    /// head's travel, the camera's seat for the current pitch and the head cut-out are redone here
    /// each frame; the sweeps, the profile and the mode blend stay on the tick.</para>
    /// </summary>
    public override void _Process(double delta)
    {
        bool playing = GameManager.Instance is { IsPlaying: true };
        bool paused = GetTree().Paused;
        if (!GodotObject.IsInstanceValid(Camera) || !GodotObject.IsInstanceValid(CameraPivot))
        {
            _frameDriven = false;
            return;
        }

        if (CameraRigMath.TicksWhilePaused(DialogueOpen, paused, playing))
        {
            _frameDriven = false;
            Feed(CameraInputs.Idle);
            Tick(delta);
            return;
        }

        // The same condition the input router ticks under. Outside it the injected nodes may be on
        // their way out (a world teardown, a save/load rebuild) and are not touched.
        if (!playing || paused)
        {
            _frameDriven = false;
            return;
        }

        _frameDriven = true;
        if (_modeBlend < 1f)
        {
            Vector3 eye = EyeOffset((float)delta, advance: true) * _eyeGuard;
            ApplyCameraRest(CameraRigMath.Blend(eye, _third, _modeBlend), _nudge);
        }

        UpdateHeadCutout(delta);
    }

    /// <summary>What the input router read off the player this frame. Stored, not acted on: the rig
    /// resolves it inside <see cref="Tick"/>, which runs inside the router's not-playing guard.</summary>
    public void Feed(in CameraInputs inputs) => _inputs = inputs;

    /// <summary>
    /// Asks for first person or over-the-shoulder third person. The swap is a spring, so it can be
    /// reversed at any moment and turns around smoothly; the look direction is untouched because yaw
    /// is the body's and pitch is the pivot's in both views.
    ///
    /// <para>First person is granted at once. Third person is granted by <see cref="Tick"/> once the
    /// seat has room (<see cref="CameraRigMath.SeatUsable"/>), so a swap requested in a closet is held
    /// in first person and completes the moment the player steps out, rather than putting the camera
    /// against the back of the head.</para>
    ///
    /// <paramref name="immediate"/> snaps rather than blends and skips the room check — used on
    /// initialize so a save resumed in third person opens there instead of swooping out on the first
    /// frame; the wall spring pulls in on that frame if it has to.
    /// </summary>
    public void SetFirstPerson(bool firstPerson, bool immediate = false)
    {
        _wantThird = !firstPerson;
        if (firstPerson)
        {
            IsFirstPerson = true;
            _modeTarget = 0f;
        }

        if (!immediate)
        {
            return;
        }

        IsFirstPerson = firstPerson;
        _modeTarget = firstPerson ? 0f : 1f;
        _modeBlend = _modeTarget;
        _modeVelocity = 0f;
        _shoulderSwapped = false;
        _swapBlend = 0f;
        _seatFraction = _lateralFraction = _riseFraction = 1f;
        _seatClear = _lateralClear = _riseClear = 0f;
        ApplyCameraRest(firstPerson ? Vector3.Zero : FullSeat(1f, 1f), CameraNudge.Identity);
    }

    /// <summary>Flips the camera mode through the <em>setting</em>, so the toggle key and the
    /// settings panel can never disagree and the choice persists across sessions. <c>Apply</c>
    /// publishes <see cref="SettingsAppliedEvent"/>, which is what actually calls
    /// <see cref="SetFirstPerson"/> — the same path the panel's toggle takes.</summary>
    public void ToggleMode()
    {
        if (_settings == null)
        {
            // No settings service (a bare test harness): flip locally so the key still works.
            SetFirstPerson(_wantThird);
            return;
        }

        _settings.Current.ThirdPersonCamera = !_settings.Current.ThirdPersonCamera;
        _settings.Apply();
        _settings.Save();
    }

    /// <summary>
    /// One frame of the camera: resolve the context and ease the profile, sample the layers, run the
    /// wall spring, spring the mode blend, and write the camera. Everything here dereferences the
    /// injected nodes, so it runs only from inside the input router's not-playing guard.
    /// </summary>
    public void Tick(double delta)
    {
        float dt = (float)delta;
        ResolveHead();
        ResolveLayers();

        // The profile leans the camera toward what the player is doing. Eased, because a context
        // change that cut between framings would be worse than having no profiles at all — and
        // eased at a rate chosen per transition, because going into a context and coming out of it
        // should not take the same time.
        CameraContext wanted = _inputs.Context;
        if (wanted != Context)
        {
            _profileSeconds = CameraProfile.TransitionSeconds(Context, wanted);
            Context = wanted;
        }

        CameraProfile target = CameraProfile.ForSpeed(Context, _inputs.Speed01);
        _profile = CameraProfile.Blend(_profile, target, CameraRigMath.Damp(dt, _profileSeconds));

        CameraNudge nudge = SampleLayers(dt);

        Vector3 third = UpdateSeat(dt, nudge.DistanceScale);

        // Third person is granted once the seat has room, and never withdrawn by this check: once the
        // swap has begun the wall spring owns the camera, and re-testing every frame would flip a
        // borderline seat in and out of the swap.
        if (_wantThird && _modeTarget < 1f && CameraRigMath.SeatUsable(third.Length()))
        {
            _modeTarget = 1f;
            IsFirstPerson = false;
        }

        CameraRigMath.SpringBlend(ref _modeBlend, ref _modeVelocity, _modeTarget, dt, ModeSettleSeconds);

        UpdatePitch(dt);
        UpdateFieldOfView(dt, nudge.FovOffset);

        // The eye anchor crossfade: the first-person eye at blend 0, the fixed-pivot seat at 1. Both
        // ends are continuous in the blend, so a swap in either direction, or reversed halfway, has
        // nothing to pop. The eye's smoothing is stepped here only when no drawn frame is doing it
        // (a dialogue's paused tick, a probe driving the rig by hand).
        _third = third;
        _nudge = nudge;
        Vector3 eye = EyeOffset(dt, advance: !_frameDriven);
        ApplyCameraRest(CameraRigMath.Blend(eye * GuardEye(dt), third, _modeBlend), nudge);
        if (!_frameDriven)
        {
            UpdateHeadCutout(delta);
        }
    }

    /// <summary>Finds the entity's camera layers, re-finding them when the component set changes.
    /// Components are children of the body, so a changed child count is the cheap signal. The
    /// entity's <c>GetComponents</c> is constrained to <see cref="EntityComponent"/>, which an
    /// interface is not, so this walks the same children.</summary>
    private void ResolveLayers()
    {
        Node host = Entity!.Body;
        int count = host.GetChildCount();
        if (count == _hostChildCount)
        {
            return;
        }

        _hostChildCount = count;
        _layers.Clear();
        foreach (Node child in host.GetChildren())
        {
            if (child is ICameraLayer layer)
            {
                _layers.Add(layer);
            }
        }

        if (_samples.Length < _layers.Count)
        {
            _samples = new CameraNudge[_layers.Count];
        }
    }

    /// <summary>Asks every layer what it wants this frame and sums the answers, clamped.</summary>
    private CameraNudge SampleLayers(float dt)
    {
        var snapshot = new CameraSnapshot(
            Context, IsFirstPerson, _modeBlend, _inputs.Speed01, _inputs.Grounded, _inputs.Sprinting, _inputs.Mounted);
        for (int i = 0; i < _layers.Count; i++)
        {
            _samples[i] = _layers[i].Sample(dt, snapshot);
        }

        return CameraRigMath.CombineLayers(_samples.AsSpan(0, _layers.Count));
    }

    /// <summary>
    /// Where the third-person camera sits relative to the pivot this frame: the seat at full
    /// extension, then squeezed by the wall spring.
    ///
    /// <para>The seat is probed in stages so a wall is answered with the smallest change that fits:
    /// the shoulder offset and the rise are each swept from the pivot and squeezed on their own (a
    /// low ceiling lowers the camera; a wall beside the head narrows the shoulder), with the other
    /// shoulder swept as well so that, when <c>AutoShoulderSwap</c> is on, the camera swings across
    /// rather than pulling in. Then a fan of sweeps (<see cref="CameraRigMath.ProbeMotion"/>) to the
    /// squeezed seat shortens the distance, so a corner the centre line slips past is still
    /// caught.</para>
    /// </summary>
    private Vector3 UpdateSeat(float dt, float layerDistanceScale)
    {
        Vector3 home = FullSeat(layerDistanceScale, 1f);
        if (CameraPivot == null || _queries == null || (!_wantThird && _modeBlend <= 0f))
        {
            // Not looking through the orbit, so nothing is being crowded. Hold every spring at clear;
            // a swap out starts from full extension and the first sweep decides how much is real.
            _seatFraction = _lateralFraction = _riseFraction = 1f;
            _seatClear = _lateralClear = _riseClear = 0f;
            return home;
        }

        Basis basis = CameraPivot.GlobalBasis;
        Vector3 origin = CameraPivot.GlobalPosition;

        bool auto = (_settings?.Current.AutoShoulderSwap ?? false) && Mathf.Abs(home.X) > 0.05f;
        float homeClear = Sweep(origin, basis, new Vector3(home.X, 0f, 0f));
        float awayClear = auto ? Sweep(origin, basis, new Vector3(-home.X, 0f, 0f)) : 1f;
        float upClear = Sweep(origin, basis, new Vector3(0f, home.Y, 0f));

        _sinceSwap += dt;
        bool swapped = auto && CameraRigMath.ShoulderSwapped(_shoulderSwapped, homeClear, awayClear, _sinceSwap);
        if (swapped != _shoulderSwapped)
        {
            _shoulderSwapped = swapped;
            _sinceSwap = 0f;
        }

        _swapBlend = Mathf.Lerp(_swapBlend, swapped ? 1f : 0f, CameraRigMath.Damp(dt, ShoulderSwapSeconds));
        _lateralFraction = CameraRigMath.SpringStep(
            _lateralFraction, 1f, swapped ? awayClear : homeClear, dt, SqueezePushOutPerSecond,
            ref _lateralClear, PushHoldSeconds);
        _riseFraction = CameraRigMath.SpringStep(
            _riseFraction, 1f, upClear, dt, SqueezePushOutPerSecond, ref _riseClear, PushHoldSeconds);

        var squeezed = new Vector3(
            home.X * Mathf.Lerp(1f, -1f, _swapBlend) * _lateralFraction,
            home.Y * _riseFraction,
            home.Z);

        float safe = 1f;
        for (int i = 0; i < CameraRigMath.ProbeCount; i++)
        {
            safe = Mathf.Min(safe, Sweep(origin, basis, CameraRigMath.ProbeMotion(squeezed, i)));
        }

        _seatFraction = CameraRigMath.SpringStep(
            _seatFraction, 1f, safe, dt, SeatPushOutPerSecond, ref _seatClear, PushHoldSeconds);
        return squeezed * _seatFraction;
    }

    /// <summary>The seat at full extension, before any wall: the player's own distance and shoulder
    /// settings, scaled by the profile and the layers, on the shoulder <paramref name="side"/> (1 = the
    /// chosen one, -1 = the other). ⚠️ The profile SCALES the player's settings rather than replacing
    /// them: the sliders are accessibility choices, and a profile that overrode them would quietly
    /// undo one every time the player drew a bow.</summary>
    private Vector3 FullSeat(float layerDistanceScale, float side)
    {
        Settings.Settings? s = _settings?.Current;
        Vector3 seat = CameraRigMath.ComposeSeat(
            s?.ThirdPersonDistance ?? PlayerFactory.ThirdPersonBackDistance,
            _profile.DistanceScale,
            layerDistanceScale,
            PlayerFactory.ThirdPersonRise + _profile.RiseOffset,
            (s?.ShoulderOffset() ?? PlayerFactory.ThirdPersonShoulder) * _profile.ShoulderScale);
        return new Vector3(seat.X * side, seat.Y, seat.Z);
    }

    /// <summary>One sphere sweep from <paramref name="origin"/> along a pivot-space motion; the
    /// fraction of it that is clear.
    ///
    /// ⚠️ <b>CameraBlocker, not World.</b> Actor bodies share the World layer, so sweeping it pulled the
    /// camera in whenever a companion stepped between the player and it — twitchy, and the previous
    /// note here admitted it and left it. Static world geometry declares itself a blocker
    /// (RegionStreamer.MarkCameraBlockers, WorldCellPresentation's terrain collider); people simply
    /// are not on the layer, so the camera passes through them and the obstruction fade handles the
    /// rest.
    ///
    /// ⚠️ NOT CombatLayers.CameraObstruction, which is CameraBlocker PLUS WorldStatic — and
    /// CharacterEntity still defaults to WorldStatic, so that mask puts actors back in the sweep and
    /// the companion problem returns exactly as it was. Measured: camera_probe.gd reports 0.60 m with
    /// a companion behind the player on that mask, 3.87 m on this one.</summary>
    private float Sweep(Vector3 origin, Basis basis, Vector3 localMotion)
    {
        Vector3 motion = basis * localMotion;
        return motion.LengthSquared() < 0.0001f
            ? 1f
            : _queries!.SafeSweepFraction(origin, motion, CameraProbeRadius, CombatLayers.CameraBlocker);
    }

    /// <summary>Applies the context's pitch limit, easing a pitch a tightening limit has left out of
    /// range back inside it.</summary>
    private void UpdatePitch(float dt)
    {
        _pitchLimit = CameraRigMath.PitchLimit(_profile.PitchLimit, _modeBlend);
        float eased = CameraRigMath.EasePitchInto(_pitch, _pitchLimit, dt);
        if (eased != _pitch)
        {
            _pitch = eased;
            if (CameraPivot != null)
            {
                CameraPivot.Rotation = new Vector3(_pitch, 0f, 0f);
            }
        }
    }

    /// <summary>The FOV offset for the frame. The widening a sprint or a roll adds is a kick, so the
    /// player's FOV Kick comfort scale (which Reduced Motion zeroes) applies to it; the narrowing of an
    /// aim or a lock is framing and does not.</summary>
    private void UpdateFieldOfView(float dt, float layerFovOffset)
    {
        Settings.Settings? current = _settings?.Current;
        float kick = CameraComfort.From(current).FovKick;
        _dodgePunch = CameraRigMath.AsymmetricDamp(
            _dodgePunch, _inputs.Dodging ? DodgeFovPunch : 0f, dt, PunchRiseSeconds, PunchFallSeconds);

        _fovOffset = CameraRigMath.ScaleFovKick(_profile.FovOffset, kick) + (_dodgePunch * kick) + layerFovOffset;
        ApplyFieldOfView(current);
    }

    /// <summary>
    /// Where the eye sits relative to the pivot, in the pivot's own (pitched) space.
    ///
    /// <para>⚠️ <b>The eye is anchored to where the head RESTS, not to where the animation has put
    /// it.</b> It used to ride the animated head bone, smoothed, and that was two of the owner's
    /// reports at once: a sprint leans the head forward and bobs it, so the view bobbed and lurched
    /// with every stride, and the smoothing let the skull run ahead of the camera and into frame.
    /// Now the anchor is the head's rest position in the body's frame, the look's pitch swings the
    /// eye about the neck, and only a fraction of the head's animated travel is followed
    /// (<see cref="CameraRigMath.EyeFollow"/>): none of it under Reduced Motion, all of it in the
    /// saddle, where the seated pose is where the head is.</para>
    ///
    /// <para>⚠️ <b>Position only. The head's ROTATION is deliberately ignored.</b> Taking it would hand
    /// the player every head turn in every clip as an involuntary camera movement, which is the
    /// single fastest way to make a first-person game unplayable. Aim stays exactly where the player
    /// pointed it.</para>
    ///
    /// <paramref name="advance"/> steps the smoothing by <paramref name="dt"/>; without it the eye
    /// is re-seated for the current pitch from the smoothing as it stands.
    /// </summary>
    private Vector3 EyeOffset(float dt, bool advance)
    {
        if (_skeleton == null || _headBone < 0 || CameraPivot == null || _modeBlend >= 1f ||
            !GodotObject.IsInstanceValid(_skeleton) || !_skeleton.IsInsideTree())
        {
            _eyeSeeded = false;
            return Vector3.Zero;
        }

        // Into the body's frame, which is the pivot's parent's. The smoother offsets the skeleton
        // and the pivot by the same residual between physics ticks, so it cancels out of both.
        Transform3D toBody = Entity!.Body.GlobalTransform.AffineInverse() * _skeleton.GlobalTransform;
        Vector3 restHead = (toBody * _headRest) - CameraPivot.Position;
        Vector3 animated = toBody.Basis * (_skeleton.GetBoneGlobalPose(_headBone).Origin - _headRest);

        // Seeded rather than lerped from zero, so entering first person does not swoop from the
        // rest pose to wherever the clip has the head over the first few frames.
        if (!_eyeSeeded)
        {
            _restHead = restHead;
            _eyeDelta = animated;
            _mountedEye = _inputs.Mounted ? 1f : 0f;
            _eyeGuard = 1f;
            _eyeClear = 0f;
            _eyeSeeded = true;
        }
        else if (advance)
        {
            // The rest anchor only moves when something moves the whole mesh under the pivot (a hit's
            // lurch, the step up into a saddle), and that is eased rather than taken in one frame.
            _restHead = _restHead.Lerp(restHead, CameraRigMath.Damp(dt, RestSmoothSeconds));
            _eyeDelta = _eyeDelta.Lerp(animated, CameraRigMath.Damp(dt, EyeSmoothSeconds));
            _mountedEye = Mathf.Lerp(
                _mountedEye, _inputs.Mounted ? 1f : 0f, CameraRigMath.Damp(dt, MountedEyeSeconds));
        }

        Vector2 follow = CameraRigMath.EyeFollow(_settings?.Current.ReducedMotion ?? false, _mountedEye);
        _eyeLocal = CameraRigMath.EyeLocal(_restHead, _pitch, CameraRigMath.FollowDelta(_eyeDelta, follow));
        return _eyeLocal;
    }

    /// <summary>
    /// The first-person near-plane guard: how much of the eye's offset from the pivot the way to it
    /// leaves clear. A head that leans into a wall (a lunge, a stagger against a corner) would put the
    /// near plane through it, so the eye is held back — pulled in at once, eased out again — exactly
    /// as the third-person seat is. It sweeps, so it runs on the physics tick only; the per-frame eye
    /// update reuses the fraction it left.
    /// </summary>
    private float GuardEye(float dt)
    {
        if (_queries == null || CameraPivot == null || !_eyeSeeded || _eyeLocal.LengthSquared() < 0.0004f)
        {
            _eyeGuard = 1f;
            return 1f;
        }

        float clear = _queries.SafeSweepFraction(
            CameraPivot.GlobalPosition, CameraPivot.GlobalBasis * _eyeLocal, EyeProbeRadius, CombatLayers.CameraBlocker);
        _eyeGuard = CameraRigMath.SpringStep(
            _eyeGuard, 1f, clear, dt, EyePushOutPerSecond, ref _eyeClear, PushHoldSeconds * 0.4f);
        return _eyeGuard;
    }

    /// <summary>Finds the head bone once the body exists. Deferred rather than done in
    /// <c>OnInitialize</c> because the visual is added to the tree after the components are.</summary>
    private void ResolveHead()
    {
        if (_skeleton != null || Entity?.Body.GetNodeOrNull<Node3D>("BodyMesh") is not { } visual)
        {
            return;
        }

        _skeleton = FindSkeleton(visual);
        if (_skeleton != null)
        {
            _headBone = Animation.EquipmentSockets.Resolve(_skeleton, Animation.EquipmentSocket.Head);
            if (_headBone >= 0)
            {
                _headRest = _skeleton.GetBoneGlobalRest(_headBone).Origin;
            }
        }

        _bodySurfaces.Clear();
        PlayerAppearance.CollectBodySurfaces(visual, _bodySurfaces);
    }

    /// <summary>
    /// Cuts the player's own head out of the body while the camera is inside it, and puts it back
    /// when the camera leaves.
    ///
    /// <para>The body shader discards every fragment inside a sphere round the animated head, in
    /// every pass but the shadow pass, so the head is gone from the view and its shadow is still on
    /// the ground. The sphere follows the head bone rather than the eye: at a sprint the head leans
    /// well ahead of where the eye is anchored, and a cut-out left behind at the eye is exactly how
    /// the skull got into frame. Pieces on the head socket (a helm) are not drawn with that shader,
    /// so they are switched to shadows-only for the same span and given their own setting back
    /// after.</para>
    ///
    /// <para>It goes by the camera's distance from the head (<see cref="CameraRigMath.HeadHidden"/>),
    /// not by the view mode, so a swap shows the head as the camera leaves it and a third-person
    /// camera a wall has squeezed into the skull hides it.</para>
    /// </summary>
    private void UpdateHeadCutout(double delta)
    {
        if (_skeleton == null || _headBone < 0 || Camera == null ||
            !GodotObject.IsInstanceValid(_skeleton) || !_skeleton.IsInsideTree())
        {
            return;
        }

        // Well out behind the body and not hiding anything: nothing to measure.
        if (!_headHidden && _modeBlend >= 1f && _cameraRest.LengthSquared() > 1f)
        {
            return;
        }

        Basis body = Entity!.Body.GlobalBasis;
        Vector3 head = (_skeleton.GlobalTransform * _skeleton.GetBoneGlobalPose(_headBone)).Origin;
        HeadSphereCentre = CameraRigMath.HeadSphereCentre(head, body.Y.Normalized(), -body.Z.Normalized());

        bool hidden = CameraRigMath.HeadHidden(
            _headHidden, Camera.GlobalPosition.DistanceTo(HeadSphereCentre));
        if (hidden || _headHidden)
        {
            PlayerAppearance.SetHeadCutout(
                _bodySurfaces, HeadSphereCentre, hidden ? CameraRigMath.HeadSphereRadius : 0f);
        }

        _headRescan -= delta;
        if (hidden != _headHidden || (hidden && _headRescan <= 0d))
        {
            _headHidden = hidden;
            _headRescan = HeadAttachmentRescanSeconds;
            RestoreHeadPieces();
            if (hidden)
            {
                HideHeadPieces();
            }
        }
    }

    /// <summary>Switches everything hung on the head bone to shadows-only, remembering what each
    /// piece was set to. A piece is on the head when its mount (the node
    /// <c>EquipmentPresentationComponent</c> parents to the skeleton) follows the head bone.</summary>
    private void HideHeadPieces()
    {
        if (_skeleton == null || !GodotObject.IsInstanceValid(_skeleton))
        {
            return;
        }

        string headName = _skeleton.GetBoneName(_headBone);
        foreach (Node child in _skeleton.GetChildren())
        {
            bool onHead = child switch
            {
                BoneAttachment3D attachment => attachment.BoneName == headName,
                Animation.SocketFollower follower => follower.BoneIndex == _headBone,
                _ => false,
            };

            if (onHead)
            {
                HidePieces(child);
            }
        }
    }

    private void HidePieces(Node node)
    {
        if (node is GeometryInstance3D geometry &&
            geometry.CastShadow != GeometryInstance3D.ShadowCastingSetting.ShadowsOnly)
        {
            _headPieces[geometry] = geometry.CastShadow;
            geometry.CastShadow = GeometryInstance3D.ShadowCastingSetting.ShadowsOnly;
        }

        foreach (Node child in node.GetChildren())
        {
            HidePieces(child);
        }
    }

    private void RestoreHeadPieces()
    {
        foreach ((GeometryInstance3D geometry, GeometryInstance3D.ShadowCastingSetting setting) in _headPieces)
        {
            if (GodotObject.IsInstanceValid(geometry))
            {
                geometry.CastShadow = setting;
            }
        }

        _headPieces.Clear();
    }

    private static Skeleton3D? FindSkeleton(Node node)
    {
        if (node is Skeleton3D skeleton)
        {
            return skeleton;
        }

        foreach (Node child in node.GetChildren())
        {
            if (FindSkeleton(child) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>Applies one look step to the pitch and writes it to the pivot. The look components
    /// decide how much; the rig owns what it means, because the pivot is the camera's — including how
    /// far the current context lets the player look up and down.</summary>
    public void ApplyPitchStep(float step, bool invertY)
    {
        _pitch = SettingsMath.ApplyPitch(_pitch, step, invertY, _pitchLimit);
        if (CameraPivot != null)
        {
            CameraPivot.Rotation = new Vector3(_pitch, 0f, 0f);
        }
    }

    /// <summary>How far the camera has been pulled back from the pivot, which the interaction reach
    /// has to add back so leaning out to third person does not extend the player's arms.</summary>
    public float Pullback =>
        Camera != null && CameraPivot != null ? Camera.GlobalPosition.DistanceTo(CameraPivot.GlobalPosition) : 0f;

    /// <summary>Pushes the FOV setting, plus this frame's offset, onto the player camera. It lives here
    /// rather than in <see cref="SettingsService"/> because it is a property of <em>this</em> camera,
    /// not of the engine, and the service has no handle on the player. Also called when settings
    /// change, so the slider previews live even while the game is paused behind the panel.</summary>
    private void ApplyFieldOfView(Settings.Settings? current)
    {
        if (current == null)
        {
            return;
        }

        float fov = CameraRigMath.ComposeFov(current.FieldOfView, _fovOffset);
        _lookScale = CameraRigMath.LookScale(fov, current.FieldOfView);
        if (Camera != null)
        {
            Camera.Fov = fov;
        }
    }

    /// <summary>Follow the camera-mode setting live — the settings panel and the toggle key both
    /// route through it, so there is one path into the mode and it is always the persisted one.</summary>
    private void OnSettingsApplied(SettingsAppliedEvent e)
    {
        ApplyFieldOfView(e.Current);
        SetFirstPerson(!e.Current.ThirdPersonCamera);
    }

    /// <summary>Writes the camera: the rest pose, then whatever the layers asked for on top. The rest
    /// pose alone is what <see cref="CameraRestPosition"/> hands to anything that offsets around it.</summary>
    private void ApplyCameraRest(Vector3 rest, in CameraNudge nudge)
    {
        _cameraRest = rest;
        if (Camera != null)
        {
            Camera.Position = rest + nudge.Offset;
            Camera.Rotation = nudge.Euler;
        }
    }
}
