using Embervale.Core;
using Embervale.Core.Diagnostics;
using Embervale.Entities;
using Embervale.Enemies;
using Embervale.Items;
using Embervale.Loot;
using Godot;

namespace Embervale.Bootstrap;

/// <summary>
/// The sandbox furniture: the loot pile, the Ashen tome on its plinth, and the goblin camp in the
/// northern wilds. None of it is the game — it is the scaffolding the systems were exercised against
/// before there was content, and it is still the fastest way to reach a pickup, a crafting material or
/// a fight from a cold boot.
///
/// <b>Why it lives here rather than in <see cref="GameBootstrap"/>.</b> Not for deletability — that
/// already existed, and still does: every call is behind <c>BuildProfile.SpawnSandboxContent</c>, so a
/// capture build places none of it. This is purely about the bootstrap being the project's largest file
/// and its coupling hotspot. Demo furniture is the part least likely to survive contact with real
/// regions, so it is the part that should be easiest to find, reason about and eventually drop —
/// grouping it here makes "what is scaffolding?" one file rather than a grep.
///
/// The training dummy deliberately stayed behind: it is wired into the bootstrap's respawn countdown
/// state, and dragging that across the boundary would trade a tidy file for a worse seam.
/// </summary>
internal static class SandboxProps
{
    /// <summary>Places every piece of sandbox scaffolding under <paramref name="root"/>.</summary>
    internal static void Seed(Node root)
    {
        SeedEnemyCamp(root);
        SeedLoot(root);
        SeedSpellTome(root);
    }

    private static void SeedEnemyCamp(Node root)
    {
        root.AddChild(new EnemySpawnDirector
        {
            Name = "GoblinCamp",
            // Out in the northern wilds (wilds_north cell), clear of the town's safe zone.
            Position = new Vector3(0f, 0f, -58f),
            MaxAlive = 3,
            SpawnRadius = 6f,
        });
        Log.Info("A goblin camp stirs in the northern wilds (−Z).");
    }

    private static void SeedLoot(Node root)
    {
        // A few collectables strewn between the player and the goblin camp.
        TryDropPickup(root, GameIds.Items.HealthPotion, 2, new Vector3(1.5f, 0f, 2f));
        TryDropPickup(root, GameIds.Items.IronOre, 3, new Vector3(-2f, 0f, 0f));
        TryDropPickup(root, GameIds.Items.Ruby, 1, new Vector3(0f, 0f, -3f));
        TryDropPickup(root, GameIds.Currency.Gold, 25, new Vector3(2.5f, 0f, -1f));

        // Crafting materials so the stations to the west have something to work with.
        TryDropPickup(root, GameIds.Items.IronOre, 4, new Vector3(-4.5f, 0f, 6f));
        TryDropPickup(root, GameIds.Items.GoblinHide, 4, new Vector3(-4f, 0f, 6.8f));
        TryDropPickup(root, GameIds.Items.HealingHerb, 5, new Vector3(-3.2f, 0f, 6.6f));

        // Equippable gear to try out the equipment screen.
        TryDropPickup(root, GameIds.Items.LeatherCap, 1, new Vector3(-1.2f, 0f, 3f));
        TryDropPickup(root, GameIds.Items.LeatherVest, 1, new Vector3(-3f, 0f, 2.5f));
        TryDropPickup(root, GameIds.Items.SteelSword, 1, new Vector3(1.5f, 0f, -2.5f));
        TryDropPickup(root, GameIds.Items.IronRing, 1, new Vector3(3f, 0f, -3.5f));

        // A procedurally-rolled Rare blade to show off the affix pipeline.
        if (ItemDatabase.Get(GameIds.Items.SteelSword) is EquippableItemResource sword)
        {
            ItemInstance rolled = LootGenerator.RollAffixed(sword, ItemRarity.Rare);
            root.AddChild(ItemPickupFactory.Create(rolled, 1, new Vector3(-1.5f, 0f, -1.5f)));
            Log.Info($"Seeded a rolled drop: {rolled.DisplayName}.");
        }
    }

    private static void TryDropPickup(Node root, string itemId, int quantity, Vector3 position)
    {
        ItemResource? item = ItemDatabase.Get(itemId);
        if (item != null)
        {
            root.AddChild(ItemPickupFactory.Create(item, quantity, position));
        }
    }

    /// <summary>Places a recovered-spellcraft tome near spawn (Phase 29.5E): the fading-Weave rule that
    /// lost spells are found, not bought. This one holds the corrupted <c>Ember Siphon</c>, so it yields
    /// only to a sufficiently corrupted reader (the 23H gate) — and that necrotic line grows cheaper/
    /// stronger as the Weave fades. Try: <c>corruption set N</c> then interact.</summary>
    private static void SeedSpellTome(Node root)
    {
        var tome = new Entity
        {
            Name = "SpellTome",
            DisplayName = "Ashen Tome",
            Position = new Vector3(-5f, 0f, 0f),
        };

        // 30J: the tome-stand model (lectern + open book with ember glyphs, origin at feet);
        // glowing box fallback if unimported.
        if (GD.Load<PackedScene>("res://assets/models/props/prp_tome_stand.glb")?.Instantiate() is Node3D tomeVisual)
        {
            tomeVisual.Name = "Mesh";
            tome.AddChild(tomeVisual);
        }
        else
        {
            tome.AddChild(new MeshInstance3D
            {
                Name = "Mesh",
                Mesh = new BoxMesh { Size = new Vector3(0.4f, 0.5f, 0.12f) },
                Position = new Vector3(0f, 0.7f, 0f),
                MaterialOverride = new StandardMaterial3D
                {
                    AlbedoColor = new Color(0.35f, 0.18f, 0.40f),
                    EmissionEnabled = true,
                    Emission = new Color(0.45f, 0.20f, 0.50f),
                    EmissionEnergyMultiplier = 0.6f,
                },
            });
        }

        var collider = new StaticBody3D { Name = "Collider" };
        collider.AddChild(new CollisionShape3D
        {
            Shape = new BoxShape3D { Size = new Vector3(0.5f, 0.7f, 0.4f) },
            Position = new Vector3(0f, 0.5f, 0f),
        });
        tome.AddChild(collider);

        tome.AddChild(new Magic.SpellTomeComponent { Name = "Tome", SpellId = GameIds.Spells.EmberSiphon });
        root.AddChild(tome);
        Log.Info("An Ashen Tome rests west of spawn — recover its lost spellcraft (corruption-gated).");
    }
}
