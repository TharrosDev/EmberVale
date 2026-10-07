using Embervale.Combat;
using Embervale.Combat.Actions;
using Embervale.Core;
using Embervale.Core.Services;
using Embervale.Entities;
using Embervale.Magic;
using Embervale.Movement;
using Embervale.Settings;
using Godot;

namespace Embervale.Player;

/// <summary>
/// Smooths what the player sees between physics ticks. The body moves at the physics rate and the
/// screen draws faster, and the look is applied per mouse event, so a camera that only moved with
/// the body stepped while the view turned smoothly: the first-person "glitch". Each drawn frame the
/// camera pivot and the visible body are offset by the interpolation residual between the last two
/// physics positions (<see cref="VisualSmoothing.Residual"/>), and the offset is taken off again
/// before anything in a physics tick runs, so aim, sweeps and hitboxes read exact positions.
/// Presentation only: it never moves the body, and there is no project-wide physics interpolation.
///
/// <para>It also owns the third-person yaw lag: the visible body trails a camera turn by a tenth of
/// a second (<see cref="VisualSmoothing.YawLagStep"/>) instead of pivoting rigidly under the
/// camera. Body yaw is still camera yaw; only the mesh trails.</para>
///
/// <para>It is added before the animation component and foot IK, so it has run before they read the
/// rig.</para>
///
/// <para>⚠️ <b>The body's offset goes on its skeleton, not on <see cref="BodyMesh"/>.</b>
/// <c>HitReactionComponent</c> and <c>MountComponent</c> both write the mesh root's position and
/// rotation outright, and both sample it as "rest"; an offset left on that node would be captured
/// into a hit's rest pose or a rider's base yaw. The skeleton under it is nobody else's, and moving
/// it moves the skin, the bone attachments and the socket followers together.</para>
/// </summary>
[GlobalClass]
public partial class PlayerVisualSmoother : EntityComponent
{
    /// <summary>The pitch pivot the camera rides. Injected by the factory.</summary>
    public Node3D? CameraPivot { get; set; }

    /// <summary>The visible body. Injected by the factory.</summary>
    public Node3D? BodyMesh { get; set; }

    /// <summary>The residual currently applied, in world space. Zero during a physics tick. Read by
    /// the camera probe.</summary>
    public Vector3 AppliedOffset { get; private set; }

    /// <summary>How far the visible body's yaw currently trails the real one, in radians.</summary>
    public float YawLag { get; private set; }

    private Node3D? _body;
    private Node3D? _visual;
    private Node3D? _visualParent;
    private Transform3D _visualRest = Transform3D.Identity;
    private bool _visualResolved;
    private bool _visualMoved;

    private Vector3 _from;
    private bool _hasFrom;

    private Vector3 _pivotBase;
    private Vector3 _pivotWritten;
    private bool _pivotOffset;

    private float _lastYaw;
    private bool _hasYaw;

    /// <summary>The yaw the factory gave the visible body (the half turn that faces a glTF model
    /// the way the capsule faces), and whether there is a body to hold to it.</summary>
    private float _meshYaw;
    private bool _hasMeshYaw;

    /// <summary>How far the body's yaw may be from <see cref="_meshYaw"/> before it is put back
    /// (radians). Wide: a hit's lean reads back as a degree or two of yaw and is not this.</summary>
    private const float MeshYawTolerance = 0.5f;

    private PlayerCameraRig? _rig;
    private LockOnComponent? _lockOn;
    private CharacterActionComponent? _weapon;
    private DodgeComponent? _dodge;
    private MountComponent? _mount;
    private SpellcastingComponent? _spellcasting;
    private SettingsService? _settings;

    protected override void OnInitialize()
    {
        IEntity owner = Entity!;
        _body = owner.Body;
        _rig = owner.GetComponent<PlayerCameraRig>();
        _lockOn = owner.GetComponent<LockOnComponent>();
        _weapon = owner.GetComponent<CharacterActionComponent>();
        _dodge = owner.GetComponent<DodgeComponent>();
        _mount = owner.GetComponent<MountComponent>();
        _spellcasting = owner.GetComponent<SpellcastingComponent>();
        _settings = ServiceLocator.Instance is { } locator && locator.TryGet(out SettingsService settings)
            ? settings
            : null;

        if (BodyMesh != null && GodotObject.IsInstanceValid(BodyMesh))
        {
            _meshYaw = BodyMesh.Rotation.Y;
            _hasMeshYaw = true;
        }

        // Ahead of every other physics callback in the tree, not just the siblings': an enemy's
        // hitbox that lands a blow this tick has to find the player's rig where it really is.
        ProcessPhysicsPriority = -1000;

        // Kept running through a pause so the offset can be taken off when one starts: the rig ticks
        // through a paused dialogue and should find the pivot where it really is.
        ProcessMode = ProcessModeEnum.Always;
    }

    protected override void OnTeardown()
    {
        ClearOffset();
        RestoreVisual();
        _visual = null;
        _visualParent = null;
        _body = null;
    }

    /// <summary>The start of a physics tick: the offset comes off, and where the body is now is where
    /// this tick's step starts from.</summary>
    public override void _PhysicsProcess(double delta)
    {
        if (_body == null || !GodotObject.IsInstanceValid(_body))
        {
            return;
        }

        ClearOffset();
        _from = _body.GlobalPosition;
        _hasFrom = true;
    }

    public override void _Process(double delta)
    {
        if (_body == null || !GodotObject.IsInstanceValid(_body))
        {
            return;
        }

        if (GameManager.Instance is { IsPlaying: false } || GetTree().Paused)
        {
            // Not playing, or paused: the world is being loaded, torn down or held still. Draw
            // things where they are, and start again from the next physics tick.
            ClearOffset();
            _hasFrom = false;
            _hasYaw = false;
            return;
        }

        ResolveVisual();
        HoldMeshYaw();

        Vector3 residual = _hasFrom
            ? VisualSmoothing.Residual(
                _from, _body.GlobalPosition, (float)Engine.GetPhysicsInterpolationFraction())
            : Vector3.Zero;

        TickYawLag((float)delta);
        ApplyPivot(residual);
        ApplyVisual(residual);
        AppliedOffset = residual;
    }

    /// <summary>
    /// Puts the visible body back on the yaw the factory gave it whenever nobody is riding.
    ///
    /// <para>⚠️ <b>This is a guard for a defect in <c>MountComponent</c>, and it goes when that is
    /// fixed.</b> <c>MountComponent.Load</c> strips the rider state on every load, mounted or not,
    /// and stripping "restores" the mesh's yaw to a base it only ever captured on mounting, which
    /// is zero for a player who has not ridden this session. Zero is the model's own +Z, the
    /// capsule's BACK: every loaded save drew the player facing the camera in third person, with
    /// its casting arm behind the first-person eye and its back where its chest should be when
    /// looking down. Nothing logged it and no probe saw it, because the probes build a body with
    /// no mount component. While mounted the yaw is the mount's and is left alone.</para>
    /// </summary>
    private void HoldMeshYaw()
    {
        if (!_hasMeshYaw || BodyMesh == null || !GodotObject.IsInstanceValid(BodyMesh) ||
            _mount is { IsMounted: true })
        {
            return;
        }

        Vector3 rotation = BodyMesh.Rotation;
        if (Mathf.Abs(VisualSmoothing.WrapAngle(rotation.Y - _meshYaw)) > MeshYawTolerance)
        {
            BodyMesh.Rotation = new Vector3(rotation.X, _meshYaw, rotation.Z);
        }
    }

    /// <summary>The visible body trails a turn of the camera in third person and nowhere else: in
    /// first person the arms are the view, a lock or an action has the body facing something on
    /// purpose, and a rider's mesh is turned by the mount.</summary>
    private void TickYawLag(float dt)
    {
        float yaw = _body!.GlobalRotation.Y;
        float turned = _hasYaw ? VisualSmoothing.WrapAngle(yaw - _lastYaw) : 0f;
        _lastYaw = yaw;
        _hasYaw = true;

        bool enabled =
            _visual != null &&
            _rig is { IsFirstPerson: false } &&
            _lockOn?.Target == null &&
            _weapon is not { IsCommitted: true } &&
            _dodge is not { IsDodging: true } &&
            _mount is not { IsMounted: true } &&
            _spellcasting is not ({ IsCharging: true } or { IsChanneling: true }) &&
            !(_settings?.Current.ReducedMotion ?? false);

        YawLag = VisualSmoothing.YawLagStep(YawLag, turned, dt, enabled);
    }

    /// <summary>
    /// Offsets the camera pivot. <c>MountComponent</c> writes the pivot's height when the player
    /// mounts and dismounts (and writes its X and Z back as it found them), so the pivot's own
    /// position is remembered separately and re-read, axis by axis, wherever something else has
    /// changed it; the offset is never folded into it.
    /// </summary>
    private void ApplyPivot(Vector3 residual)
    {
        if (CameraPivot == null || !GodotObject.IsInstanceValid(CameraPivot))
        {
            return;
        }

        Vector3 current = CameraPivot.Position;
        _pivotBase = _pivotOffset ? Unoffset(current) : current;

        if (residual == Vector3.Zero)
        {
            if (_pivotOffset)
            {
                CameraPivot.Position = _pivotBase;
                _pivotOffset = false;
            }

            return;
        }

        // The pivot is the body's child, so the offset goes in the body's frame.
        CameraPivot.Position = _pivotBase + (_body!.GlobalBasis.Inverse() * residual);
        _pivotWritten = CameraPivot.Position;
        _pivotOffset = true;
    }

    private void ApplyVisual(Vector3 residual)
    {
        if (_visual == null || !GodotObject.IsInstanceValid(_visual) ||
            _visualParent == null || !GodotObject.IsInstanceValid(_visualParent))
        {
            return;
        }

        if (residual == Vector3.Zero && YawLag == 0f)
        {
            RestoreVisual();
            return;
        }

        Basis parent = _visualParent.GlobalBasis;
        Basis basis = _visualRest.Basis;
        if (YawLag != 0f)
        {
            // A turn about the body's up axis, expressed in the skeleton's parent frame.
            Basis frame = parent.Orthonormalized();
            Basis turn = new(_body!.GlobalBasis.Y.Normalized(), YawLag);
            basis = frame.Transposed() * turn * frame * basis;
        }

        _visual.Transform = new Transform3D(basis, _visualRest.Origin + (parent.Inverse() * residual));
        _visualMoved = true;
    }

    /// <summary>Takes the position offset off both nodes. The yaw lag stays: it is visual only and
    /// nothing in a physics tick reads the mesh's facing.</summary>
    private void ClearOffset()
    {
        AppliedOffset = Vector3.Zero;

        if (_pivotOffset && CameraPivot != null && GodotObject.IsInstanceValid(CameraPivot))
        {
            CameraPivot.Position = Unoffset(CameraPivot.Position);
            _pivotOffset = false;
        }

        if (_visualMoved)
        {
            ApplyVisual(Vector3.Zero);
        }
    }

    /// <summary>The pivot's own position, given where it is now and that the offset was last
    /// written as <see cref="_pivotWritten"/>: an axis nothing else has touched goes back to its
    /// remembered value, and one something else has written is taken as it stands.</summary>
    private Vector3 Unoffset(Vector3 current) => new(
        current.X == _pivotWritten.X ? _pivotBase.X : current.X,
        current.Y == _pivotWritten.Y ? _pivotBase.Y : current.Y,
        current.Z == _pivotWritten.Z ? _pivotBase.Z : current.Z);

    private void RestoreVisual()
    {
        if (_visualMoved && _visual != null && GodotObject.IsInstanceValid(_visual))
        {
            _visual.Transform = _visualRest;
        }

        _visualMoved = false;
    }

    /// <summary>Finds the skeleton under the body once it exists. A body with none (the stand-in
    /// capsule) is not offset at all: its root is the hit reaction's to write.</summary>
    private void ResolveVisual()
    {
        if (_visualResolved)
        {
            return;
        }

        if (BodyMesh == null || !GodotObject.IsInstanceValid(BodyMesh) || !BodyMesh.IsInsideTree())
        {
            return;
        }

        _visualResolved = true;
        if (FindSkeleton(BodyMesh) is { } skeleton && skeleton.GetParent() is Node3D parent)
        {
            _visual = skeleton;
            _visualParent = parent;
            _visualRest = skeleton.Transform;
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
}
