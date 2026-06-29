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

    /// <summary>The body's first <see cref="MeshInstance3D"/> child (the actor's visual), or null.</summary>
    private static Node3D? FindMesh(Node body)
    {
        foreach (Node child in body.GetChildren())
        {
            if (child is MeshInstance3D mesh)
            {
                return mesh;
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
