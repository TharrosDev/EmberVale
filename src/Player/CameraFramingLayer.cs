using Embervale.Combat;
using Embervale.Core.Services;
using Embervale.Entities;
using Embervale.Settings;
using Godot;

namespace Embervale.Player;

/// <summary>
/// The camera layer that composes the frame for a lock-on and for aiming (Settings.LockOnFraming).
///
/// <para><b>Lock-on:</b> a target that is close is looked at past the shoulder, from a touch higher and
/// further back, so the player and the foe both stay in frame; a far target needs none of that. The
/// yaw cancels the slide so the target stays on the crosshair, and a small pitch follows ground the
/// target stands above or below. The framing eases in over half a second and out over most of a
/// second, so a lock that breaks lets the camera go rather than snapping it back, and a cycle to a
/// new target settles onto its distance and height instead of jumping.</para>
///
/// <para><b>Aim:</b> a little extra FOV tighten (scaled by the FOV-kick comfort, so Reduced Motion
/// removes it) and, in third person, a lead toward the aim side so the reticle stays clear of the
/// body — larger with the centred shoulder, where the body would sit on it.</para>
///
/// <para>The rules are pure and tested in <see cref="FramingMath"/>; this component only reads the
/// lock, the shoulder and the settings and eases the weights. With the setting off it returns the
/// identity nudge, which is the camera as it was before layers existed.</para>
/// </summary>
[GlobalClass]
public partial class CameraFramingLayer : EntityComponent, ICameraLayer
{
    /// <summary>Seconds for the lock framing to arrive, and to let go.</summary>
    private const float LockInSeconds = 0.5f;
    private const float LockOutSeconds = 0.9f;

    /// <summary>Seconds for the aim framing to arrive, and to let go.</summary>
    private const float AimInSeconds = 0.25f;
    private const float AimOutSeconds = 0.45f;

    /// <summary>Time constant of the settle onto a newly cycled target's distance and height.</summary>
    private const float SettleSeconds = 0.25f;

    private SettingsService? _settings;
    private LockOnComponent? _lockOn;
    private IEntity? _tracked;
    private float _lockWeight;
    private float _aimWeight;
    private float _distance;
    private float _heightDelta;
    private float _targetHeight;
    private IEntity? _measured;
    private float _measuredHeight;
    private bool _seeded;

    protected override void OnInitialize()
    {
        _settings = ServiceLocator.Instance is { } locator && locator.TryGet(out SettingsService settings)
            ? settings
            : null;
    }

    public CameraNudge Sample(float dt, in CameraSnapshot snapshot)
    {
        Settings.Settings? s = _settings?.Current;
        if (!(s?.LockOnFraming ?? true))
        {
            // Off is the camera as it was. Weights are dropped so turning it back on eases in.
            _lockWeight = 0f;
            _aimWeight = 0f;
            _seeded = false;
            return CameraNudge.Identity;
        }

        _lockOn ??= Entity?.GetComponent<LockOnComponent>();
        IEntity? target = snapshot.Context == CameraContext.TargetLock ? _lockOn?.Target : null;
        float shoulder = s?.ShoulderOffset() ?? PlayerFactory.ThirdPersonShoulder;

        TrackTarget(target, dt);
        _lockWeight = FramingMath.StepWeight(_lockWeight, target != null, dt, LockInSeconds, LockOutSeconds);
        _aimWeight = FramingMath.StepWeight(
            _aimWeight, snapshot.Context == CameraContext.Aim, dt, AimInSeconds, AimOutSeconds);
        if (_lockWeight <= 0f)
        {
            _seeded = false;
        }

        // ⚠️ The last tracked distance and height are KEPT while the weight eases out, so a lock that
        // breaks fades the framing away instead of collapsing it in one frame.
        CameraNudge nudge = CameraNudge.Identity;
        if (_lockWeight > 0f && _seeded)
        {
            LockFraming lockFraming = FramingMath.Lock(
                _distance, _heightDelta, FramingMath.ShoulderSign(shoulder), _targetHeight);
            nudge = FramingMath.ToNudge(lockFraming, CameraRigMath.Ease(_lockWeight), snapshot.ModeBlend);
        }

        if (_aimWeight > 0f)
        {
            AimFraming aimFraming = FramingMath.Aim(shoulder, snapshot.ModeBlend, CameraComfort.From(s).FovKick);
            nudge = nudge.Combine(FramingMath.ToNudge(aimFraming, CameraRigMath.Ease(_aimWeight)));
        }

        return nudge;
    }

    /// <summary>Follows the locked target's distance, height and stature. The first sample after a
    /// lock seeds the values outright; a cycle to another target eases onto the new ones.</summary>
    private void TrackTarget(IEntity? target, float dt)
    {
        if (target?.Body is not Node3D targetBody || Entity?.Body is not Node3D body)
        {
            _tracked = null;
            _measured = null;
            return;
        }

        Vector3 to = targetBody.GlobalPosition - body.GlobalPosition;
        float height = to.Y;
        to.Y = 0f;
        float distance = to.Length();

        // How tall the target stands as drawn, which is what has to fit the frame. A body does not
        // change size, so it is measured once per target rather than every frame.
        if (!ReferenceEquals(target, _measured))
        {
            _measured = target;
            _measuredHeight = BodyMetrics.VisualHeight(targetBody, 0f);
        }

        float stature = _measuredHeight;

        if (!_seeded)
        {
            _distance = distance;
            _heightDelta = height;
            _targetHeight = stature;
            _seeded = true;
        }
        else if (ReferenceEquals(target, _tracked))
        {
            // The same target moving: follow it directly, the weight is already smoothing the look.
            _distance = distance;
            _heightDelta = height;
            _targetHeight = stature;
        }
        else
        {
            _distance = FramingMath.Approach(_distance, distance, dt, SettleSeconds);
            _heightDelta = FramingMath.Approach(_heightDelta, height, dt, SettleSeconds);
            _targetHeight = FramingMath.Approach(_targetHeight, stature, dt, SettleSeconds);
            if (Mathf.Abs(_distance - distance) < 0.05f && Mathf.Abs(_heightDelta - height) < 0.05f)
            {
                _tracked = target;
            }

            return;
        }

        _tracked = target;
    }
}
