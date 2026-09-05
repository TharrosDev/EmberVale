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
/// locomotion consumes it; dodge is refused during a committed swing. Godot orders sibling
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

        bool jump = Godot.Input.IsActionJustPressed(GameInput.Jump);

        // Held sprint is a request. On foot it is granted outright; mounted, the horse's own pool
        // answers — Tick returns the input unchanged when not mounted, so there is no branch here.
        bool sprint = _mount?.Tick(delta, Godot.Input.IsActionPressed(GameInput.Sprint))
            ?? Godot.Input.IsActionPressed(GameInput.Sprint);
        _locomotion?.Move(delta, actionMove, sprint, jump);

        // Dodge can't interrupt a committed swing (the attack commit window); it cancels
        // recovery/idle.
        if (Godot.Input.IsActionJustPressed(GameInput.Dodge) && !(_weapon?.IsCommitted ?? false))
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

        _lockOn?.FaceTarget();

        // A warping action closes on whatever the player has locked. With no lock there is no
        // target and no warp, which is deliberate: an unlocked swing must not lunge at whatever
        // happens to be nearest.
        if (_weapon != null)
        {
            _weapon.WarpTarget = _lockOn?.Target?.Body;
        }

        if (_combat != null)
        {
            _combat.IsBlocking = Godot.Input.IsActionPressed(GameInput.Block);
        }

        if (Godot.Input.IsActionJustPressed(GameInput.Attack))
        {
            _weapon?.TryAttack();
        }

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

    /// <summary>Releases continuous input state when control is suspended (menu open / not playing),
    /// so a guard held when the menu opened can't strand as "blocking" — the live input is re-read on
    /// the first frame back in control.</summary>
    private void DropHeldInput()
    {
        if (_combat != null)
        {
            _combat.IsBlocking = false;
        }

        _spellcasting?.CancelCast(); // drop any charge/channel so it doesn't fire after a menu/pause
    }
}
