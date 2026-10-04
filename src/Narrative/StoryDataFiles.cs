using System;
using System.Collections.Generic;
using Godot;

namespace Embervale.Narrative;

/// <summary>
/// Reads the story JSON tables (<c>data/story/rules</c>, <c>data/story/reactions</c>).
///
/// The files are read as plain text through <see cref="DirAccess"/>/<see cref="FileAccess"/> rather than
/// loaded as resources, and <b>export_presets.cfg's <c>include_filter</c> ships them</b>
/// (<c>data/story/rules/*.json</c>, <c>data/story/reactions/*.json</c>): the preset exports resources
/// only, so without the filter an exported build would find the folders empty and every rule and bark
/// would silently be missing. <c>assets/models/manifest.json</c> is shipped and read the same way.
/// </summary>
public static class StoryDataFiles
{
    /// <summary>Every <c>*.json</c> in <paramref name="directory"/>, ordinal by name, as (path, text).
    /// A missing directory yields nothing.</summary>
    public static List<(string Path, string Text)> ReadJson(string directory)
    {
        var files = new List<(string, string)>();
        if (!DirAccess.DirExistsAbsolute(directory))
        {
            return files;
        }

        string[] names = DirAccess.GetFilesAt(directory);
        Array.Sort(names, StringComparer.Ordinal);
        foreach (string name in names)
        {
            if (!name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string path = $"{directory}/{name}";
            using FileAccess? file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
            if (file != null)
            {
                files.Add((path, file.GetAsText()));
            }
        }

        return files;
    }

    /// <summary>Parses every reaction file under <see cref="Companions.CompanionReactionData.Directory"/>; ids
    /// must be unique across files, problems are appended to <paramref name="errors"/>.</summary>
    public static List<Companions.CompanionReaction> LoadReactions(List<string> errors)
    {
        var reactions = new List<Companions.CompanionReaction>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach ((string path, string text) in ReadJson(Companions.CompanionReactionData.Directory))
        {
            foreach (Companions.CompanionReaction reaction in Companions.CompanionReactionData.Parse(text, path, errors))
            {
                if (seen.Add(reaction.Id))
                {
                    reactions.Add(reaction);
                }
                else
                {
                    errors.Add($"{path} reaction '{reaction.Id}': duplicate id across files");
                }
            }
        }

        return reactions;
    }

    /// <summary>Parses every rule file under <see cref="StoryRuleData.RulesDirectory"/>. Rules keep file
    /// order (files ordinal by name, rules in file order); problems are appended to <paramref name="errors"/>.</summary>
    public static List<StoryRule> LoadRules(List<string> errors)
    {
        var rules = new List<StoryRule>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach ((string path, string text) in ReadJson(StoryRuleData.RulesDirectory))
        {
            foreach (StoryRule rule in StoryRuleData.Parse(text, path, errors))
            {
                if (seen.Add(rule.Id))
                {
                    rules.Add(rule);
                }
                else
                {
                    errors.Add($"{path} rule '{rule.Id}': duplicate id across files");
                }
            }
        }

        return rules;
    }
}
