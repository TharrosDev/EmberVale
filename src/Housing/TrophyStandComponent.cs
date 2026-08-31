using Embervale.Core.Events;
using Embervale.Core.Services;
using Embervale.Entities;
using Embervale.Interaction;
using Embervale.Items;
using Embervale.Localization;
using Embervale.Save;
using Godot;

namespace Embervale.Housing;

/// <summary>
/// A display slot in a holding you own (Phase 37D). Authored on an entity that also carries an
/// <see cref="InventoryComponent"/> of <c>Capacity = 1</c> — <b>that one-slot inventory is the
/// display</b>, which is why 37D adds no persistence code either: an entity with a stable
/// <c>PersistentId</c> already round-trips its inventory through <c>SaveManager</c>
/// (<c>inventory:&lt;PersistentId&gt;</c>), exactly as <see cref="PropertyStorageComponent"/>'s chest
/// does. Fourth sub-phase running, fourth distinct reason persistence came free.
///
/// <b>A stand the player placed persists just as well as an authored one.</b>
/// <see cref="PlacementIds.Next"/> hands it a stable id, and <c>SaveManager</c>'s in-flight-load hook
/// restores a saveable that registers <em>during</em> a load — which is exactly when
/// <see cref="PersistentSpawnDirector.Load"/> respawns the stand and its inventory registers. That is
/// also why <see cref="PropertyId"/> may be left empty on a placed stand: its holding is already
/// encoded in its own id, which is the use <see cref="PlacementIds"/> was built anticipating.
///
/// The window is the existing <c>StoragePanel</c>. A display stand is a chest that holds one thing,
/// so it publishes the same <see cref="StorageOpenedEvent"/> and gets the whole two-way transfer
/// surface for nothing — carrying a <see cref="TrophyDisplay.MinimumRarity"/> floor the panel honours
/// on the Store direction only. Take is never gated: a stand that could trap an item would be a
/// worse bug than one that displays a boring one.
/// </summary>
[GlobalClass]
public partial class TrophyStandComponent : InteractableComponent
{
    /// <summary>Which holding this stand belongs to (a <c>property.*</c> id). Leave empty on a
    /// placeable template — a placed stand reads it back out of its own persistent id.</summary>
    [Export] public string PropertyId { get; set; } = string.Empty;

    /// <summary>How high above the plinth the trophy floats.</summary>
    [Export] public float DisplayHeight { get; set; } = 1.35f;

    private MeshInstance3D? _display;
    private StandardMaterial3D? _material;

    /// <summary>The holding this stand answers to: the authored id when there is one, otherwise the
    /// one baked into a placed prop's persistent id.</summary>
    public string ResolvedPropertyId =>
        !string.IsNullOrEmpty(PropertyId) ? PropertyId : PlacementIds.PropertyOf(Entity?.PersistentId);

    /// <summary>What is on the stand, or null when it is bare.</summary>
    public ItemInstance? Displayed =>
        Entity?.GetComponent<InventoryComponent>() is { Stacks.Count: > 0 } inventory
            ? inventory.Stacks[0].Instance
            : null;

    public override string Prompt
    {
        get
        {
            PropertyResource? property = PropertyDatabase.Get(ResolvedPropertyId);
            return Evaluate(property) switch
            {
                // Nothing sensible to say about an id that resolves to nothing; the validator and the
                // log carry authoring faults, the prompt stays silent rather than lying.
                TrophyOutcome.UnknownProperty => string.Empty,
                TrophyOutcome.NotOwned => Loc.TF("trophy.prompt_locked", Loc.T(property!.NameKey)),
                _ => Displayed is { } shown
                    ? Loc.TF("trophy.prompt_shown", shown.DisplayName)
                    : Loc.T("trophy.prompt_empty"),
            };
        }
    }

    public override bool Interact(IEntity instigator)
    {
        if (PropertyDatabase.Get(ResolvedPropertyId) is not { } property ||
            Evaluate(property) != TrophyOutcome.Open ||
            Entity?.GetComponent<InventoryComponent>() is not { } slot)
        {
            return false; // the prompt has already said why
        }

        EventBus.Instance?.Publish(new StorageOpenedEvent(
            instigator, slot, Loc.T("trophy.stand_name"), TrophyDisplay.MinimumRarity));
        return true;
    }

    protected override void OnInitialize()
    {
        EventBus.Instance?.Subscribe<InventoryChangedEvent>(OnInventoryChanged);

        // An EntityComponent cannot AddChild to its own Entity.Body during initialization — the
        // parent is still setting up its children, so the node is orphaned and the
        // WorldIntegrityChecker's orphan-leak invariant trips (the 29C trail).
        Entity?.Body?.CallDeferred(Node.MethodName.AddChild, BuildDisplay());
        CallDeferred(MethodName.Refresh);
    }

    protected override void OnTeardown() =>
        EventBus.Instance?.Unsubscribe<InventoryChangedEvent>(OnInventoryChanged);

    private void OnInventoryChanged(InventoryChangedEvent e)
    {
        if (ReferenceEquals(e.Owner, Entity))
        {
            Refresh();
        }
    }

    /// <summary>
    /// The trophy itself: a small mesh floating over the plinth, tinted by the item's rarity.
    /// Deliberately not the item's own model — <see cref="ItemResource"/> carries no model path and
    /// not one <c>.tres</c> in the game authors its <c>Icon</c>, so a rarity-tinted shape is the only
    /// honest thing to show. It reads at a glance across a room, which is what a trophy is for.
    /// </summary>
    private MeshInstance3D BuildDisplay()
    {
        _material = new StandardMaterial3D
        {
            EmissionEnabled = true,
            EmissionEnergyMultiplier = 1.4f,
        };

        _display = new MeshInstance3D
        {
            Name = "Trophy",
            Mesh = new PrismMesh { Size = new Vector3(0.28f, 0.42f, 0.28f) },
            Position = new Vector3(0f, DisplayHeight, 0f),
            MaterialOverride = _material,
            Visible = false,
        };

        return _display;
    }

    /// <summary>Matches the visual and the nameplate to whatever is in the slot.</summary>
    private void Refresh()
    {
        ItemInstance? shown = Displayed;

        // IEntity.DisplayName is read-only; the concrete Entity a stand is built on is not. The
        // nameplate is how a trophy reads from across the room, so it follows what is on the plinth.
        if (Entity is Entity owner)
        {
            owner.DisplayName = shown?.DisplayName ?? Loc.T("trophy.stand_name");
        }

        if (_display == null || !IsInstanceValid(_display))
        {
            return;
        }

        _display.Visible = shown != null;
        if (shown != null && _material != null)
        {
            Color tint = ItemRarities.Color(shown.Rarity);
            _material.AlbedoColor = tint;
            _material.Emission = tint;
        }
    }

    private static TrophyOutcome Evaluate(PropertyResource? property) => TrophyDisplay.Resolve(
        propertyKnown: property != null,
        owned: property != null && (Resolve<HousingService>()?.Owns(property.Id) ?? false));

    private static T? Resolve<T>()
        where T : class =>
        ServiceLocator.Instance is { } locator && locator.TryGet(out T service) ? service : null;
}
