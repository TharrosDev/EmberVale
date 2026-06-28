using Embervale.Combat;
using Embervale.Core;
using Embervale.Core.Events;
using Embervale.Core.Services;
using Embervale.Entities;
using Embervale.Interaction;
using Embervale.Items;
using Embervale.Magic;
using Embervale.Movement;
using Embervale.Settings;
using Godot;

namespace Embervale.Player;

/// <summary>
/// Third-person player input + camera component. It reads the <see cref="GameInput"/>
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
    /// <summary>Base radians-per-pixel look sensitivity; the player's settings multiplier (24F slider)
    /// scales this at runtime (Phase 25.5D).</summary>
    [Export]
    public float MouseSensitivity { get; set; } = 0.0028f;

    [Export]
    public float InteractRange { get; set; } = 3f;

    /// <summary>Radius of the hold-E auto-pickup sweep, and how often it runs while E is held.</summary>
    private const float AutoPickupRadius = 3.5f;
    private const double AutoPickupInterval = 0.12;
    private double _autoPickupTimer;

    /// <summary>Pitch clamp (radians) so the camera can't flip over the top/bottom.</summary>
    private const float PitchLimit = 1.45f;

    /// <summary>Pitch node (rotated up/down). The camera is its child.</summary>
    public Node3D? CameraPivot { get; set; }

    private Node3D _yaw = null!;
    private LocomotionComponent? _locomotion;
    private MeleeWeaponComponent? _weapon;
    private CombatComponent? _combat;
    private SpellcastingComponent? _spellcasting;
    private SettingsService? _settings;
    private float _pitch;

    /// <summary>The entity the player is currently looking at within interact range, if any.
    /// Updated each frame; read by the game HUD for a nameplate / interaction prompt.</summary>
    public IEntity? FocusedEntity { get; private set; }

    /// <summary>The interactable on the focused entity (null if it can't be interacted with).</summary>
    public InteractableComponent? FocusedInteractable { get; private set; }

    /// <summary>The prompt to show for the focused interactable, or null.</summary>
    public string? FocusPrompt => FocusedInteractable?.Prompt;

    protected override void OnInitialize()
    {
        IEntity owner = Entity!;
        _yaw = owner.Body;
        _locomotion = owner.GetComponent<LocomotionComponent>();
        _weapon = owner.GetComponent<MeleeWeaponComponent>();
        _combat = owner.GetComponent<CombatComponent>();
        _spellcasting = owner.GetComponent<SpellcastingComponent>();
        _settings = ServiceLocator.Instance is { } locator && locator.TryGet(out SettingsService settings)
            ? settings
            : null;

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
            // Not playing (paused, loading, game over): drop the focus so a target freed
            // during this window (e.g. a save/load world rebuild) can't be dereferenced as a
            // disposed node by the HUD before the raycast next refreshes it.
            ClearFocus();
            DropHeldInput();
            return;
        }

        // A blocking menu (inventory) is open: hold position, ignore combat/look
        // so UI clicks don't also drive the character.
        if (UiState.MenuOpen)
        {
            ClearFocus();
            DropHeldInput();
            _locomotion?.Move(delta, Vector3.Zero, sprint: false, jump: false);
            return;
        }

        UpdateFocus();

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

        if (Godot.Input.IsActionJustPressed(GameInput.Cast))
        {
            _spellcasting?.TryCast();
        }

        if (Godot.Input.IsActionJustPressed(GameInput.CycleSpell))
        {
            _spellcasting?.Cycle(1);
        }

        if (Godot.Input.IsActionJustPressed(GameInput.Interact))
        {
            FocusedInteractable?.Interact(Entity!);
            _autoPickupTimer = AutoPickupInterval; // brief grace before the held sweep kicks in
        }
        else if (Godot.Input.IsActionPressed(GameInput.Interact))
        {
            // Hold E to vacuum nearby loot — saves tapping E per item when a kill drops a pile.
            // Runs only on non-just-pressed frames so it never double-collects the focused item.
            _autoPickupTimer -= delta;
            if (_autoPickupTimer <= 0d)
            {
                _autoPickupTimer = AutoPickupInterval;
                AutoPickupNearby();
            }
        }
    }

    /// <summary>Collects every <see cref="ItemPickupComponent"/> within <see cref="AutoPickupRadius"/>
    /// of the player (a physics sphere sweep). Pickups free themselves when emptied, so each is taken
    /// once per sweep and gone by the next.</summary>
    private void AutoPickupNearby()
    {
        if (Entity?.Body is not CharacterBody3D body)
        {
            return;
        }

        PhysicsDirectSpaceState3D space = body.GetWorld3D().DirectSpaceState;
        var query = new PhysicsShapeQueryParameters3D
        {
            Shape = new SphereShape3D { Radius = AutoPickupRadius },
            Transform = new Transform3D(Basis.Identity, body.GlobalPosition),
            CollideWithAreas = false,
            CollideWithBodies = true,
            Exclude = new Godot.Collections.Array<Rid> { body.GetRid() },
        };

        foreach (Godot.Collections.Dictionary hit in space.IntersectShape(query, maxResults: 24))
        {
            if (hit["collider"].AsGodotObject() is Node collider &&
                EntityNode.FindOwner(collider)?.GetComponent<ItemPickupComponent>() is { } pickup)
            {
                pickup.Interact(Entity!);
            }
        }
    }

    /// <summary>Raycasts from the camera and records what the player is looking at, so the HUD
    /// can show a nameplate / interaction prompt and <c>E</c> acts on the same target.</summary>
    private void UpdateFocus()
    {
        if (CameraPivot == null || Entity?.Body is not CharacterBody3D body)
        {
            ClearFocus();
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
            ClearFocus();
            return;
        }

        FocusedEntity = EntityNode.FindOwner(collider);
        FocusedInteractable = FocusedEntity?.GetComponent<InteractableComponent>();
    }

    /// <summary>Releases continuous input state when control is suspended (menu open / not playing),
    /// so a guard held when the menu opened can't strand as "blocking" — the live input is re-read on
    /// the first frame back in control.</summary>
    private void DropHeldInput()
    {
        if (_combat != null)
        {
            _combat.IsBlocking = false;
        }
    }

    private void ClearFocus()
    {
        FocusedEntity = null;
        FocusedInteractable = null;
    }

    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventMouseMotion motion &&
            Godot.Input.MouseMode == Godot.Input.MouseModeEnum.Captured)
        {
            float multiplier = _settings?.Current.MouseSensitivity ?? 1f;
            bool invertY = _settings?.Current.InvertY ?? false;

            _yaw.RotateY(-SettingsMath.LookStep(motion.Relative.X, MouseSensitivity, multiplier));
            _pitch = SettingsMath.ApplyPitch(
                _pitch, SettingsMath.LookStep(motion.Relative.Y, MouseSensitivity, multiplier), invertY, PitchLimit);
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
