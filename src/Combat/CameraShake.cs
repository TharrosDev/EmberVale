using Embervale.Core.Events;
using Godot;

namespace Embervale.Combat;

/// <summary>
/// Camera shake (Phase 29B): a trauma-driven kick on the punchy combat states — crit, block, stagger.
/// A child of the player's <see cref="Camera3D"/>; it offsets the camera around its rest pose each frame
/// by <see cref="ShakeMath.Amplitude"/> × noise and bleeds the trauma off, snapping back to rest at zero.
/// The camera's own local transform is otherwise untouched (mouse-look writes the body yaw and the pivot
/// pitch), so the shake doesn't fight the controls. Single-player: every live hit is the player's, so it
/// reacts to all of them without a per-entity filter.
/// </summary>
public partial class CameraShake : Node
{
    private Camera3D _camera = null!;
    private Vector3 _restPosition;
    private Vector3 _restRotation;
    private float _trauma;
    private readonly RandomNumberGenerator _rng = new();

    public override void _Ready()
    {
        _camera = GetParent<Camera3D>();
        _restPosition = _camera.Position;
        _restRotation = _camera.Rotation;
        _rng.Randomize();

        EventBus.Instance?.Subscribe<DamageDealtEvent>(OnDamage);
        EventBus.Instance?.Subscribe<EntityStaggeredEvent>(OnStaggered);
    }

    public override void _ExitTree()
    {
        EventBus.Instance?.Unsubscribe<DamageDealtEvent>(OnDamage);
        EventBus.Instance?.Unsubscribe<EntityStaggeredEvent>(OnStaggered);
    }

    private void OnDamage(DamageDealtEvent e)
    {
        if (e.IsCrit)
        {
            _trauma = ShakeMath.Add(_trauma, ShakeMath.CritTrauma);
        }
        else if (e.IsBlocked)
        {
            _trauma = ShakeMath.Add(_trauma, ShakeMath.BlockTrauma);
        }
    }

    private void OnStaggered(EntityStaggeredEvent e) =>
        _trauma = ShakeMath.Add(_trauma, ShakeMath.StaggerTrauma);

    public override void _Process(double delta)
    {
        if (_trauma <= 0f)
        {
            return;
        }

        float amplitude = ShakeMath.Amplitude(_trauma);
        _camera.Position = _restPosition + new Vector3(
            _rng.RandfRange(-1f, 1f) * amplitude * ShakeMath.MaxOffset,
            _rng.RandfRange(-1f, 1f) * amplitude * ShakeMath.MaxOffset,
            0f);
        _camera.Rotation = _restRotation + new Vector3(0f, 0f, _rng.RandfRange(-1f, 1f) * amplitude * ShakeMath.MaxRoll);

        _trauma = ShakeMath.Decay(_trauma, (float)delta);
        if (_trauma <= 0f)
        {
            _camera.Position = _restPosition;
            _camera.Rotation = _restRotation;
        }
    }
}
