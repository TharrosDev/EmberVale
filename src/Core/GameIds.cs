namespace Embervale.Core;

/// <summary>
/// Central registry of the stable content ids that gameplay <em>code</em> references by literal —
/// currency, seeded items, factions, enemy/actor templates, the player's starting spells/recipes,
/// and the sandbox's quest/dialogue/schedule ids. Centralizing them means a rename happens in one
/// place instead of silently breaking scattered call sites.
///
/// These values must match the ids authored in the corresponding <c>.tres</c> files (which cannot
/// reference C# constants). The <see cref="Embervale.Debugging.ContentValidator"/> resolves every
/// cross-reference at boot (and via the <c>validate</c> console command), so any drift between a
/// constant here and an authored id is reported rather than failing silently.
///
/// Only ids used from code belong here — placeholder resource defaults (e.g. <c>"item.unknown"</c>)
/// and ids that live purely in authored data do not.
/// </summary>
public static class GameIds
{
    public static class Currency
    {
        public const string Gold = "item.currency.gold";
    }

    public static class Items
    {
        public const string HealthPotion = "item.potion.health";
        public const string IronOre = "item.material.iron_ore";
        public const string GoblinHide = "item.material.goblin_hide";
        public const string HealingHerb = "item.material.healing_herb";
        public const string Ruby = "item.gem.ruby";
        public const string LeatherCap = "item.armor.leather_cap";
        public const string LeatherVest = "item.armor.leather_vest";
        public const string SteelSword = "item.weapon.steel_sword";
        public const string Bow = "item.weapon.bow";
        public const string IronRing = "item.ring.iron";
        public const string Scrap = "item.material.scrap";
    }

    public static class Enemies
    {
        public const string Goblin = "enemy.goblin";
        public const string IronKing = "enemy.iron_king";
    }

    public static class Factions
    {
        public const string Goblins = "faction.goblins";
        public const string Villagers = "faction.villagers";
        public const string Fallen = "faction.fallen";
    }

    public static class Npcs
    {
        public const string Elder = "npc.elder";
    }

    /// <summary>Persistent-actor template ids (see PersistentActorRegistry).</summary>
    public static class Templates
    {
        public const string Cache = "prop.cache";
    }

    /// <summary>Region ids (see RegionDatabase / data/regions).</summary>
    public static class Regions
    {
        public const string EmberCrown = "region.ember_crown";
        public const string FrostfangReach = "region.frostfang_reach";
    }

    public static class Spells
    {
        public const string Firebolt = "spell.firebolt";
        public const string Fireball = "spell.fireball";
        public const string FrostNova = "spell.frost_nova";
        public const string LesserHeal = "spell.lesser_heal";
        public const string ArcaneShield = "spell.arcane_shield";
    }

    public static class Recipes
    {
        public const string IronIngot = "recipe.iron_ingot";
        public const string LeatherStrips = "recipe.leather_strips";
        public const string HealthPotion = "recipe.health_potion";
        public const string LeatherCap = "recipe.leather_cap";
        public const string SteelSword = "recipe.steel_sword";
        public const string IronRing = "recipe.iron_ring";
    }

    public static class Quests
    {
        public const string CullGoblins = "quest.cull_goblins";
    }

    public static class Dialogues
    {
        public const string Elder = "dialogue.elder";
        public const string VendorStub = "dialogue.vendor_stub";
    }

    public static class Schedules
    {
        public const string Elder = "schedule.elder";
        public const string VendorGoods = "schedule.vendor_goods";
        public const string VendorSmith = "schedule.vendor_smith";
        public const string VendorAlch = "schedule.vendor_alch";
        public const string Innkeeper = "schedule.innkeeper";
    }
}
