using Embervale.Core.Events;
using Embervale.Entities;
using Godot;

namespace Embervale.Combat;

/// <summary>
/// Directional hit reaction (Phase 29B, deepened): when this entity is struck, its visual mesh lurches
/// in the direction the blow came from (source → target) and its body leans back from it, then a damped
/// spring settles it — so weight reads. How far and how long depends on the outcome
/// (<see cref="HitReactionMath"/>): a block is a shove, a hit a lurch, a guard break or a poise break
/// rocks the whole body back, and a <em>parried attacker</em> reels away from the defender. Visual-only:
/// it offsets and tilts the mesh's local transform, never the <c>CharacterBody3D</c>, so it can't fight
/// the movement motor. Consumes <see cref="HitConfirmedEvent"/>, so it needs the
/// <see cref="CombatFeedbackDirector"/> in the session.
/// </summary>
[GlobalClass]
public partial class HitReactionComponent : EntityComponent
{
    /// <summary>How far the mesh lurches on a hit (metres).</summary>
    [Export] public float RecoilDistance { get; set; } = 0.18f;

    /// <summary>Seconds for the lurch to ease back to rest.</summary>
    [Export] public float RecoilReturn { get; set; } = 0.18f;

    private Node3D? _mesh;
    private Vector3 _restPosition;
    private Basis _restBasis = Basis.Identity;
    private Vector3 _offset;
    private Vector3 _velocity;
    private Vector3 _leanAxis = Vector3.Up;
    private float _lean;
    private float _leanVelocity;
    private float _settleSeconds = 0.18f;

    /// <summary>Where the mesh sits when it is not lurching. Sampled at spawn and re-sampled at each
    /// recoil, but a component that moves the mesh <em>during</em> a recoil must write it here —
    /// this component owns the mesh's position for those 0.18 s and would otherwise put it back
    /// where it used to be. <see cref="Movement.MountComponent"/> is the one caller.</summary>
    public Vector3 Rest
    {
        get => _restPosition;
        set => _restPosition = value;
    }

    protected override void OnInitialize()
    {
        _mesh = FindMesh(Entity!.Body);
        if (_mesh != null)
        {
            _restPosition = _mesh.Position;
            _restBasis = _mesh.Basis;
        }

        EventBus.Instance?.Subscribe<HitConfirmedEvent>(OnHit);
    }

    protected override void OnTeardown() => EventBus.Instance?.Unsubscribe<HitConfirmedEvent>(OnHit);

    /// <summary>The actor's visual root: the conventional "BodyMesh"/"Mesh" child (a plain
    /// <see cref="Node3D"/> since the 30B/30D glTF models — their meshes nest under a scene root),
    /// else the first <see cref="MeshInstance3D"/> child (legacy stand-in capsules).</summary>
    private static Node3D? FindMesh(Node body)
    {
        if (body.GetNodeOrNull<Node3D>("BodyMesh") is { } bodyMesh)
        {
            return bodyMesh;
        }

        if (body.GetNodeOrNull<Node3D>("Mesh") is { } mesh)
        {
            return mesh;
        }

        foreach (Node child in body.GetChildren())
        {
            if (child is MeshInstance3D meshChild)
            {
                return meshChild;
            }
        }

        return null;
    }

    private void OnHit(HitConfirmedEvent e)
    {
        if (_mesh == null || Entity == null || !GodotObject.IsInstanceValid(_mesh))
        {
            return;
        }

        // The defender takes the lurch; on a parry the ATTACKER is the one thrown back, which is the
        // read that says the exchange went the other way.
        bool parriedAttacker = e.Outcome == HitOutcome.Parried && ReferenceEquals(e.Source, Entity);
        if (!ReferenceEquals(e.Target, Entity) && !parriedAttacker)
        {
            return;
        }

        if (e.Outcome == HitOutcome.Parried && !parriedAttacker)
        {
            return; // the defender who parried stands firm
        }

        Vector3 dir;
        IEntity? from = parriedAttacker ? e.Target : e.Source;
        if (from != null && GodotObject.IsInstanceValid(from.Body))
        {
            dir = Entity.Body.GlobalPosition - from.Body.GlobalPosition;
        }
        else
        {
            dir = Entity.Body.GlobalTransform.Basis.Z; // pushed backward when the source is unknown
        }

        dir.Y = 0f;
        dir = dir.LengthSquared() > 0.0001f ? dir.Normalized() : Vector3.Back;

        // ⚠️ THE REST POSE IS RE-READ HERE, NOT CACHED AT SPAWN (39B), AND THAT IS A BUG FIX.
        // This component owns the mesh's position for the length of a recoil and puts it back
        // afterwards — which was correct while nothing else ever moved that mesh. 39A's
        // MountComponent raises the same BodyMesh to the saddle, so a rest captured in OnInitialize
        // is (0,0,0) and the FIRST HIT TAKEN WHILE MOUNTED slammed the rider down to the horse's
        // hooves and left them there for the rest of the ride.
        //
        // Invariant 7's shape exactly: a component cached a value another component now writes, and
        // the symptom named neither of them. The fix is here rather than in MountComponent because
        // every future thing that moves a body mesh — a cutscene pose, a knockdown, a vehicle —
        // inherits it, and a MountComponent-pokes-HitReaction fix would have to be written again
        // each time. Sampled only when the mesh is AT rest, so a second hit mid-recoil cannot
        // capture the lurch as the new rest and walk the mesh away one hit at a time.
        if (_offset.LengthSquared() < 0.000001f && Mathf.Abs(_lean) < 0.0005f)
        {
            _restPosition = _mesh.Position;
            _restBasis = _mesh.Basis;
        }

        Recoil recoil = HitReactionMath.For(e.Outcome, e.Kind, e.Amount, e.Staggered, RecoilDistance);
        _offset = dir * recoil.Distance;
        _velocity = Vector3.Zero;

        // The lean axis is a world axis; the mesh's transform is in its parent's space.
        Vector3 axis = HitReactionMath.LeanAxis(dir);
        if (axis != Vector3.Zero)
        {
            _leanAxis = (Entity.Body.GlobalTransform.Basis.Inverse() * axis).Normalized();
            _lean = recoil.LeanRadians;
            _leanVelocity = 0f;
        }

        // RecoilReturn is the authored floor: a light hit settles in that time, a heavy one takes longer.
        _settleSeconds = Mathf.Max(RecoilReturn, recoil.SettleSeconds);

        // The spring only runs while there is a recoil to settle; this is what starts it again.
        SetProcess(true);
    }

    public override void _Process(double delta)
    {
        if (_mesh == null || !GodotObject.IsInstanceValid(_mesh))
        {
            SetProcess(false); // OnHit refuses a missing mesh too, so nothing will ever need this tick
            return;
        }

        bool moving = _offset.LengthSquared() >= 0.000001f || _velocity.LengthSquared() >= 0.000001f ||
                      Mathf.Abs(_lean) >= 0.0005f || Mathf.Abs(_leanVelocity) >= 0.0005f;
        if (!moving)
        {
            // Settled. Every actor carries one of these and is at rest unless it was just struck,
            // so stop being called until the next confirmed hit (OnHit) starts a recoil.
            SetProcess(false);
            return;
        }

        // A damped spring settles the offset and the lean back to rest, overshooting once on a heavy
        // blow. Delta is 0 during a hit-stop, so the pose holds for the freeze and then plays out.
        float dt = (float)delta;
        (_offset, _velocity) = HitReactionMath.Step(_offset, _velocity, dt, _settleSeconds);
        (_lean, _leanVelocity) = HitReactionMath.Step(_lean, _leanVelocity, dt, _settleSeconds);

        if (_offset.LengthSquared() < 0.000001f && _velocity.LengthSquared() < 0.000001f)
        {
            _offset = Vector3.Zero;
            _velocity = Vector3.Zero;
        }

        if (Mathf.Abs(_lean) < 0.0005f && Mathf.Abs(_leanVelocity) < 0.0005f)
        {
            _lean = 0f;
            _leanVelocity = 0f;
        }

        _mesh.Position = _restPosition + _offset;
        _mesh.Basis = _lean == 0f ? _restBasis : new Basis(_leanAxis, _lean) * _restBasis;
    }
}
