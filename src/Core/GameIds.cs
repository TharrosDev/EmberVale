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
        public const string IronRing = "item.ring.iron";
        public const string Scrap = "item.material.scrap";
        public const string BeastPelt = "item.material.beast_pelt";
        public const string GraveDust = "item.material.grave_dust";
        public const string ElementalMote = "item.material.elemental_mote";
    }

    public static class Enemies
    {
        public const string Goblin = "enemy.goblin";
        public const string IronKing = "enemy.iron_king";
        public const string AshenAcolyte = "enemy.ashen_acolyte";

        // Phase 34B humanoids — authored under data/enemies, built by EnemyArchetypeFactory.
        public const string Bandit = "enemy.bandit";
        public const string Cultist = "enemy.cultist";
        public const string Soldier = "enemy.soldier";
        public const string SyndicateEnforcer = "enemy.syndicate_enforcer";

        // Phase 34C beasts — same pipeline, quadruped bodies.
        public const string Wolf = "enemy.wolf";
        public const string DireWolf = "enemy.dire_wolf";
        public const string FrostStalker = "enemy.frost_stalker";
        public const string ThornbackBoar = "enemy.thornback_boar";
        public const string AshfallElk = "enemy.ashfall_elk";

        // Phase 34D — the Hollow Queen's legions. The necromancer is the first archetype
        // authored as a caster (a non-empty KnownSpellIds + a standoff profile).
        public const string HollowHusk = "enemy.hollow_husk";
        public const string BoneKnight = "enemy.bone_knight";
        public const string BarrowWight = "enemy.barrow_wight";
        public const string HollowNecromancer = "enemy.hollow_necromancer";
        public const string GraveShade = "enemy.grave_shade";

        // Phase 34E — constructs (the guardians the forgotten civilizations left behind) and
        // elementals (one per offensive school, each resistant to its own).
        public const string StoneSentinel = "enemy.stone_sentinel";
        public const string WardGolem = "enemy.ward_golem";
        public const string RuinCrawler = "enemy.ruin_crawler";
        public const string CinderWisp = "enemy.cinder_wisp";
        public const string RimeShard = "enemy.rime_shard";
        public const string StormMote = "enemy.storm_mote";
        public const string ArcaneEcho = "enemy.arcane_echo";

        // Phase 34F — the Ashen. Most corrupted foes are variants of an existing archetype
        // (see AshenAffliction); these two are what a tint-and-scale modifier cannot produce.
        public const string AshMaw = "enemy.ash_maw";
        public const string CinderThrall = "enemy.cinder_thrall";
    }

    /// <summary>Enemy AI personality ids (see <c>AIProfileDatabase</c> / data/ai_profiles, Phase 34A).</summary>
    public static class AiProfiles
    {
        public const string Brute = "ai.brute";
        public const string PackFlanker = "ai.pack_flanker";
        public const string Shielded = "ai.shielded";
        public const string Caster = "ai.caster";
        public const string Skirmisher = "ai.skirmisher";
        public const string Ambusher = "ai.ambusher";
        public const string Boss = "ai.boss";

        // Phase 34C beast personalities.
        public const string Territorial = "ai.territorial";
        public const string Prey = "ai.prey";

        // Phase 34D undead nerve — the dead never break off.
        public const string Mindless = "ai.mindless";
        public const string DeathlessGuard = "ai.deathless_guard";

        // Phase 34E — a construct holds its post: never patrols, never calls for help, never leaves.
        public const string Sentry = "ai.sentry";
    }

    public static class Factions
    {
        public const string Goblins = "faction.goblins";
        public const string Villagers = "faction.villagers";
        public const string Fallen = "faction.fallen";
        public const string Outlaws = "faction.outlaws";
        public const string IronSyndicate = "faction.iron_syndicate";
        public const string Beasts = "faction.beasts";
        public const string Hollow = "faction.hollow";
    }

    public static class Npcs
    {
        public const string Elder = "npc.elder";
        public const string Kael = "npc.kael";
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
        public const string FlameLance = "spell.flame_lance";
        public const string StormConduit = "spell.storm_conduit";
        public const string EmberSiphon = "spell.ember_siphon";
        public const string BallLightning = "spell.ball_lightning";
        public const string Blizzard = "spell.blizzard";
        public const string Blink = "spell.blink";
        public const string LifebloomTotem = "spell.lifebloom_totem";

        // Phase 34D — the Necrotic school's first enemy-facing spells.
        public const string Wither = "spell.wither";
        public const string KnitBone = "spell.knit_bone";

        // Phase 34E — Arcane's first offensive spell; the school had only Self casts before.
        public const string ArcaneLance = "spell.arcane_lance";
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
        public const string KaelOath = "quest.kael.oath";
        public const string KaelBrother = "quest.kael.brother";
    }

    public static class Dialogues
    {
        public const string Elder = "dialogue.elder";
        public const string VendorStub = "dialogue.vendor_stub";
        public const string Kael = "dialogue.kael";
    }

    /// <summary>Recruitable companion ids (see <c>CompanionRegistry</c>, Phase 32).</summary>
    public static class Companions
    {
        public const string Kael = "companion.kael";
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
