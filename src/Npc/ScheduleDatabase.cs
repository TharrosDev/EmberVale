using System.Collections.Generic;
using Embervale.Core.Diagnostics;
using Godot;

namespace Embervale.Npc;

/// <summary>
/// Process-wide registry of <see cref="ScheduleResource"/>s, scanned once at startup from
/// <c>res://data/schedules</c> (mirrors the other content databases). NPCs resolve their
/// routine by stable string id. New routine = drop a <c>.tres</c>, no code change.
/// </summary>
public static class ScheduleDatabase
{
    private const string DefaultDirectory = "res://data/schedules";

    private static readonly Dictionary<string, ScheduleResource> ById = new();

    public static void Initialize(string directory = DefaultDirectory)
    {
        ById.Clear();

        if (!DirAccess.DirExistsAbsolute(directory))
        {
            Log.Warn($"ScheduleDatabase: directory '{directory}' not found; no schedules loaded.");
            return;
        }

        foreach (string file in DirAccess.GetFilesAt(directory))
        {
            string name = file.EndsWith(".remap") ? file[..^6] : file;
            if (!name.EndsWith(".tres"))
            {
                continue;
            }

            var schedule = GD.Load<ScheduleResource>($"{directory}/{name}");
            if (schedule == null)
            {
                continue;
            }

            if (ById.ContainsKey(schedule.Id))
            {
                Log.Warn($"Duplicate schedule id '{schedule.Id}' in {name}; overwriting.");
            }

            ById[schedule.Id] = schedule;
        }

        Log.Info($"ScheduleDatabase loaded {ById.Count} schedule(s) from {directory}.");
    }

    public static ScheduleResource? Get(string id)
    {
        return ById.TryGetValue(id, out ScheduleResource? schedule) ? schedule : null;
    }
}
