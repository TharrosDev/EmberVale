using Embervale.Core.Events;
using Embervale.Entities;
using Embervale.Interaction;
using Embervale.Items;
using Godot;

namespace Embervale.Player;

/// <summary>
/// What the player is looking at, whether it can be interacted with, and the hold-E sweep that
/// vacuums up nearby loot.
///
/// <para>This is the only component that decides what <c>E</c> acts on, and the HUD's nameplate and
/// prompt read the same three properties, so the reticle and the verb can never disagree.</para>
/// </summary>
[GlobalClass]
public partial class InteractionSensor : EntityComponent
{
    [Export]
    public float InteractRange { get; set; } = 3f;

    /// <summary>Radius of the hold-E auto-pickup sweep, and how often it runs while E is held.</summary>
    private const float AutoPickupRadius = 3.5f;
    private const double AutoPickupInterval = 0.12;

    /// <summary>Slack on the body-to-target range check: the body origin is at the feet, so a chest
    /// at head height is measurably further from it than from the eye.</summary>
    private const float CapsuleReachAllowance = 1.2f;

    private double _autoPickupTimer;
    private PlayerCameraRig? _rig;
    private PlayerPhysicsQueries? _queries;

    /// <summary>The entity the player is currently looking at within interact range, if any.
    /// Updated each frame; read by the game HUD for a nameplate / interaction prompt.</summary>
    public IEntity? FocusedEntity { get; private set; }

    /// <summary>The interactable on the focused entity (null if it can't be interacted with).</summary>
    public InteractableComponent? FocusedInteractable { get; private set; }

    /// <summary>The prompt to show for the focused interactable, or null.</summary>
    public string? FocusPrompt => FocusedInteractable?.Prompt;

    protected override void OnInitialize()
    {
        _rig = Entity!.GetComponent<PlayerCameraRig>();
        _queries = Entity.GetComponent<PlayerPhysicsQueries>();
    }

    /// <summary>
    /// Raycasts down the camera's own forward and records what the player is looking at.
    ///
    /// <para>The ray starts at the <b>camera</b>, not the head, so the crosshair and the focus agree
    /// in third person — from the head the two diverge by the camera's pullback and the shoulder
    /// offset, and you end up interacting with something other than what the reticle is on. The
    /// reach is then measured from the <b>character</b>, so leaning out to third person never lets
    /// the player interact with anything they could not reach in first person. In first person the
    /// camera sits on the pivot and both of those are no-ops.</para>
    /// </summary>
    public void UpdateFocus()
    {
        if (_rig?.Camera is not { } camera || _queries == null || Entity?.Body is not CharacterBody3D body)
        {
            ClearFocus();
            return;
        }

        Vector3 from = camera.GlobalPosition;
        Vector3 forward = -camera.GlobalTransform.Basis.Z;

        // Reach from the eye plus however far the camera has been pulled back, so the *player's*
        // interact range is what InteractRange means in either mode.
        if (_queries.Raycast(from, forward, InteractRange + _rig.Pullback) is not { } hit ||
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

    public void ClearFocus()
    {
        FocusedEntity = null;
        FocusedInteractable = null;
    }

    /// <summary>Acts on the focused interactable. Returns false when there was nothing to act on or
    /// the interaction was refused.</summary>
    public bool TryInteract()
    {
        _autoPickupTimer = AutoPickupInterval; // brief grace before the held sweep kicks in

        if (FocusedInteractable is not { } focused || !focused.Interact(Entity!))
        {
            return false;
        }

        // Published on success alone (see InteractionPerformedEvent): a refused shop, an unaffordable
        // deed or a pickup into a full pack is not a verb the player performed, and quest/onboarding
        // progress rides this event.
        EventBus.Instance?.Publish(new InteractionPerformedEvent(Entity!, focused));
        return true;
    }

    /// <summary>Hold E to vacuum nearby loot — saves tapping E per item when a kill drops a pile.
    /// Called only on non-just-pressed frames so it never double-collects the focused item.</summary>
    public void TickAutoPickup(double delta)
    {
        _autoPickupTimer -= delta;
        if (_autoPickupTimer > 0d)
        {
            return;
        }

        _autoPickupTimer = AutoPickupInterval;

        if (_queries == null || Entity?.Body is not Node3D body)
        {
            return;
        }

        // Pickups free themselves when emptied, so each is taken once per sweep and gone by the next.
        foreach (IEntity found in _queries.OverlapSphere(body.GlobalPosition, AutoPickupRadius, maxResults: 24))
        {
            found.GetComponent<ItemPickupComponent>()?.Interact(Entity!);
        }
    }
}
