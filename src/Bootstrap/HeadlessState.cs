using System;
using System.Collections.Generic;
using System.Text;
using Embervale.Companions;
using Embervale.Crafting;
using Embervale.Dialogue;
using Embervale.Economy;
using Embervale.Enemies;
using Embervale.Factions;
using Embervale.Items;
using Embervale.Localization;
using Embervale.Magic;
using Embervale.Progression;
using Embervale.Quests;
using Embervale.Shrines;
using Embervale.World;
using Godot;

namespace Embervale.Bootstrap;

/// <summary>
/// Headless content census (agent-ergonomics pass). <c>--state</c> loads every database, reports
/// what the world currently contains, and quits:
/// <code>godot --headless --path . -- --state</code>
///
/// It exists to replace a handful of greps at the start of every session. An agent picking the repo
/// up cold needs the same five numbers every time — how many regions, cells, shops, items,
/// conversations — and was reading `.tres` directories and doc prose to get them, which is both
/// expensive and one edit away from being wrong. This reads the databases the game itself loads, so
/// it cannot drift from reality the way a doc can.
///
/// <para><b>Queries</b> (each answers on the one <c>EMBERVALE_RESULT</c> line):
/// plain <c>--state</c> gives the counts and region ids; <c>--json</c> adds every region with its
/// portal gate and cells; <c>--ids=&lt;kind&gt;</c> lists the ids of one kind (<c>--ids=list</c>
/// names the kinds), narrowed by <c>--match=&lt;substring&gt;</c>; <c>--get=&lt;id&gt;</c> prints one
/// resource's authored properties. <c>--verbose</c> prints the old prose census as well.</para>
///
/// Exits <b>0</b>: a census is an observation and <c>--validate</c> is the gate. A query that names
/// an unknown kind or id exits 2.
///
/// ⚠️ It deliberately reports **counts and ids, not narrative**. "Where the project is" lives in
/// <c>docs/NOW.md</c> and is a human decision; this is only what is on disk.
/// </summary>
public static class HeadlessState
{
    /// <summary>The command-line argument that triggers the census.</summary>
    public const string FlagArgument = "--state";

    /// <summary>Longest property value <c>--get</c> prints before cutting it.</summary>
    private const int ValueLimit = 240;

    /// <summary>True when <see cref="FlagArgument"/> was passed on the command line.</summary>
    public static bool Requested() => HeadlessValidation.HasFlag(FlagArgument);

    /// <summary>Every kind <c>--ids</c> and <c>--get</c> know, with the resources of that kind.</summary>
    private static readonly (string Kind, Func<IEnumerable<(string Id, Resource Resource)>> Rows)[] Kinds =
    {
        ("regions", () => Rows(RegionDatabase.All, r => r.Id)),
        ("cells", Cells),
        ("items", () => Rows(ItemDatabase.All.Values, r => r.Id)),
        ("shops", () => Rows(ShopDatabase.All, r => r.Id)),
        ("services", () => Rows(ServiceDatabase.All, r => r.Id)),
        ("contracts", () => Rows(ContractDatabase.All, r => r.Id)),
        ("dialogues", () => Rows(DialogueDatabase.All, r => r.Id)),
        ("quests", () => Rows(QuestDatabase.All, r => r.Id)),
        ("map_locations", () => Rows(MapLocationDatabase.All, r => r.Id)),
        ("spells", () => Rows(SpellDatabase.All, r => r.Id)),
        ("status_effects", () => Rows(StatusEffectDatabase.All(), r => r.Id)),
        ("enemies", () => Rows(EnemyArchetypeDatabase.All, r => r.Id)),
        ("bosses", () => Rows(BossDatabase.All, r => r.Id)),
        ("ai_profiles", () => Rows(AIProfileDatabase.All, r => r.Id)),
        ("encounters", () => Rows(EncounterDatabase.All, r => r.Id)),
        ("perks", () => Rows(PerkDatabase.All, r => r.Id)),
        ("recipes", () => Rows(RecipeDatabase.All, r => r.Id)),
        ("companions", () => Rows(CompanionDatabase.All, r => r.Id)),
        ("shrines", () => Rows(ShrineDatabase.All, r => r.Id)),
        ("factions", () => Rows(FactionDatabase.All, r => r.Id)),
    };

    /// <summary>Loads the databases, answers the query, and quits.</summary>
    public static void Run(SceneTree tree)
    {
        ContentDatabases.InitializeAll();
        Loc.Initialize();

        HeadlessReport report = HeadlessGate.Begin("state");
        if (HeadlessArgs.Has("--ids"))
        {
            Ids(report, HeadlessArgs.Value("--ids") ?? string.Empty, HeadlessArgs.Value("--match") ?? string.Empty);
        }
        else if (HeadlessArgs.Has("--get"))
        {
            Get(report, HeadlessArgs.Value("--get") ?? string.Empty);
        }
        else
        {
            Census(report);
        }

        HeadlessGate.Finish(tree, report, legacyLine: false);
    }

    private static void Census(HeadlessReport report)
    {
        int cells = 0;
        var regionIds = new List<string>();
        foreach (RegionResource region in RegionDatabase.All)
        {
            cells += region.Cells.Count;
            regionIds.Add(region.Id);
        }

        report.Fact("regions", RegionDatabase.All.Count)
            .Fact("cells", cells)
            .Fact("items", ItemDatabase.All.Count)
            .Fact("shops", ShopDatabase.All.Count)
            .Fact("services", ServiceDatabase.All.Count)
            .Fact("contracts", ContractDatabase.All.Count)
            .Fact("dialogues", DialogueDatabase.All.Count)
            .Fact("quests", QuestDatabase.All.Count)
            .Fact("map_locations", MapLocationDatabase.All.Count)
            .Fact("region_ids", regionIds);

        if (HeadlessGate.Json && !HeadlessArgs.Has("--count"))
        {
            var regions = new List<object?>();
            foreach (RegionResource region in RegionDatabase.All)
            {
                var cellRows = new List<object?>();
                foreach (RegionCellResource cell in region.Cells)
                {
                    if (cell != null)
                    {
                        cellRows.Add(new Dictionary<string, object?>
                        {
                            ["id"] = cell.Id,
                            ["centre"] = new List<object?> { cell.Center.X, cell.Center.Y, cell.Center.Z },
                        });
                    }
                }

                regions.Add(new Dictionary<string, object?>
                {
                    ["id"] = region.Id, ["toll"] = region.TollGold, ["unlock_flag"] = region.UnlockFlagId,
                    ["cells"] = cellRows,
                });
            }

            report.Fact("region_detail", regions);
        }

        if (!HeadlessGate.Verbose)
        {
            return;
        }

        var text = new StringBuilder();
        text.AppendLine("=== Embervale content census ===");
        text.AppendLine("Where the project is: docs/NOW.md. This is only what is on disk.");
        text.AppendLine();
        text.AppendLine($"regions       {RegionDatabase.All.Count}");
        text.AppendLine($"cells         {cells}  (every cell of the ACTIVE region is resident — 38M2)");
        text.AppendLine($"items         {ItemDatabase.All.Count}");
        text.AppendLine($"shops         {ShopDatabase.All.Count}");
        text.AppendLine($"services      {ServiceDatabase.All.Count}");
        text.AppendLine($"contracts     {ContractDatabase.All.Count}");
        text.AppendLine($"dialogues     {DialogueDatabase.All.Count}");
        text.AppendLine($"quests        {QuestDatabase.All.Count}");
        text.AppendLine($"map locations {MapLocationDatabase.All.Count}  (39.5A — each placed in a cell scene)");
        if (HeadlessArgs.Has("--count"))
        {
            GD.Print(text.ToString());
            return;
        }

        text.AppendLine();
        foreach (RegionResource region in RegionDatabase.All)
        {
            // The door, printed with the region rather than reasoned about. RegionSetup feeds
            // UnlockFlagId straight to the portal's RequiredFlagId, and an empty one is an open
            // portal — so this line is the census's answer to "can the player get there yet".
            text.AppendLine($"{region.Id}  ({region.Cells.Count} cells" +
                (region.TollGold > 0 ? $", {region.TollGold}g toll" : string.Empty) +
                (string.IsNullOrEmpty(region.UnlockFlagId)
                    ? ", portal OPEN from the start"
                    : $", portal gated on '{region.UnlockFlagId}'") + ")");
            foreach (RegionCellResource cell in region.Cells)
            {
                if (cell != null)
                {
                    text.AppendLine($"    {cell.Id,-34} centre {cell.Center}");
                }
            }
        }

        GD.Print(text.ToString());
    }

    private static void Ids(HeadlessReport report, string kind, string match)
    {
        Func<IEnumerable<(string Id, Resource Resource)>>? rows = null;
        var kinds = new List<string>();
        foreach ((string name, Func<IEnumerable<(string, Resource)>> each) in Kinds)
        {
            kinds.Add(name);
            if (name == kind)
            {
                rows = each;
            }
        }

        if (kind == "list")
        {
            report.Fact("kinds", kinds);
            return;
        }

        if (rows == null)
        {
            report.Refuse($"--ids={kind} is not a kind. Kinds: {string.Join(", ", kinds)}.");
            return;
        }

        var ids = new List<string>();
        foreach ((string id, Resource _) in rows())
        {
            if (match.Length == 0 || id.Contains(match, StringComparison.OrdinalIgnoreCase))
            {
                ids.Add(id);
            }
        }

        ids.Sort(StringComparer.Ordinal);
        report.Fact("kind", kind).Fact("match", match).Fact("count", ids.Count).Fact("ids", ids);
    }

    private static void Get(HeadlessReport report, string id)
    {
        foreach ((string kind, Func<IEnumerable<(string Id, Resource Resource)>> rows) in Kinds)
        {
            foreach ((string each, Resource resource) in rows())
            {
                if (each != id)
                {
                    continue;
                }

                var properties = new Dictionary<string, object?>();
                foreach (Godot.Collections.Dictionary property in resource.GetPropertyList())
                {
                    var usage = (PropertyUsageFlags)property["usage"].AsInt64();
                    if ((usage & PropertyUsageFlags.ScriptVariable) == 0 || (usage & PropertyUsageFlags.Storage) == 0)
                    {
                        continue;
                    }

                    string name = property["name"].AsString();
                    string value = resource.Get(name).ToString();
                    properties[name] = value.Length > ValueLimit ? value[..ValueLimit] + "..." : value;
                }

                report.Fact("kind", kind).Fact("id", id).Fact("path", resource.ResourcePath)
                    .Fact("properties", properties);
                return;
            }
        }

        report.Refuse($"--get={id}: no resource of any kind has that id (kinds: --state --ids=list).");
    }

    private static IEnumerable<(string Id, Resource Resource)> Rows<T>(IEnumerable<T> all, Func<T, string> id)
        where T : Resource
    {
        foreach (T resource in all)
        {
            if (resource != null)
            {
                yield return (id(resource), resource);
            }
        }
    }

    private static IEnumerable<(string Id, Resource Resource)> Cells()
    {
        foreach (RegionResource region in RegionDatabase.All)
        {
            foreach (RegionCellResource cell in region.Cells)
            {
                if (cell != null)
                {
                    yield return (cell.Id, cell);
                }
            }
        }
    }
}
