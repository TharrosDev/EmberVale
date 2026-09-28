using Embervale.Combat;
using Embervale.Core.Services;
using Embervale.Entities;
using Embervale.Movement;
using Embervale.Settings;
using Godot;

namespace Embervale.Player;

/// <summary>
/// The camera's movement feel, as a layer: landing dip, first-person head bob and sway by gait, the
/// settle after a sprint, dodge lean, mounted sway and the small sprint/landing FOV punch. It reads
/// what the player's own components already expose (grounded, speed, dodge, mount gait) and the
/// rig's snapshot, feeds <see cref="CameraMotion"/> (all the rules, engine-free) and returns a
/// <see cref="CameraNudge"/>. It never writes the camera transform; <c>PlayerCameraRig</c> sums it.
///
/// <para>Comfort: bob, dip, sway and dodge lean scale by <see cref="CameraComfort.Bob"/>; FOV punches by
/// <see cref="CameraComfort.FovKick"/>. Reduced Motion zeroes both, so this layer returns nothing.</para>
///
/// <para>A hard landing also submits <see cref="ShakeSource.Landing"/> trauma to the player's
/// <see cref="CameraShake"/>; the dip is the vertical give, the shake is the jolt.</para>
/// </summary>
public partial class CameraMotionLayer : EntityComponent, ICameraLayer
{
    private readonly CameraMotion _motion = new();
    private DodgeComponent? _dodge;
    private MountComponent? _mount;
    private CameraShake? _shake;
    private SettingsService? _settings;
    private bool _wasDodging;
    private float _dodgeElapsed;

    protected override void OnInitialize()
    {
        _dodge = Entity!.GetComponent<DodgeComponent>();
        _mount = Entity!.GetComponent<MountComponent>();
        _shake = Entity!.GetComponent<CameraShake>();
        _settings = ServiceLocator.Instance is { } locator && locator.TryGet(out SettingsService settings)
            ? settings
            : null;
    }

    protected override void OnTeardown()
    {
        _dodge = null;
        _mount = null;
        _shake = null;
    }

    public CameraNudge Sample(float dt, in CameraSnapshot snapshot)
    {
        Node3D body = Entity!.Body;
        CameraComfort comfort = CameraComfort.From(_settings?.Current);

        // The dodge has no progress accessor, so time it here from the first frame it reports.
        bool dodging = _dodge is { IsDodging: true } && !snapshot.Mounted;
        _dodgeElapsed = dodging && _wasDodging ? _dodgeElapsed + dt : 0f;
        _wasDodging = dodging;

        bool roll = _dodge?.Kind == DodgeKind.Roll;
        float total = _dodge == null
            ? 1f
            : roll ? _dodge.RollDuration + _dodge.RollRecovery : _dodge.BackstepDuration + _dodge.BackstepRecovery;

        // Velocity in the body's frame: +X right, -Z forward. Only read while dodging.
        Vector3 local = body is CharacterBody3D character
            ? body.GlobalTransform.Basis.Inverse() * character.Velocity
            : Vector3.Zero;

        MotionOut o = _motion.Step(dt, new MotionInput(
            snapshot.Speed01,
            snapshot.Grounded,
            snapshot.Sprinting,
            snapshot.Mounted,
            _mount?.Gait ?? MountGait.Halt,
            1f - snapshot.ModeBlend, // 0 = first person, 1 = third
            body.GlobalPosition.Y,
            dodging,
            roll,
            total > 0f ? _dodgeElapsed / total : 1f,
            local.X,
            -local.Z,
            comfort.Bob,
            comfort.FovKick));

        if (o.LandingDrop > 0f)
        {
            _shake?.Submit(new ShakeHit(ShakeSource.Landing, ShakeMath.LandingTrauma(o.LandingDrop)));
        }

        return new CameraNudge(new Vector3(o.X, o.Y, o.Z), new Vector3(0f, 0f, o.Roll), o.Fov, 1f);
    }
}
