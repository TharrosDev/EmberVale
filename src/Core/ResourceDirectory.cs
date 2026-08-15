using System;
using System.Collections.Generic;
using Embervale.Core.Diagnostics;
using Godot;

namespace Embervale.Core;

/// <summary>
/// The one directory scan behind every <c>*Database</c> in this project: read every <c>.tres</c> in
/// a folder, key it by its authored id, warn on a duplicate, and report the count.
///
/// ⚠️ <b>This existed twenty-five times before it existed once</b> (audit 2026-08-15). Every database
/// carried its own byte-for-byte copy of the loop, and <b>the copies had already drifted</b>:
/// <c>ItemDatabase</c> was the single one missing the <c>DirExistsAbsolute</c> guard, so a mis-set
/// items directory raised a Godot error where all twenty-four siblings logged a warning and degraded.
/// That is the failure mode duplication actually produces — not the wasted lines, but the one copy
/// that quietly stopped matching the others.
///
/// A generic version was already in the tree, in <c>ContentValidator.CheckDuplicateIds&lt;T&gt;</c>,
/// doing the same walk to a different end. The validator keeps its own: it reports duplicates as
/// content issues rather than loading anything, and collapsing the two would put a diagnostic and a
/// loader on one code path.
///
/// <b>Exported builds expose resources as <c>&lt;name&gt;.tres.remap</c></b>, which is why the suffix
/// is stripped before the load rather than filtered on.
/// </summary>
public static class ResourceDirectory
{
    /// <summary>
    /// Scans <paramref name="directory"/> and rebuilds <paramref name="byId"/> (and
    /// <paramref name="all"/>, when a database exposes an ordered list) from the <c>.tres</c> files
    /// in it. Both collections are cleared first, so calling this twice is a reload, not a merge.
    /// </summary>
    /// <param name="label">The singular content noun for log lines, e.g. <c>"perk"</c>.</param>
    /// <param name="idOf">Reads the authored id off a loaded resource.</param>
    /// <param name="all">Optional insertion-ordered list, populated with first-wins entries only —
    /// a duplicate id overwrites in <paramref name="byId"/> but is not appended again, which is what
    /// every hand-written copy did and what keeps the list and the map the same length.</param>
    public static void Load<T>(
        string directory,
        string label,
        Func<T, string> idOf,
        Dictionary<string, T> byId,
        List<T>? all = null)
        where T : Resource
    {
        byId.Clear();
        all?.Clear();

        if (!DirAccess.DirExistsAbsolute(directory))
        {
            Log.Warn($"{label} database: directory '{directory}' not found; none loaded.");
            return;
        }

        foreach (string file in DirAccess.GetFilesAt(directory))
        {
            string name = file.EndsWith(".remap") ? file[..^6] : file;
            if (!name.EndsWith(".tres"))
            {
                continue;
            }

            var resource = GD.Load<T>($"{directory}/{name}");
            if (resource == null)
            {
                continue;
            }

            string id = idOf(resource);
            if (byId.ContainsKey(id))
            {
                Log.Warn($"Duplicate {label} id '{id}' in {name}; overwriting.");
            }
            else
            {
                all?.Add(resource);
            }

            byId[id] = resource;
        }

        Log.Info($"Loaded {byId.Count} {Plural(label, byId.Count)} from {directory}.");
    }

    /// <summary>
    /// English plural for a boot-log count. The hand-written loaders each spelled their own
    /// ("affix(es)", "boss(es)"), and a single "(s)" suffix would have regressed them to "affix(s)" —
    /// twenty-five log lines read on every boot and in every session's verification notes.
    /// </summary>
    private static string Plural(string label, int count)
    {
        if (count == 1)
        {
            return label;
        }

        if (label.EndsWith("y") && label.Length > 1 && !"aeiou".Contains(label[^2]))
        {
            return $"{label[..^1]}ies";   // bestiary entry → entries
        }

        return label.EndsWith("s") || label.EndsWith("x") || label.EndsWith("z") ||
               label.EndsWith("ch") || label.EndsWith("sh")
            ? $"{label}es"               // affix → affixes, boss → bosses
            : $"{label}s";
    }
}
