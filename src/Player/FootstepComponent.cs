using Embervale.Core.Events;
using Embervale.Entities;
using Embervale.World;
using Godot;

namespace Embervale.Player;

/// <summary>
/// Plays footstep SFX as the player walks (Phase 31E): a positional cue every stride while grounded and
/// moving, varied by the surface underfoot. The pure <see cref="FootstepGait"/> paces the footfalls (so
/// cadence tracks speed) and a short downward ray reads the floor collider's <c>surface</c> metadata,
/// mapped to a cue by <see cref="Surfaces"/> (default stone when untagged). Emits through
/// <see cref="SoundCueRequestedEvent"/> so it plays on the SFX bus like any other world sound.
/// </summary>
public partial class FootstepComponent : EntityComponent
{
    /// <summary>Metres between footfalls (cadence = speed / stride).</summary>
    [Export] public float StrideDistance { get; set; } = 2.0f;

    /// <summary>Below this horizontal speed the player counts as standing still (no steps).</summary>
    [Export] public float MinSpeed { get; set; } = 0.6f;

    private readonly FootstepGait _gait = new();
    private CharacterBody3D? _body;

    protected override void OnInitialize()
    {
        _gait.Stride = StrideDistance;
        _body = Entity?.Body as CharacterBody3D;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_body == null)
        {
            return;
        }

        if (!_body.IsOnFloor())
        {
            _gait.Reset();
            return;
        }

        Vector3 velocity = _body.Velocity;
        float horizontalSpeed = new Vector2(velocity.X, velocity.Z).Length();
        if (horizontalSpeed < MinSpeed)
        {
            _gait.Reset();
            return;
        }

        if (_gait.Advance(horizontalSpeed * (float)delta))
        {
            EventBus.Instance?.Publish(new SoundCueRequestedEvent(ResolveSurfaceCue(), _body.GlobalPosition));
        }
    }

    /// <summary>Ray straight down from the feet; reads the hit collider's <c>surface</c> tag (stone default).</summary>
    private string ResolveSurfaceCue()
    {
        PhysicsDirectSpaceState3D? space = _body!.GetWorld3D()?.DirectSpaceState;
        if (space == null)
        {
            return Surfaces.DefaultCue;
        }

        Vector3 origin = _body.GlobalPosition;
        PhysicsRayQueryParameters3D query = PhysicsRayQueryParameters3D.Create(
            origin + (Vector3.Up * 0.3f), origin + (Vector3.Down * 0.6f));
        query.Exclude = new Godot.Collections.Array<Rid> { _body.GetRid() };

        Godot.Collections.Dictionary hit = space.IntersectRay(query);
        if (hit.Count > 0 && hit["collider"].As<Node>() is { } collider && collider.HasMeta("surface"))
        {
            return Surfaces.CueFromTag(collider.GetMeta("surface").AsString());
        }

        return Surfaces.DefaultCue;
    }
}
