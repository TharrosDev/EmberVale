using System.Collections.Generic;
using Embervale.Core;

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
        ResourceDirectory.Load<ScheduleResource>(
            directory, "schedule", schedule => schedule.Id, ById);
    }

    public static ScheduleResource? Get(string id)
    {
        return ById.TryGetValue(id, out ScheduleResource? schedule) ? schedule : null;
    }
}
