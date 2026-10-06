using Embervale.Combat.Actions;
using Embervale.Combat;
using Embervale.Core;
using Embervale.Entities;
using Embervale.Magic;
using Embervale.Movement;
using Godot;

namespace Embervale.Player;

/// <summary>
/// Translates input into calls on the components that own the behaviour, and nothing else. It holds
/// no gameplay state: every line below reads an action and hands it to a sibling.
///
/// <para><b>Why one pump rather than a <c>_PhysicsProcess</c> per component.</b> The order here is
/// load-bearing and was documented as such long before this split: the camera rig runs inside the
/// not-playing guard because it dereferences nodes that a world teardown is freeing; focus is
/// resolved before lock-on can be toggled onto it; the mount answers the sprint request before
/// locomotion consumes it; dodge is held back (buffered) during a committed swing. Godot orders sibling
/// <c>_PhysicsProcess</c> calls by child order, which would make all of that an invisible
/// consequence of the order <see cref="PlayerFactory"/> happens to add nodes in. It is written down
/// here instead.</para>
/// </summary>
[GlobalClass]
public partial class PlayerInputRouter : EntityComponent
{
    private Node3D _yaw = null!;
    private PlayerCameraRig? _rig;
    private PlayerLookInput? _look;
    private InteractionSensor? _interaction;
    private AimController? _aim;
    private LocomotionComponent? _locomotion;
    private CharacterActionComponent? _weapon;
    private CombatComponent? _combat;
    private DodgeComponent? _dodge;
    private LockOnComponent? _lockOn;
    private MountComponent? _mount;
    private SpellcastingComponent? _spellcasting;
    private BowDrawComponent? _bowDraw;
    private AttackInputState _attackInput;

    protected override void OnInitialize()
    {
        IEntity owner = Entity!;
        _yaw = owner.Body;
        _rig = owner.GetComponent<PlayerCameraRig>();
        _look = owner.GetComponent<PlayerLookInput>();
        _interaction = owner.GetComponent<InteractionSensor>();
        _aim = owner.GetComponent<AimController>();
        _locomotion = owner.GetComponent<LocomotionComponent>();
        _weapon = owner.GetComponent<CharacterActionComponent>();
        _combat = owner.GetComponent<CombatComponent>();
        _dodge = owner.GetComponent<DodgeComponent>();
        _lockOn = owner.GetComponent<LockOnComponent>();
        _mount = owner.GetComponent<MountComponent>();
        _spellcasting = owner.GetComponent<SpellcastingComponent>();
        _bowDraw = owner.GetComponent<BowDrawComponent>();
    }

    public override void _PhysicsProcess(double delta)
    {
        if (GameManager.Instance is { IsPlaying: false })
        {
            // Not playing (paused, loading, game over): drop the focus so a target freed during this
            // window (e.g. a save/load world rebuild) can't be dereferenced as a disposed node by the
            // HUD before the raycast next refreshes it.
            _interaction?.ClearFocus();
            DropHeldInput();
            return;
        }

        // The camera rig sits inside the not-playing guard on purpose: it dereferences the injected
        // camera/pivot/aim nodes, and those are being freed during a world teardown or save/load
        // rebuild. It still runs with a non-pausing menu open (below) so the view keeps settling.
        _rig?.Tick(delta);
        _aim?.Tick();

        // A blocking menu is open: hold position, ignore combat/look so UI clicks don't also drive
        // the character. A menu now pauses the whole tree, so this is normally unreachable — it is
        // the live path for a *cinematic* lock (boss intro, opening narration), which suspends the
        // player without stopping the world.
        if (UiState.MenuOpen)
        {
            _interaction?.ClearFocus();
            DropHeldInput();
            _locomotion?.Move(delta, Vector3.Zero, sprint: false, jump: false);
            return;
        }

        if (Godot.Input.IsActionJustPressed(GameInput.ToggleCamera))
        {
            _rig?.ToggleMode();
        }

        _look?.TickStickLook(delta);
        _interaction?.UpdateFocus();

        Vector2 input = Godot.Input.GetVector(
            GameInput.MoveLeft, GameInput.MoveRight, GameInput.MoveForward, GameInput.MoveBack);

        // Orient input by the body's yaw so "forward" is where the player faces.
        Vector3 wishDir = _yaw.GlobalBasis * new Vector3(input.X, 0f, input.Y);

        // A committed action scales movement down (ActionDefinitionResource.MoveScale). The old
        // FSM restricted movement not at all, which is why every swing read as a float rather than
        // as a commitment. Applied to the wish direction rather than to the speed stat so it lasts
        // exactly as long as the action and needs no cleanup.
        Vector3 actionMove = wishDir * (_weapon?.MoveScale ?? 1f);

        if (Godot.Input.IsActionJustPressed(GameInput.Mount))
        {
            _mount?.Toggle();
        }

        // Walk is a gait on the motor, not a scale on the wish, so sprint can still override it and
        // the stick's own magnitude still counts underneath it.
        if (Godot.Input.IsActionJustPressed(GameInput.WalkToggle) && _locomotion != null)
        {
            _locomotion.Walking = !_locomotion.Walking;
        }

        // A press, not a hold: the motor buffers it (JumpAssist), so one pressed a moment before
        // landing or during a roll still fires.
        bool jump = Godot.Input.IsActionJustPressed(GameInput.Jump);

        // Held sprint is a request. On foot the motor answers it from the player's stamina (refused
        // while winded); mounted, the horse answers — it turns the raw wish (not the swing-scaled one: a mounted blow must not rein the horse
        // in) into its own heading, gait and speed, and gates the jump. Ride hands everything back
        // unchanged when not mounted, so there is no branch here.
        bool sprintHeld = Godot.Input.IsActionPressed(GameInput.Sprint);
        bool sprint = _mount?.Ride(delta, wishDir, sprintHeld, ref actionMove, ref jump) ?? sprintHeld;
        _locomotion?.Move(delta, actionMove, sprint, jump);

        // Dodge can't interrupt a committed swing (the attack commit window); DodgeComponent buffers a
        // press made inside it and fires it the instant the swing becomes cancellable.
        if (Godot.Input.IsActionJustPressed(GameInput.Dodge))
        {
            _dodge?.TryDodge(wishDir);
        }

        // Lock-on: toggle/cycle the target, drop it if dead/out of range, and face it.
        _lockOn?.Tick();
        if (Godot.Input.IsActionJustPressed(GameInput.LockOn))
        {
            _lockOn?.Toggle(_interaction?.FocusedEntity);
        }

        if (Godot.Input.IsActionJustPressed(GameInput.LockCycleNext))
        {
            _lockOn?.Cycle(1);
        }
        else if (Godot.Input.IsActionJustPressed(GameInput.LockCyclePrev))
        {
            _lockOn?.Cycle(-1);
        }

        // A committed swing may only be steered so far (ActionDefinitionResource.TurnDegreesPerSecond).
        // The lock's auto-facing is what turns the body toward a circling target, so it is what is
        // limited: the executor caps whatever rotated the body since its last tick. Free-look is
        // exempt because the body yaw IS the camera there and capping it would cap looking around.
        if (_weapon != null)
        {
            _weapon.EnforceTurnLimit = _lockOn?.Target != null;
        }

        _lockOn?.FaceTarget();

        // What the camera is FOR, read off gameplay rather than set by it — the rig resolves the
        // context itself, so nothing can put the camera in a framing that disagrees with what the
        // player is doing. Speed is measured against sprint speed, which is what the sprint lean and
        // the layers' Speed01 are.
        if (_rig != null)
        {
            Vector3 velocity = (_yaw as CharacterBody3D)?.Velocity ?? Vector3.Zero;
            float sprintSpeed = _locomotion != null ? _locomotion.BaseSpeed * _locomotion.SprintMultiplier : 0f;
            _rig.Feed(new CameraInputs(
                Aiming: Godot.Input.IsActionPressed(GameInput.Cast) ||
                        _weapon is { Weapon.IsRanged: true, IsCommitted: true },
                LockedOn: _lockOn?.Target != null,
                InCombat: _combat is { IsBlocking: true } || _weapon is { IsCommitted: true },
                Mounted: _mount is { IsMounted: true },
                Sprinting: _locomotion is { IsSprinting: true } || _mount is { IsGalloping: true },
                Grounded: _locomotion?.IsGrounded ?? true,
                Dodging: _dodge is { IsDodging: true },
                Speed01: CameraRigMath.Speed01(new Vector2(velocity.X, velocity.Z).Length(), sprintSpeed)));
        }

        // A warping action closes on whatever the player has locked. With no lock there is no
        // target and no warp, which is deliberate: an unlocked swing must not lunge at whatever
        // happens to be nearest.
        if (_weapon != null)
        {
            _weapon.WarpTarget = _lockOn?.Target?.Body;

            // Where a bow shoots: the aim controller's converged crosshair point. ⚠️ Not AimNode's
            // position — that node sits at the eye, so trusting it sent every arrow straight up.
            // RangedAttack prefers the controller's focus itself; this keeps AimPoint honest for
            // anything else that reads it.
            _weapon.AimPoint = _aim is { HasFocus: true } ? _aim.Focus : _aim?.AimNode?.GlobalPosition;
        }

        if (_combat != null)
        {
            _combat.IsBlocking = Godot.Input.IsActionPressed(GameInput.Block);
        }

        TickAttack(delta, input);

        // region combat-ranged
        // The bow's draw is how long attack stays held through the shot's startup; the arrow still
        // leaves on the action's release frame, this only sets how strong it is.
        _bowDraw?.Tick(delta, Godot.Input.IsActionPressed(GameInput.Attack));
        // endregion

        // Cast: press begins (instant fires now; charged/channeled hold), release ends.
        if (Godot.Input.IsActionJustPressed(GameInput.Cast))
        {
            _spellcasting?.BeginCast();
        }
        else if (Godot.Input.IsActionPressed(GameInput.Cast))
        {
            _spellcasting?.UpdateCast(delta);
        }

        if (Godot.Input.IsActionJustReleased(GameInput.Cast))
        {
            _spellcasting?.EndCast();
        }

        if (Godot.Input.IsActionJustPressed(GameInput.CycleSpell))
        {
            _spellcasting?.Cycle(1);
        }

        if (Godot.Input.IsActionJustPressed(GameInput.Interact))
        {
            _interaction?.TryInteract();
        }
        else if (Godot.Input.IsActionPressed(GameInput.Interact))
        {
            _interaction?.TickAutoPickup(delta);
        }
    }

    /// <summary>
    /// The attack button. A running dodge sees the press first (it buffers it until the roll's
    /// attack-cancel window, or turns it into the roll-cut). In the air a press is a plunge. On the
    /// ground a tap swings a light attack in the direction being pushed, and a hold winds a heavy that
    /// is released with the button (<see cref="AttackInputState"/>). A bow keeps the plain press.
    /// </summary>
    private void TickAttack(double delta, Vector2 input)
    {
        if (_weapon == null)
        {
            return;
        }

        bool pressed = Godot.Input.IsActionJustPressed(GameInput.Attack);
        bool held = Godot.Input.IsActionPressed(GameInput.Attack);
        bool released = Godot.Input.IsActionJustReleased(GameInput.Attack);

        if (_weapon.Weapon is { IsRanged: true })
        {
            _attackInput.Reset();
            if (pressed && !(_dodge?.InterceptAttack() ?? false))
            {
                // An empty quiver refuses the draw itself: no string pulled, no stamina spent, no
                // animation for an arrow that will not leave. RangedAttack.Fire still enforces it
                // at the release for anything that starts a shot another way.
                if (RangedAttack.HasAmmo(_weapon.Entity))
                {
                    _weapon.TryAttack();
                }
                else
                {
                    Embervale.Core.Events.EventBus.Instance?.Publish(
                        new Embervale.World.WorldHazardNoticeEvent(Embervale.Items.AmmoRules.NoAmmoReasonKey));
                }
            }

            return;
        }

        if (pressed)
        {
            if (_dodge?.InterceptAttack() ?? false)
            {
                _attackInput.Reset();
                return;
            }

            // A jump-attack dives; if it cannot (too low, no stamina) it is the ordinary swing.
            if (_locomotion is { IsGrounded: false } && _weapon.TryPlunge())
            {
                _attackInput.Reset();
                return;
            }
        }

        bool busy = _weapon.Current != null || _weapon.IsCharging;
        AttackIntent intent = _attackInput.Step(
            pressed, held, released, busy, (float)delta, ChargeRules.HoldThreshold);

        switch (intent)
        {
            case AttackIntent.Light:
                _weapon.TryAttackDirected(AttackDirections.Resolve(input));
                break;
            case AttackIntent.BeginCharge:
                if (!_weapon.BeginCharge())
                {
                    _attackInput.Abort();
                }

                break;
            case AttackIntent.Release:
                _weapon.ReleaseCharge();
                break;
        }
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

        _weapon?.CancelCharge();
        _attackInput.Reset();
        _spellcasting?.CancelCast(); // drop any charge/channel so it doesn't fire after a menu/pause
    }
}
