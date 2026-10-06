using System.Collections.Generic;
using Embervale.Core;
using Embervale.Core.Diagnostics;
using Embervale.Core.Events;
using Embervale.Core.Services;
using Embervale.Entities;
using Embervale.Items;
using Embervale.Player;
using Embervale.Progression;
using Embervale.Save;
using Embervale.World;
using Godot;

namespace Embervale.Loot;

/// <summary>
/// Drops procedurally generated loot when its owner dies. On the owner's
/// <see cref="EntityDiedEvent"/> it asks the <see cref="LootGenerator"/> to roll its
/// <see cref="Table"/> and spawns a world pickup for each resulting
/// <see cref="LootDrop"/>, scattered around the death position. This replaces
/// hard-coded death drops — give an actor a <see cref="LootTable"/> and it loots.
///
/// The roll is made for the realm the owner died in and for the player who will loot it
/// (<see cref="ContextFor"/>). A table marked <see cref="LootTable.DropsAsChest"/> (a boss) does
/// not scatter: it stands a persistent reward chest at the death position, and the chest rolls the
/// table when it is opened.
/// </summary>
[GlobalClass]
public partial class LootComponent : EntityComponent
{
    [Export] public LootTable? Table { get; set; }

    /// <summary>Optional path to load the table from when one isn't assigned
    /// directly (lets the factory wire it by resource path).</summary>
    [Export] public string TablePath { get; set; } = string.Empty;

    /// <summary>Per-actor luck added to the table's quality (e.g. elites drop better).</summary>
    [Export] public float QualityBonus { get; set; }

    private bool _dropped;

    protected override void OnInitialize()
    {
        if (Table == null && !string.IsNullOrEmpty(TablePath))
        {
            Table = ResidentResources.Load<LootTable>(TablePath);
            if (Table == null)
            {
                Log.Warn($"LootComponent on '{Entity?.DisplayName}' could not load loot table '{TablePath}'; it will drop nothing.");
            }
        }

        EventBus.Instance?.Subscribe<EntityDiedEvent>(OnEntityDied);
    }

    protected override void OnTeardown()
    {
        EventBus.Instance?.Unsubscribe<EntityDiedEvent>(OnEntityDied);
    }

    private void OnEntityDied(EntityDiedEvent e)
    {
        if (_dropped || Entity == null || !ReferenceEquals(e.Entity, Entity))
        {
            return;
        }

        _dropped = true;
        DropLoot(e.Killer);
    }

    /// <summary>
    /// Describes a roll made here and now for <paramref name="finder"/>: the tier of the realm the
    /// player is in, the looter's level and saved history, and their
    /// <see cref="PerkEffectKind.LootQuality"/> perks as luck. The looter is the player whenever
    /// there is one — a companion's kill or a burn that outlived its caster still drops for the
    /// person who picks it up — and <paramref name="finder"/> only when there is not.
    /// </summary>
    /// <param name="finder">Whoever killed the owner or opened the container; may be null.</param>
    /// <param name="extraQuality">The source's own quality (an elite's bonus).</param>
    internal static LootContext ContextFor(IEntity? finder, float extraQuality = 0f)
    {
        IEntity? looter = finder;
        if (ServiceLocator.Instance is { } locator && locator.TryGet(out PlayerCharacter player))
        {
            looter = player;
        }

        return new LootContext
        {
            Tier = CurrentRealmTier(),
            FinderLevel = looter?.GetComponent<ProgressionComponent>()?.Level ?? 0,
            ExtraQuality = extraQuality,
            Luck = PerkQuery.Of(looter, PerkEffectKind.LootQuality),
            Ledger = LootLedger.Of(looter),
        };
    }

    /// <summary>The tier of the realm currently streamed in, or 0 when no region is active (a
    /// sandbox, a probe), which rolls level-less.</summary>
    internal static int CurrentRealmTier()
    {
        if (ServiceLocator.Instance is not { } locator || !locator.TryGet(out RegionStreamer streamer))
        {
            return 0;
        }

        return RegionDatabase.Get(streamer.ActiveRegionId) is { } region ? LootTiers.TierOfRealm(region.Realm) : 0;
    }

    /// <param name="killer">Whoever landed the killing blow, or null (a fall, a damage-over-time tick with no
    /// owner).</param>
    private void DropLoot(IEntity? killer)
    {
        if (Table == null || Entity == null)
        {
            return;
        }

        Node? parent = ((Node)Entity.Body).GetParent();
        if (parent == null)
        {
            return;
        }

        Vector3 origin = Entity.Body.GlobalPosition;
        LootTable table = Table;
        float quality = QualityBonus;
        string owner = Entity.DisplayName;

        if (table.DropsAsChest)
        {
            // A duel that ends when the boss withdraws pays nothing out, even if a single blow
            // happened to kill it before it could yield: the reward belongs to the final fight.
            if (Entity.GetComponent<Embervale.Enemies.BossController>()?.Fight is { WithdrawHealthFraction: > 0f })
            {
                return;
            }

            // Deferred: death is raised mid-damage, and standing a chest adds a node to the world.
            // Nothing below touches this component again, so it may be freed before the call lands.
            Callable.From(() => DeliverAsChest(table, parent, origin, killer, quality, owner)).CallDeferred();
            return;
        }

        Scatter(LootGenerator.Generate(table, ContextFor(killer, quality)), parent, origin, owner);
    }

    /// <summary>Stands the reward chest. When that is not possible (no spawn director, no table
    /// path to save) the loot is rolled now and scattered instead, so a boss never drops nothing.</summary>
    private static void DeliverAsChest(LootTable table, Node parent, Vector3 origin, IEntity? killer, float quality,
        string owner)
    {
        if (!IsInstanceValid(parent))
        {
            return;
        }

        string tablePath = table.ResourcePath;
        if (tablePath.Length > 0 &&
            ServiceLocator.Instance is { } locator && locator.TryGet(out PersistentSpawnDirector director))
        {
            // The id is counted in the ledger so a second chest in one save never reuses the
            // first one's; without a ledger the director numbers it.
            string id = LootLedger.Of(killer) is { } ledger ? $"chest.reward.{ledger.NextChestOrdinal()}" : string.Empty;
            IEntity? chest = director.Spawn(GameIds.Templates.Cache, id, origin);
            if (chest?.GetComponent<ContainerLootComponent>() is { } container)
            {
                container.Arm(tablePath);
                EventBus.Instance?.Publish(new RewardChestSpawnedEvent(chest, tablePath));
                Log.Info($"{owner} left a reward chest ({tablePath}).");
                return;
            }
        }

        Log.Warn($"{owner} could not leave a reward chest; its loot is scattered instead.");
        Scatter(LootGenerator.Generate(table, ContextFor(killer, quality)), parent, origin, owner);
    }

    private static void Scatter(List<LootDrop> drops, Node parent, Vector3 origin, string owner)
    {
        if (drops.Count == 0)
        {
            return;
        }

        ItemRarity best = ItemRarity.Common;
        int index = 0;
        foreach (LootDrop drop in drops)
        {
            Vector3 spot = ScatterAround(origin, index++);
            Entity pickup = ItemPickupFactory.Create(drop.Instance, drop.Quantity, spot);
            // Deferred: death is raised mid-damage; don't mutate the tree inline.
            parent.CallDeferred(Node.MethodName.AddChild, pickup);
            if (drop.Instance.Rarity > best)
            {
                best = drop.Instance.Rarity;
            }
        }

        AnnounceDrop(best, origin);
        Log.Info($"{owner} dropped {drops.Count} item(s).");
    }

    /// <summary>The drop chime: one cue for the best piece in a drop, pitched by its rarity. Common
    /// and Uncommon drops are silent, so the sound means "look down".</summary>
    internal static void AnnounceDrop(ItemRarity best, Vector3 position)
    {
        if (LootPresentation.IsAnnounced(best))
        {
            EventBus.Instance?.Publish(new SoundCueRequestedEvent(
                LootPresentation.ChimeCue, position, LootPresentation.ChimeVolumeDb(best), LootPresentation.ChimePitch(best)));
        }
    }

    /// <summary>Spirals drop positions outward from <paramref name="origin"/> so multiple items
    /// don't stack on one spot (shared with container looting and quest/event reward overflow).
    ///
    /// ⚠️ <b>The Y comes from the ground, not from zero.</b> It used to be a literal <c>0f</c>, which
    /// was every drop's correct height for exactly as long as the world was flat. On real terrain a
    /// kill on a terrace buried its loot and a kill in a basin left it floating out of reach —
    /// silently, because a pickup does not complain about where it is.</summary>
    internal static Vector3 ScatterAround(Vector3 origin, int index)
    {
        float angle = index * 2.39996f; // golden angle for an even spread
        float radius = 0.4f + (index * 0.25f);
        float x = origin.X + (Mathf.Cos(angle) * radius);
        float z = origin.Z + (Mathf.Sin(angle) * radius);
        return new Vector3(x, Embervale.World.WorldGround.HeightAt(x, z), z);
    }
}
