using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Embervale.Debugging;
using Embervale.Economy;
using Embervale.Items;
using Embervale.Stats;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// The shipped item catalogue (<c>tools/gen_items.py</c>), read straight from the authored <c>.tres</c> files so a
/// retune that breaks the stat budget, the price function or a reference fails the suite instead of a play session.
/// <see cref="ItemBudget"/> is the C# mirror of the generator's numbers; these facts hold the two together far more
/// tightly than <c>--validate</c>'s quarter, which exists to catch hand edits.
/// </summary>
public class ItemCatalogueTests
{
    private static readonly Lazy<Dictionary<string, Dictionary<string, string>>> Items = new(() => LoadFolder("items"));

    private static readonly (string Field, StatType Stat)[] Bonuses =
    {
        ("BonusArmor", StatType.Armor), ("BonusPhysicalPower", StatType.PhysicalPower),
        ("BonusSpellPower", StatType.SpellPower), ("BonusMaxHealth", StatType.Health),
        ("BonusMaxStamina", StatType.Stamina), ("BonusCritChance", StatType.CritChance),
        ("BonusMoveSpeed", StatType.MoveSpeed), ("BonusFrostResist", StatType.FrostResist),
        ("BonusFireResist", StatType.FireResist), ("BonusLightningResist", StatType.LightningResist),
        ("BonusArcaneResist", StatType.ArcaneResist), ("BonusNatureResist", StatType.NatureResist),
        ("BonusNecroticResist", StatType.NecroticResist), ("BonusMana", StatType.Mana),
    };

    private static IEnumerable<Dictionary<string, string>> Equippables =>
        ItemCatalogueIds.ItemIds.Select(id => Items.Value[id]).Where(item => item.ContainsKey("Slot"));

    [Fact]
    public void EveryPlannedId_IsAuthored()
    {
        foreach (string id in ItemCatalogueIds.ItemIds)
        {
            Assert.True(Items.Value.ContainsKey(id), $"{id} has no .tres in data/items");
        }

        HashSet<string> sets = LoadFolder("item_sets").Keys.ToHashSet();
        HashSet<string> effects = LoadFolder("unique_effects").Keys.ToHashSet();
        Assert.All(ItemCatalogueIds.SetIds, id => Assert.Contains(id, sets));
        Assert.All(ItemCatalogueIds.UniqueEffectIds, id => Assert.Contains(id, effects));
        Assert.Equal(ItemCatalogueIds.ItemIds.Count, ItemCatalogueIds.ItemIds.Distinct().Count());
    }

    [Fact]
    public void EveryTier_HasEveryWeaponClassAndArmourPiece()
    {
        // The whole shelf: the twelve retrofitted originals fill grid cells no new item is planned for.
        List<Dictionary<string, string>> tiered = Items.Value.Values.Where(i => i.ContainsKey("Slot") && Int(i, "Tier") > 0).ToList();
        Assert.True(Equippables.Count() >= 150, "the catalogue should add at least 150 equippables");
        for (int tier = 1; tier <= 6; tier++)
        {
            List<Dictionary<string, string>> inTier = tiered.Where(i => Int(i, "Tier") == tier).ToList();
            foreach (WeaponClass weaponClass in Enum.GetValues<WeaponClass>().Where(c => c != WeaponClass.None))
            {
                Assert.True(inTier.Exists(i => Int(i, "WeaponClass") == (int)weaponClass), $"tier {tier} has no {weaponClass}");
            }

            foreach (ArmorWeight weight in Enum.GetValues<ArmorWeight>().Where(w => w != ArmorWeight.None))
            {
                foreach (EquipmentSlot slot in new[] { EquipmentSlot.Head, EquipmentSlot.Chest, EquipmentSlot.Hands, EquipmentSlot.Legs, EquipmentSlot.Feet })
                {
                    Assert.True(inTier.Exists(i => Int(i, "ArmorWeight") == (int)weight && Int(i, "Slot") == (int)slot),
                        $"tier {tier} has no {weight} {slot}");
                }
            }

            foreach (EquipmentSlot slot in new[] { EquipmentSlot.Ring, EquipmentSlot.Amulet, EquipmentSlot.Ammo })
            {
                Assert.True(inTier.Exists(i => Int(i, "Slot") == (int)slot), $"tier {tier} has no {slot}");
            }
        }
    }

    [Fact]
    public void ItemLevels_SitInsideTheirTierBand()
    {
        foreach (Dictionary<string, string> item in Items.Value.Values.Where(i => Int(i, "Tier") > 0 && Int(i, "ItemLevel") > 0))
        {
            (int low, int high) = ItemBudget.TierBand(Int(item, "Tier"));
            Assert.InRange(Int(item, "ItemLevel"), low, high);
            Assert.True(Int(item, "RequiredLevel") <= Int(item, "ItemLevel"), Text(item, "Id"));
        }
    }

    [Fact]
    public void Stats_LandOnTheBudget_AndPricesOnTheValueFunction()
    {
        foreach (Dictionary<string, string> item in Equippables)
        {
            string id = Text(item, "Id");
            var slot = (EquipmentSlot)Int(item, "Slot");
            var rarity = (ItemRarity)Int(item, "Rarity");
            int level = Int(item, "ItemLevel");
            float budget = ItemBudget.Points(level, slot, item.GetValueOrDefault("TwoHanded") == "true", rarity);
            float spent = Float(item, "BonusManaRegen") * ItemBudget.ManaRegenWeight +
                          Float(item, "BonusStaminaRegen") * ItemBudget.StaminaRegenWeight +
                          Bonuses.Sum(b => Float(item, b.Field) * ItemBudget.Weight(b.Stat));

            // The generator gives the primary stat whatever rounding leaves, so the miss is one grain at most.
            Assert.True(Math.Abs(spent - budget) <= Math.Max(0.12f * budget, ItemBudget.Grain), $"{id}: {spent} against {budget}");
            Assert.True(ItemBudget.WithinBudget(spent, budget), id);
            Assert.True(spent > 0f, $"{id} grants nothing");
            Assert.Equal(ItemBudget.Value(level, rarity, slot), Int(item, "Value"));
            Assert.Equal(ItemBudget.IsTwoHandedClass((WeaponClass)Int(item, "WeaponClass")), item.GetValueOrDefault("TwoHanded") == "true");
        }
    }

    [Fact]
    public void MainHandItems_CarryAWeaponOfTheirClass()
    {
        string weapons = Path.Combine(StringsCsv.RepositoryRoot(), "data", "weapons");
        foreach (Dictionary<string, string> item in Equippables.Where(i => Int(i, "Slot") == (int)EquipmentSlot.MainHand))
        {
            string id = Text(item, "Id");
            Match path = Regex.Match(item["__text"], @"path=""res://data/weapons/([^""]+)""");
            Assert.True(path.Success, $"{id} has no weapon resource");
            Dictionary<string, string> weapon = Resource(File.ReadAllText(Path.Combine(weapons, path.Groups[1].Value)));
            var weaponClass = (WeaponClass)Int(item, "WeaponClass");
            float damage = ItemBudget.WeaponDamage(Int(item, "ItemLevel"), weaponClass, (ItemRarity)Int(item, "Rarity"));

            Assert.InRange(Float(weapon, "BaseDamage"), damage - 0.51f, damage + 0.51f);
            Assert.Equal(weaponClass == WeaponClass.Bow, weapon.GetValueOrDefault("IsRanged") == "true");
            Assert.True(Float(weapon, "WindupTime") > 0f && Float(weapon, "StaminaCost") > 0f, id);
        }
    }

    [Fact]
    public void TwoHanders_AreSlowerAndHitHarderThanASword()
    {
        // Same level and rarity, so the only difference is the class.
        float sword = ItemBudget.WeaponDamage(30, WeaponClass.Sword, ItemRarity.Common);
        Assert.True(ItemBudget.WeaponDamage(30, WeaponClass.Greatsword, ItemRarity.Common) > sword * 1.4f);
        Assert.True(ItemBudget.WeaponDamage(30, WeaponClass.Dagger, ItemRarity.Common) < sword);
        Assert.True(ItemBudget.Points(30, EquipmentSlot.MainHand, true, ItemRarity.Common) >
                    ItemBudget.Points(30, EquipmentSlot.MainHand, false, ItemRarity.Common));
        Assert.True(ItemBudget.Points(30, EquipmentSlot.Chest, false, ItemRarity.Legendary) >
                    ItemBudget.Points(30, EquipmentSlot.Chest, false, ItemRarity.Epic));

        string weapons = Path.Combine(StringsCsv.RepositoryRoot(), "data", "weapons");
        Dictionary<string, string> one = Resource(File.ReadAllText(Path.Combine(weapons, "PlayerBlacksteelSword.tres")));
        Dictionary<string, string> two = Resource(File.ReadAllText(Path.Combine(weapons, "PlayerBlacksteelGreatsword.tres")));
        Assert.True(Float(two, "WindupTime") > Float(one, "WindupTime"));
        Assert.True(Float(two, "AttackSpeed") < Float(one, "AttackSpeed"));
        Assert.True(Float(two, "BaseDamage") > Float(one, "BaseDamage"));
    }

    [Fact]
    public void Values_RiseWithLevelAndRarity_AndStayInsideTheEconomy()
    {
        for (int level = 2; level <= 50; level++)
        {
            Assert.True(ItemBudget.Value(level, ItemRarity.Common, EquipmentSlot.Chest) >=
                        ItemBudget.Value(level - 1, ItemRarity.Common, EquipmentSlot.Chest));
        }

        foreach (ItemRarity rarity in Enum.GetValues<ItemRarity>().Where(r => r != ItemRarity.Common))
        {
            Assert.True(ItemBudget.Value(30, rarity, EquipmentSlot.Chest) > ItemBudget.Value(30, rarity - 1, EquipmentSlot.Chest));
        }

        // The dearest thing the catalogue sells stays a purchase a late character can make, not a wall.
        Assert.InRange(Equippables.Max(i => Int(i, "Value")), 800, 1500);
        Assert.All(ItemCatalogueIds.ItemIds, id => Assert.True(Int(Items.Value[id], "Value") >= 1, id));
    }

    [Fact]
    public void SetsAndUniqueEffects_ResolveBothWays_AndHaveText()
    {
        Dictionary<string, Dictionary<string, string>> sets = LoadFolder("item_sets");
        Dictionary<string, Dictionary<string, string>> effects = LoadFolder("unique_effects");
        Dictionary<string, string> locale = StringsCsv.Rows();

        foreach ((string setId, Dictionary<string, string> set) in sets)
        {
            Assert.True(locale.ContainsKey(Text(set, "DisplayNameKey")), setId);
            List<string> pieces = Quoted(set["PieceIds"]);
            Assert.InRange(pieces.Count, 3, 5);
            Assert.All(pieces, piece => Assert.Equal(setId, Text(Items.Value[piece], "SetId")));
            Assert.Equal(pieces.Count, pieces.Select(piece => Int(Items.Value[piece], "Slot")).Distinct().Count());
            foreach (Match effect in Regex.Matches(set["__text"], @"EffectId = ""([^""]+)"""))
            {
                Assert.Contains(effect.Groups[1].Value, effects.Keys);
            }
        }

        foreach ((string effectId, Dictionary<string, string> effect) in effects)
        {
            Assert.True(locale.ContainsKey(Text(effect, "NameKey")), effectId);
            Assert.True(locale.TryGetValue(Text(effect, "DescriptionKey"), out string? line), effectId);
            Assert.DoesNotContain("{", line);
        }

        foreach (Dictionary<string, string> item in Items.Value.Values)
        {
            Assert.True(Text(item, "SetId").Length == 0 || sets.ContainsKey(Text(item, "SetId")), Text(item, "Id"));
            Assert.True(Text(item, "UniqueEffectId").Length == 0 || effects.ContainsKey(Text(item, "UniqueEffectId")), Text(item, "Id"));
        }

        // Every effect is worn by something: a legendary or a set threshold.
        HashSet<string> worn = Items.Value.Values.Select(i => Text(i, "UniqueEffectId")).ToHashSet();
        worn.UnionWith(sets.Values.SelectMany(s => Regex.Matches(s["__text"], @"EffectId = ""([^""]+)""").Select(m => m.Groups[1].Value)));
        Assert.All(effects.Keys, id => Assert.Contains(id, worn));
    }

    [Fact]
    public void ItemText_IsPresent_KnownToTheMarket_AndNeverNamesTheFifthRealm()
    {
        foreach (string id in ItemCatalogueIds.ItemIds)
        {
            Dictionary<string, string> item = Items.Value[id];
            string text = Text(item, "DisplayName") + " " + Text(item, "Description");
            Assert.True(Text(item, "DisplayName").Length > 0 && Text(item, "Description").Length > 0, id);
            Assert.DoesNotContain("—", text);
            Assert.DoesNotContain("concord", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("pale", Text(item, "DisplayName"), StringComparison.OrdinalIgnoreCase);
            List<string> tags = Quoted(item["TradeTags"]);
            Assert.NotEmpty(tags);
            Assert.All(tags, tag => Assert.True(TradeTags.IsKnown(tag), $"{id}: {tag}"));
        }
    }

    [Fact]
    public void ShopsAndPools_NameRealItems_AndPoolsHoldNoStackables()
    {
        string data = Path.Combine(StringsCsv.RepositoryRoot(), "data");
        foreach (string folder in new[] { "shops", "loot" })
        {
            foreach (string file in Directory.GetFiles(Path.Combine(data, folder), "*.tres"))
            {
                foreach (Match row in Regex.Matches(File.ReadAllText(file), @"^ItemId = ""([^""]+)""", RegexOptions.Multiline))
                {
                    Assert.True(Items.Value.ContainsKey(row.Groups[1].Value), $"{Path.GetFileName(file)} names {row.Groups[1].Value}");
                }
            }
        }

        foreach (string pool in new[] { "ShopLeveledGear", "TravellerLeveledGear", "EmbermarketCaravanGear" })
        {
            foreach (Match row in Regex.Matches(File.ReadAllText(Path.Combine(data, "loot", pool + ".tres")), @"^ItemId = ""([^""]+)""", RegexOptions.Multiline))
            {
                Assert.True(Int(Items.Value[row.Groups[1].Value], "MaxStack", 99) == 1, $"{pool} rolls stackable {row.Groups[1].Value}");
            }
        }
    }

    [Fact]
    public void RecipeScrolls_MapOntoRecipeIds()
    {
        Assert.Equal("recipe.blacksteel_sword", ItemCatalogueIds.RecipeOfScroll("item.recipe_scroll.blacksteel_sword"));
        Assert.Equal(string.Empty, ItemCatalogueIds.RecipeOfScroll("item.weapon.iron_sword"));
        Assert.Contains(ItemCatalogueIds.ItemIds, id => id.StartsWith(ItemCatalogueIds.RecipeScrollPrefix, StringComparison.Ordinal));
    }

    // ---- reading the .tres files ----

    private static Dictionary<string, Dictionary<string, string>> LoadFolder(string folder)
    {
        var byId = new Dictionary<string, Dictionary<string, string>>();
        foreach (string file in Directory.GetFiles(Path.Combine(StringsCsv.RepositoryRoot(), "data", folder), "*.tres"))
        {
            Dictionary<string, string> resource = Resource(File.ReadAllText(file));
            byId[Text(resource, "Id")] = resource;
        }

        return byId;
    }

    /// <summary>The key/value pairs of a file's <c>[resource]</c> section, plus the whole file under <c>__text</c>.</summary>
    private static Dictionary<string, string> Resource(string text)
    {
        var fields = new Dictionary<string, string> { ["__text"] = text };
        bool inResource = false;
        foreach (string raw in text.Split('\n'))
        {
            string line = raw.TrimEnd('\r');
            if (line.StartsWith('['))
            {
                inResource = line == "[resource]";
            }
            else if (inResource && line.Contains(" = "))
            {
                int split = line.IndexOf(" = ", StringComparison.Ordinal);
                fields[line[..split]] = line[(split + 3)..];
            }
        }

        return fields;
    }

    private static string Text(Dictionary<string, string> fields, string key) =>
        fields.TryGetValue(key, out string? value) ? value.Trim('"') : string.Empty;

    private static int Int(Dictionary<string, string> fields, string key, int fallback = 0) =>
        fields.TryGetValue(key, out string? value) ? int.Parse(value, CultureInfo.InvariantCulture) : fallback;

    private static float Float(Dictionary<string, string> fields, string key) =>
        fields.TryGetValue(key, out string? value) ? float.Parse(value, CultureInfo.InvariantCulture) : 0f;

    private static List<string> Quoted(string list) =>
        Regex.Matches(list, "\"([^\"]+)\"").Select(m => m.Groups[1].Value).ToList();
}
