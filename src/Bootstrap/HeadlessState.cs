using System.Text;
using Embervale.Core.Diagnostics;
using Embervale.Dialogue;
using Embervale.Economy;
using Embervale.Items;
using Embervale.Localization;
using Embervale.Quests;
using Embervale.World;
using Godot;

namespace Embervale.Bootstrap;

/// <summary>
/// Headless content census (agent-ergonomics pass). <c>--state</c> loads every database and prints
/// what the world currently contains, then quits:
/// <code>godot --headless --path . -- --state</code>
///
/// It exists to replace a handful of greps at the start of every session. An agent picking the repo
/// up cold needs the same five numbers every time — how many regions, cells, shops, items,
/// conversations — and was reading `.tres` directories and doc prose to get them, which is both
/// expensive and one edit away from being wrong. This reads the databases the game itself loads, so
/// it cannot drift from reality the way a doc can.
///
/// Always exits <b>0</b>: a census is an observation. <c>--validate</c> is the gate.
///
/// ⚠️ It deliberately reports **counts and ids, not narrative**. "Where the project is" lives in
/// <c>docs/NOW.md</c> and is a human decision; this is only what is on disk.
/// </summary>
public static class HeadlessState
{
    /// <summary>The command-line argument that triggers the census.</summary>
    public const string FlagArgument = "--state";

    /// <summary>True when <see cref="FlagArgument"/> was passed on the command line.</summary>
    public static bool Requested() => HeadlessValidation.HasFlag(FlagArgument);

    /// <summary>Loads the databases, prints the census, and quits 0.</summary>
    public static void Run(SceneTree tree)
    {
        Log.Info("=== Embervale content census (--state) ===");
        ContentDatabases.InitializeAll();
        Loc.Initialize();

        var text = new StringBuilder();
        text.AppendLine("=== Embervale content census ===");
        text.AppendLine("Where the project is: docs/NOW.md. This is only what is on disk.");
        text.AppendLine();

        int cells = 0;
        foreach (RegionResource region in RegionDatabase.All)
        {
            cells += region.Cells.Count;
        }

        text.AppendLine($"regions       {RegionDatabase.All.Count}");
        text.AppendLine($"cells         {cells}  (every cell of the ACTIVE region is resident — 38M2)");
        text.AppendLine($"items         {ItemDatabase.All.Count}");
        text.AppendLine($"shops         {ShopDatabase.All.Count}");
        text.AppendLine($"services      {ServiceDatabase.All.Count}");
        text.AppendLine($"dialogues     {DialogueDatabase.All.Count}");
        text.AppendLine($"quests        {QuestDatabase.All.Count}");
        text.AppendLine();

        foreach (RegionResource region in RegionDatabase.All)
        {
            text.AppendLine($"{region.Id}  ({region.Cells.Count} cells" +
                (region.TollGold > 0 ? $", {region.TollGold}g toll" : string.Empty) + ")");
            foreach (RegionCellResource cell in region.Cells)
            {
                if (cell != null)
                {
                    text.AppendLine($"    {cell.Id,-34} centre {cell.Center}");
                }
            }
        }

        GD.Print(text.ToString());
        tree.Quit(0);
    }
}
