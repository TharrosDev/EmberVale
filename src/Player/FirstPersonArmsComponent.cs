using Embervale.Combat;
using Embervale.Core.Events;
using Embervale.Entities;
using Embervale.Interaction;
using Embervale.Magic;
using Godot;
using Embervale.Core;

namespace Embervale.Player;

/// <summary>
/// The first-person viewmodel (Phase 30L/Session 2 art pass): authored left and right arm assets
/// parented to the
/// player camera, the right hand holding the sword model. All motion is procedural — the arm
/// mesh has no baked clips — so this component drives a walk bob off the body's velocity, a
/// slash arc on <see cref="AttackPerformedEvent"/> (direction alternates with the combo index),
/// a raised guard pose while blocking, and short interaction/cast presentation beats. Purely
/// cosmetic: hit timing/damage stay with
/// <see cref="MeleeWeaponComponent"/>. Visible only while <see cref="PlayerCameraRig.IsFirstPerson"/>;
/// the retained third-person rig (cutscenes) shows the full body instead.
/// </summary>
[GlobalClass]
public partial class FirstPersonArmsComponent : EntityComponent
{
    private const string RightArmModelPath = ModelAssets.FirstPersonArmRight;
    private const string LeftArmModelPath = ModelAssets.FirstPersonArmLeft;

    /// <summary>The player camera the arms ride (injected by <see cref="PlayerFactory"/>).</summary>
    public Node3D? Camera { get; set; }

    private static readonly Vector3 RightRest = new(0.30f, -0.49f, -0.74f);
    private static readonly Vector3 LeftRest = new(-0.30f, -0.49f, -0.74f);
    private static readonly Vector3 RightRestRotation = new(26f, -8f, -8f);
    private static readonly Vector3 LeftRestRotation = new(18f, 8f, 8f);
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
    private Node3D? _weaponSocket;
    private Node3D? _spellSocket;
    private Node3D? _interactionSocket;
    private CombatComponent? _combat;
    private SpellcastingComponent? _spellcasting;
    private PlayerCameraRig? _rig;
    private float _bobTime;
    private float _swing;      // 1 → 0 while a slash plays
    private int _swingDir = 1; // alternates per combo hit
    private float _blockBlend; // 0 → 1 guard pose
    private float _castBeat;
    private float _interactionBeat;
    private float _scaledForFov; // camera FOV the arms were last sized for

    protected override void OnInitialize()
    {
        IEntity owner = Entity!;
        _combat = owner.GetComponent<CombatComponent>();
        _spellcasting = owner.GetComponent<SpellcastingComponent>();
        _rig = owner.GetComponent<PlayerCameraRig>();
        BuildArms();
        EventBus.Instance?.Subscribe<AttackPerformedEvent>(OnAttack);
        EventBus.Instance?.Subscribe<AttackInterruptedEvent>(OnInterrupted);
        EventBus.Instance?.Subscribe<SpellCastEvent>(OnSpellCast);
        EventBus.Instance?.Subscribe<InteractionPerformedEvent>(OnInteraction);
    }

    protected override void OnTeardown()
    {
        EventBus.Instance?.Unsubscribe<AttackPerformedEvent>(OnAttack);
        EventBus.Instance?.Unsubscribe<AttackInterruptedEvent>(OnInterrupted);
        EventBus.Instance?.Unsubscribe<SpellCastEvent>(OnSpellCast);
        EventBus.Instance?.Unsubscribe<InteractionPerformedEvent>(OnInteraction);
    }

    private void BuildArms()
    {
        if (Camera == null ||
            GD.Load<PackedScene>(RightArmModelPath) is not { } rightArmScene ||
            GD.Load<PackedScene>(LeftArmModelPath) is not { } leftArmScene)
        {
            return;
        }

        _root = new Node3D { Name = "FpArms" };
        Camera.AddChild(_root);

        _rightArm = new Node3D { Name = "RightArm", Position = RightRest };
        _rightArm.AddChild(rightArmScene.Instantiate());
        _root.AddChild(_rightArm);

        // Session 2 replaced the runtime negative-scale mirror with an authored left mesh.  A
        // positive determinant keeps normals, tangent space and future arm animation predictable.
        _leftArm = new Node3D { Name = "LeftArm", Position = LeftRest };
        _leftArm.AddChild(leftArmScene.Instantiate());
        _root.AddChild(_leftArm);

        ApplyViewmodelScale();

        // The held sword rides the right hand. These numbers are not eyeballed: the arm mesh is
        // the Adventurer's right forearm captured in its own Idle_Sword pose, so the fist already
        // closes around a hilt. GripPoint is the centre of that closed fist and BladeDirection the
        // axis of the tunnel the curled fingers make (index-base → pinky-base), both measured off
        // the posed rig and carried through the same fit the mesh went through. The old
        // hand-tuned offset put the hilt in front of the fingers rather than through them, which
        // a straight-on view hides completely — it only shows side-on.
        _weaponSocket = new Node3D { Name = "WeaponSocket", Transform = GripTransform() };
        _rightArm.AddChild(_weaponSocket);
        if (GD.Load<PackedScene>(PlayerFactory.WeaponModelPath)?.Instantiate() is Node3D sword)
        {
            sword.Name = "IronSword";
            _weaponSocket.AddChild(sword);
        }

        // Stable semantic sockets let interaction/spell VFX attach without knowing mesh paths.
        _spellSocket = new Node3D { Name = "SpellSocket", Position = MirrorGripPoint };
        _interactionSocket = new Node3D { Name = "InteractionSocket", Position = MirrorGripPoint };
        _leftArm.AddChild(_spellSocket);
        _leftArm.AddChild(_interactionSocket);
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
            _leftArm.Scale = Vector3.One * k;
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
    private static readonly Vector3 MirrorGripPoint = new(-0.0595f, 0.1526f, -0.1343f);

    /// <summary>Where the blade points out of that fist — up and forward.</summary>
    private static readonly Vector3 BladeDirection = new(-0.10f, 0.82f, -0.56f);

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

    private void OnSpellCast(SpellCastEvent e)
    {
        if (ReferenceEquals(e.Caster, Entity) && _spellcasting is not { IsChanneling: true })
        {
            _castBeat = 1f;
        }
    }

    private void OnInteraction(InteractionPerformedEvent e)
    {
        if (ReferenceEquals(e.Instigator, Entity))
        {
            _interactionBeat = 1f;
        }
    }

    public override void _Process(double delta)
    {
        if (_root == null || _rightArm == null || _leftArm == null)
        {
            return;
        }

        bool visible = _rig?.IsFirstPerson ?? true;
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
        _castBeat = Mathf.Max(_castBeat - (dt / 0.38f), 0f);
        _interactionBeat = Mathf.Max(_interactionBeat - (dt / 0.28f), 0f);

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
        var guard = new Vector3(-0.06f * _blockBlend, 0.10f * _blockBlend, -0.02f * _blockBlend);
        float sustainedCast = _spellcasting is { IsCharging: true } or { IsChanneling: true } ? 1f : 0f;
        float cast = Mathf.Max(sustainedCast, Mathf.Sin((1f - _castBeat) * Mathf.Pi) * (_castBeat > 0f ? 1f : 0f));
        float interact = Mathf.Sin((1f - _interactionBeat) * Mathf.Pi) * (_interactionBeat > 0f ? 1f : 0f);

        // Slash arc: a smooth out-and-back curve over the swing window.
        _swing = Mathf.Max(_swing - (dt / SwingSeconds), 0f);
        float arc = Mathf.Sin((1f - _swing) * Mathf.Pi) * (_swing > 0f ? 1f : 0f);

        _rightArm.Position = RightRest + bob + guard + new Vector3(0f, 0f, -0.16f * arc);
        _rightArm.RotationDegrees = RightRestRotation + new Vector3(
            (-40f * arc) + (16f * _blockBlend),
            _swingDir * 30f * arc,
            (-15f * _blockBlend) + (_swingDir * -12f * arc));

        _leftArm.Position = LeftRest + bob + new Vector3(-guard.X, guard.Y, guard.Z)
            + new Vector3(0.05f * cast, 0.08f * cast, -0.08f * cast)
            + new Vector3(0.035f * interact, 0.025f * interact, -0.06f * interact);
        _leftArm.RotationDegrees = LeftRestRotation + new Vector3(
            (16f * _blockBlend) - (18f * cast) - (12f * interact),
            -14f * cast,
            (15f * _blockBlend) - (30f * cast) - (10f * interact));
    }
}
