using System.Collections.Generic;
using Embervale.Companions;
using Embervale.Debugging;
using Embervale.Enemies;
using Embervale.World;
using Godot;

namespace Embervale.Bootstrap;

/// <summary>One boss brazier read off a cell scene: what it summons and what holds it cold.</summary>
internal sealed record SceneBrazier(
    string Scene, string Template, string FightId, string RequiredQuest, string RequiredFlag, string Defeated, string Closed);

/// <summary>
/// What the authored scenes place, read as text for the story gate (editor builds; an export ships
/// binary scenes, where <see cref="Placed"/> is null and every check that needs it is skipped).
/// Answers two questions the quest data cannot: is a conversation held by anyone in the world, and
/// what gates each boss brazier.
/// </summary>
internal static class ScenePlacement
{
    private static bool _scanned;
    private static HashSet<string>? _placed;
    private static readonly List<SceneBrazier> BrazierList = new();

    /// <summary>Every dialogue id some scene node, companion or boss fight carries; null when the scenes are not readable.</summary>
    public static HashSet<string>? Placed
    {
        get
        {
            Scan();
            return _placed;
        }
    }

    public static IEnumerable<SceneBrazier> Braziers(string template)
    {
        Scan();
        foreach (SceneBrazier brazier in BrazierList)
        {
            if (brazier.Template == template)
            {
                yield return brazier;
            }
        }
    }

    public static IReadOnlyList<SceneBrazier> AllBraziers
    {
        get
        {
            Scan();
            return BrazierList;
        }
    }

    public static string? TemplateForFight(string fightId)
    {
        Scan();
        foreach (SceneBrazier brazier in BrazierList)
        {
            if (brazier.FightId == fightId)
            {
                return brazier.Template;
            }
        }

        return null;
    }

    public static string? RequiredFlagForFight(string fightId)
    {
        Scan();
        foreach (SceneBrazier brazier in BrazierList)
        {
            if (brazier.FightId == fightId)
            {
                return brazier.RequiredFlag.Length > 0 ? brazier.RequiredFlag : null;
            }
        }

        return null;
    }

    /// <summary>
    /// Where a placed map pin stands, from scene text: its cell centre plus the transforms from the cell
    /// root down to the pin node. A stand-in for the bake measurement (WorldPlaceIndex) while a location
    /// authored since the last bake has none. Null when no scene places the pin.
    /// </summary>
    public static Vector3? LocationPosition(string locationId)
    {
        if (MapLocationDatabase.Get(locationId) is not { } location)
        {
            return null;
        }

        RegionCellResource? cell = null;
        foreach (RegionResource region in RegionDatabase.All)
        {
            foreach (RegionCellResource? candidate in region.Cells)
            {
                if (candidate?.Id == location.CellId)
                {
                    cell = candidate;
                }
            }
        }

        if (cell == null || cell.ScenePath.Length == 0 || !FileAccess.FileExists(cell.ScenePath))
        {
            return null;
        }

        List<SceneNodeText> nodes = SceneTextParser.Nodes(FileAccess.GetFileAsString(cell.ScenePath));
        var byPath = new Dictionary<string, SceneNodeText>();
        SceneNodeText? pin = null;
        string pinPath = string.Empty;
        foreach (SceneNodeText node in nodes)
        {
            string path = node.Parent.Length == 0 ? "." : node.Parent == "." ? node.Name : $"{node.Parent}/{node.Name}";
            byPath[path] = node;
            if (node.ScriptPath.EndsWith("/MapLocationComponent.cs", System.StringComparison.Ordinal) &&
                node.Str("LocationId") == locationId)
            {
                pin = node;
                pinPath = path;
            }
        }

        if (pin == null)
        {
            return null;
        }

        // Compose the transforms root -> pin.
        Transform3D world = Transform3D.Identity;
        var chain = new List<SceneNodeText>();
        string at = pinPath;
        while (byPath.TryGetValue(at, out SceneNodeText? node))
        {
            chain.Add(node);
            if (at == ".")
            {
                break;
            }

            at = node.Parent.Length == 0 ? "." : node.Parent;
        }

        for (int i = chain.Count - 1; i >= 0; i--)
        {
            world *= TransformOf(chain[i]);
        }

        return cell.Center + world.Origin;
    }

    private static Transform3D TransformOf(SceneNodeText node)
    {
        if (!node.Props.TryGetValue("transform", out string? raw))
        {
            return Transform3D.Identity;
        }

        System.Text.RegularExpressions.MatchCollection numbers =
            System.Text.RegularExpressions.Regex.Matches(raw, @"-?\d+(?:\.\d+)?(?:e-?\d+)?");
        if (numbers.Count != 12)
        {
            return Transform3D.Identity;
        }

        var v = new float[12];
        for (int i = 0; i < 12; i++)
        {
            v[i] = float.Parse(numbers[i].Value, System.Globalization.CultureInfo.InvariantCulture);
        }

        return new Transform3D(new Basis(v[0], v[1], v[2], v[3], v[4], v[5], v[6], v[7], v[8]), new Vector3(v[9], v[10], v[11]));
    }

    private static void Scan()
    {
        if (_scanned)
        {
            return;
        }

        _scanned = true;
        var placed = new HashSet<string>();
        int scenes = 0;
        foreach (string path in ScenePaths("res://scenes"))
        {
            using FileAccess? file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
            if (file == null)
            {
                continue;
            }

            scenes++;
            string text = file.GetAsText();
            foreach (SceneNodeText node in SceneTextParser.Nodes(text))
            {
                string dialogue = node.Str("DialogueId");
                if (dialogue.Length > 0)
                {
                    placed.Add(dialogue);
                }

                if (node.ScriptPath.EndsWith("/BossSummonComponent.cs", System.StringComparison.Ordinal))
                {
                    BrazierList.Add(new SceneBrazier(
                        path,
                        node.Str("BossTemplateId", "enemy.iron_king"),
                        node.Str("FightId"),
                        node.Str("RequiredQuestId", "quest.warband.heart"),
                        node.Str("RequiredFlagId"),
                        node.Str("DefeatedFlagId", BossEncounterDirector.DefeatedFlag),
                        node.Str("ClosedFlagId")));
                }
            }
        }

        if (scenes == 0)
        {
            return; // an export: no readable scenes, nothing to assert against
        }

        foreach (CompanionResource companion in CompanionDatabase.All)
        {
            if (companion.DialogueId.Length > 0)
            {
                placed.Add(companion.DialogueId);
            }
        }

        foreach (BossResource boss in BossDatabase.All)
        {
            if (boss.DefeatDialogueId.Length > 0)
            {
                placed.Add(boss.DefeatDialogueId);
            }
        }

        _placed = placed;
    }

    private static IEnumerable<string> ScenePaths(string directory)
    {
        if (!DirAccess.DirExistsAbsolute(directory))
        {
            yield break;
        }

        foreach (string file in DirAccess.GetFilesAt(directory))
        {
            if (file.EndsWith(".tscn", System.StringComparison.OrdinalIgnoreCase))
            {
                yield return $"{directory}/{file}";
            }
        }

        foreach (string sub in DirAccess.GetDirectoriesAt(directory))
        {
            foreach (string nested in ScenePaths($"{directory}/{sub}"))
            {
                yield return nested;
            }
        }
    }
}
