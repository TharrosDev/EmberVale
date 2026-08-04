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
/// Player input + camera component. It reads the <see cref="GameInput"/>
/// actions, drives the sibling <see cref="LocomotionComponent"/>, applies
/// mouse-look (yaw on the body, pitch on the camera pivot), and routes attack and
/// block input into the combat components (<see cref="MeleeWeaponComponent"/> and
/// <see cref="CombatComponent"/>).
///
/// The game is **hybrid**: the same controls drive first person and an over-the-shoulder
/// third person, swapped at any time from the settings panel or the toggle-camera key.
/// Body yaw always equals camera yaw in both modes, so combat, lock-on, dodge and melee
/// reach are mode-agnostic — the only things that differ are where the camera sits
/// (blended, and sprung off world geometry) and that third person aims from the camera
/// rather than the head so the crosshair still means something.
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

    /// <summary>The player camera (injected by <see cref="PlayerFactory"/>) so the rig
    /// can move it between the eye and the over-the-shoulder orbit.</summary>
    public Camera3D? Camera { get; set; }

    /// <summary>The node spellcasting aims along (injected by <see cref="PlayerFactory"/>). It sits
    /// on the body but is re-aimed each frame at the point the crosshair converges on, so a bolt
    /// goes where the reticle is in both modes rather than along the head's forward.</summary>
    public Node3D? AimNode { get; set; }

    /// <summary>Whether gameplay is currently first-person (the shipping default).</summary>
    public bool IsFirstPerson { get; private set; } = true;

    /// <summary>Seconds the camera takes to travel between the two modes.</summary>
    private const float ModeBlendSeconds = 0.18f;

    /// <summary>Radius of the sphere swept from the pivot to the camera. Bigger than the camera's
    /// near plane so a corner can never poke inside it.</summary>
    private const float CameraProbeRadius = 0.22f;

    /// <summary>How fast the camera eases back out after geometry stops crowding it (m/s). Pulling
    /// in is instant; see <see cref="CameraRigMath.SpringDistance"/>.</summary>
    private const float CameraPushOutSpeed = 6f;

    /// <summary>How far the crosshair convergence ray reaches before falling back to a far point.</summary>
    private const float AimTraceDistance = 200f;

    /// <summary>0 = first person, 1 = third person. Eased into the camera's rest pose each frame.</summary>
    private float _modeBlend;

    /// <summary>The blend target (0/1) the mode toggle sets.</summary>
    private float _modeTarget;

    /// <summary>Camera distance from the pivot after the collision spring, in metres.</summary>
    private float _springDistance;

    /// <summary>The camera's rest offset as of the last physics frame — what <see cref="CameraShake"/>
    /// offsets around.</summary>
    private Vector3 _cameraRest = Vector3.Zero;

    private Node3D _yaw = null!;
    private LocomotionComponent? _locomotion;
    private MeleeWeaponComponent? _weapon;
    private CombatComponent? _combat;
    private DodgeComponent? _dodge;
    private LockOnComponent? _lockOn;
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

    /// <summary>The locked-on target (Phase 29H), or null. Read by the HUD for the reticle/nameplate.</summary>
    public IEntity? LockedTarget => _lockOn?.Target;

    protected override void OnInitialize()
    {
        IEntity owner = Entity!;
        _yaw = owner.Body;
        _locomotion = owner.GetComponent<LocomotionComponent>();
        _weapon = owner.GetComponent<MeleeWeaponComponent>();
        _combat = owner.GetComponent<CombatComponent>();
        _dodge = owner.GetComponent<DodgeComponent>();
        _lockOn = owner.GetComponent<LockOnComponent>();
        _spellcasting = owner.GetComponent<SpellcastingComponent>();
        _settings = ServiceLocator.Instance is { } locator && locator.TryGet(out SettingsService settings)
            ? settings
            : null;

        EventBus.Instance?.Subscribe<GameStateChangedEvent>(OnGameStateChanged);
        EventBus.Instance?.Subscribe<SettingsAppliedEvent>(OnSettingsApplied);
        CaptureMouse(true);
        SetFirstPerson(!(_settings?.Current.ThirdPersonCamera ?? false), immediate: true);
    }

    /// <summary>Follow the camera-mode setting live — the settings panel and the toggle key both
    /// route through it, so there is one path into the mode and it is always the persisted one.</summary>
    private void OnSettingsApplied(SettingsAppliedEvent e) => SetFirstPerson(!e.Current.ThirdPersonCamera);

    /// <summary>The camera's live rest position — the single source of truth shared with
    /// <see cref="Combat.CameraShake"/>, which offsets around it per frame. It follows the mode
    /// blend and the wall spring, so a crit mid-swap or against a wall shakes around where the
    /// camera actually is, not where the mode says it should be (the "camera glitches into the
    /// head on a crit while third-person" bug).</summary>
    public Vector3 CameraRestPosition => _cameraRest;

    /// <summary>The third-person rest offset at full extension, before the wall spring.</summary>
    private static Vector3 ThirdPersonRest => CameraRigMath.RestOffset(
        firstPerson: false,
        PlayerFactory.ThirdPersonBackDistance,
        PlayerFactory.ThirdPersonRise,
        PlayerFactory.ThirdPersonShoulder);

    /// <summary>Switches between first-person (camera at the eye, own body casting shadows
    /// only — the viewmodel arms carry the visible weapon) and over-the-shoulder third person
    /// (camera orbits behind and to the right, full body shown). Player-selectable at any time
    /// via the ThirdPersonCamera setting or the toggle-camera key; the Phase 43 cutscene director
    /// will also drive it, restoring to the setting (not a hard-coded mode) on cutscene end.
    ///
    /// <paramref name="immediate"/> snaps rather than blends — used on initialize so a save
    /// resumed in third person opens there instead of swooping out on the first frame.</summary>
    public void SetFirstPerson(bool firstPerson, bool immediate = false)
    {
        // The flag flips at the *start* of the blend so the viewmodel arms hide on the way out
        // rather than fading past the camera.
        IsFirstPerson = firstPerson;
        _modeTarget = firstPerson ? 0f : 1f;
        if (immediate)
        {
            _modeBlend = _modeTarget;
            _springDistance = firstPerson ? 0f : ThirdPersonRest.Length();
            ApplyCameraRest(ResolveRestOffset());
        }

        if (Entity?.Body.GetNodeOrNull<Node3D>("BodyMesh") is { } bodyVisual)
        {
            SetShadowOnly(bodyVisual, firstPerson);
        }
    }

    /// <summary>Flips the camera mode through the *setting*, so the toggle key and the settings
    /// panel can never disagree and the choice persists across sessions. <c>Apply</c> publishes
    /// <see cref="SettingsAppliedEvent"/>, which is what actually calls
    /// <see cref="SetFirstPerson(bool, bool)"/> — the same path the panel's toggle takes.</summary>
    private void ToggleCameraMode()
    {
        if (_settings == null)
        {
            // No settings service (a bare test harness): flip locally so the key still works.
            SetFirstPerson(!IsFirstPerson);
            return;
        }

        _settings.Current.ThirdPersonCamera = !_settings.Current.ThirdPersonCamera;
        _settings.Apply();
        _settings.Save();
    }

    /// <summary>The camera's rest offset this frame: the eased blend between the two modes, with
    /// the third-person leg shortened to whatever the wall spring currently allows.</summary>
    private Vector3 ResolveRestOffset()
    {
        Vector3 full = ThirdPersonRest;
        float extent = full.Length();
        Vector3 third = extent > 0.0001f ? full * (_springDistance / extent) : Vector3.Zero;
        return CameraRigMath.Blend(Vector3.Zero, third, CameraRigMath.Ease(_modeBlend));
    }

    private void ApplyCameraRest(Vector3 rest)
    {
        _cameraRest = rest;
        if (Camera != null)
        {
            Camera.Position = rest;
        }
    }

    /// <summary>Advances the mode blend and the wall spring, then writes the camera's rest pose.
    /// The spring sweeps a small sphere from the pivot out to the camera's desired seat and clamps
    /// the distance to the first thing it touches, so the camera never ends up inside geometry.</summary>
    private void UpdateCameraRig(double delta)
    {
        float dt = (float)delta;
        _modeBlend = CameraRigMath.StepBlend(_modeBlend, _modeTarget, dt, ModeBlendSeconds);

        float desired = ThirdPersonRest.Length();
        _springDistance = CameraRigMath.SpringDistance(
            _springDistance, desired, AllowedCameraDistance(desired), dt, CameraPushOutSpeed);

        ApplyCameraRest(ResolveRestOffset());
    }

    /// <summary>How far the camera can sit from the pivot before it would clip world geometry.
    /// Returns <paramref name="desired"/> when nothing is in the way (including in first person,
    /// where the blend collapses the offset to zero anyway and the cast would be wasted work).</summary>
    private float AllowedCameraDistance(float desired)
    {
        if (_modeBlend <= 0f || CameraPivot == null || Entity?.Body is not CharacterBody3D body ||
            desired <= 0.0001f)
        {
            return desired;
        }

        // Built per call, like every other query site in this codebase (AutoPickupNearby,
        // SpellResolver, …). A cached RefCounted field would save the churn but keeps a native
        // object alive on the component across shutdown, which is not worth the disposal-order
        // risk for one small sphere cast a frame.
        var query = new PhysicsShapeQueryParameters3D
        {
            Shape = new SphereShape3D { Radius = CameraProbeRadius },
            Transform = new Transform3D(Basis.Identity, CameraPivot.GlobalPosition),
            Motion = CameraPivot.GlobalTransform.Basis * ThirdPersonRest,
            // ponytail: actor bodies share the World layer, so a companion stepping between the
            // player and the camera pulls it in too. Honest (it *is* in the way) if slightly
            // twitchy; a dedicated camera-blocker layer is the upgrade if it ever annoys.
            CollisionMask = CombatLayers.World,
            CollideWithAreas = false,
            CollideWithBodies = true,
            Exclude = new Godot.Collections.Array<Rid> { body.GetRid() },
        };

        // CastMotion returns [safe, unsafe] fractions of the motion; the safe one is the last
        // position the sphere occupies without overlapping anything.
        float[] fractions = body.GetWorld3D().DirectSpaceState.CastMotion(query);
        float safe = fractions.Length > 0 ? fractions[0] : 1f;
        return desired * Mathf.Clamp(safe, 0f, 1f);
    }

    /// <summary>Sets every mesh under <paramref name="node"/> to shadows-only (or restores it).
    /// Includes the skeleton-held weapon — in first person the viewmodel arms
    /// (<see cref="FirstPersonArmsComponent"/>) carry their own visible sword instead.</summary>
    private static void SetShadowOnly(Node node, bool shadowOnly)
    {
        if (node is GeometryInstance3D geometry)
        {
            geometry.CastShadow = shadowOnly
                ? GeometryInstance3D.ShadowCastingSetting.ShadowsOnly
                : GeometryInstance3D.ShadowCastingSetting.On;
        }

        foreach (Node child in node.GetChildren())
        {
            SetShadowOnly(child, shadowOnly);
        }
    }

    protected override void OnTeardown()
    {
        EventBus.Instance?.Unsubscribe<GameStateChangedEvent>(OnGameStateChanged);
        EventBus.Instance?.Unsubscribe<SettingsAppliedEvent>(OnSettingsApplied);
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

        // The camera rig sits inside the not-playing guard on purpose: it dereferences the injected
        // camera/pivot/aim nodes, and those are being freed during a world teardown or save/load
        // rebuild. It still runs with a non-pausing menu open (below) so the view keeps settling.
        UpdateCameraRig(delta);
        UpdateAim();

        // A blocking menu (inventory) is open: hold position, ignore combat/look
        // so UI clicks don't also drive the character.
        if (UiState.MenuOpen)
        {
            ClearFocus();
            DropHeldInput();
            _locomotion?.Move(delta, Vector3.Zero, sprint: false, jump: false);
            return;
        }

        if (Godot.Input.IsActionJustPressed(GameInput.ToggleCamera))
        {
            ToggleCameraMode();
        }

        UpdateFocus();

        Vector2 input = Godot.Input.GetVector(
            GameInput.MoveLeft, GameInput.MoveRight, GameInput.MoveForward, GameInput.MoveBack);

        // Orient input by the body's yaw so "forward" is where the player faces.
        Vector3 wishDir = _yaw.GlobalBasis * new Vector3(input.X, 0f, input.Y);

        bool sprint = Godot.Input.IsActionPressed(GameInput.Sprint);
        bool jump = Godot.Input.IsActionJustPressed(GameInput.Jump);
        _locomotion?.Move(delta, wishDir, sprint, jump);

        // Dodge can't interrupt a committed swing (Phase 29G commit window); it cancels recovery/idle.
        if (Godot.Input.IsActionJustPressed(GameInput.Dodge) && !(_weapon?.IsCommitted ?? false))
        {
            _dodge?.TryDodge(wishDir);
        }

        // Lock-on (Phase 29H): toggle/cycle the target, drop it if dead/out of range, and face it.
        _lockOn?.Tick();
        if (Godot.Input.IsActionJustPressed(GameInput.LockOn))
        {
            _lockOn?.Toggle(FocusedEntity);
        }

        if (Godot.Input.IsActionJustPressed(GameInput.LockCycleNext))
        {
            _lockOn?.Cycle(1);
        }
        else if (Godot.Input.IsActionJustPressed(GameInput.LockCyclePrev))
        {
            _lockOn?.Cycle(-1);
        }

        FaceLockTarget();

        if (_combat != null)
        {
            _combat.IsBlocking = Godot.Input.IsActionPressed(GameInput.Block);
        }

        if (Godot.Input.IsActionJustPressed(GameInput.Attack))
        {
            _weapon?.TryAttack();
        }

        // Cast (Phase 29.5A): press begins (instant fires now; charged/channeled hold), release ends.
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
            if (FocusedInteractable is { } focused)
            {
                focused.Interact(Entity!);
                EventBus.Instance?.Publish(new InteractionPerformedEvent(Entity!, focused));
            }

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

    /// <summary>While locked on, yaws the body to face the target (mouse-look only pitches). The level
    /// look (target sampled at the body's height) keeps it a pure yaw, so attacks/strafe orient at the foe.</summary>
    private void FaceLockTarget()
    {
        if (_lockOn?.Target is not { } target || target.Body is not Node3D targetBody ||
            Entity?.Body is not Node3D body)
        {
            return;
        }

        Vector3 to = targetBody.GlobalPosition - body.GlobalPosition;
        to.Y = 0f;
        if (to.LengthSquared() < 0.01f)
        {
            return;
        }

        _yaw.LookAt(new Vector3(targetBody.GlobalPosition.X, body.GlobalPosition.Y, targetBody.GlobalPosition.Z), Vector3.Up);
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

    /// <summary>Raycasts down the camera's own forward and records what the player is looking at, so
    /// the HUD can show a nameplate / interaction prompt and <c>E</c> acts on the same target.
    ///
    /// The ray starts at the <b>camera</b>, not the head, so the crosshair and the focus agree in
    /// third person — from the head the two diverge by the camera's pullback and the shoulder
    /// offset, and you end up interacting with something other than what the reticle is on. The
    /// reach is then measured from the <b>character</b>, so leaning out to third person never lets
    /// the player interact with anything they couldn't reach in first person. In first person the
    /// camera sits on the pivot and both of those are no-ops.</summary>
    private void UpdateFocus()
    {
        if (Camera == null || Entity?.Body is not CharacterBody3D body)
        {
            ClearFocus();
            return;
        }

        Vector3 from = Camera.GlobalPosition;
        Vector3 forward = -Camera.GlobalTransform.Basis.Z;

        // Reach from the eye plus however far the camera has been pulled back, so the *player's*
        // interact range is what InteractRange means in either mode.
        float pullback = from.DistanceTo(CameraPivot?.GlobalPosition ?? from);
        if (RaycastWorld(body, from, forward, InteractRange + pullback) is not { } hit ||
            hit.Collider is not Node collider)
        {
            ClearFocus();
            return;
        }

        if (body.GlobalPosition.DistanceTo(hit.Point) > InteractRange + CapsuleReachAllowance)
        {
            ClearFocus();
            return;
        }

        FocusedEntity = EntityNode.FindOwner(collider);
        FocusedInteractable = FocusedEntity?.GetComponent<InteractableComponent>();
    }

    /// <summary>Slack on the body-to-target range check: the body origin is at the feet, so a chest
    /// at head height is measurably further from it than from the eye.</summary>
    private const float CapsuleReachAllowance = 1.2f;

    /// <summary>Points <see cref="AimNode"/> at whatever the crosshair converges on, so spells fire
    /// where the reticle is instead of along the head's forward. In first person the convergence
    /// point lies on the pivot's own forward axis, so the node's aim is unchanged from before the
    /// rig existed — that is the invariant that keeps this safe for the shipping mode.</summary>
    private void UpdateAim()
    {
        if (AimNode == null || Camera == null || Entity?.Body is not CharacterBody3D body)
        {
            return;
        }

        Vector3 from = Camera.GlobalPosition;
        Vector3 forward = -Camera.GlobalTransform.Basis.Z;
        Vector3 focus = RaycastWorld(body, from, forward, AimTraceDistance) is { } hit
            ? hit.Point
            : from + (forward * AimTraceDistance);

        Vector3 direction = CameraRigMath.AimDirection(AimNode.GlobalPosition, focus);
        if (Mathf.Abs(direction.Dot(Vector3.Up)) > 0.999f)
        {
            return; // straight up/down: LookAt has no valid basis, so keep the last aim
        }

        AimNode.LookAt(AimNode.GlobalPosition + direction, Vector3.Up);
    }

    /// <summary>One ray against everything the player can look at, excluding their own body.</summary>
    private static (Node? Collider, Vector3 Point)? RaycastWorld(
        CharacterBody3D body, Vector3 from, Vector3 direction, float distance)
    {
        var query = PhysicsRayQueryParameters3D.Create(from, from + (direction * distance));
        query.Exclude = new Godot.Collections.Array<Rid> { body.GetRid() };

        Godot.Collections.Dictionary hit = body.GetWorld3D().DirectSpaceState.IntersectRay(query);
        return hit.Count == 0
            ? null
            : (hit["collider"].AsGodotObject() as Node, hit["position"].AsVector3());
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

            // While locked on, the body auto-faces the target (FaceLockTarget) — mouse only pitches.
            if (_lockOn?.Target == null)
            {
                _yaw.RotateY(-SettingsMath.LookStep(motion.Relative.X, MouseSensitivity, multiplier));
            }

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
