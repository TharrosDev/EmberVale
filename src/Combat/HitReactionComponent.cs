using Embervale.Core.Events;
using Embervale.Entities;
using Godot;

namespace Embervale.Combat;

/// <summary>
/// Directional hit reaction (Phase 29B): when this entity is struck, its visual mesh lurches in the
/// direction the blow came from (source → target) and eases back. Visual-only — it offsets the mesh's
/// local position, never the <c>CharacterBody3D</c>, so it can't fight the movement motor. Works for any
/// hit, melee or arrow, since <see cref="DamageDealtEvent"/> carries the attacker as <c>Source</c>.
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
    private Vector3 _offset;

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
        }

        EventBus.Instance?.Subscribe<DamageDealtEvent>(OnDamage);
    }

    protected override void OnTeardown() => EventBus.Instance?.Unsubscribe<DamageDealtEvent>(OnDamage);

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

    private void OnDamage(DamageDealtEvent e)
    {
        if (_mesh == null || !ReferenceEquals(e.Target, Entity))
        {
            return;
        }

        Vector3 dir;
        if (e.Source != null)
        {
            dir = Entity!.Body.GlobalPosition - e.Source.Body.GlobalPosition;
        }
        else
        {
            dir = Entity!.Body.GlobalTransform.Basis.Z; // pushed backward when the source is unknown
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
        if (_offset.LengthSquared() < 0.000001f)
        {
            _restPosition = _mesh.Position;
        }

        _offset = dir * RecoilDistance;
    }

    public override void _Process(double delta)
    {
        if (_mesh == null || _offset.LengthSquared() < 0.000001f)
        {
            return;
        }

        // Ease the offset back to zero, then write mesh = rest + offset.
        float t = RecoilReturn > 0f ? Mathf.Clamp((float)delta / RecoilReturn, 0f, 1f) : 1f;
        _offset = _offset.Lerp(Vector3.Zero, t);
        if (_offset.LengthSquared() < 0.000001f)
        {
            _offset = Vector3.Zero;
        }

        _mesh.Position = _restPosition + _offset;
    }
}
