using Embervale.Core;
using Embervale.Core.Diagnostics;
using Embervale.Core.Events;
using Embervale.Core.Services;
using Embervale.Entities;
using Embervale.Interaction;
using Embervale.Items;
using Embervale.Loot;
using Embervale.Player;
using Embervale.Save;
using Embervale.Stats;
using Embervale.World;
using Godot;

namespace Embervale.Bootstrap;

/// <summary>
/// The player actor's owner: spawning it, respawning it on death, and building the persistent
/// world actors that prove the spawned-actor save path.
///
/// <para>It also routes the death event, because the only thing that used to route it was the
/// bootstrap. A player death respawns; anything else is left to the system that owns it — enemies
/// despawn through their own AI's death state, and their loot rolls in their loot component.</para>
/// </summary>
public sealed partial class PlayerHost : Node3D
{
    /// <summary>Where the player is built, before any region is known. The world director moves it
    /// onto the region's real spawn as soon as the streamer has published a heightfield.</summary>
    private static readonly Vector3 InitialSpawn = new(0f, 1.2f, 5f);

    public GameSession Session { get; init; } = null!;

    public PlayerCharacter? Player { get; private set; }

    public override void _EnterTree()
    {
        EventBus.Instance?.Subscribe<EntityDiedEvent>(OnEntityDied);
        EventBus.Instance?.Subscribe<GameLoadedEvent>(OnGameLoaded);
    }

    public override void _ExitTree()
    {
        EventBus.Instance?.Unsubscribe<EntityDiedEvent>(OnEntityDied);
        EventBus.Instance?.Unsubscribe<GameLoadedEvent>(OnGameLoaded);
    }

    public void SpawnPlayer()
    {
        Player = PlayerFactory.Create(InitialSpawn, Session.Profile, Session.ApplyStartingGrants);
        AddChild(Player);
        ServiceScope.RegisterOwned(Player, Player);

        Session.Ui.BindPlayer(Player);
        Session.DevTools?.SetPlayer(Player);

        // No seeded quest: the first quest is the guild board's bounty, earned by walking into town
        // and talking to someone. A journal that is already full before the player has done anything
        // undercuts the whole opening.
        Log.Info($"Spawned player at {Player.Position}.");
    }

    public void SpawnPersistentActors(PersistentSpawnDirector director)
    {
        // A persistent supply cache: recreated on load (existence + transform) with its inventory
        // restoring its contents — the spawned-actor persistence path, exercised.
        director.Spawn(GameIds.Templates.Cache, "cache.world.start", new Vector3(5f, 0f, 0f));
        Log.Info("A persistent supply cache sits east of spawn; it survives save/load.");
    }

    /// <summary>Builds a persistent storage cache prop (registered as the "prop.cache" template).</summary>
    public static Node3D BuildPersistentCache(Vector3 position)
    {
        var cache = new Entity
        {
            Name = "PersistentCache",
            DisplayName = "Supply Cache",
            Position = position,
        };

        // The banded cache-chest model (origin at feet), box fallback if unimported.
        if (GD.Load<PackedScene>(ModelAssets.CacheChest)?.Instantiate() is Node3D chestVisual)
        {
            chestVisual.Name = "Mesh";
            cache.AddChild(chestVisual);
        }
        else
        {
            cache.AddChild(new MeshInstance3D
            {
                Name = "Mesh",
                Mesh = new BoxMesh { Size = new Vector3(0.8f, 0.8f, 0.8f) },
                Position = new Vector3(0f, 0.4f, 0f),
                MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.55f, 0.43f, 0.20f) },
            });
        }

        var collider = new StaticBody3D { Name = "Collider" };
        collider.AddChild(new CollisionShape3D
        {
            Shape = new BoxShape3D { Size = new Vector3(0.8f, 0.8f, 0.8f) },
            Position = new Vector3(0f, 0.4f, 0f),
        });
        cache.AddChild(collider);

        // A persistent container's contents round-trip through the inventory save path.
        var inventory = new InventoryComponent { Name = "Inventory", Capacity = 12 };
        cache.AddChild(inventory);

        // Chests are lootable — E transfers the contents to the player. Seed starter loot on a fresh
        // spawn; a save's restored (possibly emptied) contents overwrite this on load.
        cache.AddChild(new ContainerLootComponent { Name = "Loot" });
        if (ItemDatabase.Get(GameIds.Items.HealthPotion) is { } potion)
        {
            inventory.AddItem(potion, 2);
        }

        if (ItemDatabase.Get(GameIds.Currency.Gold) is { } gold)
        {
            inventory.AddItem(gold, 20);
        }

        return cache;
    }

    /// <summary>Returns the player to the active region's spawn, on the ground.</summary>
    public void Respawn()
    {
        if (Player == null || !IsInstanceValid(Player))
        {
            return;
        }

        Player.Velocity = Vector3.Zero;

        // The active region's spawn, seated on the ground — not a literal, which would be one
        // region's spawn point frozen at the elevation the world had before it had any.
        Player.GlobalPosition = RegionDatabase.Get(Session.CurrentRegionId) is { } region
            ? WorldSessionDirector.RegionSpawn(region)
            : WorldSessionDirector.SafeLanding(InitialSpawn);
        Player.GetComponent<StatsComponent>()?.RefillResources();
        Log.Info("You were slain — respawning at the start.");
    }

    private void OnEntityDied(EntityDiedEvent e)
    {
        if (ReferenceEquals(e.Entity, Player))
        {
            Respawn();
        }
    }

    private void OnGameLoaded(GameLoadedEvent e)
    {
        Log.Info($"Game loaded from slot '{e.Slot}'.");

        // The player wakes at full vitals on every load (maintainer direction, 2026-07-02).
        // Deferred two steps so it lands after StatsComponent's own deferred resource restore.
        Callable.From(() => Callable.From(RefillPlayerResources).CallDeferred()).CallDeferred();
    }

    private void RefillPlayerResources()
    {
        if (Player != null && IsInstanceValid(Player) && Player.GetComponent<StatsComponent>() is { } stats)
        {
            stats.RefillResources();
        }
    }
}
