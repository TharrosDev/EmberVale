using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Embervale.Items;
using Embervale.UI;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// The id-to-archetype table behind the painted item icons, checked against every item the game
/// ships (read from <c>data/items/*.tres</c> as text; the resources themselves cannot be built here).
/// </summary>
public class ItemIconRulesTests
{
    private static readonly string[] Generic =
    {
        ItemIconRules.Weapon, ItemIconRules.Armor, ItemIconRules.Consumable,
        ItemIconRules.Material, ItemIconRules.Quest, ItemIconRules.Misc,
    };

    [Fact]
    public void EveryShippedItem_ResolvesToADeclaredKey()
    {
        List<(string Id, ItemType Type, EquipmentSlot Slot)> items = ShippedItems();
        Assert.True(items.Count >= 373, $"only {items.Count} items were read from data/items");

        var keys = new HashSet<string>(ItemIconRules.Keys);
        foreach ((string id, ItemType type, EquipmentSlot slot) in items)
        {
            string key = ItemIconRules.Key(id, type, slot);
            Assert.False(string.IsNullOrEmpty(key), $"{id} resolved to no key");
            Assert.True(keys.Contains(key), $"{id} resolved to '{key}', which is not in ItemIconRules.Keys");
        }
    }

    [Fact]
    public void AlmostEveryShippedItem_HasAnArchetypeAndNotJustItsTypesPicture()
    {
        List<(string Id, ItemType Type, EquipmentSlot Slot)> items = ShippedItems();
        string[] generic = items
            .Where(item => Generic.Contains(ItemIconRules.Key(item.Id, item.Type, item.Slot)))
            .Select(item => item.Id)
            .ToArray();

        // Not zero by rule: a new item may ship before its picture does. A tenth of the catalogue
        // on type pictures means the table has fallen behind the content.
        Assert.True(generic.Length * 10 <= items.Count,
            "Items with no archetype: " + string.Join(", ", generic.Take(30)));
    }

    [Fact]
    public void TheKeyList_HasNoDuplicatesAndIsAFileName()
    {
        Assert.Equal(ItemIconRules.Keys.Count, ItemIconRules.Keys.Distinct().Count());
        Assert.All(ItemIconRules.Keys, key => Assert.Matches("^[a-z][a-z0-9_]*$", key));
    }

    [Theory]
    [InlineData("item.weapon.steel_sword", ItemType.Weapon, EquipmentSlot.MainHand, "sword")]
    [InlineData("item.weapon.steel_greatsword", ItemType.Weapon, EquipmentSlot.MainHand, "greatsword")]
    [InlineData("item.weapon.winters_bite", ItemType.Weapon, EquipmentSlot.MainHand, "sword")]
    [InlineData("item.weapon.hunting_bow", ItemType.Weapon, EquipmentSlot.MainHand, "bow")]
    [InlineData("item.ammo.steel_arrows", ItemType.Weapon, EquipmentSlot.Ammo, "arrows")]
    [InlineData("item.armor.iron_cuirass", ItemType.Armor, EquipmentSlot.Chest, "plate_chest")]
    [InlineData("item.armor.rawhide_jerkin", ItemType.Armor, EquipmentSlot.Chest, "leather_chest")]
    [InlineData("item.armor.homespun_robe", ItemType.Armor, EquipmentSlot.Chest, "cloth_robe")]
    [InlineData("item.armor.round_shield", ItemType.Armor, EquipmentSlot.OffHand, "shield")]
    [InlineData("item.ring.gold", ItemType.Misc, EquipmentSlot.Ring, "ring")]
    [InlineData("item.ring.brokers_signet", ItemType.Misc, EquipmentSlot.Ring, "ring_signet")]
    [InlineData("item.amulet.silver_pendant", ItemType.Misc, EquipmentSlot.Amulet, "amulet")]
    [InlineData("item.potion.health", ItemType.Consumable, EquipmentSlot.None, "potion_health")]
    [InlineData("item.potion.health_superior", ItemType.Consumable, EquipmentSlot.None, "potion_health")]
    [InlineData("item.potion.resist_frost_lesser", ItemType.Consumable, EquipmentSlot.None, "potion_resist_frost")]
    [InlineData("item.food.roe_toast", ItemType.Consumable, EquipmentSlot.None, "food_bread")]
    [InlineData("item.food.salted_eel", ItemType.Material, EquipmentSlot.None, "food_fish")]
    [InlineData("item.material.iron_ore", ItemType.Material, EquipmentSlot.None, "ore")]
    [InlineData("item.material.steel_ingot", ItemType.Material, EquipmentSlot.None, "ingot")]
    [InlineData("item.gem.moonstone_rough", ItemType.Material, EquipmentSlot.None, "gem_moonstone")]
    [InlineData("item.currency.gold", ItemType.Misc, EquipmentSlot.None, "coin")]
    [InlineData("item.tome.forged_writ", ItemType.Misc, EquipmentSlot.None, "writ")]
    [InlineData("item.tome.herbal", ItemType.Misc, EquipmentSlot.None, "tome")]
    public void AnItem_FindsItsArchetype(string id, ItemType type, EquipmentSlot slot, string expected)
    {
        Assert.Equal(expected, ItemIconRules.Key(id, type, slot));
    }

    [Fact]
    public void ACategoryThatSharesOnePicture_WinsOverWhatTheNameEndsIn()
    {
        // A recipe for a jerkin is a scroll, not a jerkin.
        Assert.Equal("scroll", ItemIconRules.Key("item.recipe_scroll.ashhide_jerkin", ItemType.Misc, EquipmentSlot.None));
        Assert.Equal("scroll", ItemIconRules.Key("item.recipe_scroll.moonsilver_sword", ItemType.Misc, EquipmentSlot.None));
    }

    [Fact]
    public void ASuffix_MatchesOnlyOnAWordBoundary()
    {
        // "elbow" ends in "bow" and "seashore" in "ore"; neither is one.
        Assert.Equal(ItemIconRules.Misc, ItemIconRules.Key("item.thing.elbow", ItemType.Misc, EquipmentSlot.None));
        Assert.Equal(ItemIconRules.Material, ItemIconRules.Key("item.material.seashore", ItemType.Material, EquipmentSlot.None));
    }

    [Theory]
    [InlineData(ItemType.Weapon, EquipmentSlot.MainHand, "weapon")]
    [InlineData(ItemType.Armor, EquipmentSlot.Chest, "armor")]
    [InlineData(ItemType.Armor, EquipmentSlot.OffHand, "shield")]
    [InlineData(ItemType.Consumable, EquipmentSlot.None, "consumable")]
    [InlineData(ItemType.Material, EquipmentSlot.None, "material")]
    [InlineData(ItemType.Quest, EquipmentSlot.None, "quest")]
    [InlineData(ItemType.Misc, EquipmentSlot.None, "misc")]
    [InlineData(ItemType.Misc, EquipmentSlot.Ring, "ring")]
    [InlineData(ItemType.Misc, EquipmentSlot.Amulet, "amulet")]
    public void AnIdNothingRecognises_GetsItsSlotsOrItsTypesPicture(ItemType type, EquipmentSlot slot, string expected)
    {
        Assert.Equal(expected, ItemIconRules.Key("item.unknown.thing", type, slot));
        Assert.Equal(expected, ItemIconRules.Key(null, type, slot));
        Assert.Contains(expected, ItemIconRules.Keys);
    }

    [Fact]
    public void TheAtlasIndex_ReadsItsCellsAndSurvivesAnythingElse()
    {
        (int cell, Dictionary<string, (int Column, int Row)> cells) = ItemIconRules.ParseAtlas(
            "{\"cell\": 128, \"columns\": 10, \"keys\": {\"sword\": [0, 0], \"ingot\": [3, 2], \"bad\": [1], \"worse\": \"x\", \"text\": [\"3\", 2], \"null\": [1, null]}}");

        Assert.Equal(128, cell);
        Assert.Equal(2, cells.Count);
        Assert.Equal((0, 0), cells["sword"]);
        Assert.Equal((3, 2), cells["ingot"]);

        foreach (string broken in new[] { "", "   ", "not json", "[]", "{}", "{\"cell\": 0, \"keys\": {}}", "{\"cell\": 128}", "{\"cell\": \"128\", \"keys\": {\"sword\": [0, 0]}}" })
        {
            (int none, Dictionary<string, (int Column, int Row)> empty) = ItemIconRules.ParseAtlas(broken);
            Assert.Equal(0, none);
            Assert.Empty(empty);
        }

        Assert.Equal(0, ItemIconRules.ParseAtlas(null).Cell);
    }

    private static List<(string Id, ItemType Type, EquipmentSlot Slot)> ShippedItems()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "project.godot")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        var items = new List<(string, ItemType, EquipmentSlot)>();
        foreach (string file in Directory.EnumerateFiles(Path.Combine(dir!.FullName, "data", "items"), "*.tres"))
        {
            string text = File.ReadAllText(file);
            Match id = Regex.Match(text, "^Id = \"([^\"]+)\"", RegexOptions.Multiline);
            Assert.True(id.Success, $"{Path.GetFileName(file)} has no Id");

            // An omitted field is the resource's default: Misc, and no slot for a plain item. An
            // equippable that omits Slot would be MainHand, and the generator always writes it.
            Match type = Regex.Match(text, @"^Type = (\d+)", RegexOptions.Multiline);
            Match slot = Regex.Match(text, @"^Slot = (\d+)", RegexOptions.Multiline);
            items.Add((
                id.Groups[1].Value,
                type.Success ? (ItemType)int.Parse(type.Groups[1].Value) : ItemType.Misc,
                slot.Success ? (EquipmentSlot)int.Parse(slot.Groups[1].Value) : EquipmentSlot.None));
        }

        return items;
    }
}
