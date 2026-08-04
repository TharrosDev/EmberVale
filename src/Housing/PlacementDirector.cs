using Embervale.Combat;
using Embervale.Core;
using Embervale.Core.Diagnostics;
using Embervale.Core.Services;
using Embervale.Entities;
using Embervale.Items;
using Embervale.Localization;
using Embervale.Player;
using Embervale.Save;
using Godot;

namespace Embervale.Housing;

/// <summary>
/// Placement mode (Phase 37C): the player holds a translucent copy of a prop against the world, and
/// sets it down where their holding allows. The first world-editing mechanic in the game — nothing
/// here is a reuse, because nothing like it existed.
///
/// <b>It does not pause.</b> The player has to walk and aim to choose a spot, so this is not a modal:
/// <see cref="UI.PlacementHud"/> opens with <c>Modal =&gt; false</c> and no <c>UiState</c> pause.
/// CLAUDE.md §7 is explicit that scattering menu checks through gameplay systems is the approach that
/// already failed once, so nothing else is suppressed while placing either — you can swing a sword
/// holding a ghost workbench. Harmless, and cheaper than a mode stack this repo does not have.
///
/// <b>Persistence is entirely inherited.</b> A placed prop goes through
/// <see cref="PersistentSpawnDirector"/>, which already records template, position and yaw and
/// rebuilds them on load. 37C adds no <see cref="ISaveable"/>; it registers builders
/// (<see cref="PlaceableTemplates"/>) and names ids (<see cref="PlacementIds"/>).
/// </summary>
[GlobalClass]
public partial class PlacementDirector : Node
{
    /// <summary>How far the aim ray reaches. Generous, because the holding's own radius is what
    /// actually bounds where a prop may go — a short reach would only add a second, invisible limit
    /// that refuses without explaining itself.</summary>
    private const float RayLength = 20f;

    /// <summary>The overlap probe's box. Lifted clear of the ground (a cell floor's collider tops out
    /// at y = 0) so the world's own floor cannot report every spot as blocked.</summary>
    private static readonly Vector3 ProbeSize = new(0.8f, 0.8f, 0.8f);
    private const float ProbeLift = 0.55f;

    private Node3D? _ghost;
    private StandardMaterial3D? _ghostMaterial;

    /// <summary>The kit being placed, or null when not in placement mode.</summary>
    public PlaceableItemResource? Kit { get; private set; }

    /// <summary>Live verdict for the spot under the cursor — what tints the ghost and what the HUD
    /// reports. Both read this one value, so the colour and the words cannot disagree.</summary>
    public PlacementOutcome Outcome { get; private set; } = PlacementOutcome.NoGround;

    /// <summary>The holding the verdict was measured against; empty when the player owns none.</summary>
    public string HoldingName { get; private set; } = string.Empty;

    /// <summary>A placed prop under the cursor, which placement mode offers to take back up.</summary>
    public IEntity? RemovalTarget { get; private set; }

    public bool Active => Kit != null;

    /// <summary>Kits of the held type still in the pack (the HUD's count).</summary>
    public int Remaining =>
        Kit != null && Player()?.GetComponent<InventoryComponent>() is { } pack ? pack.CountOf(Kit.Id) : 0;

    public override void _EnterTree() => ServiceLocator.Instance?.Register(this);

    public override void _ExitTree() => ServiceLocator.Instance?.Unregister(this);

    /// <summary>Enters placement mode holding <paramref name="kit"/>. Called from the inventory's
    /// Place button — placement is entered from the item, not from a keybind, because every letter
    /// key and every gamepad button in this game is already spoken for.</summary>
    public void Begin(PlaceableItemResource kit)
    {
        Cancel();
        Kit = kit;
        BuildGhost(kit.TemplateId);
    }

    /// <summary>Leaves placement mode and drops the ghost.</summary>
    public void Cancel()
    {
        Kit = null;
        RemovalTarget = null;
        if (_ghost != null && IsInstanceValid(_ghost))
        {
            _ghost.QueueFree();
        }

        _ghost = null;
        _ghostMaterial = null;
    }

    public override void _Process(double delta)
    {
        if (!Active || GameManager.Instance is not { IsPlaying: true })
        {
            return;
        }

        UpdateAim();

        if (Godot.Input.IsActionJustPressed(GameInput.Place))
        {
            if (RemovalTarget != null)
            {
                Remove(RemovalTarget);
            }
            else if (Outcome == PlacementOutcome.Ok)
            {
                Commit();
            }
        }
    }

    // --- Aiming -------------------------------------------------------------

    /// <summary>Re-evaluates the spot under the cursor: what is there, whether it may be built on,
    /// and where the ghost should stand.</summary>
    private void UpdateAim()
    {
        RemovalTarget = null;

        if (Player() is not { } player || player.Body is not CharacterBody3D body ||
            body.GetViewport()?.GetCamera3D() is not { } camera)
        {
            Outcome = PlacementOutcome.NoGround;
            ShowGhost(false);
            return;
        }

        (Node? Collider, Vector3 Point)? hit = RayToWorld(
            body, camera.GlobalPosition, -camera.GlobalTransform.Basis.Z, RayLength);

        // A prop the player already placed takes priority: placement mode is also how you take one
        // back up, and there is no other verb for it (a station's own Interact opens its crafting
        // window, and a decoration has no interaction at all).
        if (hit?.Collider is { } collider && EntityNode.FindOwner(collider) is { } owner &&
            PlacementIds.IsPlacement(owner.PersistentId))
        {
            RemovalTarget = owner;
            ShowGhost(false);
            return;
        }

        Vector3 point = hit?.Point ?? Vector3.Zero;
        PropertyResource? holding = NearestOwnedHolding(point);
        HoldingName = holding != null ? Loc.T(holding.NameKey) : string.Empty;

        Outcome = PlacementCheck.Resolve(
            owned: holding != null,
            hasGround: hit != null,
            distanceFromCenter: holding == null ? float.MaxValue : HorizontalDistance(point, holding.PlacementCenter),
            radius: holding?.PlacementRadius ?? 0f,
            blocked: hit != null && IsBlocked(body, point));

        ShowGhost(hit != null);
        if (_ghost != null && hit != null)
        {
            _ghost.GlobalPosition = point;
            _ghost.RotationDegrees = new Vector3(0f, FacingYaw(body, point), 0f);
            Tint(Outcome == PlacementOutcome.Ok ? UI.UiTheme.Good : UI.UiTheme.Bad);
        }
    }

    /// <summary>
    /// The owned holding this point belongs to — the one that contains it, or failing that the
    /// nearest, so an out-of-bounds refusal can still name the house the player is not standing in.
    /// </summary>
    private static PropertyResource? NearestOwnedHolding(Vector3 point)
    {
        HousingService? housing = Resolve<HousingService>();
        if (housing == null)
        {
            return null;
        }

        PropertyResource? nearest = null;
        float best = float.MaxValue;

        foreach (PropertyResource property in PropertyDatabase.All)
        {
            if (property.PlacementRadius <= 0f || !housing.Owns(property.Id))
            {
                continue;
            }

            float distance = HorizontalDistance(point, property.PlacementCenter);
            if (distance < best)
            {
                best = distance;
                nearest = property;
            }
        }

        return nearest;
    }

    /// <summary>XZ only. A radius measured in three dimensions would refuse a good spot for being
    /// uphill of the centre.</summary>
    private static float HorizontalDistance(Vector3 a, Vector3 b) =>
        new Vector2(a.X - b.X, a.Z - b.Z).Length();

    /// <summary>Yaw that turns the prop to face the player, so a station's front is the side you
    /// walk up to. This is why 37C ships no rotate key: the useful default needs no input.</summary>
    private static float FacingYaw(Node3D body, Vector3 point)
    {
        Vector3 toPlayer = body.GlobalPosition - point;
        return Mathf.RadToDeg(Mathf.Atan2(toPlayer.X, toPlayer.Z));
    }

    /// <summary>Is something already standing here? A box probe lifted clear of the ground.</summary>
    private static bool IsBlocked(CharacterBody3D body, Vector3 point)
    {
        var query = new PhysicsShapeQueryParameters3D
        {
            Shape = new BoxShape3D { Size = ProbeSize },
            Transform = new Transform3D(Basis.Identity, point + (Vector3.Up * ProbeLift)),
            CollideWithAreas = false,
            CollideWithBodies = true,
            CollisionMask = CombatLayers.World | CombatLayers.Body,
        };

        return body.GetWorld3D().DirectSpaceState.IntersectShape(query, maxResults: 1).Count > 0;
    }

    /// <summary>
    /// A ray at the ground and the props on it. Deliberately not shared with
    /// <c>PlayerController.RaycastWorld</c>: that one takes every layer and excludes the player, which
    /// is right for "what am I looking at" and wrong here — placement needs the world layer only, or
    /// it would happily sit a workbench on a goblin's head.
    /// </summary>
    private static (Node? Collider, Vector3 Point)? RayToWorld(
        CharacterBody3D body, Vector3 from, Vector3 direction, float distance)
    {
        PhysicsRayQueryParameters3D query =
            PhysicsRayQueryParameters3D.Create(from, from + (direction * distance), CombatLayers.World);
        query.Exclude = new Godot.Collections.Array<Rid> { body.GetRid() };

        Godot.Collections.Dictionary hit = body.GetWorld3D().DirectSpaceState.IntersectRay(query);
        return hit.Count == 0 ? null : (hit["collider"].AsGodotObject() as Node, hit["position"].AsVector3());
    }

    // --- Commit / remove ----------------------------------------------------

    /// <summary>Takes one kit from the pack and spawns the prop it builds.</summary>
    private void Commit()
    {
        if (Kit is not { } kit || Player()?.GetComponent<InventoryComponent>() is not { } pack ||
            Resolve<PersistentSpawnDirector>() is not { } spawns || _ghost == null)
        {
            return;
        }

        Vector3 position = _ghost.GlobalPosition;
        float yaw = _ghost.RotationDegrees.Y;
        string propertyId = NearestOwnedHolding(position)?.Id ?? string.Empty;

        // Charge before spawning, and only spawn if the charge went through — the deed post's order,
        // and for the same reason: never take the item for a prop that failed to appear.
        if (!pack.RemoveItem(kit.Id, 1))
        {
            return;
        }

        string id = PlacementIds.Next(spawns.TrackedIds, propertyId);
        if (spawns.Spawn(kit.TemplateId, id, position, yaw) == null)
        {
            // The template did not build. Refund rather than silently eating the kit.
            pack.AddItem(kit, 1);
            Log.Warn($"Placement of '{kit.TemplateId}' failed to build; kit refunded.");
            return;
        }

        Log.Info($"Placed {kit.DisplayName} as '{id}'.");
        if (Remaining <= 0)
        {
            Cancel(); // that was the last one; nothing left to hold
        }
    }

    /// <summary>Takes a placed prop back up, returning the kit that built it.</summary>
    private void Remove(IEntity target)
    {
        if (Resolve<PersistentSpawnDirector>() is not { } spawns ||
            Player()?.GetComponent<InventoryComponent>() is not { } pack)
        {
            return;
        }

        ItemResource? kit = KitFor(target.TemplateId);
        if (kit == null)
        {
            Log.Warn($"No kit rebuilds template '{target.TemplateId}'; refusing to remove it.");
            return;
        }

        // Refuse rather than destroy: a full pack must never be a reason the prop evaporates.
        if (pack.AddItem(kit, 1) < 1)
        {
            return;
        }

        spawns.Despawn(target.PersistentId);
        RemovalTarget = null;
        Log.Info($"Picked up {kit.DisplayName}.");
    }

    /// <summary>The kit that builds <paramref name="templateId"/>. A linear scan over the item
    /// database, which runs only on a removal — a cached reverse map would be two things to keep in
    /// step for a list of twenty-odd items.</summary>
    private static ItemResource? KitFor(string templateId)
    {
        foreach (ItemResource item in ItemDatabase.All.Values)
        {
            if (item is PlaceableItemResource placeable && placeable.TemplateId == templateId)
            {
                return placeable;
            }
        }

        return null;
    }

    // --- Ghost --------------------------------------------------------------

    /// <summary>
    /// Builds the held preview from the very same builder that will make the real thing, then strips
    /// it back to its visual — everything but the mesh goes, so the ghost has no collider (it would
    /// block its own placement probe and its own aim ray) and no components (a ghost station that
    /// opened the crafting window would be a genuinely confusing bug).
    /// </summary>
    private void BuildGhost(string templateId)
    {
        if (PersistentActorRegistry.Create(templateId, Vector3.Zero) is not { } host)
        {
            return;
        }

        foreach (Node child in host.GetChildren())
        {
            if (child.Name != "Mesh")
            {
                host.RemoveChild(child);
                child.QueueFree();
            }
        }

        _ghostMaterial = new StandardMaterial3D
        {
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoColor = new Color(UI.UiTheme.Good, GhostAlpha),
        };

        _ghost = host;
        AddChild(host);
        ApplyGhostMaterial(host);
        ShowGhost(false);
    }

    private const float GhostAlpha = 0.45f;

    /// <summary>Overrides every mesh in the preview, however deep — a glb arrives as a subtree, not
    /// as one <see cref="MeshInstance3D"/>.</summary>
    private void ApplyGhostMaterial(Node node)
    {
        if (node is MeshInstance3D mesh)
        {
            mesh.MaterialOverride = _ghostMaterial;
        }

        foreach (Node child in node.GetChildren())
        {
            ApplyGhostMaterial(child);
        }
    }

    private void Tint(Color color)
    {
        if (_ghostMaterial != null)
        {
            _ghostMaterial.AlbedoColor = new Color(color, GhostAlpha);
        }
    }

    private void ShowGhost(bool visible)
    {
        if (_ghost != null && IsInstanceValid(_ghost))
        {
            _ghost.Visible = visible;
        }
    }

    private static PlayerCharacter? Player() =>
        ServiceLocator.Instance is { } locator && locator.TryGet(out PlayerCharacter player) ? player : null;

    private static T? Resolve<T>()
        where T : class =>
        ServiceLocator.Instance is { } locator && locator.TryGet(out T service) ? service : null;
}
