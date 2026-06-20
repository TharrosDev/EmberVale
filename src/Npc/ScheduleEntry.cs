using Godot;

namespace Embervale.Npc;

/// <summary>
/// One block of an NPC's day: from <see cref="StartHour"/> until the next entry's
/// start, the NPC heads to <see cref="Destination"/> and reports doing
/// <see cref="Activity"/>. Authored as a sub-resource inside a schedule <c>.tres</c>.
/// </summary>
[GlobalClass]
public partial class ScheduleEntry : Resource
{
    /// <summary>Hour of day (0–23) this block begins. Blocks run until the next one starts.</summary>
    [Export] public int StartHour { get; set; }

    /// <summary>Short activity label for UI/log, e.g. "Tending the well".</summary>
    [Export] public string Activity { get; set; } = "Idle";

    /// <summary>World position the NPC walks to for this block.</summary>
    [Export] public Vector3 Destination { get; set; }
}
