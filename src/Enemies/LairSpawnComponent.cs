using Embervale.Core.Diagnostics;
using Embervale.Core.Events;
using Embervale.Entities;
using Embervale.Save;
using Godot;

namespace Embervale.Enemies;

/// <summary>
/// Places a world boss in its lair and remembers that you killed it (Phase 35D). Authored as a
/// component on a marker <see cref="Entity"/> inside a region cell's <c>.tscn</c>; on load it builds
/// the creature through <see cref="EnemyTemplateRegistry"/> — no new factory, so any registered
/// archetype can be a lair boss.
///
/// <b>Why the spawner persists and the boss does not.</b> <see cref="CellPersistenceDirector"/>
/// already keeps authored cell actors dead across streaming and save/load, but it reconciles on
/// <c>RegionCellLoadedEvent</c>, which <see cref="World.RegionStreamer"/> publishes *after* it adds
/// the cell root. A boss spawned during that same frame's <c>_Ready</c> is racing that walk, and one
/// spawned deferred loses it outright — either way a killed boss comes back every time the player
/// re-enters the valley. So the thing that persists is this component, which is authored in the
/// scene and therefore guaranteed to be there when the director looks. It stores one fact —
/// <see cref="Defeated"/> — and simply does not spawn when it is true. No race, and no new
/// persistence machinery.
/// </summary>
[GlobalClass]
public partial class LairSpawnComponent : EntityComponent, ISaveable
{
    /// <summary>Which archetype lives here, e.g. <c>enemy.wild_dragon</c>.</summary>
    [Export] public string TemplateId { get; set; } = string.Empty;

    /// <summary>Where it stands relative to this marker.</summary>
    [Export] public Vector3 SpawnOffset { get; set; } = Vector3.Zero;

    /// <summary>True once the occupant has been killed. Persisted; the lair then stays empty.</summary>
    public bool Defeated { get; private set; }

    public string SaveId => SaveKey("lair");

    private EnemyEntity? _occupant;

    protected override void OnInitialize()
    {
        RegisterSaveable();
        EventBus.Instance?.Subscribe<EntityDiedEvent>(OnDied);

        // Deferred so the occupant is added after the cell root has finished taking on its own
        // children — Godot refuses an AddChild into a node that is mid-add. The boss itself is a
        // plain transient actor, so nothing depends on it existing before the cell's load event.
        CallDeferred(nameof(SpawnOccupant));
    }

    protected override void OnTeardown()
    {
        EventBus.Instance?.Unsubscribe<EntityDiedEvent>(OnDied);
        SaveManager.Instance?.Unregister(this);
    }

    private void SpawnOccupant()
    {
        if (Defeated || TemplateId.Length == 0 || Entity?.Body is not Node3D marker)
        {
            return;
        }

        if (_occupant != null && IsInstanceValid(_occupant))
        {
            return;
        }

        if (!EnemyTemplateRegistry.IsRegistered(TemplateId))
        {
            Log.Warn($"LairSpawnComponent: '{TemplateId}' is not a registered enemy template; the lair stays empty.");
            return;
        }

        _occupant = EnemyTemplateRegistry.Create(TemplateId, marker.GlobalPosition + SpawnOffset);
        marker.GetParent()?.AddChild(_occupant);
    }

    private void OnDied(EntityDiedEvent e)
    {
        if (_occupant == null || !ReferenceEquals(e.Entity, _occupant))
        {
            return;
        }

        Defeated = true;
        _occupant = null;
    }

    public Godot.Collections.Dictionary Save() => new() { ["defeated"] = Defeated };

    public void Load(Godot.Collections.Dictionary data)
    {
        Defeated = data.TryGetValue("defeated", out Variant value) && value.AsBool();

        // A load can arrive after the lair has already populated (the save is applied to a live
        // world), so an occupant that should no longer exist is cleared out here.
        if (Defeated && _occupant != null && IsInstanceValid(_occupant))
        {
            _occupant.QueueFree();
            _occupant = null;
        }
    }
}
