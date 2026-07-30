using Embervale.Companions;
using Embervale.Crafting;
using Embervale.Dialogue;
using Embervale.Enemies;
using Embervale.Factions;
using Embervale.Items;
using Embervale.Magic;
using Embervale.Npc;
using Embervale.Progression;
using Embervale.Quests;
using Embervale.Races;
using Embervale.World;

namespace Embervale.Bootstrap;

/// <summary>
/// Loads every authored-content database (and the enemy/companion registries) in one place, so the
/// sandbox bootstrap and the headless validation path stay in lockstep — neither can validate or
/// run against a different set of initialized content than the other. Order matches the original
/// <see cref="GameBootstrap"/> sequence; the databases are independent, so the order is not
/// load-bearing, but kept stable for readability.
/// </summary>
public static class ContentDatabases
{
    /// <summary>Scans <c>res://data/**</c> and (re)builds every content database + the enemy and
    /// companion registries. Safe to call more than once (each database clears and rebuilds).</summary>
    public static void InitializeAll()
    {
        ItemDatabase.Initialize();
        AffixDatabase.Initialize();
        PerkDatabase.Initialize();
        QuestDatabase.Initialize();
        DialogueDatabase.Initialize();
        ScheduleDatabase.Initialize();
        StatusEffectDatabase.Initialize();
        SpellDatabase.Initialize();
        WeatherDatabase.Initialize();
        RegionDatabase.Initialize();
        EncounterDatabase.Initialize();
        RecipeDatabase.Initialize();
        FactionDatabase.Initialize();
        WorldEventDatabase.Initialize();
        RaceDatabase.Initialize();
        AIProfileDatabase.Initialize();        // before the enemy registry: factories resolve profiles by id
        EnemyArchetypeDatabase.Initialize();   // and before it too: the registry builds from these
        EnemyTemplateRegistry.Initialize();
        CompanionDatabase.Initialize();
        CompanionRegistry.Initialize();
    }
}
