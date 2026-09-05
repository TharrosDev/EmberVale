using Embervale.Combat;
using Embervale.Core.Events;
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
/// always equals camera yaw in both modes, so combat, lock-on, dodge and melee reach are
/// mode-agnostic — the only things that differ are where the camera sits (blended, and sprung off
/// world geometry) and that third person aims from the camera rather than the head so the crosshair
/// still means something.</para>
///
/// <para><b>First person is TRUE first person.</b> The camera rides the body's own head bone and the
/// body stays visible; you see its arms, its weapon and its equipment because they are the same
/// arms, weapon and equipment the world sees. What this replaced was a rigless viewmodel with its
/// own procedural swing — a second skeleton, a second action state and a second weapon that had to
/// be kept in step with the first, which is the duplication the overhaul's §18 is about.</para>
///
/// <para>This component owns the camera and nothing else does. The pure geometry it needs is in
/// <see cref="CameraRigMath"/>, which is engine-free and unit-tested; what is left here is the node
/// writes and the one physics sweep.</para>
/// </summary>
[GlobalClass]
public partial class PlayerCameraRig : EntityComponent
{
    /// <summary>Seconds the camera takes to travel between the two modes.</summary>
    private const float ModeBlendSeconds = 0.18f;

    /// <summary>Radius of the sphere swept from the pivot to the camera. Bigger than the camera's
    /// near plane so a corner can never poke inside it.</summary>
    private const float CameraProbeRadius = 0.22f;

    /// <summary>How fast the camera eases back out after geometry stops crowding it (m/s). Pulling
    /// in is instant; see <see cref="CameraRigMath.SpringDistance"/>.</summary>
    private const float CameraPushOutSpeed = 6f;

    /// <summary>Pitch clamp (radians) so the camera can't flip over the top/bottom.</summary>
    private const float PitchLimit = 1.45f;

    /// <summary>0 = first person, 1 = third person. Eased into the camera's rest pose each frame.</summary>
    private float _modeBlend;

    /// <summary>The blend target (0/1) the mode toggle sets.</summary>
    private float _modeTarget;

    /// <summary>Camera distance from the pivot after the collision spring, in metres.</summary>
    private float _springDistance;

    /// <summary>Seconds of smoothing on the eye anchor. The head bone is animated, so following it
    /// raw hands the player every footfall and every swing as camera shake.</summary>
    private const float EyeSmoothSeconds = 0.06f;

    /// <summary>How far the eye sits in front of the head bone's origin, in metres. Big enough that
    /// the skull falls behind the camera's near plane and clips away on its own — which is why there
    /// is no head-hiding code anywhere in this file.</summary>
    private const float EyeForward = 0.14f;

    /// <summary>How far above the head bone's origin the eye sits.</summary>
    private const float EyeRise = 0.04f;

    /// <summary>The furthest the eye may be dragged from the fixed pivot. A clip that throws the
    /// head — a knockdown, a death — must not throw the camera with it.</summary>
    private const float MaxEyeOffset = 0.45f;

    /// <summary>The camera's current shape, eased toward whatever the context asks for.</summary>
    private CameraProfile _profile = CameraProfile.Neutral;

    /// <summary>Set each frame by the input router from live gameplay state. Held here rather than
    /// queried so the rig does not have to know about lock-on, aiming or combat components.</summary>
    public CameraContext Context { get; set; } = CameraContext.Exploration;

    private Skeleton3D? _skeleton;
    private int _headBone = -1;
    private Vector3 _eyeLocal;
    private bool _eyeSeeded;
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

    /// <summary>Whether gameplay is currently first-person (the shipping default).</summary>
    public bool IsFirstPerson { get; private set; } = true;

    /// <summary>The camera's live rest position — the single source of truth shared with
    /// <see cref="CameraShake"/>, which offsets around it per frame. It follows the mode blend and
    /// the wall spring, so a crit mid-swap or against a wall shakes around where the camera actually
    /// is, not where the mode says it should be (the "camera glitches into the head on a crit while
    /// third-person" bug).</summary>
    public Vector3 CameraRestPosition => _cameraRest;

    protected override void OnInitialize()
    {
        _queries = Entity!.GetComponent<PlayerPhysicsQueries>();
        _settings = ServiceLocator.Instance is { } locator && locator.TryGet(out SettingsService settings)
            ? settings
            : null;

        EventBus.Instance?.Subscribe<SettingsAppliedEvent>(OnSettingsApplied);
        ApplyFieldOfView(_settings?.Current);
        SetFirstPerson(!(_settings?.Current.ThirdPersonCamera ?? false), immediate: true);
    }

    protected override void OnTeardown()
    {
        EventBus.Instance?.Unsubscribe<SettingsAppliedEvent>(OnSettingsApplied);
    }

    /// <summary>
    /// Switches between first person (camera at the eye, own body casting shadows only — the
    /// viewmodel arms carry the visible weapon) and over-the-shoulder third person (camera orbits
    /// behind and to the right, full body shown).
    ///
    /// <paramref name="immediate"/> snaps rather than blends — used on initialize so a save resumed
    /// in third person opens there instead of swooping out on the first frame.
    /// </summary>
    public void SetFirstPerson(bool firstPerson, bool immediate = false)
    {
        if (IsFirstPerson == firstPerson && !immediate && _modeTarget == (firstPerson ? 0f : 1f))
        {
            // Nothing changed. Worth checking: the settings panel re-applies live on every slider
            // drag frame, and the body-mesh shadow walk below is not free.
            return;
        }

        // The flag flips at the *start* of the blend so the viewmodel arms hide on the way out
        // rather than fading past the camera.
        IsFirstPerson = firstPerson;
        _modeTarget = firstPerson ? 0f : 1f;
        if (immediate)
        {
            _modeBlend = _modeTarget;
            _springDistance = firstPerson ? 0f : ThirdPersonRest.Length();
            ApplyCameraRest(ResolveRestOffset());
        }

        // ⚠️ THE BODY IS VISIBLE IN BOTH VIEWS NOW, and that is the whole of "true first person".
        // It used to be shadows-only in first person while a separate rigless viewmodel drew a pair
        // of arms with its own procedural swing — two skeletons, two action states and two weapons
        // to keep in step. There is one body; you look out of its head and see its own arms.
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
            SetFirstPerson(!IsFirstPerson);
            return;
        }

        _settings.Current.ThirdPersonCamera = !_settings.Current.ThirdPersonCamera;
        _settings.Apply();
        _settings.Save();
    }

    /// <summary>Advances the mode blend and the wall spring, then writes the camera's rest pose. The
    /// spring sweeps a small sphere from the pivot out to the camera's desired seat and clamps the
    /// distance to the first thing it touches, so the camera never ends up inside geometry.</summary>
    public void Tick(double delta)
    {
        float dt = (float)delta;
        ResolveHead();
        _modeBlend = CameraRigMath.StepBlend(_modeBlend, _modeTarget, dt, ModeBlendSeconds);

        // The profile leans the camera toward what the player is doing. Eased, because a context
        // change that cut between framings would be worse than having no profiles at all.
        CameraProfile wanted = CameraProfile.For(Context);
        _profile = CameraProfile.Blend(_profile, wanted, CameraRigMath.Damp(dt, wanted.BlendSeconds));
        ApplyFieldOfView(_settings?.Current);

        float desired = ThirdPersonRest.Length();
        _springDistance = CameraRigMath.SpringDistance(
            _springDistance, desired, AllowedCameraDistance(desired), dt, CameraPushOutSpeed);

        ApplyCameraRest(ResolveRestOffset() + EyeOffset(dt));
    }

    /// <summary>
    /// Where the eye sits relative to the pivot, in pivot space — the head bone, smoothed, clamped,
    /// and faded out as the camera leaves first person.
    ///
    /// ⚠️ <b>Position only. The head's ROTATION is deliberately ignored.</b> Taking it would hand
    /// the player every head turn in every clip as an involuntary camera movement, which is the
    /// single fastest way to make a first-person game unplayable. Aim stays exactly where the player
    /// pointed it; only the viewpoint rides the body.
    /// </summary>
    private Vector3 EyeOffset(float dt)
    {
        if (_skeleton == null || _headBone < 0 || CameraPivot == null || _modeBlend >= 1f)
        {
            _eyeSeeded = false;
            return Vector3.Zero;
        }

        Transform3D head = _skeleton.GlobalTransform * _skeleton.GetBoneGlobalPose(_headBone);
        Vector3 forward = -CameraPivot.GlobalTransform.Basis.Z;
        Vector3 world = head.Origin + (forward * EyeForward) + (Vector3.Up * EyeRise);
        Vector3 target = CameraPivot.ToLocal(world);

        if (target.Length() > MaxEyeOffset)
        {
            target = target.Normalized() * MaxEyeOffset;
        }

        // Seeded rather than lerped from zero, so entering first person does not swoop from the
        // pivot up to the head over the first few frames.
        _eyeLocal = _eyeSeeded
            ? _eyeLocal.Lerp(target, CameraRigMath.Damp(dt, EyeSmoothSeconds))
            : target;
        _eyeSeeded = true;

        // Faded out by the mode blend so the third-person orbit is measured from the fixed pivot and
        // does not inherit a bobbing origin.
        return _eyeLocal * (1f - CameraRigMath.Ease(_modeBlend));
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
        }
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
    /// decide how much; the rig owns what it means, because the pivot is the camera's.</summary>
    public void ApplyPitchStep(float step, bool invertY)
    {
        _pitch = SettingsMath.ApplyPitch(_pitch, step, invertY, PitchLimit);
        if (CameraPivot != null)
        {
            CameraPivot.Rotation = new Vector3(_pitch, 0f, 0f);
        }
    }

    /// <summary>How far the camera has been pulled back from the pivot, which the interaction reach
    /// has to add back so leaning out to third person does not extend the player's arms.</summary>
    public float Pullback =>
        Camera != null && CameraPivot != null ? Camera.GlobalPosition.DistanceTo(CameraPivot.GlobalPosition) : 0f;

    /// <summary>Pushes the FOV setting onto the player camera. It lives here rather than in
    /// <see cref="SettingsService"/> because it is a property of <em>this</em> camera, not of the
    /// engine, and the service has no handle on the player.</summary>
    private void ApplyFieldOfView(Settings.Settings? current)
    {
        if (Camera != null && current != null)
        {
            // Same rule as the distance: the player's FOV is the baseline and the profile leans off
            // it, clamped to the range the settings panel itself allows so no context can push the
            // camera somewhere the player could not have chosen.
            Camera.Fov = Mathf.Clamp(current.FieldOfView + _profile.FovOffset, 55f, 115f);
        }
    }

    /// <summary>Follow the camera-mode setting live — the settings panel and the toggle key both
    /// route through it, so there is one path into the mode and it is always the persisted one.</summary>
    private void OnSettingsApplied(SettingsAppliedEvent e)
    {
        ApplyFieldOfView(e.Current);
        SetFirstPerson(!e.Current.ThirdPersonCamera);
    }

    /// <summary>The third-person rest offset at full extension, before the wall spring. Read from
    /// the live settings each frame so the distance/shoulder sliders move the camera while the
    /// player drags them, rather than on the next mode swap.</summary>
    private Vector3 ThirdPersonRest
    {
        get
        {
            Settings.Settings? s = _settings?.Current;
            // ⚠️ The profile SCALES the player's own settings rather than replacing them. The
            // distance and shoulder sliders are accessibility choices; a profile that overrode them
            // would quietly undo one every time the player drew a bow.
            return CameraRigMath.RestOffset(
                firstPerson: false,
                (s?.ThirdPersonDistance ?? PlayerFactory.ThirdPersonBackDistance) * _profile.DistanceScale,
                PlayerFactory.ThirdPersonRise + _profile.RiseOffset,
                (s?.ShoulderOffset() ?? PlayerFactory.ThirdPersonShoulder) * _profile.ShoulderScale);
        }
    }

    /// <summary>The camera's rest offset this frame: the eased blend between the two modes, with the
    /// third-person leg shortened to whatever the wall spring currently allows.</summary>
    private Vector3 ResolveRestOffset()
    {
        Vector3 full = ThirdPersonRest;
        float extent = full.Length();
        Vector3 third = extent > 0.0001f ? full * (_springDistance / extent) : Vector3.Zero;
        return CameraRigMath.Blend(Vector3.Zero, third, CameraRigMath.Ease(_modeBlend));
    }

    private void ApplyCameraRest(Vector3 rest)
    {
        _cameraRest = rest;
        if (Camera != null)
        {
            Camera.Position = rest;
        }
    }

    /// <summary>How far the camera can sit from the pivot before it would clip world geometry.
    /// Returns <paramref name="desired"/> when nothing is in the way (including in first person,
    /// where the blend collapses the offset to zero anyway and the cast would be wasted work).</summary>
    private float AllowedCameraDistance(float desired)
    {
        if (_modeBlend <= 0f || CameraPivot == null || _queries == null || desired <= 0.0001f)
        {
            return desired;
        }

        // ⚠️ CameraBlocker, not World. Actor bodies share the World layer, so sweeping it pulled the
        // camera in whenever a companion stepped between the player and it — twitchy, and the
        // previous note here admitted it and left it. Static world geometry declares itself a
        // blocker (RegionStreamer.MarkCameraBlockers, WorldCellPresentation's terrain collider);
        //
        // ⚠️ NOT CombatLayers.CameraObstruction, which is CameraBlocker PLUS WorldStatic — and
        // CharacterEntity still defaults to WorldStatic, so that mask puts actors back in the
        // sweep and the companion problem returns exactly as it was. Measured:
        // camera_probe.gd reports 0.60 m with a companion behind the player on that mask, 3.87 m
        // on this one.
        // people simply are not on the layer, so the camera passes through them and the obstruction
        // fade handles the rest.
        float safe = _queries.SafeSweepFraction(
            CameraPivot.GlobalPosition,
            CameraPivot.GlobalTransform.Basis * ThirdPersonRest,
            CameraProbeRadius,
            CombatLayers.CameraBlocker);

        return desired * safe;
    }

}
