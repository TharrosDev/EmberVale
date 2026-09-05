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
    /// <summary>Live source of the camera's rest position (the mode-aware pose owned by
    /// <c>PlayerCameraRig.CameraRestPosition</c>). Without it the shake snaps back to a rest
    /// captured at ready time — wrong the moment the player toggles third person.</summary>
    public System.Func<Vector3>? RestPosition { get; set; }

    private Camera3D _camera = null!;
    private Vector3 _fallbackRestPosition;
    private Vector3 _restRotation;
    private float _trauma;
    private bool _shaking;
    private readonly RandomNumberGenerator _rng = new();

    private Vector3 Rest => RestPosition?.Invoke() ?? _fallbackRestPosition;

    public override void _Ready()
    {
        _camera = GetParent<Camera3D>();
        _fallbackRestPosition = _camera.Position;
        _rng.Randomize();

        EventBus.Instance?.Subscribe<DamageDealtEvent>(OnDamage);
        EventBus.Instance?.Subscribe<EntityStaggeredEvent>(OnStaggered);
        EventBus.Instance?.Subscribe<ActionReleasedEvent>(OnActionReleased);
    }

    public override void _ExitTree()
    {
        EventBus.Instance?.Unsubscribe<DamageDealtEvent>(OnDamage);
        EventBus.Instance?.Unsubscribe<EntityStaggeredEvent>(OnStaggered);
        EventBus.Instance?.Unsubscribe<ActionReleasedEvent>(OnActionReleased);
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

    /// <summary>
    /// An action's own authored kick, on the frame it lands.
    ///
    /// ⚠️ <b>Only the PLAYER's actions shake the player's camera.</b> Every actor in the region
    /// publishes this event, and a camera that kicked for all of them would shudder continuously
    /// during any fight involving more than two people. What an enemy's blow does to the camera is
    /// already covered: it is a DamageDealtEvent when it connects.
    /// </summary>
    private void OnActionReleased(ActionReleasedEvent e)
    {
        if (_playerBody != null && ReferenceEquals(e.Actor.Body, _playerBody) &&
            e.Actor is Entities.IEntity actor &&
            actor.GetComponent<Actions.CharacterActionComponent>()?.Current is { } action)
        {
            Impulse(action.CameraImpulse);
        }
    }

    /// <summary>The body whose actions may shake this camera — the player's, injected by the
    /// factory. Null means no action ever kicks it.</summary>
    public Node? PlayerBody { get => _playerBody; set => _playerBody = value; }

    private Node? _playerBody;

    /// <summary>
    /// The one way anything else moves the camera: submit an impulse, in the same 0..1 trauma the
    /// hit reactions use.
    ///
    /// ⚠️ <b>Combat, spells and landings submit; they never write the transform.</b> Before this the
    /// only camera motion came from the two events above, so an action wanting a knock had nowhere
    /// to put it but the camera's own position — and two systems writing one transform is how a
    /// camera ends up fighting itself. <c>ActionDefinitionResource.CameraImpulse</c> is authored per
    /// action and arrives here.
    /// </summary>
    public void Impulse(float trauma)
    {
        if (trauma > 0f)
        {
            _trauma = ShakeMath.Add(_trauma, trauma);
        }
    }

    public override void _Process(double delta)
    {
        if (_trauma <= 0f)
        {
            return;
        }

        // ⚠️ RE-READ EVERY SHAKE, NOT CAPTURED IN _Ready. The rest rotation used to be sampled once
        // at build time, so a shake wrote `capturedRest + roll` — and anything that had legitimately
        // changed the camera's rotation since (a view swap, a cutscene) was silently undone the next
        // time the player took a crit. The position half already re-read its rest through a
        // delegate; this is the rotation half catching up.
        if (!_shaking)
        {
            _restRotation = _camera.Rotation;
            _shaking = true;
        }

        float amplitude = ShakeMath.Amplitude(_trauma);
        _camera.Position = Rest + new Vector3(
            _rng.RandfRange(-1f, 1f) * amplitude * ShakeMath.MaxOffset,
            _rng.RandfRange(-1f, 1f) * amplitude * ShakeMath.MaxOffset,
            0f);
        _camera.Rotation = _restRotation + new Vector3(0f, 0f, _rng.RandfRange(-1f, 1f) * amplitude * ShakeMath.MaxRoll);

        _trauma = ShakeMath.Decay(_trauma, (float)delta);
        if (_trauma <= 0f)
        {
            _camera.Position = Rest;
            _camera.Rotation = _restRotation;
            _shaking = false;
        }
    }
}
