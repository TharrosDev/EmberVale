using System;
using System.Collections.Generic;
using System.Text.Json;
using Embervale.Items;

namespace Embervale.UI;

/// <summary>
/// Which painted icon an item draws, by its id. Pure.
///
/// Item <c>.tres</c> files are generated, so none carries a hand-set <c>Icon</c>; the 373 items
/// share about eighty painted archetypes on one atlas instead, and an item finds its archetype from
/// the shape of its id (<c>item.weapon.steel_sword</c> ends in <c>_sword</c>). The key list here is
/// the list of pictures to paint: <c>tools/pack_ui_atlas.gd</c> packs one <c>&lt;key&gt;.png</c>
/// per entry of <see cref="Keys"/>.
///
/// Resolution order: a category whose every item shares a picture (<see cref="Prefixes"/>, longest
/// first), then what the name ends in (<see cref="Suffixes"/>, longest first, on a word boundary),
/// then the equipment slot, then the item type. So every id resolves to something, and a key with
/// no picture on the atlas yet simply leaves the slot drawing its category glyph.
/// </summary>
public static class ItemIconRules
{
    private const string IdPrefix = "item.";

    // The last resort: one picture per item type, and one for the two jewellery slots.
    public const string Weapon = "weapon";
    public const string Armor = "armor";
    public const string Consumable = "consumable";
    public const string Material = "material";
    public const string Quest = "quest";
    public const string Misc = "misc";
    public const string Ring = "ring";
    public const string Amulet = "amulet";

    /// <summary>Categories (the part of the id after <c>item.</c>) that decide the picture on their
    /// own, whatever the name ends in: a recipe scroll for a jerkin is a scroll.</summary>
    private static readonly (string Prefix, string Key)[] Prefixes =
    {
        ("recipe_scroll.", "scroll"),
        ("relic.", "relic_heart"),
        ("currency.", "coin"),
        ("ammo.", "arrows"),
        ("kit.alchemy", "kit_alchemy"),
        ("kit.forge", "kit_forge"),
        ("kit.workbench", "kit_workbench"),
        ("decor.banner", "decor_banner"),
        ("decor.brazier", "decor_brazier"),
        ("decor.crate", "decor_crate"),
        ("decor.display_stand", "decor_stand"),
        ("tome.forged_writ", "writ"),
        ("tome.", "tome"),
        ("gem.ruby", "gem_ruby"),
        ("gem.sapphire", "gem_sapphire"),
        ("gem.", "gem_moonstone"),

        // A potion's tier is the end of its name, so its kind is read from the front.
        ("potion.health", "potion_health"),
        ("potion.mana", "potion_mana"),
        ("potion.stamina", "potion_stamina"),
        ("potion.cure", "potion_cure"),
        ("potion.might", "potion_might"),
        ("potion.insight", "potion_insight"),
        ("potion.stoneskin", "potion_stoneskin"),
        ("potion.resist_fire", "potion_resist_fire"),
        ("potion.resist_frost", "potion_resist_frost"),
        ("potion.resist_lightning", "potion_resist_lightning"),
        ("potion.resist_nature", "potion_resist_nature"),
        ("potion.resist_arcane", "potion_resist_arcane"),
        ("potion.resist_necrotic", "potion_resist_necrotic"),
        ("potion.", Consumable),
    };

    /// <summary>What a name ends in. A suffix matches only on a word boundary (the whole name, or
    /// after <c>_</c> or <c>.</c>), so <c>_bow</c> never claims an elbow.</summary>
    private static readonly (string Suffix, string Key)[] Suffixes =
    {
        // Weapons, and the named ones whose id does not say what they are.
        ("greatsword", "greatsword"), ("sword", "sword"), ("axe", "axe"), ("mace", "mace"),
        ("dagger", "dagger"), ("spear", "spear"), ("bow", "bow"), ("staff", "staff"),
        ("ashen_oath", "greatsword"), ("crownbreaker", "mace"), ("emberpike", "spear"),
        ("farsight", "bow"), ("packlords_talon", "dagger"), ("prophets_pyre", "staff"),
        ("stormcleaver", "axe"), ("winters_bite", "sword"), ("arrows", "arrows"),

        // Plate.
        ("cuirass", "plate_chest"), ("mail", "plate_chest"), ("aegis", "plate_chest"),
        ("helm", "plate_helm"), ("gauntlets", "plate_gauntlets"), ("greaves", "plate_greaves"),
        ("sabatons", "plate_sabatons"),

        // Leather.
        ("jerkin", "leather_chest"), ("vest", "leather_chest"), ("coif", "leather_coif"),
        ("cap", "leather_coif"), ("gloves", "leather_gloves"), ("grips", "leather_gloves"),
        ("breeches", "leather_breeches"), ("boots", "leather_boots"), ("treads", "leather_boots"),

        // Cloth.
        ("robe", "cloth_robe"), ("hood", "cloth_hood"), ("wraps", "cloth_wraps"),
        ("trousers", "cloth_trousers"), ("shoes", "cloth_shoes"), ("diadem", "circlet"),

        ("shield", "shield"), ("bulwark", "shield"), ("mirrorguard", "shield"),

        // Jewellery beyond the plain band and pendant.
        ("signet", "ring_signet"), ("seal", "ring_signet"),
        ("charm", "amulet_charm"), ("totem", "amulet_charm"), ("talisman", "amulet_charm"),

        // Food.
        ("bread", "food_bread"), ("oatcake", "food_bread"), ("toast", "food_bread"),
        ("ration", "food_bread"), ("pie", "food_pie"), ("stew", "food_stew"), ("broth", "food_stew"),
        ("tea", "food_tea"), ("roast", "food_roast"), ("catch", "food_fish"), ("fish", "food_fish"),
        ("salted_eel", "food_fish"),

        // Materials.
        ("ingot", "ingot"), ("ore", "ore"), ("coal", "coal"), ("charcoal", "coal"),
        ("pelt", "hide"), ("hide", "hide"), ("fur", "hide"), ("leather_strips", "hide"),
        ("scale", "scale"),
        ("wool", "cloth"), ("linen_bolt", "cloth"), ("sailcloth", "cloth"),
        ("cordage", "tackle"), ("hooks", "tackle"),
        ("ash", "dust"), ("dust", "dust"), ("salt", "dust"), ("chalk", "dust"),
        ("herb", "herb"), ("emberbloom", "herb"), ("dreamsmoke", "herb"),
        ("sack", "sack"), ("pouch", "sack"),
        ("pitch", "jar"), ("oil", "jar"), ("roe", "jar"),
        ("mote", "mote"), ("shard", "shard"), ("plank", "plank"), ("scrap", "scrap"),
    };

    /// <summary>Every key <see cref="Key"/> can return: the pictures the atlas is painted from.</summary>
    public static readonly IReadOnlyList<string> Keys = BuildKeys();

    /// <summary>The archetype key for an item. Never null or empty: an id nothing recognises gets
    /// its slot's or its type's picture.</summary>
    public static string Key(string? id, ItemType type, EquipmentSlot slot)
    {
        string name = id ?? string.Empty;
        if (name.StartsWith(IdPrefix, StringComparison.Ordinal))
        {
            name = name.Substring(IdPrefix.Length);
        }

        string? key = null;
        int length = -1;
        foreach ((string prefix, string value) in Prefixes)
        {
            if (prefix.Length > length && name.StartsWith(prefix, StringComparison.Ordinal))
            {
                key = value;
                length = prefix.Length;
            }
        }

        if (key != null)
        {
            return key;
        }

        foreach ((string suffix, string value) in Suffixes)
        {
            if (suffix.Length > length && EndsOnBoundary(name, suffix))
            {
                key = value;
                length = suffix.Length;
            }
        }

        return key ?? Fallback(type, slot);
    }

    /// <summary>The picture for an item no table entry names.</summary>
    public static string Fallback(ItemType type, EquipmentSlot slot) => slot switch
    {
        EquipmentSlot.Ring => Ring,
        EquipmentSlot.Amulet => Amulet,
        EquipmentSlot.Ammo => "arrows",
        EquipmentSlot.OffHand when type == ItemType.Armor => "shield",
        _ => type switch
        {
            ItemType.Weapon => Weapon,
            ItemType.Armor => Armor,
            ItemType.Consumable => Consumable,
            ItemType.Material => Material,
            ItemType.Quest => Quest,
            _ => Misc,
        },
    };

    /// <summary>
    /// Reads <c>atlas.json</c> as written by <c>tools/pack_ui_atlas.gd</c>:
    /// <c>{"cell": 128, "keys": {"sword": [column, row], ...}}</c>. Returns the cell size and the
    /// cell of each key; anything malformed is an empty atlas, never an exception.
    /// </summary>
    public static (int Cell, Dictionary<string, (int Column, int Row)> Cells) ParseAtlas(string? json)
    {
        var cells = new Dictionary<string, (int, int)>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(json))
        {
            return (0, cells);
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("cell", out JsonElement cellElement) ||
                !TryNumber(cellElement, out double cell) || cell < 1 ||
                !root.TryGetProperty("keys", out JsonElement keys) || keys.ValueKind != JsonValueKind.Object)
            {
                return (0, cells);
            }

            foreach (JsonProperty entry in keys.EnumerateObject())
            {
                JsonElement at = entry.Value;
                if (at.ValueKind == JsonValueKind.Array && at.GetArrayLength() == 2 &&
                    TryNumber(at[0], out double column) && TryNumber(at[1], out double row) &&
                    column >= 0 && row >= 0)
                {
                    cells[entry.Name] = ((int)column, (int)row);
                }
            }

            return ((int)cell, cells);
        }
        catch (JsonException)
        {
            cells.Clear();
            return (0, cells);
        }
    }

    // TryGetDouble throws on anything that is not a JSON number, so the kind is checked first.
    private static bool TryNumber(JsonElement element, out double value)
    {
        value = 0;
        return element.ValueKind == JsonValueKind.Number && element.TryGetDouble(out value);
    }

    private static bool EndsOnBoundary(string name, string suffix)
    {
        if (!name.EndsWith(suffix, StringComparison.Ordinal))
        {
            return false;
        }

        int before = name.Length - suffix.Length - 1;
        return before < 0 || name[before] == '_' || name[before] == '.';
    }

    private static string[] BuildKeys()
    {
        var keys = new List<string>();
        void Add(string key)
        {
            if (!keys.Contains(key))
            {
                keys.Add(key);
            }
        }

        foreach ((_, string key) in Prefixes)
        {
            Add(key);
        }

        foreach ((_, string key) in Suffixes)
        {
            Add(key);
        }

        foreach (string key in new[] { Weapon, Armor, Consumable, Material, Quest, Misc, Ring, Amulet })
        {
            Add(key);
        }

        return keys.ToArray();
    }
}
