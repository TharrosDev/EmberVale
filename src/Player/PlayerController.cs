using Embervale.Combat;
using Embervale.Core;
using Embervale.Core.Events;
using Embervale.Entities;
using Embervale.Interaction;
using Embervale.Movement;
using Godot;

namespace Embervale.Player;

/// <summary>
/// First-person player input + camera component. It reads the <see cref="GameInput"/>
/// actions, drives the sibling <see cref="LocomotionComponent"/>, applies
/// mouse-look (yaw on the body, pitch on the camera pivot), and routes attack and
/// block input into the combat components (<see cref="MeleeWeaponComponent"/> and
/// <see cref="CombatComponent"/>).
///
/// The camera pivot is injected by <see cref="PlayerFactory"/> so the component
/// does not assume a specific scene path.
/// </summary>
[GlobalClass]
public partial class PlayerController : EntityComponent
{
    [Export]
    public float MouseSensitivity { get; set; } = 0.0028f;

    [Export]
    public float InteractRange { get; set; } = 3f;

    /// <summary>Pitch node (rotated up/down). The camera is its child.</summary>
    public Node3D? CameraPivot { get; set; }

    private Node3D _yaw = null!;
    private LocomotionComponent? _locomotion;
    private MeleeWeaponComponent? _weapon;
    private CombatComponent? _combat;
    private float _pitch;

    protected override void OnInitialize()
    {
        IEntity owner = Entity!;
        _yaw = owner.Body;
        _locomotion = owner.GetComponent<LocomotionComponent>();
        _weapon = owner.GetComponent<MeleeWeaponComponent>();
        _combat = owner.GetComponent<CombatComponent>();

        EventBus.Instance?.Subscribe<GameStateChangedEvent>(OnGameStateChanged);
        CaptureMouse(true);
    }

    protected override void OnTeardown()
    {
        EventBus.Instance?.Unsubscribe<GameStateChangedEvent>(OnGameStateChanged);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (GameManager.Instance is { IsPlaying: false })
        {
            return;
        }

        // A blocking menu (inventory) is open: hold position, ignore combat/look
        // so UI clicks don't also drive the character.
        if (UiState.MenuOpen)
        {
            _locomotion?.Move(delta, Vector3.Zero, sprint: false, jump: false);
            return;
        }

        Vector2 input = Godot.Input.GetVector(
            GameInput.MoveLeft, GameInput.MoveRight, GameInput.MoveForward, GameInput.MoveBack);

        // Orient input by the body's yaw so "forward" is where the player faces.
        Vector3 wishDir = _yaw.GlobalBasis * new Vector3(input.X, 0f, input.Y);

        bool sprint = Godot.Input.IsActionPressed(GameInput.Sprint);
        bool jump = Godot.Input.IsActionJustPressed(GameInput.Jump);
        _locomotion?.Move(delta, wishDir, sprint, jump);

        if (_combat != null)
        {
            _combat.IsBlocking = Godot.Input.IsActionPressed(GameInput.Block);
        }

        if (Godot.Input.IsActionJustPressed(GameInput.Attack))
        {
            _weapon?.TryAttack();
        }

        if (Godot.Input.IsActionJustPressed(GameInput.Interact))
        {
            TryInteract();
        }
    }

    private void TryInteract()
    {
        if (CameraPivot == null || Entity?.Body is not CharacterBody3D body)
        {
            return;
        }

        PhysicsDirectSpaceState3D space = body.GetWorld3D().DirectSpaceState;
        Vector3 from = CameraPivot.GlobalPosition;
        Vector3 to = from + (-CameraPivot.GlobalTransform.Basis.Z * InteractRange);

        var query = PhysicsRayQueryParameters3D.Create(from, to);
        query.Exclude = new Godot.Collections.Array<Rid> { body.GetRid() };

        Godot.Collections.Dictionary hit = space.IntersectRay(query);
        if (hit.Count == 0 || hit["collider"].AsGodotObject() is not Node collider)
        {
            return;
        }

        IEntity? target = EntityNode.FindOwner(collider);
        target?.GetComponent<InteractableComponent>()?.Interact(Entity!);
    }

    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventMouseMotion motion &&
            Godot.Input.MouseMode == Godot.Input.MouseModeEnum.Captured)
        {
            _yaw.RotateY(-motion.Relative.X * MouseSensitivity);

            _pitch = Mathf.Clamp(_pitch - (motion.Relative.Y * MouseSensitivity), -1.45f, 1.45f);
            if (CameraPivot != null)
            {
                CameraPivot.Rotation = new Vector3(_pitch, 0f, 0f);
            }
        }
    }

    private void OnGameStateChanged(GameStateChangedEvent e)
    {
        CaptureMouse(e.Current == GameState.Playing);
    }

    private static void CaptureMouse(bool captured)
    {
        Godot.Input.MouseMode = captured
            ? Godot.Input.MouseModeEnum.Captured
            : Godot.Input.MouseModeEnum.Visible;
    }
}
