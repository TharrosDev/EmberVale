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

        if (Entity?.Body.GetNodeOrNull<Node3D>("BodyMesh") is { } bodyVisual)
        {
            SetShadowOnly(bodyVisual, firstPerson);
        }
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
        _modeBlend = CameraRigMath.StepBlend(_modeBlend, _modeTarget, dt, ModeBlendSeconds);

        float desired = ThirdPersonRest.Length();
        _springDistance = CameraRigMath.SpringDistance(
            _springDistance, desired, AllowedCameraDistance(desired), dt, CameraPushOutSpeed);

        ApplyCameraRest(ResolveRestOffset());
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
            Camera.Fov = current.FieldOfView;
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
            return CameraRigMath.RestOffset(
                firstPerson: false,
                s?.ThirdPersonDistance ?? PlayerFactory.ThirdPersonBackDistance,
                PlayerFactory.ThirdPersonRise,
                s?.ShoulderOffset() ?? PlayerFactory.ThirdPersonShoulder);
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

        // ponytail: actor bodies share the World layer, so a companion stepping between the
        // player and the camera pulls it in too. Honest (it *is* in the way) if slightly
        // twitchy; a dedicated camera-blocker layer is the upgrade if it ever annoys.
        float safe = _queries.SafeSweepFraction(
            CameraPivot.GlobalPosition,
            CameraPivot.GlobalTransform.Basis * ThirdPersonRest,
            CameraProbeRadius,
            CombatLayers.World);

        return desired * safe;
    }

    /// <summary>Sets every mesh under <paramref name="node"/> to shadows-only (or restores it).
    /// Includes the skeleton-held weapon — in first person the viewmodel arms
    /// (<see cref="FirstPersonArmsComponent"/>) carry their own visible sword instead.</summary>
    private static void SetShadowOnly(Node node, bool shadowOnly)
    {
        if (node is GeometryInstance3D geometry)
        {
            geometry.CastShadow = shadowOnly
                ? GeometryInstance3D.ShadowCastingSetting.ShadowsOnly
                : GeometryInstance3D.ShadowCastingSetting.On;
        }

        foreach (Node child in node.GetChildren())
        {
            SetShadowOnly(child, shadowOnly);
        }
    }
}
