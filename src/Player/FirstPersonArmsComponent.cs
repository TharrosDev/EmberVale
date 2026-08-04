using Embervale.Combat;
using Embervale.Core.Events;
using Embervale.Entities;
using Godot;

namespace Embervale.Player;

/// <summary>
/// The first-person viewmodel (Phase 30L): a pair of <c>fp_arm.glb</c> arms parented to the
/// player camera, the right hand holding the sword model. All motion is procedural — the arm
/// mesh has no baked clips — so this component drives a walk bob off the body's velocity, a
/// slash arc on <see cref="AttackPerformedEvent"/> (direction alternates with the combo index),
/// and a raised guard pose while blocking. Purely cosmetic: hit timing/damage stay with
/// <see cref="MeleeWeaponComponent"/>. Visible only while <see cref="PlayerController.IsFirstPerson"/>;
/// the retained third-person rig (cutscenes) shows the full body instead.
/// </summary>
[GlobalClass]
public partial class FirstPersonArmsComponent : EntityComponent
{
    private const string ArmModelPath = "res://assets/models/characters/fp_arm.glb";

    /// <summary>The player camera the arms ride (injected by <see cref="PlayerFactory"/>).</summary>
    public Node3D? Camera { get; set; }

    private static readonly Vector3 RightRest = new(0.26f, -0.34f, -0.48f);
    private static readonly Vector3 LeftRest = new(-0.26f, -0.34f, -0.48f);
    private const float SwingSeconds = 0.35f;

    /// <summary>
    /// The narrower field of view the viewmodel is drawn as if it were rendered at (degrees).
    /// The world needs a wide FOV to feel right to move through, but at that width anything held
    /// in front of the camera reads as small and far away — which is why hands and weapons looked
    /// undersized. Real engines fix this with a second camera; with one camera the equivalent is
    /// to scale the arms by the ratio of the two half-angle tangents, at unchanged distance.
    /// Lower = the arms loom larger. Purely cosmetic: nothing here touches reach or hit timing.
    /// </summary>
    [Export] public float ViewmodelFov { get; set; } = 55f;

    private Node3D? _root;
    private Node3D? _rightArm;
    private Node3D? _leftArm;
    private CombatComponent? _combat;
    private PlayerController? _controller;
    private float _bobTime;
    private float _swing;      // 1 → 0 while a slash plays
    private int _swingDir = 1; // alternates per combo hit
    private float _blockBlend; // 0 → 1 guard pose
    private float _scaledForFov; // camera FOV the arms were last sized for

    protected override void OnInitialize()
    {
        IEntity owner = Entity!;
        _combat = owner.GetComponent<CombatComponent>();
        _controller = owner.GetComponent<PlayerController>();
        BuildArms();
        EventBus.Instance?.Subscribe<AttackPerformedEvent>(OnAttack);
        EventBus.Instance?.Subscribe<AttackInterruptedEvent>(OnInterrupted);
    }

    protected override void OnTeardown()
    {
        EventBus.Instance?.Unsubscribe<AttackPerformedEvent>(OnAttack);
        EventBus.Instance?.Unsubscribe<AttackInterruptedEvent>(OnInterrupted);
    }

    private void BuildArms()
    {
        if (Camera == null || GD.Load<PackedScene>(ArmModelPath) is not { } armScene)
        {
            return;
        }

        _root = new Node3D { Name = "FpArms" };
        Camera.AddChild(_root);

        _rightArm = new Node3D { Name = "RightArm", Position = RightRest };
        _rightArm.AddChild(armScene.Instantiate());
        _root.AddChild(_rightArm);

        // The arm is now an actual right forearm and hand with fingers and a thumb, so the left
        // side has to be mirrored — an unmirrored copy reads as two right hands. Godot flips face
        // winding for a negative-determinant basis, so the mesh renders correctly; this was not
        // worth doing while the arm was a featureless 448-tri stub.
        _leftArm = new Node3D { Name = "LeftArm", Position = LeftRest };
        _leftArm.AddChild(armScene.Instantiate());
        _root.AddChild(_leftArm);

        ApplyViewmodelScale();

        // The held sword rides the right hand. These numbers are not eyeballed: the arm mesh is
        // the Adventurer's right forearm captured in its own Idle_Sword pose, so the fist already
        // closes around a hilt. GripPoint is the centre of that closed fist and BladeDirection the
        // axis of the tunnel the curled fingers make (index-base → pinky-base), both measured off
        // the posed rig and carried through the same fit the mesh went through. The old
        // hand-tuned offset put the hilt in front of the fingers rather than through them, which
        // a straight-on view hides completely — it only shows side-on.
        if (GD.Load<PackedScene>(PlayerFactory.WeaponModelPath)?.Instantiate() is Node3D sword)
        {
            sword.Transform = GripTransform();
            _rightArm.AddChild(sword);
        }
    }

    /// <summary>Sizes both arms for the camera's current FOV and records what it was sized for.
    /// Scaling the arms (not the rest offsets, and not the whole rig — a uniform scale about the
    /// eye point is a visual no-op) is what emulates a separate viewmodel FOV. The left arm keeps
    /// its mirrored X.</summary>
    private void ApplyViewmodelScale()
    {
        float k = ViewmodelScale();
        _scaledForFov = Camera is Camera3D cam ? cam.Fov : 0f;

        if (_rightArm != null)
        {
            _rightArm.Scale = Vector3.One * k;
        }

        if (_leftArm != null)
        {
            _leftArm.Scale = new Vector3(-k, k, k);
        }
    }

    /// <summary>
    /// How much larger the arms are drawn so they read as if rendered at
    /// <see cref="ViewmodelFov"/> while the world stays at the camera's own FOV.
    /// </summary>
    private float ViewmodelScale()
    {
        float world = Camera is Camera3D cam ? cam.Fov : 75f;
        if (ViewmodelFov <= 1f || ViewmodelFov >= 179f || world <= 1f)
        {
            return 1f;
        }

        return Mathf.Tan(Mathf.DegToRad(world) * 0.5f) / Mathf.Tan(Mathf.DegToRad(ViewmodelFov) * 0.5f);
    }

    /// <summary>Centre of the closed fist, in the arm mesh's local space.</summary>
    private static readonly Vector3 GripPoint = new(0.0595f, 0.1526f, -0.1343f);

    /// <summary>Where the blade points out of that fist — up and forward.</summary>
    private static readonly Vector3 BladeDirection = new(-0.0874f, 0.3498f, -0.9327f);

    /// <summary>Height along the sword's own +Y of the middle of its wrapped grip.</summary>
    private const float SwordGripHeight = 0.03f;

    /// <summary>
    /// Places a weapon so its grip sits inside the fist and its blade (local +Y) runs along
    /// <see cref="BladeDirection"/>. Built from vectors rather than authored Euler angles so a
    /// future weapon swap only has to match the "blade along +Y, grip near the origin" convention.
    /// </summary>
    private static Transform3D GripTransform()
    {
        Vector3 y = BladeDirection.Normalized();
        Vector3 x = y.Cross(Vector3.Up).Normalized();
        Vector3 z = x.Cross(y);
        var basis = new Basis(x, y, z);
        return new Transform3D(basis, GripPoint - (basis.Y * SwordGripHeight));
    }

    private void OnAttack(AttackPerformedEvent e)
    {
        if (!ReferenceEquals(e.Attacker, Entity))
        {
            return;
        }

        _swing = 1f;
        _swingDir = e.ComboIndex % 2 == 0 ? 1 : -1;
    }

    /// <summary>A staggered swing is cancelled before the hitbox opens (36C), so the arms have to
    /// drop with it — an arc that finishes on its own reads as a hit that should have landed.</summary>
    private void OnInterrupted(AttackInterruptedEvent e)
    {
        if (ReferenceEquals(e.Attacker, Entity))
        {
            _swing = 0f;
        }
    }

    public override void _Process(double delta)
    {
        if (_root == null || _rightArm == null || _leftArm == null)
        {
            return;
        }

        bool visible = _controller?.IsFirstPerson ?? true;
        _root.Visible = visible;
        if (!visible)
        {
            return;
        }

        // The arm scale emulates a narrower viewmodel FOV against the world's, so it has to follow
        // the FOV setting — otherwise dragging that slider silently undoes the whole point of
        // ViewmodelScale and the hands read undersized again.
        if (Camera is Camera3D cam && !Mathf.IsEqualApprox(cam.Fov, _scaledForFov))
        {
            ApplyViewmodelScale();
        }

        float dt = (float)delta;

        // Walk bob: phase advances with ground speed, amplitude fades in with it.
        Vector3 velocity = Entity?.Body is CharacterBody3D body ? body.Velocity : Vector3.Zero;
        float speed = new Vector2(velocity.X, velocity.Z).Length();
        _bobTime += dt * Mathf.Max(speed, 0.001f) * 1.9f;
        float amp = Mathf.Clamp(speed / 5f, 0f, 1.2f);
        var bob = new Vector3(
            Mathf.Cos(_bobTime * 0.5f) * 0.010f * amp,
            Mathf.Sin(_bobTime) * 0.014f * amp,
            0f);

        // Guard pose: both arms rise and pull in while the block is held.
        float blockTarget = _combat?.IsBlocking == true ? 1f : 0f;
        _blockBlend = Mathf.MoveToward(_blockBlend, blockTarget, dt * 6f);
        var guard = new Vector3(-0.08f * _blockBlend, 0.14f * _blockBlend, 0.06f * _blockBlend);

        // Slash arc: a smooth out-and-back curve over the swing window.
        _swing = Mathf.Max(_swing - (dt / SwingSeconds), 0f);
        float arc = Mathf.Sin((1f - _swing) * Mathf.Pi) * (_swing > 0f ? 1f : 0f);

        _rightArm.Position = RightRest + bob + guard + new Vector3(0f, 0f, -0.16f * arc);
        _rightArm.RotationDegrees = new Vector3(
            (-55f * arc) + (35f * _blockBlend),
            _swingDir * 35f * arc,
            (-20f * _blockBlend) + (_swingDir * -15f * arc));

        _leftArm.Position = LeftRest + bob + new Vector3(-guard.X, guard.Y, guard.Z);
        _leftArm.RotationDegrees = new Vector3(35f * _blockBlend, 0f, 20f * _blockBlend);
    }
}
