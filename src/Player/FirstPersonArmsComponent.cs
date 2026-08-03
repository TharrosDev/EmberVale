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

    private Node3D? _root;
    private Node3D? _rightArm;
    private Node3D? _leftArm;
    private CombatComponent? _combat;
    private PlayerController? _controller;
    private float _bobTime;
    private float _swing;      // 1 → 0 while a slash plays
    private int _swingDir = 1; // alternates per combo hit
    private float _blockBlend; // 0 → 1 guard pose

    protected override void OnInitialize()
    {
        IEntity owner = Entity!;
        _combat = owner.GetComponent<CombatComponent>();
        _controller = owner.GetComponent<PlayerController>();
        BuildArms();
        EventBus.Instance?.Subscribe<AttackPerformedEvent>(OnAttack);
    }

    protected override void OnTeardown()
    {
        EventBus.Instance?.Unsubscribe<AttackPerformedEvent>(OnAttack);
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
        _leftArm = new Node3D { Name = "LeftArm", Position = LeftRest, Scale = new Vector3(-1f, 1f, 1f) };
        _leftArm.AddChild(armScene.Instantiate());
        _root.AddChild(_leftArm);

        // The held sword rides the right hand (blade +Y at the grip origin), tilted up-forward.
        if (GD.Load<PackedScene>(PlayerFactory.WeaponModelPath)?.Instantiate() is Node3D sword)
        {
            sword.Position = new Vector3(0f, -0.02f, -0.34f);
            sword.RotationDegrees = new Vector3(-65f, 0f, 0f);
            _rightArm.AddChild(sword);
        }
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
